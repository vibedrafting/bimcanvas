"""Standalone Nano Banana 2 image generation client."""

from __future__ import annotations

import asyncio
import base64
import mimetypes
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

import aiohttp


SUPPORTED_IMAGE_SIZES = {"512px", "1K", "2K", "4K"}
SUPPORTED_ASPECT_RATIOS = {
    "1:1",
    "1:4",
    "4:1",
    "1:8",
    "8:1",
    "2:3",
    "3:2",
    "3:4",
    "4:3",
    "4:5",
    "5:4",
    "9:16",
    "16:9",
    "21:9",
}
SUPPORTED_INPUT_MIME_TYPES = {"image/png", "image/jpeg"}
OUTPUT_SUFFIX_BY_MIME_TYPE = {
    "image/png": ".png",
    "image/jpeg": ".jpg",
}


class ImageInputError(ValueError):
    """Raised when local input parameters are invalid."""


class ImageApiError(RuntimeError):
    """Raised when the remote API call fails."""


class ImageResponseError(RuntimeError):
    """Raised when the remote API response is malformed."""


class ImageSaveError(RuntimeError):
    """Raised when the generated image cannot be persisted."""


@dataclass(slots=True)
class GeneratedImageResult:
    output_path: str
    mime_type: str
    request_url: str
    model: str
    raw_response: dict[str, Any]


