# BIMCanvas 系统架构文档

> 版本：v2.0
> 更新日期：2025-12-02
> 状态：已定稿（基于专家评审结论）

---

## 1. 项目概述

### 1.1 项目定位

**BIMCanvas 只做一件事：在用户提供的建筑平面内，布置符合设计逻辑的家具组合。**

通过 Claude Code 作为 AI 入口，实现：
- 从 Revit 提取建筑结构（墙/门/窗）
- AI 在 JSON 数据模型上进行家具布置
- 用户在 Web 画布上实时协作编辑
- 将设计方案同步回 Revit

### 1.2 核心工作流

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              完整工作流                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Revit 模型                                                                │
│       │                                                                     │
│       ▼ [Revit-MCP: 提取建筑结构]                                           │
│   建筑数据 (墙/门/窗坐标)                                                    │
│       │                                                                     │
│       ▼ [BIMCanvas.Core: 转换为 JSON]                                       │
│   CanvasDocument (JSON)  ←────── 核心数据格式                               │
│       │                                                                     │
│       ├──────────────────────┬──────────────────────┐                       │
│       ▼                      ▼                      ▼                       │
│   [Canvas-MCP]          [Web画布]              [Library-MCP]                │
│   AI操作JSON数据         JSON→SVG渲染           搜索族资源                   │
│       │                      │                      │                       │
│       └──────────────────────┴──────────────────────┘                       │
│                              │                                              │
│                              ▼ [WebSocket 实时同步]                          │
│                         最终设计方案 (JSON)                                  │
│                              │                                              │
│                              ▼ [Revit-MCP: 创建元素]                         │
│                         Revit 模型                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 1.3 关键设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **数据核心** | **JSON** | Token 效率高，AI 理解好，易于版本控制 |
| **视图渲染** | **SVG** | 矢量图形，浏览器原生支持 |
| AI 入口 | Claude Code | 已有成熟工具，直接复用 |
| 画布载体 | Web 页面 | 跨平台、交互库丰富 |
| 同步机制 | WebSocket | AI 每次操作用户立即可见 |
| MCP 部署 | 独立 Server | 职责清晰、独立维护 |
| **Core 运行时** | **.NET Standard 2.0** | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |

### 1.4 "JSON 为骨，SVG 为皮"

| 层面 | 格式 | 职责 |
|------|------|------|
| **数据层（骨）** | JSON | 存储、传输、AI 交互、业务逻辑 |
| **视图层（皮）** | SVG | 渲染、显示、视觉反馈 |

**数据流：**
```
AI 操作 → 修改 JSON 数据 → WebSocket 推送 → 前端根据 JSON 生成 SVG → 用户看到画布
```

详细 JSON Schema 定义见：[Schema-JSON.md](./Schema-JSON.md)

### 1.5 坐标系统规范

BIMCanvas 采用 **CAD 标准坐标系**（笛卡尔坐标系），而非 Web 屏幕坐标系：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | **向上为正** | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

#### 设计理由

1. **数据层纯净性**：后端数据模型应符合数学直觉，不应迁就前端渲染
2. **AI 语义一致性**：`spatialRelations` 中 `above` = Y 值更大，逻辑自洽
3. **Revit 兼容性**：与 Revit 坐标系方向一致，减少转换复杂度

#### 各层职责

```
┌─────────────────────────────────────────────────────────┐
│  Revit 层 (.NET FW 4.7.2)                               │
│  - 导出原始坐标（Y-up）                                  │
│  - 计算视图裁剪框偏移量                                  │
│  - 存入 metadata.revitMapping.projectBaseOffset         │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Core 层 (.NET Standard 2.0)                            │
│  - 纯笛卡尔坐标运算                                      │
│  - 空间关系计算                                          │
│  - 不做任何坐标系转换                                    │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Web 层 (Vue 3 + TypeScript)                            │
│  - 渲染时进行坐标转换：y_screen = height - y_model      │
│  - 禁止使用 CSS scaleY(-1)                              │
│  - 事件处理时反向转换：y_model = height - y_screen      │
└─────────────────────────────────────────────────────────┘
```

#### 前端坐标转换函数

