"""HTTP Server for Web integration using aiohttp"""

import json
import logging
from typing import Any

from aiohttp import web
import aiohttp_cors

from ..agent.placement_agent import PlacementAgent
from ..config.settings import get_settings

# Configure logging
logger = logging.getLogger(__name__)

# Global agent instances (cached by project path)
agents: dict[str, PlacementAgent] = {}


def get_agent(project_path: str) -> PlacementAgent:
    """
    Get or create an Agent instance for a project.

    Args:
        project_path: Path to the project

    Returns:
        PlacementAgent instance
    """
    cache_key = project_path or "__default__"

    if cache_key not in agents:
        agents[cache_key] = PlacementAgent(project_path)
        logger.info(f"Created new agent for project: {project_path or 'default'}")

    return agents[cache_key]


async def health_handler(request: web.Request) -> web.Response:
    """Health check endpoint"""
    return web.json_response({
        "status": "ok",
        "service": "bimcanvas-agent",
        "version": "0.1.0"
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
        agent = get_agent(project_path)
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
            "message": "user message"
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
        agent = get_agent(project_path)

        async for chunk in agent.chat_stream(message):
            # Send each chunk as SSE event with type info
            event_data = json.dumps({
                "type": chunk.type,
                "content": chunk.content
            }, ensure_ascii=False)
            await response.write(f"data: {event_data}\n\n".encode("utf-8"))

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

    agent = get_agent(project_path)
    history = agent.get_history()

    return web.json_response({
        "history": history,
        "projectPath": project_path
    })


async def layout_task_handler(request: web.Request) -> web.Response:
    """
    Execute a layout task (P2 feature).

    This endpoint triggers the AI to read project data, analyze rooms,
    and generate furniture placement in modules.json.

    Request body:
        {
            "projectPath": "path/to/project",  // required
            "schemeId": "default",              // optional, defaults to "default"
            "prompt": "user request"            // optional, defaults to generic prompt
        }

    Response:
        {
            "success": true,
            "summary": "AI execution summary",
            "schemeId": "default"
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
    scheme_id = data.get("schemeId", "default")
    user_prompt = data.get("prompt", "请为这个户型布置家具")

    if not project_path:
        return web.json_response(
            {"error": "projectPath is required"},
            status=400
        )

    try:
        agent = get_agent(project_path)
        logger.info(f"Starting layout task for project: {project_path}, scheme: {scheme_id}")

        # Execute layout task with tools enabled
        result = await agent.run_layout(user_prompt, scheme_id)

        logger.info(f"Layout task completed for scheme: {scheme_id}")

        return web.json_response({
            "success": True,
            "summary": result,
            "schemeId": scheme_id
        })

    except Exception as e:
        logger.exception(f"Layout task error: {e}")
        return web.json_response(
            {"error": str(e)},
            status=500
        )


async def layout_task_stream_handler(request: web.Request) -> web.StreamResponse:
    """
    Execute a layout task with streaming output (P2 feature).

    Request body:
        {
            "projectPath": "path/to/project",
            "schemeId": "default",
            "prompt": "user request"
        }

    Response: SSE stream with thinking and text chunks
    """
    try:
        data = await request.json()
    except json.JSONDecodeError:
        return web.json_response(
            {"error": "Invalid JSON"},
            status=400
        )

    project_path = data.get("projectPath", "")
    scheme_id = data.get("schemeId", "default")
    user_prompt = data.get("prompt", "请为这个户型布置家具")

    if not project_path:
        return web.json_response(
            {"error": "projectPath is required"},
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
        agent = get_agent(project_path)
        logger.info(f"Starting streaming layout task for project: {project_path}")

        async for chunk in agent.run_layout_stream(user_prompt, scheme_id):
            event_data = json.dumps({
                "type": chunk.type,
                "content": chunk.content
            }, ensure_ascii=False)
            await response.write(f"data: {event_data}\n\n".encode("utf-8"))

        # Send done event with scheme info
        done_data = json.dumps({
            "type": "done",
            "schemeId": scheme_id
        }, ensure_ascii=False)
        await response.write(f"data: {done_data}\n\n".encode("utf-8"))

    except Exception as e:
        logger.exception(f"Layout stream error: {e}")
        error_data = json.dumps({"error": str(e)}, ensure_ascii=False)
        await response.write(f"data: {error_data}\n\n".encode("utf-8"))

    return response


def create_app() -> web.Application:
    """
    Create and configure the aiohttp application.

    Returns:
        Configured web.Application
    """
    app = web.Application()

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
    routes = [
        web.get("/health", health_handler),
        web.post("/api/chat", chat_handler),
        web.post("/api/chat/stream", chat_stream_handler),
        web.post("/api/clear-history", clear_history_handler),
        web.get("/api/history", get_history_handler),
        # P2: Layout task endpoints
        web.post("/api/task/layout", layout_task_handler),
        web.post("/api/task/layout/stream", layout_task_stream_handler),
    ]

    for route in routes:
        resource = cors.add(app.router.add_resource(route.path))
        cors.add(resource.add_route(route.method, route.handler))

    logger.info("HTTP application created with routes: /health, /api/chat, /api/chat/stream, /api/clear-history, /api/history, /api/task/layout, /api/task/layout/stream")

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

    logger.info(f"Starting BIMCanvas Agent server at http://{host}:{port}")
    web.run_app(app, host=host, port=port, print=lambda s: logger.info(s))
