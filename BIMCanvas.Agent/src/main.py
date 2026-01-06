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

from .agent.placement_agent import PlacementAgent
from .server.http_server import run_server
from .config.settings import get_settings

# Configure logging
logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(name)s: %(message)s",
    handlers=[logging.StreamHandler(sys.stdout)]
)

logger = logging.getLogger(__name__)


async def interactive_mode(project_path: str = None) -> None:
    """
    Run the agent in interactive CLI mode.

    Args:
        project_path: Optional path to the project
    """
    print("=" * 50)
    print("BIMCanvas PlacementAgent - Interactive Mode")
    print("=" * 50)
    print("Type your message and press Enter to chat.")
    print("Commands:")
    print("  /clear  - Clear conversation history")
    print("  /exit   - Exit the program")
    print("  /help   - Show this help")
    print("=" * 50)

    agent = PlacementAgent(project_path)

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


def main() -> None:
    """Main entry point"""
    parser = argparse.ArgumentParser(
        description="BIMCanvas PlacementAgent - AI-powered interior layout assistant"
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
        help="Server port (default: 8765)"
    )

    parser.add_argument(
        "--project",
        type=str,
        default=None,
        help="Path to the project directory"
    )

    args = parser.parse_args()

    if args.serve:
        # Run as HTTP server
        logger.info("Starting in HTTP server mode...")
        run_server(host=args.host, port=args.port)
    else:
        # Run in interactive CLI mode
        logger.info("Starting in interactive mode...")
        asyncio.run(interactive_mode(args.project))


if __name__ == "__main__":
    main()
