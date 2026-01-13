"""HTTP Server for Web integration using aiohttp"""

import asyncio
import json
import logging
from typing import Any

from aiohttp import web
import aiohttp_cors

from ..agent.main_agent import MainAgent
from ..config.settings import get_settings

# Configure logging
logger = logging.getLogger(__name__)

# Global agent instances (cached by project path)
agents: dict[str, MainAgent] = {}
_agents_lock = asyncio.Lock()


async def get_agent(project_path: str) -> MainAgent:
    """
    异步获取或创建 Agent 实例（带预连接）

    Args:
        project_path: Path to the project

    Returns:
        MainAgent instance
    """
    cache_key = project_path or "__default__"

    async with _agents_lock:
        if cache_key not in agents:
            agent = MainAgent(project_path)
            await agent.connect()  # 预连接
            agents[cache_key] = agent

        return agents[cache_key]


async def cleanup_agents() -> None:
    """清理所有 Agent 连接（shutdown 时调用）"""
    async with _agents_lock:
        for cache_key, agent in agents.items():
            try:
                await agent.disconnect()
                logger.info(f"Disconnected agent: {cache_key}")
            except Exception as e:
                logger.error(f"Error disconnecting agent {cache_key}: {e}")
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


async def chat_handler(request: web.Request) -> web.Response:
    """
    Handle chat requests.

    Request body:
        {
            "projectPath": "path/to/project",  // optional
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
    message = data.get("message", "")

    if not message:
        return web.json_response(
            {"error": "Message cannot be empty"},
            status=400
        )

    try:
        agent = await get_agent(project_path)  # 异步获取
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
            "message": "user message",
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
    message = data.get("message", "")
    model = data.get("model")              # 新增：模型名称
    thinking_level = data.get("thinkingLevel")  # 新增：思考强度

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
        agent = await get_agent(project_path)  # 异步获取

        # 动态切换模型（如果指定了模型且与当前不同）
        if model:
            await agent.set_model(model)

        async for chunk in agent.chat_stream(message, thinking_level=thinking_level):
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
    Clear conversation history for a project.

    Request body:
        {
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
    cache_key = project_path or "__default__"

    if cache_key in agents:
        agents[cache_key].clear_history()
        logger.info(f"Cleared history for project: {project_path or 'default'}")

    return web.json_response({"success": True})


async def get_history_handler(request: web.Request) -> web.Response:
    """
    Get conversation history for a project.

    Query params:
        projectPath: path to the project
    """
    project_path = request.query.get("projectPath", "")

    agent = await get_agent(project_path)  # 异步获取
    history = agent.get_history()

    return web.json_response({
        "history": history,
        "projectPath": project_path
    })


async def interrupt_handler(request: web.Request) -> web.Response:
    """
    中断当前任务

    Request body:
        {
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
    cache_key = project_path or "__default__"

    async with _agents_lock:
        if cache_key in agents:
            await agents[cache_key].interrupt()
            logger.info(f"Interrupted task for project: {project_path or 'default'}")
            return web.json_response({"success": True})

    return web.json_response({"error": "Agent not found"}, status=404)


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
        web.post("/api/chat", chat_handler),
        web.post("/api/chat/stream", chat_stream_handler),
        web.post("/api/clear-history", clear_history_handler),
        web.get("/api/history", get_history_handler),
        web.post("/api/interrupt", interrupt_handler),
    ]

    for route in routes:
        resource = cors.add(app.router.add_resource(route.path))
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
