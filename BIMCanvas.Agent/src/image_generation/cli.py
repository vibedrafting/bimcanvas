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
3. 视觉风格学习第二张图：深黑背景、深绿色室内底色、绿色墙体线、橙色家具块、简洁的中文标签、干净的工程制图感；如需表达家具朝向，可使用少量细小的暖红色箭头。
4. 本次任务只聚焦两件事：
   - 原建筑元素的风格转换：墙、门、窗都保留，但只做图形风格转换，不做语义补全。
   - 家具的极简化表达：家具尽量简化为实心矩形，并保留家具标签。
5. 只处理第一张图中“带明确标签的家具模块”；没有标签、语义不清、边界模糊的家具，直接忽略，不要猜测它是什么，也不要新增同类家具。
6. 所有被保留的家具模块尽量简化为带填充的实心矩形或实心直角块面，只保留外轮廓，不要内部留洞，不要镂空，不要台盆开孔，不要复杂曲线，不要多余装饰轮廓。
7. 家具内部细节全部删除：不要软包褶皱、把手、缝线、枕头、床品纹理、洁具内部结构、柜门分格线、椅子结构线。
8. 家具标签要保留，并尽量与第一张图中的家具语义一致；不要把“定制柜体”擅自改成“衣柜”等其他名字。如果标签不确定，宁可省略该家具标签，也不要乱改名。
9. 对于被保留且带明确标签的家具模块，如果其摆放朝向在第一张图中足够明确，则尽量补一个小型、细线、简洁的方向箭头，风格参考第二张图中的暖红/粉红箭头；箭头表达的是家具朝向，不是移动轨迹。
10. 每个家具最多只画一个箭头，箭头要小，不要遮挡家具标签和家具外轮廓，优先放在家具内部空白处或紧邻家具边缘。
11. 只给家具画方向箭头，不给墙、门、窗画箭头；如果方向不明确，宁可不画，也不要猜测或额外创造朝向信息。
12. 门和窗都要尽量识别并保留其图形位置与形态，但不要输出任何门标签或窗标签，不要在图上写“门”“窗”。
13. 对于边界模糊、细节不清的门窗，不要强行重绘成规则新图形，优先保留原图中已有的开口轮廓和细节，只做轻度风格统一，避免幻觉。
14. 墙体、门、窗、柜体、家具请尽量使用纯色块面和清晰轮廓，不要保留墙体内部填充图案、剖面斜线、CAD 杂线、尺寸辅助线、隐藏线或施工纹样。
15. 输出必须是清晰的 2D 正投影平面图，不要透视、不要 3D、不要照片感、不要真实材质、不要光影、不要体积渲染。
16. 如果第一张图与第二张图冲突，永远服从第一张图的结构与布局，只借用第二张图的配色与图形语言。"""


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
