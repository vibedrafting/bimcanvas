"""BIMCanvas Agent entry point"""

import os
import sys

# Set git-bash path for Claude Agent SDK on Windows (must be before SDK imports)
if sys.platform == "win32":
    git_bash_paths = [
        r"D:\Git\bin\bash.exe",
        r"C:\Program Files\Git\bin\bash.exe",
        r"C:\Git\bin\bash.exe",
    ]
    for path in git_bash_paths:
        if os.path.exists(path):
            os.environ["CLAUDE_CODE_GIT_BASH_PATH"] = path
            break

import argparse
import asyncio
import logging

from .agent.main_agent import MainAgent
from .server.http_server import run_server

# Configure logging - 简化格式，时间戳由 Server 统一添加
class SimpleFormatter(logging.Formatter):
    """简化格式化器：添加 [Agent] 前缀（Server 级日志统一使用）"""
    def format(self, record):
        return f"[Agent] {record.getMessage()}"

handler = logging.StreamHandler(sys.stdout)
handler.setFormatter(SimpleFormatter())
logging.basicConfig(level=logging.INFO, handlers=[handler])

# 禁用 aiohttp HTTP 访问日志（减少控制台噪音）
logging.getLogger("aiohttp.access").setLevel(logging.WARNING)

logger = logging.getLogger(__name__)


def ensure_server_managed_startup() -> None:
    """Agent 只能由 Server 托管启动，禁止手工独立运行。"""
    if (
        os.getenv("BIMCANVAS_AGENT_MANAGED_BY_SERVER") == "1" and
        os.getenv("BIMCANVAS_SERVER_URL", "").strip()
    ):
        return

    raise RuntimeError(
        "BIMCanvas.Agent 不支持脱离 BIMCanvas.Server 独立启动。"
        " Agent 运行依赖 Server 提供的 MCP 与辅助服务。"
        " 请先启动 BIMCanvas.Server，由 Server 完成初始化并托管拉起 Agent。"
    )


async def interactive_mode(project_path: str = None) -> None:
    """
    Run the agent in interactive CLI mode.

    Args:
        project_path: Optional path to the project
    """
    print("=" * 50)
    print("BIMCanvas MainAgent - Interactive Mode")
    print("=" * 50)
    print("Type your message and press Enter to chat.")
    print("The agent will automatically dispatch tasks to SubAgents as needed.")
    print("Commands:")
    print("  /clear  - Clear conversation history")
    print("  /exit   - Exit the program")
    print("  /help   - Show this help")
    print("=" * 50)

    agent = MainAgent(project_path)

    try:
        while True:
            try:
                user_input = input("\nYou: ").strip()

                if not user_input:
                    continue

                # Handle commands
                if user_input.lower() == "/exit":
                    print("Goodbye!")
                    break

                if user_input.lower() == "/clear":
                    agent.clear_history()
                    print("Conversation history cleared.")
                    continue

                if user_input.lower() == "/help":
                    print("Commands:")
                    print("  /clear  - Clear conversation history")
                    print("  /exit   - Exit the program")
                    print("  /help   - Show this help")
                    continue

                # Regular chat (流式输出)
                print("\nAgent: ", end="", flush=True)
                async for chunk in agent.chat_stream(user_input):
                    # 修复：输出 chunk.content 而非 chunk 对象
                    if chunk.type in ("text", "text_complete"):
                        print(chunk.content, end="", flush=True)
                print()

            except KeyboardInterrupt:
                print("\n\nInterrupted. Goodbye!")
                break
            except Exception as e:
                logger.exception(f"Error: {e}")
                print(f"\nError: {e}")
    finally:
        # 确保连接清理
        await agent.disconnect()


def build_parser() -> argparse.ArgumentParser:
    """Build the CLI parser used by the server-managed host."""
    parser = argparse.ArgumentParser(
        description="BIMCanvas MainAgent - AI coordinator with SubAgent support"
    )

    parser.add_argument(
        "--serve",
        action="store_true",
        help="Run as HTTP server (for Web integration)"
    )

    parser.add_argument(
        "--host",
        type=str,
        default=None,
        help="Server host (default: 127.0.0.1)"
    )

    parser.add_argument(
        "--port",
        type=int,
        default=None,
        help="Server port (default: 8865)"
    )

    parser.add_argument(
        "--project",
        type=str,
        default=None,
        help="Path to the project directory"
    )

    parser.add_argument(
        "--launch-context",
        type=str,
        default=None,
        help="LaunchContext JSON file path (Server 注入);main.py 转写到 BIMCANVAS_LAUNCH_CONTEXT env",
    )

    parser.add_argument(
        "--managed-by-server",
        nargs="?",
        const="",
        default=None,
        help=argparse.SUPPRESS,
    )

    parser.add_argument(
        "--managed-agent-root",
        type=str,
        default=None,
        help=argparse.SUPPRESS,
    )

    parser.add_argument(
        "--managed-home",
        type=str,
        default=None,
        help=argparse.SUPPRESS,
    )

    return parser


def main(argv: list[str] | None = None) -> None:
    """Main entry point"""
    parser = build_parser()
    args = parser.parse_args(argv)

    if args.launch_context:
        os.environ["BIMCANVAS_LAUNCH_CONTEXT"] = args.launch_context

    try:
        ensure_server_managed_startup()

        if args.serve:
            # Run as HTTP server
            logger.info("启动 HTTP 服务模式...")
            run_server(host=args.host, port=args.port)
        else:
            # Run in interactive CLI mode
            logger.info("启动交互模式...")
            asyncio.run(interactive_mode(args.project))
    except (FileNotFoundError, RuntimeError) as ex:
        logger.error(str(ex))
        sys.exit(1)


if __name__ == "__main__":
    main()