```typescript
// 世界坐标 (mm) → 屏幕坐标 (px)
function toScreen(modelX: number, modelY: number, scale: number, canvasHeight: number) {
  return {
    x: modelX * scale,
    y: canvasHeight - (modelY * scale)
  };
}

// 屏幕坐标 (px) → 世界坐标 (mm)
function toModel(screenX: number, screenY: number, scale: number, canvasHeight: number) {
  return {
    x: screenX / scale,
    y: (canvasHeight - screenY) / scale
  };
}
```

> ⚠️ **重要**：禁止使用 CSS `scaleY(-1)` 进行坐标翻转，会导致文字倒置等副作用。
> 必须使用上述显式转换函数。

详细坐标系定义见：[Schema-JSON.md - §1.3 坐标系统](./Schema-JSON.md#13-坐标系统)

---

## 2. 系统架构

### 2.1 整体架构图

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Claude Code (AI CLI)                               │
│                         用户与 AI 的对话交互入口                              │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │ MCP Protocol
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            MCP Server 集群                                   │
├───────────────────┬───────────────────┬─────────────────────────────────────┤
│    Revit-MCP      │   Canvas-MCP      │         Library-MCP                 │
│    (已有基础)      │   (画布工具)       │         (族库工具)                   │
│                   │                   │                                     │
│  • 提取建筑结构    │  • 操作JSON数据    │  • 搜索族资源                        │
│  • 创建Revit元素   │  • 版本控制       │  • 获取族信息                        │
│  • 查询模型信息    │  • 变更追踪       │  • 获取SVG预览                       │
│  • 视图截图       │  • 画布截图       │  • Visual Fallback                  │
│                   │                   │                                     │
│  .NET FW 4.7.2    │  .NET 6+          │  .NET 6+                            │
│  (Revit限制)      │  (引用Core)       │  (引用Core)                         │
└───────────────────┴───────────────────┴─────────────────────────────────────┘
                                      │
                                      │ 引用
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BIMCanvas.Core                                     │
│                      核心类库 (.NET Standard 2.0)                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  Models/              • CanvasDocument, Element, Zone 等数据模型             │
│  Algorithms/          • 空间计算（碰撞检测、网格对齐、关系计算）               │
│  Converters/          • Revit数据 ↔ JSON 转换                               │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │ HTTP / WebSocket
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BIMCanvas.Web.Server                                  │
│                      Web 后端服务 (ASP.NET Core .NET 6+)                      │
├─────────────────────────────────────────────────────────────────────────────┤
│  • SignalR Hub（实时通信）            • 画布状态管理（JSON 存储）             │
│  • REST API                         • Canvas-MCP 内嵌运行                   │
│  • 版本控制与变更追踪                  • 截图服务（JSON → SVG → PNG）         │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      │ HTTP / WebSocket
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BIMCanvas.Web                                      │
│                      Web 前端应用 (Vue 3 + TypeScript)                        │
├─────────────────────────────────────────────────────────────────────────────┤
│  • JSON → SVG 动态渲染               • 元素拖拽/旋转/缩放                     │
│  • 实时状态同步                       • 批注绘制工具                          │
│  • 撤销/重做                         • Commit 同步按钮                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 数据流向

```
【从 Revit 到画布】
Revit 模型
    → [Revit-MCP: ai_element_filter] 提取墙/门/窗元素
    → [Revit-MCP: capture_view] 获取视图范围
    → [BIMCanvas.Core: RevitToJsonConverter] 转换为 CanvasDocument (JSON)
    → [Canvas-MCP: canvas_create] 创建画布
    → [WebSocket] 推送到 Web
    → [BIMCanvas.Web] JSON → SVG 渲染显示

【AI 设计方案】
AI 理解用户需求
    → [Library-MCP: family_search] 搜索合适的家具
    → [Canvas-MCP: element_add] 修改 JSON 数据
    → [WebSocket] 实时推送 JSON 变更
    → [BIMCanvas.Web] 重新渲染 SVG

【用户交互修改】
用户在 Web 画布操作（拖拽家具）
    → [前端] 修改本地 JSON 状态
    → [WebSocket] 发送变更到 Server
    → [用户点击 Commit 按钮] 生成 change_set
    → [Canvas-MCP: canvas_get_changes] AI 查询变更
    → AI 感知变化并响应

【同步回 Revit】
设计方案确定
    → [Canvas-MCP: canvas_export] 导出 JSON
    → [BIMCanvas.Core: JsonToRevitConverter] 解析家具元素
    → [Revit-MCP: load_family_from_library] 加载族
    → [Revit-MCP: create_element] 创建 Revit 元素
```

---

## 3. 项目结构

### 3.1 解决方案目录结构

```
BIMCanvas/                                    【根目录】
│
├── BIMCanvas.slnx                            【解决方案文件】
│
├── BIMCanvas.Core/                           【项目】核心类库 (.NET Standard 2.0)
│   ├── BIMCanvas.Core.csproj
│   ├── Models/                                  【目录】数据模型
│   │   ├── CanvasDocument.cs                       画布文档（根对象）
│   │   ├── Elements/                               【目录】元素类型
│   │   │   ├── CanvasElement.cs                       元素基类
│   │   │   ├── FurnitureElement.cs                    家具
│   │   │   ├── WallElement.cs                         墙
│   │   │   ├── DoorElement.cs                         门
│   │   │   └── WindowElement.cs                       窗
│   │   ├── Zones/                                  【目录】区域
│   │   │   └── Zone.cs                                功能区域
│   │   ├── Relations/                              【目录】空间关系
│   │   │   └── SpatialRelation.cs                     空间关系
│   │   └── Shared/                                 【目录】通用类型
│   │       ├── Point2D.cs
│   │       ├── Bounds3D.cs
│   │       └── Result.cs
│   ├── Algorithms/                              【目录】空间计算
│   │   ├── CollisionDetector.cs                    碰撞检测
│   │   ├── GridHelper.cs                           网格对齐
│   │   ├── RelationCalculator.cs                   空间关系计算
│   │   └── SpaceAnalyzer.cs                        空间分析
│   └── Converters/                              【目录】转换器
│       ├── RevitToJsonConverter.cs                 Revit数据 → JSON
│       └── JsonToRevitConverter.cs                 JSON → Revit数据
│
├── BIMCanvas.Revit/                          【项目】Revit 相关 (.NET FW 4.7.2)
│   ├── BIMCanvas.Revit.csproj                     ⚠️ 仅此项目可引用 Revit API
│   ├── Adapters/                                【目录】Revit 适配器
│   │   ├── ElementAdapter.cs                       元素转换适配器
│   │   └── ViewAdapter.cs                          视图适配器
│   ├── Commands/                                【目录】Ribbon 命令
│   │   ├── QuickLayoutCommand.cs                   快速布置按钮
│   │   └── StartDialogCommand.cs                   开启对话按钮
│   ├── Views/                                   【目录】WPF 窗口
│   │   └── ConfigWindow.xaml                       配置窗口
│   └── Services/                                【目录】服务
│       └── AiLauncherService.cs                    AI 启动服务
│
├── BIMCanvas.MCP.Canvas/                     【项目】画布 MCP Server (.NET 6+)
│   ├── BIMCanvas.MCP.Canvas.csproj
│   ├── Program.cs                               MCP Server 入口
│   ├── Tools/                                   【目录】MCP 工具
│   │   ├── CanvasTools.cs                          画布管理工具
│   │   ├── ElementTools.cs                         元素操作工具
│   │   ├── QueryTools.cs                           查询工具
│   │   └── VersionTools.cs                         版本控制工具
│   └── Services/                                【目录】服务
│       ├── CanvasStateService.cs                   画布状态管理
│       └── ScreenshotService.cs                    截图服务
│
├── BIMCanvas.MCP.Library/                    【项目】族库 MCP Server (.NET 6+)
│   ├── BIMCanvas.MCP.Library.csproj
│   ├── Program.cs
│   ├── Tools/                                   【目录】MCP 工具
│   │   └── FamilyTools.cs                          族库查询工具
│   └── Services/                                【目录】服务
│       ├── FamilyApiClient.cs                      族库 API 客户端
│       └── FallbackGenerator.cs                    占位符生成器
│
├── BIMCanvas.Web.Server/                     【项目】Web 后端服务 (.NET 6+)
│   ├── BIMCanvas.Web.Server.csproj
│   ├── Program.cs
│   ├── Hubs/                                    【目录】SignalR Hub
│   │   └── CanvasHub.cs                            画布实时通信
│   ├── Controllers/                             【目录】REST API
│   │   └── CanvasController.cs                     画布 API
│   └── Services/                                【目录】服务
│       ├── CanvasStateManager.cs                   画布状态管理
│       └── ChangeSetService.cs                     变更集服务
│
├── BIMCanvas.Web/                            【项目】Web 前端 (Vue 3 + TypeScript)
│   ├── package.json
│   ├── vite.config.ts
│   ├── src/
│   │   ├── main.ts
│   │   ├── App.vue
│   │   ├── components/
│   │   │   ├── Canvas/
│   │   │   │   ├── SvgCanvas.vue                    SVG 画布主组件
│   │   │   │   ├── CanvasElement.vue                元素渲染
│   │   │   │   └── CanvasToolbar.vue                工具栏
│   │   │   └── Sync/
│   │   │       └── CommitButton.vue                 同步按钮
│   │   ├── stores/
│   │   │   └── canvasStore.ts                       画布状态（Pinia）
│   │   ├── services/
│   │   │   ├── signalrService.ts                    SignalR 客户端
│   │   │   └── svgRenderer.ts                       JSON → SVG 渲染
│   │   └── types/
│   │       └── canvas.d.ts                          类型定义
│   └── public/
│
├── docs/                                     【目录】文档
│   ├── PRD.md                                   产品需求文档
│   ├── Architecture.md                          架构文档（本文件）
│   ├── Schema-JSON.md                           JSON Schema 规范
│   └── ExpertReviews.md                         专家评审记录
│
└── external/                                 【目录】外部依赖
    └── Revit-MCP/                               现有 Revit-MCP 项目
```

### 3.2 项目清单与技术栈

| 项目名 | 类型 | 运行时 | 说明 |
|--------|------|--------|------|
| **BIMCanvas.Core** | 类库 | **.NET Standard 2.0** | 核心数据模型和算法，**所有项目可引用** |
| **BIMCanvas.Revit** | 类库 | .NET Framework 4.7.2 | Revit 相关代码，**仅限 Revit 插件** |
| **BIMCanvas.MCP.Canvas** | 控制台 | .NET 6+ | 画布 MCP Server |
| **BIMCanvas.MCP.Library** | 控制台 | .NET 6+ | 族库 MCP Server |
| **BIMCanvas.Web.Server** | Web 应用 | .NET 6+ | Web 后端，ASP.NET Core |
| **BIMCanvas.Web** | 前端应用 | Node.js | Vue 3 + TypeScript + Vite |

### 3.3 项目依赖关系

```
                    ┌─────────────────────────┐
                    │    BIMCanvas.Core       │
                    │  (.NET Standard 2.0)    │
                    │                         │
                    │  Models/                │
                    │  Algorithms/            │
                    │  Converters/            │
                    └─────────────────────────┘
                              ▲
                              │ 引用
          ┌───────────────────┼───────────────────┬───────────────────┐
          │                   │                   │                   │
          ▼                   ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ BIMCanvas.MCP   │ │ BIMCanvas.MCP   │ │ BIMCanvas.Web   │ │ BIMCanvas.Revit │
│ .Canvas         │ │ .Library        │ │ .Server         │ │                 │
│ (.NET 6+)       │ │ (.NET 6+)       │ │ (.NET 6+)       │ │ (.NET FW 4.7.2) │
└─────────────────┘ └─────────────────┘ └─────────────────┘ └─────────────────┘
          │                                       │
          │ HTTP                                  │ HTTP/WS
          └───────────────────┬───────────────────┘
                              ▼
                    ┌─────────────────────┐
                    │   BIMCanvas.Web     │
                    │   (Vue 3 + TS)      │
                    └─────────────────────┘
```

> [!IMPORTANT]
> **命名空间边界警告**
>
> `BIMCanvas.Revit` 命名空间**绝对不能**被 MCP Server 或 Web Server 引用！
>
> - `BIMCanvas.Core.*` → 所有 .NET 项目可引用
> - `BIMCanvas.Revit.*` → **仅** BIMCanvas.Revit 项目内部使用
>
> 违反此边界将导致运行时错误（.NET 6+ 无法加载 Revit API 依赖）。

---

## 4. Canvas-MCP 与 Web.Server 通信

### 4.1 架构选择：同进程运行

Canvas-MCP 与 Web.Server 共享画布状态的问题，采用**同进程运行**方案：

```
┌─────────────────────────────────────────────────────────────┐
│                    BIMCanvas.Web.Server                      │
│                        (.NET 6+)                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ┌─────────────────┐      ┌─────────────────────────────┐  │
│   │  Canvas-MCP     │ ←──→ │  CanvasStateManager         │  │
│   │  Tools 实现     │      │  (内存中的画布状态)          │  │
│   └─────────────────┘      └─────────────────────────────┘  │
│            ↑                           ↑                    │
│            │                           │                    │
│   ┌────────┴────────┐      ┌───────────┴───────────────┐    │
│   │  MCP Protocol   │      │  SignalR Hub              │    │
│   │  (stdio)        │      │  (WebSocket)              │    │
│   └─────────────────┘      └───────────────────────────┘    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
         ↑                               ↑
         │                               │
    Claude Code                     Web Browser
```

**优点：**
- 无需跨进程通信
- 状态一致性有保障
- 部署简单

### 4.2 状态管理

```csharp
// CanvasStateManager - 画布状态管理器
public class CanvasStateManager
{
    // 内存中的画布状态（JSON 格式的 CanvasDocument）
    private readonly ConcurrentDictionary<string, CanvasDocument> _canvases;

    // 获取画布
    public CanvasDocument GetCanvas(string canvasId);

    // 更新画布（带版本检查）
    public Result<CanvasDocument> UpdateCanvas(string canvasId, int expectedVersion, Action<CanvasDocument> modifier);

    // 获取变更
    public List<ChangeSet> GetPendingCommits(string canvasId);

    // 确认变更
    public void AcknowledgeCommits(string canvasId, List<string> changeSetIds);
}
```

---

## 5. 实时同步架构

### 5.1 同步流程图

```
┌───────────────┐         ┌─────────────────────┐         ┌───────────────┐
│  Claude Code  │         │  BIMCanvas.Web      │         │    用户       │
│  (AI Agent)   │         │  .Server            │         │  Web Browser  │
└───────┬───────┘         └──────────┬──────────┘         └───────┬───────┘
        │                            │                            │
        │ MCP: element_add           │                            │
        │ (修改 JSON 数据)            │                            │
        ├───────────────────────────>│                            │
        │                            │  WebSocket: push JSON      │
        │                            ├───────────────────────────>│
        │                            │                            │ 前端渲染 SVG
        │                            │                            │ 用户看到变化
        │                            │                            │
        │                            │  用户拖动家具               │
        │                            │  前端修改本地 JSON          │
        │                            │<───────────────────────────┤
        │                            │                            │
        │                            │  用户点击 Commit 按钮       │
        │                            │  发送 change_set           │
        │                            │<───────────────────────────┤
        │                            │                            │
        │ MCP 工具返回时附带          │                            │
        │ pendingCommits 信息        │                            │
        │<───────────────────────────┤                            │
        │                            │                            │
        │ AI 感知用户修改并响应       │                            │
```

### 5.2 Commit 同步机制

用户在 Web 画布修改后，点击"同步"按钮：

1. **用户操作**：填写修改摘要，点击"发送给 AI"
2. **Server 处理**：生成 `change_set`，存入待处理队列
3. **AI 感知**：下次 MCP 工具调用时，返回结果附带 `pendingCommits`
4. **AI 确认**：调用 `canvas_ack_commits` 确认已处理

详细机制见：[Schema-JSON.md - 版本控制与变更追踪](./Schema-JSON.md#7-版本控制与变更追踪)

### 5.3 乐观锁机制

防止 AI 和用户同时修改导致冲突：

```json
// AI 调用
{
  "tool": "element_move",
  "params": {
    "canvasId": "canvas_001",
    "expectedVersion": 42,
    "elementId": "f_001",
    "position": { "x": 5000, "y": 3000 }
  }
}

// 版本冲突时返回
{
  "success": false,
  "error": "VERSION_CONFLICT",
  "currentVersion": 43,
  "hint": "请调用 canvas_describe() 获取最新状态后重试"
}
```

---

## 6. 模块详细设计

### 6.1 BIMCanvas.Core

**职责**：提供核心数据模型和算法，被所有 .NET 项目共享引用

#### 6.1.1 数据模型

详细定义见：[Schema-JSON.md](./Schema-JSON.md)

```csharp
// CanvasDocument - 画布文档根对象
public class CanvasDocument
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Version { get; set; }
    public CanvasBounds Bounds { get; set; }
    public CanvasGrid Grid { get; set; }
    public CanvasMetadata Metadata { get; set; }
    public CanvasStructure Structure { get; set; }
    public List<Zone> Zones { get; set; }
    public List<FurnitureElement> Elements { get; set; }
    public List<SpatialRelation> SpatialRelations { get; set; }
    public List<PendingCommit> PendingCommits { get; set; }
}

// FurnitureElement - 家具元素
public class FurnitureElement
{
    public string Id { get; set; }
    public string FamilyId { get; set; }
    public string FamilyName { get; set; }
    public string Category { get; set; }
    public Point2D Position { get; set; }
    public GridPosition GridPosition { get; set; }
    public double Rotation { get; set; }
    public Bounds3D Bounds { get; set; }
    public string ZoneId { get; set; }
    public VisualInfo Visual { get; set; }
    public RevitMapping RevitMapping { get; set; }
    public ElementMetadata Metadata { get; set; }
}
```

#### 6.1.2 空间计算

```csharp
// 碰撞检测
public class CollisionDetector
{
    public bool HasCollision(FurnitureElement element, CanvasDocument document);
    public List<FurnitureElement> GetCollisions(FurnitureElement element, CanvasDocument document);
}

// 空间关系计算
public class RelationCalculator
{
    // 计算所有空间关系
    public List<SpatialRelation> CalculateRelations(CanvasDocument document);

    // 计算单个元素的关系
    public List<SpatialRelation> CalculateElementRelations(string elementId, CanvasDocument document);
}

// 网格对齐
public class GridHelper
{
    public Point2D SnapToGrid(Point2D position, int gridSize);
    public GridPosition ToGridPosition(Point2D position, int gridSize);
    public Point2D FromGridPosition(GridPosition gridPos, int gridSize);
}
```

### 6.2 BIMCanvas.MCP.Canvas

**职责**：提供画布操作的 MCP 工具集

#### 6.2.1 MCP 工具列表

| 工具名 | 功能 | 参数 |
|--------|------|------|
| **画布管理** |
| `canvas_create` | 创建画布 | `name`, `width`, `height`, `revitData?` |
| `canvas_describe` | 获取画布描述（AI 友好） | `canvasId` |
| `canvas_get_state` | 获取完整 JSON 状态 | `canvasId` |
| `canvas_screenshot` | 获取画布截图 | `canvasId`, `format?` |
| `canvas_export` | 导出 JSON 文件 | `canvasId`, `filePath` |
| **元素操作** |
| `element_add` | 添加元素 | `canvasId`, `expectedVersion`, `familyId`, `position`, `rotation?`, `intent` |
| `element_move` | 移动元素 | `canvasId`, `expectedVersion`, `elementId`, `position`, `intent` |
| `element_rotate` | 旋转元素 | `canvasId`, `expectedVersion`, `elementId`, `angle`, `intent` |
| `element_delete` | 删除元素 | `canvasId`, `expectedVersion`, `elementId`, `intent` |
| `element_list` | 列出元素 | `canvasId`, `zoneId?` |
| **版本控制** |
| `canvas_get_changes` | 获取待处理变更 | `canvasId` |
| `canvas_ack_commits` | 确认已处理变更 | `canvasId`, `changeSetIds` |
| **查询分析** |
| `element_at` | 查询位置元素 | `canvasId`, `position` |
| `space_analyze` | 空间分析 | `canvasId` |
| `relation_get` | 获取元素关系 | `canvasId`, `elementId` |

#### 6.2.2 canvas_describe 返回示例

```json
{
  "version": 42,
  "timestamp": "2025-12-02T15:30:00Z",
  "text": "客厅区域（8m × 6m）：北侧靠窗放置三人沙发，面向电视墙方向；沙发前方650mm处有圆形茶几；东北角落设有阅读角，放置单人椅和落地灯。阅读角：放置阅读单椅，靠近窗户利用自然光。",
  "summary": {
    "totalElements": 3,
    "byZone": { "zone_living": 2, "zone_reading": 1 },
    "pendingCommits": 0
  },
  "staleAfterMs": 30000
}
```

### 6.3 BIMCanvas.Web

**职责**：Web 前端应用，JSON → SVG 渲染

#### 6.3.1 核心组件

```typescript
// stores/canvasStore.ts
export const useCanvasStore = defineStore('canvas', {
  state: () => ({
    document: null as CanvasDocument | null,  // JSON 数据
    selectedElementIds: [] as string[],
    pendingChanges: [] as ElementChange[],    // 本地未提交的修改
  }),

  actions: {
    // 从服务器加载画布
    async loadCanvas(canvasId: string),

    // 本地修改元素
    moveElement(elementId: string, position: Point2D),

    // 提交修改到服务器
    async commitChanges(summary: string),
  }
});
```

#### 6.3.2 JSON → SVG 渲染

```typescript
// services/svgRenderer.ts
export class SvgRenderer {
  // 根据 JSON 生成 SVG
  render(document: CanvasDocument): SVGElement {
    const svg = this.createSvgRoot(document.bounds);

    // 渲染建筑结构（锁定）
    this.renderStructure(svg, document.structure);

    // 渲染家具元素
    for (const element of document.elements) {
      this.renderFurniture(svg, element);
    }

    return svg;
  }

  // 渲染单个家具
  private renderFurniture(svg: SVGElement, element: FurnitureElement) {
    const g = this.createGroup(element.id);
    g.setAttribute('transform', `translate(${element.position.x}, ${element.position.y}) rotate(${element.rotation})`);

    if (element.visual.svgAvailable) {
      // 使用真实 SVG Symbol
      this.useSymbol(g, element.visual.svgSymbolId);
    } else {
      // 使用占位符
      this.renderPlaceholder(g, element);
    }

    svg.appendChild(g);
  }
}
```

---

## 7. 开发阶段规划

### Phase 1: 核心基础（MVP）

**目标**：AI 可以在画布上设计，Web 可以显示

| 任务 | 项目 | 说明 |
|------|------|------|
| 实现数据模型 | BIMCanvas.Core | CanvasDocument, FurnitureElement 等 |
| 实现空间计算 | BIMCanvas.Core | 碰撞检测、网格对齐、关系计算 |
| 实现 Canvas-MCP | BIMCanvas.MCP.Canvas | 基础工具：create, add, move, delete |
| 实现 Web 后端 | BIMCanvas.Web.Server | SignalR Hub, REST API |
| 实现 Web 前端 | BIMCanvas.Web | JSON → SVG 渲染、基础交互 |

### Phase 2: 协作编辑

**目标**：AI 和用户可以实时协作

| 任务 | 项目 |
|------|------|
| 实现 Commit 同步机制 | Web.Server + Web |
| 实现元素拖拽/旋转 | BIMCanvas.Web |
| 实现 Library-MCP | BIMCanvas.MCP.Library |
| 实现 Visual Fallback | Library-MCP |
| 实现截图服务 | Web.Server |

### Phase 3: Revit 集成

**目标**：完整的 Revit 双向同步

| 任务 | 项目 |
|------|------|
| 实现 Revit → JSON 导出 | Revit-MCP + Core |
| 实现 Ribbon 面板 | BIMCanvas.Revit |
| 实现配置窗口 | BIMCanvas.Revit |
| 实现 JSON → Revit 同步 | Revit-MCP + Core |

---

## 8. 技术选型总结

| 组件 | 技术选择 | 版本 |
|------|----------|------|
| **Core 类库** | .NET Standard | 2.0 |
| **MCP Server** | .NET | 6.0+ |
| **Web 后端** | ASP.NET Core | 6.0+ |
| **实时通信** | SignalR | - |
| **Web 前端** | Vue 3 + TypeScript | 3.x |
| **前端构建** | Vite | 5.x |
| **状态管理** | Pinia | 2.x |
| **数据格式** | JSON | - |
| **渲染格式** | SVG | - |

---

## 附录

### A. 参考文档

- [Schema-JSON.md](./Schema-JSON.md) - JSON Schema 完整规范
- [PRD.md](./PRD.md) - 产品需求文档
- [ExpertReviews.md](./ExpertReviews.md) - 专家评审记录
- [MCP 协议规范](https://modelcontextprotocol.io/)
- [Revit API 文档](https://www.revitapidocs.com/)

### B. 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v1.0 | 2025-12-01 | 初始版本 |
| v2.0 | 2025-12-02 | 重大更新：采纳专家评审结论，修正 .NET 兼容性，改用 JSON 核心数据格式 |
