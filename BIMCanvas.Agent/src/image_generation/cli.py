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
DEFAULT_PROMPT = """你正在做“平面图风格转译”，不是重新设计户型。

输入规则：
- 第一张图是内容真理源，只决定空间结构、门窗位置、隔墙关系、家具摆放和取景范围。
- 第二张图只提供视觉风格，不提供布局内容；不要照搬第二张图里的房间形状、家具位置、文字内容或构图。

请输出一张新的二维俯视平面布局图，要求：
1. 严格保留第一张图中可见的房间轮廓、墙体厚度关系、门窗开口、卫生间位置和主要家具位置，不要新增、删除、旋转或重排家具。
2. 保持与第一张图相同的朝向、画幅和取景范围，不要裁切，不要补全未显示区域，不要改变比例关系。
3. 视觉风格学习第二张图：深黑背景、深绿色室内底色、绿色墙体线、橙色家具块、简洁的中文标签、干净的工程制图感。
4. 输出必须是清晰的 2D 正投影平面图，不要透视、不要 3D、不要照片感、不要真实材质、不要光影、不要体积渲染。
5. 如果第一张图与第二张图冲突，永远服从第一张图的结构与布局，只借用第二张图的配色与图形语言。"""


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
