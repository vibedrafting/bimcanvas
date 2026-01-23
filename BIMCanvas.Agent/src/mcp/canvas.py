"""Canvas MCP Server - BIMCanvas 画布操作工具

按 Calculator MCP 模式重构，直接使用 @tool 装饰器，避免复杂的动态发现机制。
"""

from typing import Any
import json
import aiohttp
from claude_agent_sdk import tool, create_sdk_mcp_server

SERVER_URL = "http://localhost:5000"


def parse_names_param(raw: Any) -> list[str]:
    """解析 names 参数，兼容多种输入格式"""
    if isinstance(raw, list):
        return [str(n).strip() for n in raw if n]

    if not isinstance(raw, str) or not raw.strip():
        return []

    raw = raw.strip()

    # 尝试 JSON 解析
    if raw.startswith("["):
        try:
            parsed = json.loads(raw)
            if isinstance(parsed, list):
                return [str(n).strip() for n in parsed if n]
        except json.JSONDecodeError:
            pass

    # 逗号分隔
    return [n.strip() for n in raw.split(",") if n.strip()]


@tool("create_job", "批量创建隔离工作环境（Git Worktree）。参数 count: 创建个数（默认1，最大10）", {"count": int})
async def ai_job_create(args: dict[str, Any]) -> dict[str, Any]:
    """创建独立的 Git Worktree，让 SubAgent 在隔离环境中执行修改。"""
    count = args.get("count", 1)

    # 参数验证
    if not isinstance(count, int) or count < 1 or count > 10:
        return {
            "content": [{"type": "text", "text": "错误: count 必须在 1-10 之间"}],
            "is_error": True
        }

    results = []
    try:
        async with aiohttp.ClientSession() as session:
            for i in range(count):
                async with session.post(
                    f"{SERVER_URL}/api/git/ai-job",
                    json={}  # 空 body，Server 自动生成 name 和 baseBranch
                ) as resp:
                    if resp.status == 200:
                        data = await resp.json()
                        results.append({
                            "name": data.get("name", "?"),
                            "path": data.get("worktreePath", "?"),
                            "branch": data.get("branchName", "?")
                        })
                    else:
                        # 部分失败处理
                        try:
                            error_data = await resp.json()
                            error_msg = error_data.get("message", "未知错误")
                        except:
                            error_msg = await resp.text()
                        results.append({"error": error_msg})

        # 格式化输出
        success_count = len([r for r in results if "error" not in r])

        if success_count == 0:
            # 全部失败
            error_msgs = [r.get("error", "未知错误") for r in results]
            return {
                "content": [{"type": "text", "text": f"创建隔离环境失败:\n" + "\n".join(error_msgs)}],
                "is_error": True
            }

        # 构建成功输出
        output_lines = [f"创建 {success_count}/{count} 个隔离环境:"]
        for r in results:
            if "error" not in r:
                output_lines.append(f"- {r['name']}: {r['path']} (分支: {r['branch']})")
            else:
                output_lines.append(f"- [失败]: {r['error']}")

        output_lines.append("")
        output_lines.append("SubAgent 应在对应目录下执行文件修改。")

        return {"content": [{"type": "text", "text": "\n".join(output_lines)}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


@tool("complete_job", "批量通知 Web 端：指定的 AI Job 已完成。参数 names: 逗号分隔的名称列表（如 'job-1,job-2'）; summary: 修改总结（可选）", {"names": str, "summary": str})
async def ai_job_complete(args: dict[str, Any]) -> dict[str, Any]:
    """Web 端收到通知后，会打开 diff/merge 可视化界面。"""
    raw_names = args.get("names", "")
    summary = args.get("summary", "")

    # 解析 names（兼容逗号分隔和 JSON 数组）
    names_list = parse_names_param(raw_names)

    if not names_list:
        return {
            "content": [{"type": "text", "text": "错误: 必须指定 names（AI Job 名称列表，逗号分隔）"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            # 1. 调用 Server 标记完成
            async with session.post(
                f"{SERVER_URL}/api/git/ai-jobs/complete",
                json={"names": names_list}
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"批量标记完成失败: {error_data.get('message', '未知错误')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                jobs = result.get("jobs", [])

            # 2. 发送弹窗通知到 Web 端
            notification_message = f"完成 {len(names_list)} 个任务: {', '.join(names_list)}"
            if summary:
                notification_message += f"\n\n{summary}"

            try:
                async with session.post(
                    f"{SERVER_URL}/api/notification/agent",
                    json={
                        "title": "AI Job 已完成",
                        "message": notification_message,
                        "type": "success"
                    }
                ) as notify_resp:
                    if notify_resp.status != 200:
                        # 通知失败不影响主流程，只记录警告
                        pass
            except Exception:
                # 通知失败不影响主流程
                pass

            job_lines = []
            for job in jobs:
                job_lines.append(f"  - {job.get('name')}: {job.get('branchName')} ({job.get('status')})")

            text = f"""AI Jobs 已标记完成 ({len(jobs)} 个):
{chr(10).join(job_lines)}

用户将在 Web 端看到 diff 预览，并决定是否合并这些修改。"""

            return {"content": [{"type": "text", "text": text}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"无法连接 Server: {str(e)}"}],
            "is_error": True
        }


# 创建 Canvas MCP Server
canvas_mcp = create_sdk_mcp_server(
    name="canvas",
    version="1.0.0",
    tools=[ai_job_create, ai_job_complete],
)

# 预批准工具列表
CANVAS_ALLOWED_TOOLS = [
    "mcp__canvas__create_job",
    "mcp__canvas__complete_job",
]
