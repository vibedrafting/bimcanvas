"""Web 截图 MCP 工具 - 请求前端截图"""
from typing import Any
import aiohttp

from ..decorators import mcp_tool

# Agent Server URL
SERVER_URL = "http://localhost:8765"


@mcp_tool()
async def request_screenshot(args: dict[str, Any]) -> dict[str, Any]:
    """
    请求 Web 端截图

    通过 Server API 通知 Web 端执行截图，并返回截图结果。
    支持截取整个画布或指定房间。

    Args:
        args: 包含以下字段的字典：
            - project_path: 项目路径（截图保存位置）
            - room_id: 房间 ID（可选，不传则截取整个画布）

    Returns:
        MCP 标准响应格式，包含：
        1. 图片数据（供多模态分析）
        2. 文件路径（留档）
    """
    project_path = args.get("project_path", ".")
    room_id = args.get("room_id")

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/screenshot/request",
                json={"projectPath": project_path, "roomId": room_id}
            ) as resp:
                result = await resp.json()

                if "error" in result:
                    return {
                        "content": [{"type": "text", "text": f"截图失败: {result['error']}"}],
                        "is_error": True
                    }

                # 返回图片 + 路径，支持多模态分析
                return {
                    "content": [
                        {
                            "type": "image",
                            "data": result["base64"],
                            "mimeType": "image/png"
                        },
                        {
                            "type": "text",
                            "text": f"截图已保存到: {result['path']}"
                        }
                    ]
                }

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }
