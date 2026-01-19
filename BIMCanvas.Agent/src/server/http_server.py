"""HTTP Server for Web integration using aiohttp"""

import asyncio
import base64
import json
import logging
import uuid
from datetime import datetime
from pathlib import Path
from typing import Any

from aiohttp import web
import aiohttp_cors

from ..agent.main_agent import MainAgent
from ..config.settings import get_settings
from ..config.loader import ConfigLoader

# Configure logging
logger = logging.getLogger(__name__)

# Global agent instances (cached by window ID for multi-window parallel support)
agents: dict[str, MainAgent] = {}  # windowId → Agent
_agents_lock = asyncio.Lock()

# 窗口序号管理（日志前缀用）
_window_counter = 1  # 从 1 开始，primary 不占用（序号 0）
_window_seq_map: dict[str, int] = {}  # windowId → 序号


def _get_window_prefix(window_seq: int) -> str:
    """获取窗口日志前缀"""
    if window_seq == 0:
        return "[Agent]"
    else:
        return f"[Agent#{window_seq}]"

# Screenshot request management
_screenshot_requests: dict[str, asyncio.Future] = {}
_screenshot_sse_queues: list[asyncio.Queue] = []


async def get_agent(
    window_id: str,
    project_path: str,
    worktree_path: str = None
) -> MainAgent:
    """
    获取或创建窗口专属的 Agent 实例

    Args:
        window_id: 窗口唯一标识
        project_path: 项目根目录
        worktree_path: 实际工作目录（虚拟窗口的 Worktree 路径）

    Returns:
        MainAgent instance
    """
    global _window_counter

    async with _agents_lock:
        if window_id not in agents:
            # 分配窗口序号（primary=0，其他递增）
            if window_id == "primary":
                seq = 0
            else:
                seq = _window_counter
                _window_counter += 1
            _window_seq_map[window_id] = seq

            working_dir = worktree_path or project_path
            agent = MainAgent(project_path, working_directory=working_dir, window_seq=seq)
            await agent.connect()  # 预连接
            agents[window_id] = agent

            # 醒目的控制台输出（带窗口前缀）
            prefix = _get_window_prefix(seq)
            print(f"{prefix} [Server] ========== Agent 实例创建 ==========")
            print(f"{prefix} [Server] 窗口ID: {window_id}")
            print(f"{prefix} [Server] 窗口序号: {seq}")
            print(f"{prefix} [Server] 项目路径: {project_path}")
            print(f"{prefix} [Server] 工作目录: {working_dir}")
            print(f"{prefix} [Server] 当前实例数: {len(agents)}")
            print(f"{prefix} [Server] =====================================")
            logger.info(f"Created agent for window: {window_id} (seq={seq}), working_dir: {working_dir}")

        return agents[window_id]


async def cleanup_agents() -> None:
    """清理所有 Agent 连接（shutdown 时调用）"""
    async with _agents_lock:
        if agents:
            print(f"[Server] ========== 清理所有 Agent ==========")
            print(f"[Server] 待清理实例数: {len(agents)}")
        for cache_key, agent in agents.items():
            try:
                await agent.disconnect()
                print(f"[Server] 已断开: {cache_key}")
                logger.info(f"Disconnected agent: {cache_key}")
            except Exception as e:
                print(f"[Server] 断开失败: {cache_key} - {e}")
                logger.error(f"Error disconnecting agent {cache_key}: {e}")
        if agents:
            print(f"[Server] =====================================")
        agents.clear()


async def health_handler(request: web.Request) -> web.Response:
    """Health check endpoint"""
    return web.json_response({
        "status": "ok",
        "service": "bimcanvas-agent",
        "version": "0.1.0"
    })


async def config_handler(request: web.Request) -> web.Response:
    """
    Get default configuration for Web client.

    Response:
        {
            "model": "claude-sonnet-4-20250514",
            "thinkingLevel": "medium"
        }
    """
    settings = get_settings()
    return web.json_response({
        "model": settings.model_name,
        "thinkingLevel": settings.thinking.default_level
    })


def _get_web_config_path() -> Path:
    """获取 Web 配置文件路径"""
    return ConfigLoader.DEFAULT_CONFIG_DIR / "web_config.json"


def _load_web_config() -> dict:
    """加载 Web 配置文件"""
    config_path = _get_web_config_path()
    if config_path.exists():
        try:
            with open(config_path, 'r', encoding='utf-8') as f:
                return json.load(f)
        except Exception as e:
            logger.warning(f"Failed to load web config: {e}")
    return {"customModels": []}


