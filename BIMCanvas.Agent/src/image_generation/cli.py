"""CLI for standalone Nano Banana 2 image generation."""

from __future__ import annotations

import argparse
import sys

from src.image_generation.nano_banana_client import (
    ImageApiError,
    ImageInputError,
    ImageResponseError,
    ImageSaveError,
    NanoBananaImageClient,
)


DEFAULT_PROMPT = """请基于第一张输入图的空间轮廓、门窗开口和房间结构，生成一张二维俯视平面布局图。
严格保留原始建筑结构，不要改动墙体、门、窗的位置与整体户型。
视觉风格请参考第二张图片：深色背景、绿色墙体线、橙色家具块、清晰中文标签、工程平面图表达。
输出必须是正投影平面图，不要透视图，不要3D效果，不要照片质感，不要真实材质，不要阴影渲染。"""


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Standalone Nano Banana 2 image generation client"
    )
    parser.add_argument("--api-key", required=True, help="API易 API Key")
    parser.add_argument("--source", required=True, help="待处理原图路径")
    parser.add_argument("--style", required=True, help="风格参考图路径")
    parser.add_argument("--output", required=True, help="输出图片路径")
    parser.add_argument("--prompt", default=DEFAULT_PROMPT, help="自定义提示词")
    parser.add_argument(
        "--image-size",
        default="2K",
        choices=["512px", "1K", "2K", "4K"],
        help="输出分辨率，默认 2K",
    )
    parser.add_argument(
        "--aspect-ratio",
        default=None,
        choices=[
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
        ],
        help="可选宽高比",
    )
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()

    try:
        client = NanoBananaImageClient(api_key=args.api_key)
        result = client.generate_from_paths_sync(
            source_image_path=args.source,
            style_image_path=args.style,
            prompt=args.prompt,
            output_path=args.output,
            image_size=args.image_size,
            aspect_ratio=args.aspect_ratio,
        )
    except (ImageInputError, ImageApiError, ImageResponseError, ImageSaveError, RuntimeError) as exc:
        print(f"[ERROR] {exc}", file=sys.stderr)
        return 1

    print("Image generation completed.")
    print(f"Output: {result.output_path}")
    print(f"MIME: {result.mime_type}")
    print(f"Model: {result.model}")
    print(f"Request URL: {result.request_url}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
