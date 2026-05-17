"""indoor-layout plugin MCP 工具入口 (主真理源 v1.1 §3.8 / 组5 §5.A.3 迁出)。

5 个 indoor-layout 专属工具,通过 `register(builder)` 范式注册:
- save_semantic_plan / load_semantic_plan (语义方案标签管理)
- save_reference_analysis / load_reference_analysis (参考分析快照管理)
- clone_scheme_to_variant (relocation 工作流入口,仅 module-relocation-agent 调用)

迁出前位置:`BIMCanvas.Agent/src/mcp/canvas.py` 的 5 个 @tool 定义。
迁出后所有 5 个工具:
- 使用 PluginContext (ctx.session / ctx.server_url) 替代模块级 aiohttp.ClientSession + SERVER_URL
- 通过 `register(builder)` 在 plugin 加载时(`_build_mcp_servers` 内 importlib.spec_from_file_location)被注入
- 通过 `mcpNamespace="indoor-layout"` 暴露为 `mcp__indoor-layout__<tool_name>`

注意:本 plugin 不依赖 platform 内的 `reference_analysis/` 模块(那是 generic 图像分析后端,仅 core analyze_image 工具用)。
本 plugin 通过 `Read` lib/reference_prompts/reference_analysis_prompt_v1.md 后传给 core analyze_image 工具来完成参考图布局分析。
"""

from __future__ import annotations

from typing import Any

import aiohttp

from bimcanvas_plugin_sdk import McpServerBuilder


