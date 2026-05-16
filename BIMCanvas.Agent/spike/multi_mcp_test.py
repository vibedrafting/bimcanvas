"""SDK 多 MCP server spike

主真理源 v1.1 §3.8 + 组1 模板 §4.1 + R2/§6.1:本 spike 验证 claude-agent-sdk 0.1.41 下
``ClaudeAgentOptions(mcp_servers={"a": server_a, "b": server_b})`` 这种 multi-server dict
形态是否真的能让 LLM 发现并调用两个 namespace 下的工具(``mcp__a__echo`` /
``mcp__b__echo``),以及一个 server 的工具运行时抛异常时另一个 server 是否仍能正常服务。

实验
====

E1 双 server 工具发现 + 调用
    挂 ``server_a`` + ``server_b``,各暴露最小 echo 工具,prompt 强制 LLM 依次调用
    ``mcp__a__echo`` 与 ``mcp__b__echo``,确认两个工具都进入 LLM 可见工具列表
    且都被实际调用,返回 payload 中能用 ``[A]`` / ``[B]`` 前缀区分。

E2 工具运行时异常隔离
    ``server_a`` 的 echo 工具被替换为"抛 RuntimeError 的版本",``server_b`` 保持正常;
    prompt 让 LLM 必须依次调两个工具,确认 ``server_a`` 工具报错后 ``server_b``
    工具仍能被 LLM 调用并返回结果。这是后续 fallback 决策(主真理源 §6.1 R2)
    的必要依据 —— 若 ``server_a`` 的工具异常会拖垮整个 ``mcp_servers`` dict,
    将改用单 server + 工具名前缀方案。

实验结论由指挥部根据本 spike 跑后的 stdout 回填到主真理源 / v1.2 计划。本文件只交付
可执行代码 + ``README.md`` 运行说明,不包含任何业务知识 / 项目私有内容。
"""

from __future__ import annotations

import asyncio
import os
import sys
import traceback
from typing import Any

from claude_agent_sdk import (
    ClaudeAgentOptions,
    ClaudeSDKClient,
    create_sdk_mcp_server,
    tool,
)


# ---------------------------------------------------------------------------
# 工具定义:两个独立 namespace 的最小 echo 工具,不涉及任何业务知识
# ---------------------------------------------------------------------------

_ECHO_INPUT_SCHEMA = {
    "$schema": "http://json-schema.org/draft-07/schema#",
    "type": "object",
    "properties": {
        "text": {"type": "string", "description": "要原样回显的内容"},
    },
    "required": ["text"],
    "additionalProperties": False,
}


@tool("echo", "Server A 的 echo 工具:返回 '[A] <text>'", _ECHO_INPUT_SCHEMA)
async def echo_a_ok(args: dict[str, Any]) -> dict[str, Any]:
    text = args.get("text", "")
    return {"content": [{"type": "text", "text": f"[A] {text}"}]}


@tool("echo", "Server A 的 echo 工具:故意抛 RuntimeError,用于 spike E2 异常隔离验证", _ECHO_INPUT_SCHEMA)
async def echo_a_fail(args: dict[str, Any]) -> dict[str, Any]:
    raise RuntimeError("intentional failure in server_a echo (spike E2)")


@tool("echo", "Server B 的 echo 工具:返回 '[B] <text>'", _ECHO_INPUT_SCHEMA)
async def echo_b_ok(args: dict[str, Any]) -> dict[str, Any]:
    text = args.get("text", "")
    return {"content": [{"type": "text", "text": f"[B] {text}"}]}


# ---------------------------------------------------------------------------
# experiment runner
# ---------------------------------------------------------------------------

async def run_experiment(label: str, prompt: str, server_a: Any, server_b: Any) -> None:
    """单个子实验入口。

    异常向外抛,由 ``main`` 决定是否记 fail 并继续下一个实验。
    """
    print(f"\n========== {label} ==========", flush=True)
    options = ClaudeAgentOptions(
        mcp_servers={"a": server_a, "b": server_b},
        allowed_tools=["mcp__a__echo", "mcp__b__echo"],
        permission_mode="acceptEdits",
        max_turns=4,
        setting_sources=None,
    )

    async with ClaudeSDKClient(options=options) as client:
        await client.query(prompt)
        async for message in client.receive_response():
            # 详细 repr 便于后续 grep "mcp__a__echo" / "mcp__b__echo" 工具调用记录
            print(repr(message), flush=True)


