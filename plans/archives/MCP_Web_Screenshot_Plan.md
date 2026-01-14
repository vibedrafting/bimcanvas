# MCP 服务实现：轻量级截图方案（修订版）

## 需求变更

| 原方案 | 新方案 |
|--------|--------|
| Playwright 截取任意网页 | 前端截图 + MCP 请求 |
| 169 MB Chromium 依赖 | ~50 KB html2canvas |
| Agent 单向获取 | **双向**：Web↔Agent |

## 功能需求

1. **Web → Agent**：用户通过附件菜单截图，随 chat 消息发送给 Agent 分析
2. **Agent → Web**：Agent 调用 MCP 工具请求截图，Web 执行后返回
3. **截图粒度**：整个画布 / 单个房间

---

## 架构设计

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Web 端        │◀───▶│   Server        │◀───▶│   Agent         │
│  (Vue 3 + TS)   │     │  (aiohttp)      │     │  (MCP 工具)     │
│                 │     │                 │     │                 │
│  html2canvas    │     │  图片存储       │     │  request_screenshot
│  截图服务       │     │  SSE 事件       │     │  多模态分析     │
└─────────────────┘     └─────────────────┘     └─────────────────┘
```

---

## 关键文件

| 文件 | 操作 | 说明 |
|------|------|------|
| `BIMCanvas.Web/src/services/ScreenshotService.ts` | **新建** | 前端截图服务 |
| `BIMCanvas.Web/src/components/AgentChat.vue` | **修改** | 附件菜单新增截图选项 |
| `BIMCanvas.Agent/src/server/http_server.py` | **修改** | 添加截图 API + chat 支持图片 |
| `BIMCanvas.Agent/src/mcp/tools/web_screenshot.py` | **修改** | 改用 HTTP 请求 |

---

## 实现方案

### 1. Web 端截图服务

**文件**: `BIMCanvas.Web/src/services/ScreenshotService.ts`

```typescript
import html2canvas from 'html2canvas';

export class ScreenshotService {
  private serverUrl: string;
  private eventSource: EventSource | null = null;

  constructor(serverUrl: string = 'http://localhost:8765') {
    this.serverUrl = serverUrl;
  }

  /** 截取整个画布 */
  async captureCanvas(): Promise<string> {
    const element = document.getElementById('bim-canvas');
    if (!element) throw new Error('Canvas element not found');
    const canvas = await html2canvas(element);
    return canvas.toDataURL('image/png');
  }

  /** 截取指定房间 */
  async captureRoom(roomId: string): Promise<string> {
    const element = document.querySelector(`[data-room-id="${roomId}"]`);
    if (!element) throw new Error(`Room ${roomId} not found`);
    const canvas = await html2canvas(element as HTMLElement);
    return canvas.toDataURL('image/png');
  }

  /** 监听 Agent 截图请求 (SSE) */
  startListening(): void {
    this.eventSource = new EventSource(`${this.serverUrl}/api/screenshot/events`);
    this.eventSource.addEventListener('screenshot_request', async (event) => {
      const { requestId, roomId } = JSON.parse(event.data);
      try {
        const imageData = roomId
          ? await this.captureRoom(roomId)
          : await this.captureCanvas();
        await this.submitResult(requestId, imageData);
      } catch (e) {
        await this.submitResult(requestId, null, String(e));
      }
    });
  }

  stopListening(): void {
    this.eventSource?.close();
    this.eventSource = null;
  }

  private async submitResult(requestId: string, imageData: string | null, error?: string): Promise<void> {
    await fetch(`${this.serverUrl}/api/screenshot/result`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId, imageData, error })
    });
  }
}
```

### 2. Server 端 API (aiohttp)

**文件**: `BIMCanvas.Agent/src/server/http_server.py` - 添加以下内容

```python
import asyncio
import uuid
import base64
from datetime import datetime

# 截图请求等待队列
_screenshot_requests: dict[str, asyncio.Future] = {}
_screenshot_sse_queues: list[asyncio.Queue] = []