class NanoBananaImageClient:
    """Minimal API client for Nano Banana 2 image generation/editing."""

    def __init__(
        self,
        api_key: str,
        base_url: str = "https://api.apiyi.com",
        model: str = "gemini-3.1-flash-image-preview",
        timeout_seconds: int = 300,
    ) -> None:
        api_key = (api_key or "").strip()
        if not api_key:
            raise ImageInputError("API Key 不能为空")

        base_url = (base_url or "").strip().rstrip("/")
        if not base_url:
            raise ImageInputError("base_url 不能为空")

        model = (model or "").strip()
        if not model:
            raise ImageInputError("model 不能为空")

        if timeout_seconds <= 0:
            raise ImageInputError("timeout_seconds 必须大于 0")

        self.api_key = api_key
        self.base_url = base_url
        self.model = model
        self.timeout_seconds = timeout_seconds

    @property
    def request_url(self) -> str:
        return f"{self.base_url}/v1beta/models/{self.model}:generateContent"

    async def generate_from_paths(
        self,
        source_image_path: str | Path,
        prompt: str,
        style_image_path: str | Path | None = None,
        output_path: str | Path | None = None,
        image_size: str = "2K",
        aspect_ratio: str | None = None,
    ) -> GeneratedImageResult:
        prompt = (prompt or "").strip()
        if not prompt:
            raise ImageInputError("prompt 不能为空")

        normalized_image_size = self._validate_image_size(image_size)
        normalized_aspect_ratio = self._validate_aspect_ratio(aspect_ratio)

        source_path = self._validate_input_image_path(source_image_path, "原图")
        resolved_output_path = self._resolve_output_path(output_path, source_path)

        parts: list[dict[str, Any]] = [self._build_inline_image_part(source_path)]
        if style_image_path is not None:
            style_path = self._validate_input_image_path(style_image_path, "风格参考图")
            parts.append(self._build_inline_image_part(style_path))
        parts.append({"text": prompt})

        payload = {
            "contents": [{"parts": parts}],
            "generationConfig": {
                "responseModalities": ["IMAGE"],
                "imageConfig": {
                    "imageSize": normalized_image_size,
                },
            },
        }

        if normalized_aspect_ratio:
            payload["generationConfig"]["imageConfig"]["aspectRatio"] = normalized_aspect_ratio

        headers = {
            "Authorization": f"Bearer {self.api_key}",
            "Content-Type": "application/json",
        }

        timeout = aiohttp.ClientTimeout(total=self.timeout_seconds)
        try:
            async with aiohttp.ClientSession(timeout=timeout) as session:
                async with session.post(self.request_url, headers=headers, json=payload) as response:
                    response_text = await response.text()
                    if response.status != 200:
                        raise ImageApiError(
                            f"图片生成请求失败: HTTP {response.status} - {response_text}"
                        )

                    try:
                        response_json = await response.json(content_type=None)
                    except Exception as exc:  # pragma: no cover - defensive
                        raise ImageApiError(f"响应不是有效 JSON: {response_text}") from exc
        except aiohttp.ClientError as exc:
            raise ImageApiError(f"图片生成请求失败: {exc}") from exc

        image_mime_type, image_base64 = self._extract_generated_image(response_json)
        saved_output_path = self._save_output_image(
            resolved_output_path,
            image_mime_type,
            image_base64,
        )

        return GeneratedImageResult(
            output_path=str(saved_output_path),
            mime_type=image_mime_type,
            request_url=self.request_url,
            model=self.model,
            raw_response=response_json,
        )

    def generate_from_paths_sync(
        self,
        source_image_path: str | Path,
        prompt: str,
        style_image_path: str | Path | None = None,
        output_path: str | Path | None = None,
        image_size: str = "2K",
        aspect_ratio: str | None = None,
    ) -> GeneratedImageResult:
        try:
            asyncio.get_running_loop()
        except RuntimeError:
            return asyncio.run(
                self.generate_from_paths(
                    source_image_path=source_image_path,
                    style_image_path=style_image_path,
                    prompt=prompt,
                    output_path=output_path,
                    image_size=image_size,
                    aspect_ratio=aspect_ratio,
                )
            )

        raise RuntimeError(
            "当前线程中已有运行中的事件循环，请改用异步方法 generate_from_paths()"
        )

    def _validate_image_size(self, image_size: str) -> str:
        value = (image_size or "").strip()
        if value not in SUPPORTED_IMAGE_SIZES:
            supported = ", ".join(sorted(SUPPORTED_IMAGE_SIZES))
            raise ImageInputError(f"image_size 必须是以下之一: {supported}")
        return value

    def _validate_aspect_ratio(self, aspect_ratio: str | None) -> str | None:
        if aspect_ratio is None:
            return None

        value = aspect_ratio.strip()
        if not value:
            return None
        if value not in SUPPORTED_ASPECT_RATIOS:
            supported = ", ".join(sorted(SUPPORTED_ASPECT_RATIOS))
            raise ImageInputError(f"aspect_ratio 必须是以下之一: {supported}")
        return value

    def _validate_input_image_path(self, raw_path: str | Path, label: str) -> Path:
        path = Path(raw_path).expanduser().resolve()
        if not path.exists():
            raise ImageInputError(f"{label}不存在: {path}")
        if not path.is_file():
            raise ImageInputError(f"{label}必须是文件: {path}")

        mime_type, _ = mimetypes.guess_type(path.name)
        if not mime_type or not mime_type.startswith("image/"):
            raise ImageInputError(f"{label}不是有效图片文件: {path}")
        if mime_type not in SUPPORTED_INPUT_MIME_TYPES:
            supported = ", ".join(sorted(SUPPORTED_INPUT_MIME_TYPES))
            raise ImageInputError(f"{label}仅支持以下格式: {supported}")

        return path

    def _resolve_output_path(self, output_path: str | Path | None, source_path: Path) -> Path:
        if output_path is None:
            timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
            return source_path.parent / f"generated_{timestamp}.png"

        path = Path(output_path).expanduser().resolve()
        if path.suffix:
            return path
        return path.with_suffix(".png")

    def _build_inline_image_part(self, image_path: Path) -> dict[str, Any]:
        mime_type, _ = mimetypes.guess_type(image_path.name)
        try:
            image_bytes = image_path.read_bytes()
        except OSError as exc:
            raise ImageInputError(f"无法读取图片文件: {image_path}") from exc

        image_base64 = base64.b64encode(image_bytes).decode("utf-8")
        return {
            "inlineData": {
                "mimeType": mime_type,
                "data": image_base64,
            }
        }

    def _extract_generated_image(self, response_json: dict[str, Any]) -> tuple[str, str]:
        candidates = response_json.get("candidates")
        if not isinstance(candidates, list) or not candidates:
            raise ImageResponseError("响应中缺少 candidates")

        first_candidate = candidates[0]
        content = first_candidate.get("content")
        if not isinstance(content, dict):
            raise ImageResponseError("响应中缺少 content")

        parts = content.get("parts")
        if not isinstance(parts, list) or not parts:
            raise ImageResponseError("响应中缺少 content.parts")

        for part in parts:
            if not isinstance(part, dict):
                continue
            inline_data = part.get("inlineData")
            if not isinstance(inline_data, dict):
                continue

            image_base64 = inline_data.get("data")
            mime_type = inline_data.get("mimeType", "image/png")
            if image_base64:
                return mime_type, image_base64

        raise ImageResponseError("响应中未找到生成图片数据")

    def _save_output_image(self, output_path: Path, mime_type: str, image_base64: str) -> Path:
        expected_suffix = OUTPUT_SUFFIX_BY_MIME_TYPE.get(mime_type)
        normalized_path = output_path
        if expected_suffix:
            if normalized_path.suffix.lower() != expected_suffix:
                normalized_path = normalized_path.with_suffix(expected_suffix)

        try:
            normalized_path.parent.mkdir(parents=True, exist_ok=True)
        except OSError as exc:
            raise ImageSaveError(f"无法创建输出目录: {normalized_path.parent}") from exc

        try:
            image_bytes = base64.b64decode(image_base64)
        except Exception as exc:  # pragma: no cover - defensive
            raise ImageResponseError("响应中的图片数据不是有效 Base64") from exc

        try:
            normalized_path.write_bytes(image_bytes)
        except OSError as exc:
            raise ImageSaveError(f"无法写入输出文件: {normalized_path}") from exc

        return normalized_path
