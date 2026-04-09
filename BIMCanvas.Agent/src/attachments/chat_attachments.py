from __future__ import annotations

import base64
import io
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from PIL import Image, ImageOps, UnidentifiedImageError

CHAT_ATTACHMENT_MANIFEST = "_chat_attachments.json"
CHAT_ATTACHMENT_DIR = "screenshots"
MAX_IMAGE_BYTES = int(3.75 * 1024 * 1024)
MAX_IMAGE_DIMENSION = 2000
JPEG_QUALITIES = [92, 85, 78, 70, 62]
WEBP_QUALITIES = [90, 82, 74, 66]
SCALE_STEPS = [1.0, 0.85, 0.7, 0.55, 0.42, 0.32]


class AttachmentResolutionError(Exception):
    def __init__(self, error_type: str, message: str, status: int) -> None:
        super().__init__(message)
        self.error_type = error_type
        self.message = message
        self.status = status


@dataclass
class PreparedImageBlock:
    media_type: str
    base64_data: str
    width: int
    height: int
    source_path: str

    def to_content_block(self) -> dict[str, Any]:
        return {
            "type": "image",
            "source": {
                "type": "base64",
                "media_type": self.media_type,
                "data": self.base64_data,
            },
        }


def resolve_attachment_image_blocks(project_path: str, attachment_ids: list[str]) -> list[dict[str, Any]]:
    if not attachment_ids:
        return []

    records_by_id = _load_manifest_records(project_path)
    blocks: list[dict[str, Any]] = []

    for attachment_id in attachment_ids:
        record = records_by_id.get(attachment_id)
        if not record:
            raise AttachmentResolutionError(
                "attachment_missing",
                f"attachment_missing: {attachment_id}",
                404,
            )

        status = str(record.get("status") or "").lower()
        if status == "deleted":
            raise AttachmentResolutionError(
                "attachment_missing",
                f"attachment_missing: {attachment_id}",
                404,
            )

        stored_path = record.get("storedPath")
        if not stored_path:
            raise AttachmentResolutionError(
                "attachment_invalid",
                f"attachment_invalid: {attachment_id}",
                400,
            )

        image_path = Path(stored_path)
        if not image_path.is_file():
            raise AttachmentResolutionError(
                "attachment_missing",
                f"attachment_missing: {attachment_id}",
                404,
            )

        mime_type = str(record.get("mimeType") or _guess_media_type(image_path)).lower()
        if not mime_type.startswith("image/"):
            raise AttachmentResolutionError(
                "attachment_invalid",
                f"attachment_invalid: {attachment_id}",
                400,
            )

        prepared = _prepare_image_block(image_path, mime_type)
        blocks.append(prepared.to_content_block())

    return blocks


