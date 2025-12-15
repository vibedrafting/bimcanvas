"""
MOSS - 基于 Anthropic SDK 的对话机器人（支持流式输出 + 思考过程）

运行前设置环境变量:
  Windows: $env:ANTHROPIC_API_KEY = "your-key"
  Linux:   export ANTHROPIC_API_KEY=your-key
"""

import anthropic

# 可用模型列表（支持 Extended Thinking）
AVAILABLE_MODELS = {
    "sonnet": ("claude-sonnet-4-5-20250929", "Claude Sonnet 4.5 (平衡)"),
    "opus": ("claude-opus-4-5-20251101", "Claude Opus 4.5 (最强)"),
    "haiku": ("claude-haiku-4-5-20251001", "Claude Haiku 4.5 (最快)"),
}

# 当前模型
current_model = "sonnet"

# 思考过程 token 预算
thinking_budget = 10000

# Anthropic 客户端
client = anthropic.Anthropic()


def chat_stream(user_input: str):
    """流式对话，支持思考过程"""
    model_id, _ = AVAILABLE_MODELS[current_model]

    with client.messages.stream(
        model=model_id,
        max_tokens=16000,
        thinking={"type": "enabled", "budget_tokens": thinking_budget},
        system="你是 MOSS，一个友好的 AI 助手。请用简洁的中文回答。",
        messages=[{"role": "user", "content": user_input}],
    ) as stream:
        in_thinking = False
        in_response = False

        for event in stream:
            if event.type == "content_block_start":
                block_type = getattr(event.content_block, "type", None)
                if block_type == "thinking":
                    in_thinking = True
                    in_response = False
                    print("\n\033[90m[思考中]\033[0m ", end="", flush=True)
                elif block_type == "text":
                    in_thinking = False
                    in_response = True
                    print("\n\n\033[1mMOSS:\033[0m ", end="", flush=True)

            elif event.type == "content_block_delta":
                delta = event.delta
                if hasattr(delta, "thinking") and delta.thinking:
                    # 思考过程（灰色）
                    print(f"\033[90m{delta.thinking}\033[0m", end="", flush=True)
                elif hasattr(delta, "text") and delta.text:
                    # 正常回复
                    print(delta.text, end="", flush=True)

            elif event.type == "content_block_stop":
                pass

        print()  # 最后换行


def show_help():
    """显示帮助信息"""
    print("\n可用命令:")
    print("  /model          - 查看当前模型")
    print("  /model <名称>   - 切换模型 (sonnet/opus/haiku)")
    print("  /models         - 列出所有可用模型")
    print("  /budget         - 查看思考预算")
    print("  /budget <数值>  - 设置思考 token 预算")
    print("  /help           - 显示此帮助")
    print("  exit/quit/q     - 退出程序")


def show_models():
    """显示所有可用模型"""
    print("\n可用模型:")
    for name, (model_id, desc) in AVAILABLE_MODELS.items():
        marker = " <-- 当前" if name == current_model else ""
        print(f"  {name}: {desc}{marker}")


def switch_model(model_name: str) -> bool:
    """切换模型"""
    global current_model
    model_name = model_name.lower().strip()

    if model_name in AVAILABLE_MODELS:
        current_model = model_name
        _, desc = AVAILABLE_MODELS[model_name]
        print(f"\n已切换到: {desc}")
        return True
    else:
        print(f"\n未知模型: {model_name}")
        print(f"可用模型: {', '.join(AVAILABLE_MODELS.keys())}")
        return False


def set_budget(value: str) -> bool:
    """设置思考预算"""
    global thinking_budget
    try:
        budget = int(value.strip())
        if budget < 1024:
            print("\n思考预算最小为 1024")
            return False
        thinking_budget = budget
        print(f"\n思考预算已设置为: {thinking_budget} tokens")
        return True
    except ValueError:
        print(f"\n无效数值: {value}")
        return False


def main():
    print("=" * 55)
    print("  MOSS - AI 对话助手 (Extended Thinking)")
    print(f"  模型: {current_model} | 思考预算: {thinking_budget} tokens")
    print("  输入 /help 查看命令, exit 退出")
    print("=" * 55)

    while True:
        try:
            user_input = input("\n你: ").strip()

            if not user_input:
                continue

            # 退出命令
            if user_input.lower() in ['exit', 'quit', 'q']:
                print("\nMOSS: 再见！")
                break

            # 处理斜杠命令
            if user_input.startswith('/'):
                parts = user_input[1:].split(maxsplit=1)
                cmd = parts[0].lower()

                if cmd == 'help':
                    show_help()
                elif cmd == 'models':
                    show_models()
                elif cmd == 'model':
                    if len(parts) > 1:
                        switch_model(parts[1])
                    else:
                        _, desc = AVAILABLE_MODELS[current_model]
                        print(f"\n当前模型: {current_model} - {desc}")
                elif cmd == 'budget':
                    if len(parts) > 1:
                        set_budget(parts[1])
                    else:
                        print(f"\n当前思考预算: {thinking_budget} tokens")
                else:
                    print(f"\n未知命令: /{cmd}")
                    show_help()
                continue

            # 正常对话（流式 + 思考）
            chat_stream(user_input)

        except KeyboardInterrupt:
            print("\n\nMOSS: 再见！")
            break
        except anthropic.APIError as e:
            print(f"\nAPI 错误: {e.message}")
        except Exception as e:
            print(f"\n错误: {e}")


if __name__ == "__main__":
    main()
