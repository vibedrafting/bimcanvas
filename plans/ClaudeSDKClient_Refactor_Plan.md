# ClaudeSDKClient 改造计划

> 创建日期：2026-01-06

## 目标
将 BIMCanvas.Agent 从 `query()` 改造为 `ClaudeSDKClient`，支持：
1. **持续对话**：保持会话上下文
2. **程序触发**：Server 通过 SSE 事件触发 Agent 执行任务

---

## 一、API 对比

| 特性 | `query()` | `ClaudeSDKClient` |
|------|-----------|-------------------|
| 会话 | 每次创建新会话 | 复用同一会话 |
| 上下文 | 需手动 `resume` | 自动保持 |
| 中断 | ❌ 不支持 | ✅ `interrupt()` |
| Hooks | ❌ 不支持 | ✅ 支持 |
| Custom Tools | ❌ 不支持 | ✅ 支持 |

### ClaudeSDKClient API（官方文档确认）

```python
class ClaudeSDKClient:
    def __init__(self, options: ClaudeAgentOptions | None = None)
    async def connect(self, prompt: str | AsyncIterable[dict] | None = None) -> None
    async def query(self, prompt: str | AsyncIterable[dict], session_id: str = "default") -> None
    async def receive_messages(self) -> AsyncIterator[Message]
    async def receive_response(self) -> AsyncIterator[Message]
    async def interrupt(self) -> None
    async def disconnect(self) -> None

# 支持 context manager
async with ClaudeSDKClient(options) as client:
    await client.query("Hello")
    async for msg in client.receive_response():
        print(msg)
```

---

## 二、改造方案

### 2.1 Phase 1：核心改造（PlacementAgent）

**文件**：`BIMCanvas.Agent/src/agent/placement_agent.py`

#### 当前代码（query）：
```python
async def chat(self, user_message: str) -> str:
    options = ClaudeAgentOptions(
        system_prompt=SYSTEM_PROMPT,
        resume=self.session_id,  # 手动会话恢复
        ...
    )
    async for message in query(prompt=user_message, options=options):
        if hasattr(message, 'subtype') and message.subtype == 'init':
            self.session_id = message.data.get('session_id')  # 手动捕获
        ...
```

#### 改造后（ClaudeSDKClient）：
```python
class PlacementAgent:
    def __init__(self, project_path: str = None):
        self.project_path = project_path
        self._client: ClaudeSDKClient | None = None
        self._connected = False
        self._lock = asyncio.Lock()

    async def connect(self, mode: str = "chat") -> None:
        """建立持久连接"""
        async with self._lock:
            if self._connected:
                return
            options = self._create_options(mode)
            self._client = ClaudeSDKClient(options)
            await self._client.connect()
            self._connected = True

    async def disconnect(self) -> None:
        """断开连接"""
        async with self._lock:
            if self._client and self._connected:
                await self._client.disconnect()
                self._connected = False
                self._client = None

    async def chat(self, user_message: str) -> str:
        """对话（自动保持上下文）"""
        if not self._connected:
            await self.connect(mode="chat")

        await self._client.query(user_message)

        full_response = ""
        async for message in self._client.receive_response():
            if isinstance(message, AssistantMessage):
                for block in message.content:
                    if isinstance(block, TextBlock):
                        full_response += block.text
        return full_response

    async def execute_task(self, task_prompt: str) -> str:
        """执行任务（SSE 触发入口）"""
        if not self._connected:
            await self.connect(mode="layout")

        await self._client.query(task_prompt)
        # ... 同上处理响应

    async def interrupt(self) -> None:
        """中断当前任务"""
        if self._client and self._connected:
            await self._client.interrupt()

    def clear_history(self) -> None:
        """清除历史（重建连接）"""
        asyncio.create_task(self._reset_session())

    async def _reset_session(self) -> None:
        await self.disconnect()
        await self.connect()
```

### 2.2 Phase 2：SSE 事件监听

**新增文件**：`BIMCanvas.Agent/src/events/`

#### 目录结构：
```
src/events/
├── __init__.py
├── models.py      # 事件数据模型
├── listener.py    # SSE 监听器
└── handlers.py    # 事件处理器
```

#### 事件模型（models.py）：
```python
class EventType(str, Enum):
    LAYOUT_CORRECTION = "layout_correction"  # 布置修正
    VALIDATION_FAILED = "validation_failed"  # 验证失败
    USER_REQUEST = "user_request"            # 用户请求

@dataclass
class LayoutCorrectionEvent:
    event_type: EventType
    project_path: str
    zone_id: str                    # 问题区域
    module_ids: list[str]           # 需修正模块
    violation_type: str             # "collision" | "out_of_bounds"
    suggested_action: str           # "relocate" | "remove"
```

#### SSE 监听器（listener.py）：
```python
class EventListener:
    def __init__(self, config: SSEConfig, on_event: Callable):
        self.config = config
        self.on_event = on_event

    async def start(self) -> None:
        """启动监听（长期运行）"""
        while self._running:
            try:
                async with self._session.get(self.config.sse_url) as response:
                    async for line in response.content:
                        event = self._parse_event(line)
                        if event:
                            await self.on_event(event)
            except aiohttp.ClientError:
                await asyncio.sleep(self.config.reconnect_delay)
```