def _has_llm_credentials() -> bool:
    return bool(
        os.environ.get("ANTHROPIC_API_KEY")
        or os.environ.get("ANTHROPIC_AUTH_TOKEN")
    )


async def main() -> int:
    if not _has_llm_credentials():
        print(
            "[fatal] 缺少 ANTHROPIC_API_KEY 或 ANTHROPIC_AUTH_TOKEN 环境变量,无法连接 LLM。\n"
            "        参见 BIMCanvas.Agent/spike/README.md「环境准备」章节。",
            file=sys.stderr,
        )
        return 2

    overall_exit = 0

    # --- E1 ---
    server_a_ok_e1 = create_sdk_mcp_server(name="a", version="0.0.1", tools=[echo_a_ok])
    server_b_ok_e1 = create_sdk_mcp_server(name="b", version="0.0.1", tools=[echo_b_ok])
    try:
        await run_experiment(
            label="E1 双 server 工具发现 + 调用",
            prompt=(
                "请依次调用以下两个 MCP 工具并把两次返回拼成一段简短文字:\n"
                "  1) 调用 mcp__a__echo,参数 text=\"hello-from-a\"\n"
                "  2) 调用 mcp__b__echo,参数 text=\"hello-from-b\"\n"
                "两次调用都完成后,用一句话告诉我你看到的两个返回值各是什么。"
            ),
            server_a=server_a_ok_e1,
            server_b=server_b_ok_e1,
        )
        print("\n[E1] DONE - 由指挥部根据 stdout 判定两个工具是否都被调到。", flush=True)
    except Exception as exc:  # noqa: BLE001 — spike 需要把所有异常打全
        print(f"\n[E1] FAILED: {type(exc).__name__}: {exc}", file=sys.stderr)
        traceback.print_exc()
        overall_exit = 1

    # --- E2 ---
    server_a_fail_e2 = create_sdk_mcp_server(name="a", version="0.0.1", tools=[echo_a_fail])
    server_b_ok_e2 = create_sdk_mcp_server(name="b", version="0.0.1", tools=[echo_b_ok])
    try:
        await run_experiment(
            label="E2 server_a 工具运行时异常 → server_b 仍可调用",
            prompt=(
                "请按以下顺序调用工具,不论调用结果如何都要把两次调用都跑完:\n"
                "  1) 调用 mcp__a__echo,参数 text=\"will-fail\";如返回错误也继续下一步,不要终止\n"
                "  2) 调用 mcp__b__echo,参数 text=\"should-still-work\"\n"
                "两次调用都完成后,用一句话同时报告两次调用各自的结果(成功/失败 + 返回值)。"
            ),
            server_a=server_a_fail_e2,
            server_b=server_b_ok_e2,
        )
        print(
            "\n[E2] DONE - 由指挥部根据 stdout 判定:\n"
            "    (1) mcp__a__echo 是否如预期返回 error / 工具调用被标记失败;\n"
            "    (2) mcp__b__echo 是否随后仍被 LLM 成功调用并返回 [B] should-still-work。",
            flush=True,
        )
    except Exception as exc:  # noqa: BLE001
        print(f"\n[E2] FAILED: {type(exc).__name__}: {exc}", file=sys.stderr)
        traceback.print_exc()
        overall_exit = 1

    # --- 结论模板(指挥部回填) ---
    print(
        "\n========== 结论模板(指挥部填写) ==========\n"
        "E1 result: [ PASS / FAIL ] - 是否两个 mcp__*__echo 工具都被调到,返回值能区分 [A]/[B]?\n"
        "E2 result: [ PASS / FAIL ] - server_a 工具异常后 server_b 工具是否仍被成功调用?\n"
        "\n影响:\n"
        "  E1 + E2 同时 PASS  → 组 3 采用 multi-MCP-server dict 实现路线\n"
        "  任一 FAIL          → 组 3 fallback 到单 server + 工具名前缀方案\n"
        "                       (主真理源 §6.1 R2),manifest 的 mcpNamespace 字段\n"
        "                       语义需要相应调整,JSONSchema(组 1 Step 3)需重审。",
        flush=True,
    )

    return overall_exit


if __name__ == "__main__":
    sys.exit(asyncio.run(main()))