async def screenshot_events_handler(request: web.Request) -> web.StreamResponse:
    """SSE 端点：Web 端监听截图请求"""
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

    try:
        while True:
            event = await queue.get()
            await response.write(f"event: screenshot_request\ndata: {json.dumps(event)}\n\n".encode())
    except asyncio.CancelledError:
        pass
    finally:
        _screenshot_sse_queues.remove(queue)

    return response

async def screenshot_request_handler(request: web.Request) -> web.Response:
    """Agent 请求截图 → 通知 Web 端"""
    try:
        data = await request.json()
    except json.JSONDecodeError:
        data = {}

    room_id = data.get("roomId")
    project_path = data.get("projectPath", ".")
    request_id = str(uuid.uuid4())

    # 创建等待 Future
    future: asyncio.Future = asyncio.get_event_loop().create_future()
    _screenshot_requests[request_id] = future

    # 广播给所有 SSE 客户端
    event = {"requestId": request_id, "roomId": room_id}
    for queue in _screenshot_sse_queues:
        await queue.put(event)

    try:
        # 等待 Web 端返回（10秒超时）
        result = await asyncio.wait_for(future, timeout=10.0)

        if result.get("error"):
            return web.json_response({"error": result["error"]}, status=400)

        # 保存图片并返回 path + base64
        image_data = result["imageData"]
        filepath, pure_base64 = _save_screenshot(image_data, project_path, room_id)
        return web.json_response({
            "path": filepath,
            "base64": pure_base64  # 纯 base64（无 data:image/png;base64, 前缀）
        })

    except asyncio.TimeoutError:
        return web.json_response({"error": "Screenshot request timeout"}, status=504)
    finally:
        _screenshot_requests.pop(request_id, None)

async def screenshot_result_handler(request: web.Request) -> web.Response:
    """Web 端返回截图结果"""
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

    return web.json_response({"success": True})

def _save_screenshot(base64_data: str, project_path: str, room_id: str = None) -> tuple[str, str]:
    """
    保存 Base64 图片到文件

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
```

**路由注册** (在 `create_app()` 的 routes 列表中添加):

```python
web.get("/api/screenshot/events", screenshot_events_handler),
web.post("/api/screenshot/request", screenshot_request_handler),
web.post("/api/screenshot/result", screenshot_result_handler),
```

### 3. Agent MCP 工具

**文件**: `BIMCanvas.Agent/src/mcp/tools/web_screenshot.py` - 替换内容

```python
"""Web 截图 MCP 工具 - 请求前端截图"""
from typing import Any
import aiohttp
from ..decorators import mcp_tool

SERVER_URL = "http://localhost:8765"

@mcp_tool()
async def request_screenshot(args: dict[str, Any]) -> dict[str, Any]:
    """
    请求 Web 端截图

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
```

**MCP 响应说明**：
- `type: "image"` + `data` + `mimeType` → Claude 可直接"看到"图片进行多模态分析
- `type: "text"` → 告知文件保存路径，便于后续引用

### 4. Web → Agent：Chat 支持图片附件

#### 4.1 附件菜单新增截图选项

**文件**: `BIMCanvas.Web/src/components/AgentChat.vue` - 在附件菜单中添加

```vue
<template>
  <!-- 现有附件按钮的子菜单 -->
  <div class="attachment-menu">
    <!-- 现有选项... -->
    <button @click="captureAndAttach('canvas')">截取画布</button>
    <button @click="captureAndAttach('room')">截取当前房间</button>
  </div>
</template>

<script setup>
import { ScreenshotService } from '@/services/ScreenshotService';

const screenshotService = new ScreenshotService();
const pendingImages = ref<string[]>([]);  // 待发送的图片 base64 列表

async function captureAndAttach(type: 'canvas' | 'room') {
  try {
    const imageData = type === 'canvas'
      ? await screenshotService.captureCanvas()
      : await screenshotService.captureRoom(currentRoomId.value);

    pendingImages.value.push(imageData);
    // 显示预览缩略图...
  } catch (e) {
    console.error('截图失败:', e);
  }
}

