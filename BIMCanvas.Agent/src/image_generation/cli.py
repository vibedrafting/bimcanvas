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


DEFAULT_API_KEY = "sk-UlHrU9kQxlBSmLXADfB6A8B0611946Ce8fAfFc0771C6066a"
DEFAULT_PROMPT = """生成该户型草图的彩平图放在纯白色背景图中，建筑结构和家具布置保持不变，文字标注不变。
彩平图的颜色要求：
普通地面填充颜色：#EBE9DE
卧室、书房地面填充颜色：#BEC0C0
卫生间地面填充颜色：#D2DEE5
阳台地面填充颜色：#EBDFC9
墙体填充颜色：#858585"""


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Standalone Nano Banana 2 image generation client"
    )
    parser.add_argument(
        "--api-key",
        default=DEFAULT_API_KEY,
        help="API易 API Key；不传则使用 cli.py 中的默认值",
    )
    parser.add_argument("--source", required=True, help="待处理原图路径")
    parser.add_argument("--style", default=None, help="风格参考图路径（可选，不传则纯文本驱动风格）")
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
            prompt=args.prompt,
            style_image_path=args.style,
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