#### 事件处理器（handlers.py）：
```python
class EventHandler:
    def __init__(self, agent: PlacementAgent):
        self.agent = agent

    async def handle(self, event: AgentEvent) -> None:
        if event.event_type == EventType.LAYOUT_CORRECTION:
            prompt = self._build_correction_prompt(event)
            await self.agent.execute_task(prompt)
```

### 2.3 Phase 3：服务整合

**修改文件**：`BIMCanvas.Agent/src/main.py`

```python
class AgentService:
    """Agent 服务 - 管理生命周期"""

    def __init__(self, project_path: str = None):
        self.agent: PlacementAgent | None = None
        self.event_listener: EventListener | None = None

    async def start(self) -> None:
        # 1. 初始化 Agent
        self.agent = PlacementAgent(project_path)
        await self.agent.connect(mode="layout")

        # 2. 初始化事件监听
        handler = EventHandler(self.agent)
        self.event_listener = EventListener(
            config=SSEConfig(server_url="http://localhost:5000"),
            on_event=handler.handle
        )

        # 3. 启动监听
        asyncio.create_task(self.event_listener.start())

    async def stop(self) -> None:
        if self.event_listener:
            await self.event_listener.stop()
        if self.agent:
            await self.agent.disconnect()

# 新增守护进程模式
def main():
    if args.daemon:
        asyncio.run(main_daemon(args.project))
    elif args.serve:
        run_server(...)
```

---

## 三、文件清单

| 操作 | 文件 | 说明 |
|------|------|------|
| 修改 | `src/agent/placement_agent.py` | query → ClaudeSDKClient |
| 修改 | `src/server/http_server.py` | 更新 Agent 实例管理 |
| 修改 | `src/main.py` | 添加守护进程模式 |
| 新增 | `src/events/__init__.py` | 事件模块 |
| 新增 | `src/events/models.py` | 事件数据模型 |
| 新增 | `src/events/listener.py` | SSE 监听器 |
| 新增 | `src/events/handlers.py` | 事件处理器 |
| 修改 | `src/config/settings.py` | 添加 SSE 配置 |

---

## 四、执行步骤

### Step 1：PlacementAgent 改造
1. 添加 `ClaudeSDKClient` 实例管理
2. 实现 `connect()` / `disconnect()` 方法
3. 改造 `chat()` / `chat_stream()` 使用新 API
4. 添加 `execute_task()` 方法（程序触发入口）
5. 添加 `interrupt()` 方法

### Step 2：HTTP 服务器适配
1. 更新 `get_or_create_agent()` 使用异步连接
2. 添加 `on_shutdown` 清理逻辑

### Step 3：SSE 事件系统
1. 创建 `events/` 目录结构
2. 实现事件模型
3. 实现 SSE 监听器
4. 实现事件处理器

### Step 4：主入口改造
1. 实现 `AgentService` 类
2. 添加 `--daemon` 命令行参数
3. 实现守护进程模式

### Step 5：配置扩展
1. 添加 SSE 相关配置项
2. 更新 `.env.example`

---

## 五、架构图

```
用户请求                    程序触发
   │                          │
   ▼                          ▼
┌──────────────────────────────────────────────┐
│  BIMCanvas.Agent                             │
│  ┌────────────────────────────────────────┐  │
│  │  AgentService                          │  │
│  │  ├─ PlacementAgent (ClaudeSDKClient)   │  │
│  │  │   └─ 持续连接，保持上下文            │  │
│  │  │                                     │  │
│  │  └─ EventListener (SSE)                │  │
│  │      └─ 监听 Server 事件               │  │
│  └────────────────────────────────────────┘  │
│                      │                       │
│  ┌───────────────────┴────────────────────┐  │
│  │  HTTP Server (现有 API 保持兼容)        │  │
│  │  /api/chat  /api/task/layout  ...      │  │
│  └────────────────────────────────────────┘  │
└──────────────────────────────────────────────┘
         │                    ▲
         │ MCP 工具调用        │ SSE 事件
         ▼                    │
┌──────────────────────────────────────────────┐
│  BIMCanvas.Server                            │
│  ├─ Canvas-MCP 工具                          │
│  ├─ 验证服务 → 检测问题 → 发送修正事件        │
│  └─ /api/events (SSE 端点)                   │
└──────────────────────────────────────────────┘
```

---

## 六、兼容性

| API 端点 | 改造后行为 | 兼容性 |
|----------|-----------|--------|
| `POST /api/chat` | 复用 ClaudeSDKClient 连接 | ✅ 完全兼容 |
| `POST /api/chat/stream` | 流式输出不变 | ✅ 完全兼容 |
| `POST /api/clear-history` | 触发 disconnect/connect | ✅ 完全兼容 |
| `POST /api/task/layout` | 复用会话执行 | ✅ 完全兼容 |
