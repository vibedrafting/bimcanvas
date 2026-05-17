"""图像分析 prompt 构造器（generic 部分；indoor-layout 专属 prompt 由 plugin 提供）。

组5 §5.A.4 修订后:
- 原 `REFERENCE_ANALYSIS_PROMPT_V1`(indoor-layout 专属 reference layout 分析 prompt)
  已迁到 `<BIMCANVAS_HOME>/plugins/indoor-layout/mcp_tools/lib/reference_prompts/reference_analysis_prompt_v1.md`,
  由 indoor-layout Skill 通过 Read 工具读取后作为 task 参数传给 analyze_image
- 原 `load_reference_analysis_prompt()` 函数同步删除(Skill 通过 Read 直接读 .md)
- 本文件只保留 generic `build_custom_image_analysis_prompt(task)`:供 analyze_image 把任意 task 包装为安全外壳的完整 prompt
"""

from __future__ import annotations


def build_custom_image_analysis_prompt(task: str) -> str:
    """构造自定义识图任务 prompt。

    只允许调用方传入本次识图目标，安全外壳固定在代码内。
    """
    task_text = (task or "").strip()
    if not task_text:
        raise ValueError("task 不能为空")

    return f"""这是一个纯文本图片理解任务，不是图片生成、图片编辑、设计落位或施工任务。请只输出中文文字结果，禁止返回任何图片、图片链接、markdown图片、代码、工具调用过程、分析过程说明或“正在查看图片”之类的过程性文字。

请严格围绕下面的识图目标观察图片并回答：
{task_text}

要求：
- 只描述图片中能够观察或合理判断的信息。
- 无法确认的内容要明确说明不确定，不要包装成事实。
- 不要把视觉观察直接升级为设计决策、施工合同、家具落位方案或对当前项目的最终建议。
- 不要主动补充与识图目标无关的装修建议、风格点评、营销描述或泛泛总结。
- 如果识图目标要求列清单、分组或对比，请用简洁清晰的中文条目表达。

请直接输出最终识图结果。"""
