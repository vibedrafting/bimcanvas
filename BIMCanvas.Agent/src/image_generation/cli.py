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
DEFAULT_PROMPT = """任务：把输入的 CAD 平面图重绘为一张风格化的 2D 正投影平面图，只改变视觉外观，不改变任何空间内容。

绝对优先级：结构保真 > 家具保真 > 视觉风格。任何冲突都按此顺序仲裁。

【结构保真 - 最高优先级】
1. 精确复制输入图的房间外轮廓、内墙位置、墙体厚度比例、门窗开口位置与开启方向；保持完全相同的画幅、朝向与取景范围，禁止裁切或补全。原因：这是同一张图的风格转译，不是重新设计。
2. 精确还原输入图中每一件带中文标签的家具的位置、轮廓尺寸与朝向；不新增、不删除、不重排、不旋转家具。

【家具风格化 - 次高优先级】
3. 把每件保留的家具简化为单一橙色实心矩形色块，外轮廓清晰；家具内部一律纯色，不画任何分格、纹理或细节。
4. 家具中文标签直接沿用输入图的原文，白色字体叠在家具色块上；输入图中没有清晰中文标签的物体（如卫生间洁具、设备线条等）一律不画，只保留该区域的空地面填充。

【视觉风格规约】
5. 配色严格按以下映射使用：背景=深黑；室内地面填充=深绿；墙体填充=中灰；墙体描边=亮绿色细线；家具=橙色实心块；窗=蓝色矩形条；门=绿色矩形+白色开启弧线。
6. 全图为干净的 2D 工程制图风格：纯色块面、清晰描边、无 CAD 剖面斜线、无尺寸标注、无透视、无 3D、无光影、无材质、无照片感。

【反幻觉回扣】
7. 输出中的家具数量与标签数量绝不允许超过输入图中可识别的内容；当某个元素不确定时，宁可省略也不要补全或猜测。"""


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