def register(builder: McpServerBuilder) -> None:
    """indoor-layout plugin 注册入口 (组3 任务模板 §4.1 入口约定)。"""
    ctx = builder.context

    # ---------- save_semantic_plan ----------
    @builder.tool(
        "save_semantic_plan",
        "保存语义方案标签。在规划阶段的每个子阶段（2.1/2.2/2.3）完成后调用，提交当前标签的语义方案。"
        "可选 variantId 用于写入变体路径；spatial-skeleton / multi-plan-overview 是 canonical 全局单 owner，禁止与 variantId 同时传入。",
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
                "zoneId": {
                    "type": "string",
                    "description": "目标 Zone ID，如 'rz_3'",
                },
                "tag": {
                    "type": "string",
                    "enum": ["spatial-skeleton", "strategic-plan", "multi-plan-overview", "construction-brief"],
                    "description": "语义方案标签：spatial-skeleton=空间骨架, strategic-plan=战略层方案, multi-plan-overview=多方案概述, construction-brief=完整施工简报",
                },
                "planType": {
                    "type": "string",
                    "enum": ["derived"],
                    "description": "图纸类型：当前正式流程统一为 derived；旧的 reference 仅用于识别历史数据。",
                },
                "content": {
                    "type": "string",
                    "description": "语义方案文本内容（markdown 格式）",
                },
                "referenceAnalysisTag": {
                    "type": "string",
                    "description": "可选。若当前方案消费了定稿 reference_analysis，记录对应的标签（如 v3 / v4）。",
                },
                "variantId": {
                    "type": "string",
                    "description": "可选。非空时写变体路径 schemes/{zoneId}/variants/{variantId}/semantic_plan.json；为空时写 canonical。"
                                   "**spatial-skeleton / multi-plan-overview 禁止传 variantId**（server 强制 400，这两个 tag 全局只在 canonical 出现）。"
                                   "Phase 1 暂无调用方需要传入；预留给后续 multi-plan / variant-design-agent。",
                },
            },
            "required": ["zoneId", "tag", "planType", "content"],
            "additionalProperties": False,
        },
    )
    async def save_semantic_plan(args: dict[str, Any]) -> dict[str, Any]:
        zone_id = args["zoneId"]
        tag = args["tag"]
        plan_type = args["planType"]
        content = args["content"]

        body: dict[str, Any] = {
            "zoneId": zone_id,
            "tag": tag,
            "planType": plan_type,
            "content": content,
        }
        if args.get("referenceAnalysisTag"):
            body["referenceAnalysisTag"] = args["referenceAnalysisTag"]
        if args.get("variantId"):
            body["variantId"] = args["variantId"]

        try:
            async with ctx.session.post(
                f"{ctx.server_url}/api/semantic-plan/save",
                json=body,
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    ref_tag = data.get("referenceAnalysisTag")
                    suffix = f"（reference={ref_tag}）" if ref_tag else ""
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"语义方案 {plan_type} {tag} 已保存{suffix}。继续下一阶段。",
                        }]
                    }
                error_text = await resp.text()
                return {
                    "content": [{"type": "text", "text": f"保存失败: {error_text}"}],
                    "is_error": True,
                }
        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }

    # ---------- load_semantic_plan ----------
    @builder.tool(
        "load_semantic_plan",
        "加载当前设计区的生效语义方案。返回当前可施工图纸，而不是完整历史。"
        "传 variantId 时返回 merge view（canonical 的 spatial-skeleton + 变体的 strategic-plan/construction-brief entries）。",
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
                "zoneId": {
                    "type": "string",
                    "description": "目标 Zone ID，如 'rz_3'",
                },
                "variantId": {
                    "type": "string",
                    "description": "可选。非空时返回 merge view（canonical 的 spatial-skeleton + 变体的 strategic-plan/construction-brief entries）；effectiveTag 落在变体的合同上。"
                                   "Phase 1 暂无调用方需要传入；预留给后续 multi-plan / variant-design-agent。",
                },
            },
            "required": ["zoneId"],
            "additionalProperties": False,
        },
    )
    async def load_semantic_plan(args: dict[str, Any]) -> dict[str, Any]:
        zone_id = args["zoneId"]
        variant_id = args.get("variantId")
        params = {"variantId": variant_id} if variant_id else None

        try:
            async with ctx.session.get(
                f"{ctx.server_url}/api/semantic-plan/{zone_id}",
                params=params,
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    text_parts = [
                        f"status: {data['status']}",
                        f"zoneId: {data['zoneId']}",
                        f"planType: {data['planType']}",
                        f"effectiveTag: {data['effectiveTag']}",
                        f"timestamp: {data['timestamp']}",
                    ]
                    if data.get("referenceAnalysisTag"):
                        text_parts.append(f"referenceAnalysisTag: {data['referenceAnalysisTag']}")
                    text_parts.append(f"\n{data['content']}")
                    return {
                        "content": [{"type": "text", "text": "\n".join(text_parts)}],
                        "structuredContent": data,
                    }
                if resp.status in (400, 404, 409):
                    data = await resp.json()
                    message = data.get("message", "加载语义方案失败")
                    return {
                        "content": [{"type": "text", "text": message}],
                        "structuredContent": data,
                        "is_error": True,
                    }
                error_text = await resp.text()
                return {
                    "content": [{"type": "text", "text": f"加载失败: {error_text}"}],
                    "is_error": True,
                }
        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }

    # ---------- load_reference_analysis ----------
    @builder.tool(
        "load_reference_analysis",
        "加载当前设计区的参考分析。默认返回最新标签；可选 tag 参数读取指定标签。",
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
                "zoneId": {
                    "type": "string",
                    "description": "目标 Zone ID，如 'rz_3'",
                },
                "tag": {
                    "type": "string",
                    "description": "可选。指定参考分析标签，如 'v1'；不传则返回最新标签。",
                },
            },
            "required": ["zoneId"],
            "additionalProperties": False,
        },
    )
    async def load_reference_analysis(args: dict[str, Any]) -> dict[str, Any]:
        zone_id = args["zoneId"]
        tag = args.get("tag")

        try:
            async with ctx.session.get(
                f"{ctx.server_url}/api/semantic-plan/{zone_id}/reference-analysis",
                params={"tag": tag} if tag else None,
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    text_parts = [
                        f"status: {data['status']}",
                        f"zoneId: {data['zoneId']}",
                        f"tag: {data['tag']}",
                        f"sourceImageId: {data.get('sourceImageId', '')}",
                        f"timestamp: {data['timestamp']}",
                        "",
                        data["content"],
                    ]
                    return {
                        "content": [{"type": "text", "text": "\n".join(text_parts)}],
                        "structuredContent": data,
                    }
                if resp.status in (400, 404):
                    data = await resp.json()
                    message = data.get("message", "加载参考分析失败")
                    return {
                        "content": [{"type": "text", "text": message}],
                        "structuredContent": data,
                        "is_error": True,
                    }
                error_text = await resp.text()
                return {
                    "content": [{"type": "text", "text": f"加载失败: {error_text}"}],
                    "is_error": True,
                }
        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }

    # ---------- save_reference_analysis ----------
    @builder.tool(
        "save_reference_analysis",
        "保存完整参考分析快照。在参考图分析各阶段完成后调用，提交当前标签的完整 Markdown 分析内容。",
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
                "zoneId": {
                    "type": "string",
                    "description": "目标 Zone ID，如 'rz_3'",
                },
                "sourceImageId": {
                    "type": "string",
                    "description": "参考图附件 ID（可选）",
                },
                "content": {
                    "type": "string",
                    "description": "参考分析内容（Markdown 格式），必须是当前阶段的完整、自包含快照",
                },
            },
            "required": ["zoneId", "content"],
            "additionalProperties": False,
        },
    )
    async def save_reference_analysis(args: dict[str, Any]) -> dict[str, Any]:
        zone_id = args["zoneId"]
        content = args["content"]

        body: dict[str, Any] = {
            "zoneId": zone_id,
            "sourceImageId": args.get("sourceImageId", ""),
            "content": content,
        }

        try:
            async with ctx.session.post(
                f"{ctx.server_url}/api/semantic-plan/save-reference-analysis",
                json=body,
            ) as resp:
                if resp.status == 200:
                    data = await resp.json()
                    tag_text = data.get("tag", "N/A")
                    return {
                        "content": [{
                            "type": "text",
                            "text": f"参考分析结果已保存为 {tag_text}。",
                        }],
                        "structuredContent": data,
                    }
                error_text = await resp.text()
                return {
                    "content": [{"type": "text", "text": f"保存失败: {error_text}"}],
                    "is_error": True,
                }
        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }

    # ---------- clone_scheme_to_variant ----------
    @builder.tool(
        "clone_scheme_to_variant",
        "克隆设计区方案（canonical 或某变体）到一个或多个新变体目录。"
        "**仅 module-relocation-agent 使用**——variant-design-agent / generate-placement 等其他 SubAgent 禁止调用。"
        "用途：relocation 工作流的入口，先 clone 出隔离的变体目录（含 semantic_plan + 全部叶子 modules.json），再在变体目录内局部修改。",
        {
            "$schema": "http://json-schema.org/draft-07/schema#",
            "type": "object",
            "properties": {
                "designZoneId": {
                    "type": "string",
                    "description": "设计区 ID，如 'rz_3'。",
                },
                "sourceVariant": {
                    "type": "string",
                    "description": "源变体；'canonical' 或省略表示从 canonical 克隆；也可填某个已存在 slug 实现链式克隆。",
                },
                "newVariantSlugs": {
                    "type": "array",
                    "items": {"type": "string"},
                    "description": "要创建的新变体 slug 列表（[a-zA-Z0-9_-]，单次请求批量原子创建）。",
                },
                "overwrite": {
                    "type": "boolean",
                    "description": "默认 false；为 true 时已存在的同名 slug 会被覆盖。",
                },
            },
            "required": ["designZoneId", "newVariantSlugs"],
            "additionalProperties": False,
        },
    )
    async def clone_scheme_to_variant(args: dict[str, Any]) -> dict[str, Any]:
        design_zone_id = args["designZoneId"]
        new_variant_slugs = args["newVariantSlugs"]
        source_variant = args.get("sourceVariant")
        overwrite = args.get("overwrite", False)

        body: dict[str, Any] = {
            "designZoneId": design_zone_id,
            "newVariantSlugs": new_variant_slugs,
            "overwrite": overwrite,
        }
        if source_variant:
            body["sourceVariant"] = source_variant

        try:
            async with ctx.session.post(
                f"{ctx.server_url}/api/scheme/variant/clone",
                json=body,
            ) as resp:
                text = await resp.text()
                if resp.status == 200:
                    return {"content": [{"type": "text", "text": text}]}
                return {
                    "content": [{"type": "text", "text": f"克隆失败: HTTP {resp.status} {text}"}],
                    "is_error": True,
                }
        except aiohttp.ClientError as e:
            return {
                "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
                "is_error": True,
            }