async function sendMessage() {
  const payload = {
    projectPath: props.projectPath,
    message: inputText.value,
    images: pendingImages.value,  // 新增：图片附件
    model: selectedModel.value,
    thinkingLevel: thinkingLevel.value
  };

  // 发送后清空
  pendingImages.value = [];
  // ... 现有发送逻辑
}
</script>
```

#### 4.2 Server API 支持图片附件

**文件**: `BIMCanvas.Agent/src/server/http_server.py` - 修改 `chat_stream_handler`

```python
async def chat_stream_handler(request: web.Request) -> web.StreamResponse:
    # ... 现有代码 ...

    message = data.get("message", "")
    images = data.get("images", [])  # 新增：图片 base64 列表

    # ... 现有代码 ...

    # 传递图片给 agent
    async for chunk in agent.chat_stream(message, images=images, thinking_level=thinking_level):
        # ...
```

#### 4.3 Agent 处理多模态消息

**文件**: `BIMCanvas.Agent/src/agent/main_agent.py` - 修改 `chat_stream` 方法

```python
async def chat_stream(
    self,
    message: str,
    images: list[str] = None,  # 新增
    thinking_level: str = None
) -> AsyncGenerator[StreamChunk, None]:
    """流式对话（支持图片附件）"""

    # 构建多模态消息内容
    content = []

    # 添加图片
    if images:
        for img_base64 in images:
            # 移除 data:image/png;base64, 前缀
            if "," in img_base64:
                img_base64 = img_base64.split(",", 1)[1]
            content.append({
                "type": "image",
                "source": {
                    "type": "base64",
                    "media_type": "image/png",
                    "data": img_base64
                }
            })

    # 添加文本
    content.append({"type": "text", "text": message})

    # 使用多模态内容发送
    user_message = {"role": "user", "content": content}

    # ... 现有流式处理逻辑
```

---

## 实现步骤

### Phase 1: Server API (先实现，可独立测试)
1. 在 `http_server.py` 添加截图相关 handler（SSE + request + result）
2. 注册新路由
3. 修改 `chat_stream_handler` 支持 images 参数
4. 测试 SSE 连接

### Phase 2: Agent 层
1. 修改 `main_agent.py` 的 `chat_stream` 支持多模态消息
2. 修改 `web_screenshot.py` 为 HTTP 请求方式（替换 Playwright）

### Phase 3: Web 端 (完成闭环)
1. 安装 html2canvas: `cd BIMCanvas.Web && npm install html2canvas`
2. 创建 `ScreenshotService.ts`
3. 在 App 启动时调用 `startListening()`（响应 Agent 截图请求）
4. 修改 `AgentChat.vue` 附件菜单，新增截图选项

---

## 依赖

| 组件 | 依赖 | 大小 |
|------|------|------|
| Web 端 | html2canvas | ~50 KB |
| Server | 无新依赖 | 0 |
| Agent | aiohttp (已有) | 0 |

**总计**：~50 KB（相比 Playwright 的 169 MB）

---

## 验证方式

```bash
# 1. 启动 Agent Server
cd BIMCanvas.Agent && python -m src.main --serve

# 2. 测试 SSE 连接 (新终端)
curl -N http://localhost:8765/api/screenshot/events

# 3. 启动 Web 端
cd BIMCanvas.Web && npm run dev
```

### 场景 A：Agent → Web（Agent 主动请求截图）

```
You: 请帮我看看当前布局有什么问题
Agent: 我需要先看一下画布内容 [调用 mcp__canvas__request_screenshot]
Agent: [看到截图] 我注意到客厅的沙发摆放位置...
```

### 场景 B：Web → Agent（用户附加截图）

```
[用户点击附件按钮 → 截取画布]
[输入框显示截图预览缩略图]
You: 这个布局怎么样？
Agent: [看到截图] 整体布局不错，但建议...
```