def _load_manifest_records(project_path: str) -> dict[str, dict[str, Any]]:
    manifest_path = Path(project_path) / CHAT_ATTACHMENT_DIR / CHAT_ATTACHMENT_MANIFEST
    if not manifest_path.is_file():
        raise AttachmentResolutionError(
            "attachment_missing",
            f"attachment_missing: manifest not found ({manifest_path})",
            404,
        )

    try:
        manifest_data = json.loads(manifest_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise AttachmentResolutionError(
            "attachment_invalid",
            f"attachment_invalid: manifest parse failed ({exc})",
            400,
        ) from exc

    raw_records = manifest_data if isinstance(manifest_data, list) else manifest_data.get("attachments", [])
    if not isinstance(raw_records, list):
        raise AttachmentResolutionError(
            "attachment_invalid",
            "attachment_invalid: manifest attachments must be a list",
            400,
        )

    records_by_id: dict[str, dict[str, Any]] = {}
    for item in raw_records:
        if not isinstance(item, dict):
            continue
        attachment_id = item.get("attachmentId")
        if isinstance(attachment_id, str) and attachment_id:
            records_by_id[attachment_id] = item

    return records_by_id


def _prepare_image_block(image_path: Path, mime_type: str) -> PreparedImageBlock:
    try:
        with Image.open(image_path) as image:
            image = ImageOps.exif_transpose(image)
            base_image = image.copy()
    except (UnidentifiedImageError, OSError) as exc:
        raise AttachmentResolutionError(
            "attachment_invalid",
            f"attachment_invalid: unable to read image ({image_path.name})",
            400,
        ) from exc

    preferred_format, preferred_mime = _resolve_preferred_output(mime_type)
    candidate_image = _downscale_to_limit(base_image, MAX_IMAGE_DIMENSION)

    for scale in _build_scale_candidates(candidate_image):
        scaled_image = _resize_image(candidate_image, scale)
        for encoded_bytes, encoded_mime in _encode_candidates(scaled_image, preferred_format, preferred_mime):
            if len(encoded_bytes) <= MAX_IMAGE_BYTES:
                width, height = scaled_image.size
                return PreparedImageBlock(
                    media_type=encoded_mime,
                    base64_data=base64.b64encode(encoded_bytes).decode("ascii"),
                    width=width,
                    height=height,
                    source_path=str(image_path),
                )

    raise AttachmentResolutionError(
        "attachment_too_large",
        f"attachment_too_large: {image_path.name}",
        413,
    )


def _resolve_preferred_output(mime_type: str) -> tuple[str, str]:
    normalized = mime_type.lower()
    if normalized == "image/jpeg":
        return "JPEG", "image/jpeg"
    if normalized == "image/webp":
        return "WEBP", "image/webp"
    return "PNG", "image/png"


def _downscale_to_limit(image: Image.Image, max_dimension: int) -> Image.Image:
    width, height = image.size
    max_side = max(width, height)
    if max_side <= max_dimension:
        return image.copy()

    scale = max_dimension / max_side
    return _resize_image(image, scale)


def _build_scale_candidates(image: Image.Image) -> list[float]:
    width, height = image.size
    if width <= 0 or height <= 0:
        return [1.0]

    result: list[float] = []
    for scale in SCALE_STEPS:
        clamped = max(scale, 0.1)
        if clamped not in result:
            result.append(clamped)
    return result


def _resize_image(image: Image.Image, scale: float) -> Image.Image:
    if scale >= 0.999:
        return image.copy()

    width = max(1, int(round(image.width * scale)))
    height = max(1, int(round(image.height * scale)))
    return image.resize((width, height), Image.Resampling.LANCZOS)


def _encode_candidates(
    image: Image.Image,
    preferred_format: str,
    preferred_mime: str,
) -> list[tuple[bytes, str]]:
    candidates: list[tuple[bytes, str]] = []

    if preferred_format == "PNG":
        candidates.append((_save_png(image), preferred_mime))
        for quality in JPEG_QUALITIES:
            candidates.append((_save_jpeg(image, quality), "image/jpeg"))
        return candidates

    if preferred_format == "JPEG":
        for quality in JPEG_QUALITIES:
            candidates.append((_save_jpeg(image, quality), preferred_mime))
        return candidates

    if preferred_format == "WEBP":
        for quality in WEBP_QUALITIES:
            candidates.append((_save_webp(image, quality), preferred_mime))
        for quality in JPEG_QUALITIES:
            candidates.append((_save_jpeg(image, quality), "image/jpeg"))
        return candidates

    candidates.append((_save_png(image), "image/png"))
    return candidates


def _save_png(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="PNG", optimize=True)
    return buffer.getvalue()


def _save_webp(image: Image.Image, quality: int) -> bytes:
    buffer = io.BytesIO()
    image_to_save = _ensure_rgb_for_lossy(image)
    image_to_save.save(buffer, format="WEBP", quality=quality, method=6)
    return buffer.getvalue()


def _save_jpeg(image: Image.Image, quality: int) -> bytes:
    buffer = io.BytesIO()
    image_to_save = _ensure_rgb_for_lossy(image)
    image_to_save.save(buffer, format="JPEG", quality=quality, optimize=True, progressive=True)
    return buffer.getvalue()


def _ensure_rgb_for_lossy(image: Image.Image) -> Image.Image:
    if image.mode in ("RGB", "L"):
        return image.convert("RGB")

    alpha_source = image.convert("RGBA")
    background = Image.new("RGB", alpha_source.size, (255, 255, 255))
    background.paste(alpha_source, mask=alpha_source.getchannel("A"))
    return background


def _guess_media_type(path: Path) -> str:
    suffix = path.suffix.lower()
    if suffix in (".jpg", ".jpeg"):
        return "image/jpeg"
    if suffix == ".webp":
        return "image/webp"
    if suffix == ".gif":
        return "image/gif"
    return "image/png"