def _save_web_config(config: dict) -> None:
    """保存 Web 配置文件"""
    config_path = _get_web_config_path()
    config_path.parent.mkdir(parents=True, exist_ok=True)
    with open(config_path, 'w', encoding='utf-8') as f:
        json.dump(config, f, ensure_ascii=False, indent=2)


async def web_config_get_handler(request: web.Request) -> web.Response:
    """
    Get Web client configuration (custom models, etc.)

    Response:
        {
            "customModels": [
                { "id": "model-id", "label": "Model Label" }
            ]
        }
    """
    config = _load_web_config()
    return web.json_response(config)


async def web_config_save_handler(request: web.Request) -> web.Response:
    """
    Save Web client configuration.

    Request body:
        {
            "customModels": [
                { "id": "model-id", "label": "Model Label" }
            ]
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    # 验证数据结构
    custom_models = data.get("customModels", [])
    if not isinstance(custom_models, list):
        return web.json_response({"error": "customModels must be an array"}, status=400)

    # 保存配置
    config = {"customModels": custom_models}
    try:
        _save_web_config(config)
        logger.info(f"Web config saved: {len(custom_models)} custom models")
        return web.json_response({"success": True})
    except Exception as e:
        logger.error(f"Failed to save web config: {e}")
        return web.json_response({"error": str(e)}, status=500)


async def chat_handler(request: web.Request) -> web.Response:
    """
    Handle chat requests.

    Request body:
        {
            "projectPath": "path/to/project",  // optional
            "windowId": "window-1",            // 窗口唯一标识
            "worktreePath": null,              // optional, 工作目录
            "message": "user message"
        }

    Response:
        {
            "reply": "AI response",
            "projectPath": "path/to/project"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response(
            {"error": "Invalid JSON"},
            status=400
        )

    project_path = data.get("projectPath", "")
    window_id = data.get("windowId", "primary")
    worktree_path = data.get("worktreePath")
    message = data.get("message", "")

    if not message:
        return web.json_response(
            {"error": "Message cannot be empty"},
            status=400
        )

    try:
        agent = await get_agent(window_id, project_path, worktree_path)  # 按窗口获取
        reply = await agent.chat(message)

        return web.json_response({
            "reply": reply,
            "projectPath": project_path
        })

    except Exception as e:
        logger.exception(f"Chat error: {e}")
        return web.json_response(
            {"error": str(e)},
            status=500
        )


async def chat_stream_handler(request: web.Request) -> web.StreamResponse:
    """
    Handle streaming chat requests using Server-Sent Events.

    Request body:
        {
            "projectPath": "path/to/project",
            "windowId": "window-1",               // 窗口唯一标识（必填）
            "worktreePath": null,                 // optional, 工作目录（虚拟窗口的 Worktree 路径）
            "message": "user message",
            "images": ["base64..."],              // optional, 图片附件列表
            "model": "claude-sonnet-4-20250514",  // optional, 动态切换模型
            "thinkingLevel": "high"               // optional, 思考强度 (off/low/medium/high)
        }

    Response: SSE stream with chunks
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response(
            {"error": "Invalid JSON"},
            status=400
        )

    project_path = data.get("projectPath", "")
    window_id = data.get("windowId", "primary")       # 窗口ID，默认 primary
    worktree_path = data.get("worktreePath")          # 工作目录
    message = data.get("message", "")
    images = data.get("images", [])        # 图片附件列表
    model = data.get("model")              # 模型名称
    thinking_level = data.get("thinkingLevel")  # 思考强度

    # 调试日志：记录收到的请求
    logger.info(f"[chat_stream] Received request: windowId={window_id}, projectPath={project_path[:50] if project_path else 'None'}")

    # 空字符串保护
    if not window_id:
        window_id = "primary"
        logger.warning("[chat_stream] Empty windowId received, using default 'primary'")

    if not message:
        return web.json_response(
            {"error": "Message cannot be empty"},
            status=400
        )

    # Set up SSE response
    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        }
    )
    await response.prepare(request)

    try:
        agent = await get_agent(window_id, project_path, worktree_path)  # 按窗口获取

        # 动态切换模型（仅当模型实际变化时）
        if model and model != agent.get_current_model():
            await agent.set_model(model)

        async for chunk in agent.chat_stream(message, images=images, thinking_level=thinking_level):
            # 构建 SSE 事件数据
            event_data = {"type": chunk.type}

            # 添加基础内容字段
            if chunk.content:
                event_data["content"] = chunk.content

            # SubAgent 事件字段
            if chunk.subagent_id:
                event_data["subAgentId"] = chunk.subagent_id
            if chunk.subagent_name:
                event_data["subAgentName"] = chunk.subagent_name
            if chunk.subagent_type:
                event_data["subAgentType"] = chunk.subagent_type

            # ToolCall 事件字段
            if chunk.tool_call_id:
                event_data["toolCallId"] = chunk.tool_call_id
            if chunk.tool_name:
                event_data["toolName"] = chunk.tool_name
            if chunk.tool_description:
                event_data["toolDescription"] = chunk.tool_description
            if chunk.tool_params:
                event_data["toolParams"] = chunk.tool_params
            if chunk.tool_output:
                event_data["toolOutput"] = chunk.tool_output

            # 状态字段
            if chunk.success is not None:
                event_data["success"] = chunk.success
            if chunk.error:
                event_data["error"] = chunk.error

            # 错误分类字段
            if chunk.error_type:
                event_data["errorType"] = chunk.error_type
            if chunk.error_content:
                event_data["errorContent"] = chunk.error_content
            if chunk.hidden_content:
                event_data["hiddenContent"] = chunk.hidden_content

            # TaskOutput 事件字段
            if chunk.task_id:
                event_data["taskId"] = chunk.task_id
            if chunk.timeout is not None:
                event_data["timeout"] = chunk.timeout

            await response.write(f"data: {json.dumps(event_data, ensure_ascii=False)}\n\n".encode("utf-8"))

        # Send done event
        await response.write(b"data: [DONE]\n\n")

    except Exception as e:
        logger.exception(f"Stream error: {e}")
        error_data = json.dumps({"error": str(e)}, ensure_ascii=False)
        await response.write(f"data: {error_data}\n\n".encode("utf-8"))

    return response


async def clear_history_handler(request: web.Request) -> web.Response:
    """
    Clear conversation history for a window.

    Request body:
        {
            "windowId": "window-1"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response(
            {"error": "Invalid JSON"},
            status=400
        )

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    if window_id in agents:
        agents[window_id].clear_history()
        logger.info(f"Cleared history for window: {window_id}")

    return web.json_response({"success": True})


async def get_history_handler(request: web.Request) -> web.Response:
    """
    Get conversation history for a window.

    Query params:
        windowId: window identifier
    """
    window_id = request.query.get("windowId", "primary")

    if window_id not in agents:
        return web.json_response({
            "history": [],
            "windowId": window_id
        })

    history = agents[window_id].get_history()

    return web.json_response({
        "history": history,
        "windowId": window_id
    })


async def interrupt_handler(request: web.Request) -> web.Response:
    """
    中断当前任务

    Request body:
        {
            "windowId": "window-1"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response(
            {"error": "Invalid JSON"},
            status=400
        )

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    async with _agents_lock:
        if window_id in agents:
            await agents[window_id].interrupt()
            print(f"[Server] 任务中断: 窗口 {window_id}")
            logger.info(f"Interrupted task for window: {window_id}")
            return web.json_response({"success": True})

    return web.json_response({"error": "Agent not found"}, status=404)


async def close_agent_handler(request: web.Request) -> web.Response:
    """
    关闭指定窗口的 Agent（窗口关闭时调用）

    Request body:
        {
            "windowId": "window-1"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    window_id = data.get("windowId")
    if not window_id:
        return web.json_response({"error": "windowId required"}, status=400)

    async with _agents_lock:
        if window_id in agents:
            # 获取窗口序号（用于日志前缀）
            seq = _window_seq_map.get(window_id, 0)
            prefix = _get_window_prefix(seq)

            await agents[window_id].disconnect()
            del agents[window_id]
            # 清理序号映射（但不回收序号）
            _window_seq_map.pop(window_id, None)

            # 醒目的控制台输出（带窗口前缀）
            print(f"{prefix} [Server] ========== Agent 实例关闭 ==========")
            print(f"{prefix} [Server] 窗口ID: {window_id}")
            print(f"{prefix} [Server] 窗口序号: {seq}")
            print(f"{prefix} [Server] 剩余实例数: {len(agents)}")
            print(f"{prefix} [Server] =====================================")
            logger.info(f"Closed agent for window: {window_id} (seq={seq})")
            return web.json_response({"success": True})

    return web.json_response({"error": "Agent not found"}, status=404)


# ============== Screenshot API ==============

async def screenshot_events_handler(request: web.Request) -> web.StreamResponse:
    """
    SSE 端点：Web 端监听 Agent 截图请求

    Web 端连接此端点后，当 Agent 调用截图 MCP 工具时，
    会收到 screenshot_request 事件，需要执行截图并提交结果。
    """
    response = web.StreamResponse(
        status=200,
        headers={
            "Content-Type": "text/event-stream",
            "Cache-Control": "no-cache",
            "Connection": "keep-alive",
        }
    )
    await response.prepare(request)

    queue: asyncio.Queue = asyncio.Queue()
    _screenshot_sse_queues.append(queue)
    logger.info(f"Screenshot SSE client connected, total: {len(_screenshot_sse_queues)}")

    try:
        while True:
            event = await queue.get()
            event_str = json.dumps(event, ensure_ascii=False)
            await response.write(f"event: screenshot_request\ndata: {event_str}\n\n".encode("utf-8"))
    except asyncio.CancelledError:
        pass
    finally:
        _screenshot_sse_queues.remove(queue)
        logger.info(f"Screenshot SSE client disconnected, remaining: {len(_screenshot_sse_queues)}")

    return response


async def screenshot_request_handler(request: web.Request) -> web.Response:
    """
    Agent 请求截图 → 通知 Web 端

    Request body:
        {
            "projectPath": "path/to/project",
            "roomId": "room_001"  // optional, 不传则截取整个画布
        }

    Response:
        {
            "path": "screenshots/canvas_20260113_150000.png",
            "base64": "iVBORw0KGgo..."  // 纯 base64 数据
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        data = {}

    room_id = data.get("roomId")
    project_path = data.get("projectPath", ".")
    request_id = str(uuid.uuid4())

    # 检查是否有 Web 客户端连接
    if not _screenshot_sse_queues:
        return web.json_response(
            {"error": "No Web client connected for screenshot"},
            status=503
        )

    # 创建等待 Future
    loop = asyncio.get_event_loop()
    future: asyncio.Future = loop.create_future()
    _screenshot_requests[request_id] = future

    # 广播给所有 SSE 客户端
    event = {"requestId": request_id, "roomId": room_id}
    for queue in _screenshot_sse_queues:
        await queue.put(event)

    logger.info(f"Screenshot request sent: {request_id}, roomId={room_id}")

    try:
        # 等待 Web 端返回（10秒超时）
        result = await asyncio.wait_for(future, timeout=10.0)

        if result.get("error"):
            return web.json_response({"error": result["error"]}, status=400)

        # 保存图片并返回 path + base64
        image_data = result["imageData"]
        filepath, pure_base64 = _save_screenshot(image_data, project_path, room_id)

        logger.info(f"Screenshot saved: {filepath}")
        return web.json_response({
            "path": filepath,
            "base64": pure_base64
        })

    except asyncio.TimeoutError:
        logger.warning(f"Screenshot request timeout: {request_id}")
        return web.json_response({"error": "Screenshot request timeout"}, status=504)
    finally:
        _screenshot_requests.pop(request_id, None)


async def screenshot_result_handler(request: web.Request) -> web.Response:
    """
    Web 端返回截图结果

    Request body:
        {
            "requestId": "uuid",
            "imageData": "data:image/png;base64,...",  // 或纯 base64
            "error": "error message"  // optional
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    request_id = data.get("requestId")
    if not request_id or request_id not in _screenshot_requests:
        return web.json_response({"error": "Unknown request ID"}, status=404)

    future = _screenshot_requests[request_id]
    future.set_result({
        "imageData": data.get("imageData"),
        "error": data.get("error")
    })

    logger.info(f"Screenshot result received: {request_id}")
    return web.json_response({"success": True})


def _save_screenshot(base64_data: str, project_path: str, room_id: str = None) -> tuple[str, str]:
    """
    保存 Base64 图片到文件

    Args:
        base64_data: Base64 编码的图片数据（可带 data:image/png;base64, 前缀）
        project_path: 项目路径
        room_id: 房间 ID（可选）

    Returns:
        (filepath, pure_base64) - 文件路径和纯 base64 数据
    """
    # 移除 data:image/png;base64, 前缀
    pure_base64 = base64_data
    if "," in base64_data:
        pure_base64 = base64_data.split(",", 1)[1]

    image_bytes = base64.b64decode(pure_base64)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    prefix = f"room_{room_id}" if room_id else "canvas"
    filename = f"{prefix}_{timestamp}.png"

    save_dir = Path(project_path) / "screenshots"
    save_dir.mkdir(parents=True, exist_ok=True)
    filepath = save_dir / filename

    filepath.write_bytes(image_bytes)
    return str(filepath), pure_base64


async def screenshot_save_handler(request: web.Request) -> web.Response:
    """
    保存截图到本地临时目录

    Request body:
        {
            "imageData": "data:image/png;base64,...",
            "filename": "screenshot_001.png"  // optional
        }

    Response:
        {
            "path": "C:\\Users\\...\\BIMCanvas\\screenshots\\screenshot_20260113_150000.png"
        }
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response({"error": "Invalid JSON"}, status=400)

    image_data = data.get("imageData")
    if not image_data:
        return web.json_response({"error": "Missing imageData"}, status=400)

    # 保存到用户文档目录
    import os
    docs_dir = Path(os.path.expanduser("~/Documents/BIMCanvas/Screenshots"))
    docs_dir.mkdir(parents=True, exist_ok=True)

    # 生成文件名
    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    filename = data.get("filename") or f"screenshot_{timestamp}.png"
    filepath = docs_dir / filename

    # 移除 data:image/png;base64, 前缀
    pure_base64 = image_data
    if "," in image_data:
        pure_base64 = image_data.split(",", 1)[1]

    image_bytes = base64.b64decode(pure_base64)
    filepath.write_bytes(image_bytes)

    logger.info(f"Screenshot saved to: {filepath}")
    return web.json_response({"path": str(filepath)})


async def on_shutdown(app: web.Application) -> None:
    """应用关闭时清理资源"""
    logger.info("Shutting down, cleaning up agents...")
    await cleanup_agents()


def create_app() -> web.Application:
    """
    Create and configure the aiohttp application.

    Returns:
        Configured web.Application
    """
    app = web.Application()

    # 注册清理回调
    app.on_shutdown.append(on_shutdown)

    # Configure CORS for Web access
    cors = aiohttp_cors.setup(app, defaults={
        "*": aiohttp_cors.ResourceOptions(
            allow_credentials=True,
            expose_headers="*",
            allow_headers="*",
            allow_methods=["GET", "POST", "OPTIONS"]
        )
    })

    # Add routes
    # Note: Layout tasks are now handled through /api/chat - MainAgent
    # autonomously decides when to dispatch layout-agent SubAgent
    routes = [
        web.get("/health", health_handler),
        web.get("/api/config", config_handler),
        web.get("/api/web_config", web_config_get_handler),
        web.post("/api/web_config", web_config_save_handler),
        web.post("/api/chat", chat_handler),
        web.post("/api/chat/stream", chat_stream_handler),
        web.post("/api/clear-history", clear_history_handler),
        web.post("/api/agent/close", close_agent_handler),
        web.get("/api/history", get_history_handler),
        web.post("/api/interrupt", interrupt_handler),
        # Screenshot API
        web.get("/api/screenshot/events", screenshot_events_handler),
        web.post("/api/screenshot/request", screenshot_request_handler),
        web.post("/api/screenshot/result", screenshot_result_handler),
        web.post("/api/screenshot/save", screenshot_save_handler),
    ]

    # 按路径分组路由（避免同一路径重复创建 resource 导致 CORS 冲突）
    routes_by_path: dict[str, list] = {}
    for route in routes:
        if route.path not in routes_by_path:
            routes_by_path[route.path] = []
        routes_by_path[route.path].append(route)

    # 注册路由
    for path, path_routes in routes_by_path.items():
        resource = cors.add(app.router.add_resource(path))
        for route in path_routes:
            cors.add(resource.add_route(route.method, route.handler))

    return app


def run_server(host: str = None, port: int = None) -> None:
    """
    Start the HTTP server.

    Args:
        host: Host to bind to (default from settings)
        port: Port to bind to (default from settings)
    """
    settings = get_settings()

    host = host or settings.server_host
    port = port or settings.server_port

    app = create_app()

    logger.info(f"Agent 服务已启动: http://{host}:{port}")
    web.run_app(app, host=host, port=port, print=lambda s: logger.info(s))
