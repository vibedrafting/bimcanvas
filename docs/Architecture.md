# BIMCanvas 系统架构文档

> 版本：v2.7
> 更新日期：2025-12-06
> 状态：已定稿（后端项目合并：BIMCanvas.MCP.Canvas + BIMCanvas.Web.Server → BIMCanvas.Server）

---

## 0. 程序执行流程

> **核心原则**：KISS - Keep It Simple, Stupid
>
> 详细执行流程见：**[Workflows.md](./Workflows.md)**

### 0.1 流程总览

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           BIMCanvas 完整执行流程                              │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Phase 1: 数据准备                                                           │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │ 1. 提取原建筑信息 → 2. 生成项目配置要求 → 3. 划分工作区                   │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                         │
│  Phase 2: 素材准备                                                           │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │ 4. 准备设计素材（家具）                                                   │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                         │
│  Phase 3: 方案生成与交互                                                     │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │ 5. 家具布置 → 6. 交互式修改 ←→ (循环迭代)                                 │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                    ↓                                         │
│  Phase 4: 应用与反馈                                                         │
│  ┌─────────────────────────────────────────────────────────────────────────┐ │
│  │ 7. 应用到Revit → 8. 用户反馈 → 9. 重新应用 → 10. 记录布置结果             │ │
│  └─────────────────────────────────────────────────────────────────────────┘ │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 0.2 MVP 执行流程（Phase 1 精简版）

```
Phase 1: 数据提取（Revit 层）
═══════════════════════════════════════════════════════════════════════════════
1. 用户在 Revit 中激活目标平面视图
2. 点击"开始设计"按钮
3. 插件端提取原始数据：
   - outline.walls: 墙体轮廓多边形
   - outline.openings: 门窗线段
   - rooms[]: 物理房间（边界 + 类型）
4. 生成精简版 CanvasDocument JSON（zones/wallFinishes/modules 为空数组）
5. POST 到 BIMCanvas.Server


Phase 2: 数据处理（Server 层）
═══════════════════════════════════════════════════════════════════════════════
6. ZoneCalculator 计算：
   - rooms[] → zones[]（从房间生成设计区）
   - zones[].innerBoundary: 可用空间（已扣除完成面）
   - zones[].exclusionAreas: 门扇禁区（简化矩形）
   - wallFinishes[]: 墙面完成面禁区
7. WebSocket 推送完整版 CanvasDocument 到 Web 端
8. Web 端显示户型底图


Phase 3: 区域确认
═══════════════════════════════════════════════════════════════════════════════
9. AI 根据房间类型推断 zones[].tags
10. Web 端显示功能标签
11. 用户确认/修改各区域功能


Phase 4: 方案生成
═══════════════════════════════════════════════════════════════════════════════
12. AI 直接生成布置方案（不做候选清单）
   - 遵循约束：innerBoundary 内、避开 exclusionAreas
13. Web 端显示完整平面布置图


Phase 5: 交互修改
═══════════════════════════════════════════════════════════════════════════════
14. 用户可以：
    a) 在 Web 端拖拽调整家具位置
    b) 通过对话指导 AI 修改（如"把床转90度"）
15. 循环迭代直到满意


Phase 6: 回写 Revit
═══════════════════════════════════════════════════════════════════════════════
16. 用户确认最终方案
17. 调用 Revit-MCP：
    - load_family_from_library 加载族
    - create_element 创建家具（基于 levelId + position）
18. Revit 中显示布置结果
```

**MVP 流程简化点**：
- 砍掉"候选清单"和"多方案对比"
- 砍掉"设计知识库"和"户型记忆"
- AI 直出方案，对话调整

### 0.3 核心设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **坐标系** | Y-Up (笛卡尔) | 符合 CAD/BIM/数学直觉，只在前端渲染时转换 |
| **数据分层** | Layer 1 (AI 上下文) + Layer 2 (详细数据，Phase 1 暂缓) | Token 效率，职责清晰 |
| **墙体表示** | 封闭轮廓多边形 | AI 不需要理解墙体结构，只需知道空间边界 |
| **门窗表示** | 简化为线段 | 厚度不影响家具布置 |
| **门扇区域** | 预计算为矩形禁区（AABB） | KISS - AI 只需知道"这里不能放" |
| **房间结构** | 只有 zones，无 rooms | 单一数据源原则，zones 是设计概念 |
| **标高信息** | 全局 levelId | 一张平面图对应一个 Level |
| **布置单元** | modules（模块） | 支持单一家具或组合（如睡眠模块=床+床头柜） |
| **模块位置** | AABB 包围盒 | 直观显示占用空间，碰撞检测简单 |
| **模块朝向** | 语义化方向（north/south/...） | AI 友好，插件端转换为角度 |
| **多方案** | Phase 1 不做 | MVP 先让 AI 直出方案，对话调整 |

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
├─────────────────────────┬───────────────────────────────────────────────────┤
│       Revit-MCP         │              Library-MCP                          │
│       (已有基础)         │              (族库工具)                            │
│                         │                                                   │
│  • 提取建筑结构          │  • 搜索族资源                                      │
│  • 创建Revit元素         │  • 获取族信息                                      │
│  • 查询模型信息          │  • 获取SVG预览                                     │
│  • 视图截图             │  • Visual Fallback                                │
│                         │                                                   │
│  .NET FW 4.7.2          │  .NET 6+                                          │
│  (Revit限制)            │  (引用Core)                                        │
└─────────────────────────┴───────────────────────────────────────────────────┘
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
                 ┌────────────────────┴────────────────────┐
                 │                                         │
                 ▼                                         ▼
┌─────────────────────────────────────┐  ┌─────────────────────────────────────┐
│     BIMCanvas.Agent (Python)        │  │       BIMCanvas.Server (.NET 6+)    │
│     PlacementAgent 服务              │  │       统一后端服务                   │
├─────────────────────────────────────┤  ├─────────────────────────────────────┤
│  • 基于 Anthropic Agent SDK         │  │  McpTools/     Canvas-MCP 工具      │
│  • 长期运行的 AI Agent              │  │  Controllers/  REST API + SSE       │
│  • SSE 事件监听                     │  │  Hubs/         SignalR Hub          │
│  • MCP 工具集成                     │  │  Services/     状态管理、EventBus   │
│  Python 3.10+                       │  │  .NET 6+                            │
└─────────────────────────────────────┘  └─────────────────────────────────────┘
         ↑ SSE 事件                               │
         │                                        │
         └────────────────────────────────────────┘
                                      │
                                      │ HTTP / WebSocket
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BIMCanvas.Web                                      │
│                      Web 前端应用 (Vue 3 + TypeScript)                        │
├─────────────────────────────────────────────────────────────────────────────┤
│  • JSON → SVG 动态渲染               • 元素拖拽/旋转/缩放                     │
│  • 实时状态同步                       • 批注绘制工具                          │
│  • 撤销/重做                         • 「一键布置」按钮 → EventBus            │
└─────────────────────────────────────────────────────────────────────────────┘
```

> **架构说明**：
> - `BIMCanvas.Server`：合并了原 Canvas-MCP 和 Web.Server，新增 EventBus + SSE 端点支持事件驱动
> - `BIMCanvas.Agent`：v2.6 新增，基于 Agent SDK 的独立 Python 服务，负责 AI 布置规划

### 2.2 数据流向

```
【从 Revit 到画布】
Revit 模型
    → [BIMCanvas.Revit] 提取原始数据
        - 墙体 → outline.walls (轮廓多边形)
        - 门窗 → outline.openings (线段)
        - 房间 → rooms[] (边界 + 类型)
    → 生成精简版 CanvasDocument (zones/wallFinishes/modules 为空数组)
    → [POST] 发送到 BIMCanvas.Server
    → [BIMCanvas.Server: ZoneCalculator] 数据处理
        - rooms[] → zones[] (生成设计区)
        - 计算 zones[].innerBoundary (扣除完成面)
        - 计算 zones[].exclusionAreas (门扇禁区)
        - 计算 wallFinishes[] (墙面完成面禁区)
    → [WebSocket] 推送完整版 CanvasDocument 到 Web
    → [BIMCanvas.Web] JSON → SVG 渲染显示

【AI 布置方案】
AI 理解用户需求
    → [Library-MCP: module_search] 搜索合适的模块/家具
    → [BIMCanvas.Server: McpTools] 修改 JSON 数据
        - 约束检查：bounds 在 innerBoundary 内
        - 避障检查：不与 exclusionAreas 重叠
        - 碰撞检查：不与其他 modules 重叠
    → [WebSocket] 实时推送 JSON 变更
    → [BIMCanvas.Web] 重新渲染 SVG

【用户交互修改】
用户在 Web 画布操作（拖拽家具）
    → [前端] 修改本地 JSON 状态（modules 数组）
    → [WebSocket] 发送变更到 Server
    → [用户点击 Commit 按钮] 生成 change_set
    → [BIMCanvas.Server: McpTools] AI 查询变更
    → AI 感知变化并响应

【同步回 Revit】
设计方案确定
    → [BIMCanvas.Server] 导出 JSON
    → [BIMCanvas.Core: JsonToRevitConverter] 解析 modules
        - 遍历 modules[].items
        - 计算各 item 的世界坐标 (bounds 中心 + offset)
        - 转换 facing → 旋转角度
    → [Revit-MCP: load_family_from_library] 加载族
    → [Revit-MCP: create_element] 创建 Revit 元素（基于 levelId）
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
│   ├── BIMCanvas.Core.csproj                       依赖: Newtonsoft.Json, NetTopologySuite
│   │
│   ├── Models/                                  【目录】数据模型
│   │   ├── Primitives/                             【目录】几何基元
│   │   │   ├── Point2D.cs                             readonly struct, 坐标点
│   │   │   ├── Vec2D.cs                               readonly struct, 向量
│   │   │   ├── Line2D.cs                              线段
│   │   │   ├── Polygon2D.cs                           多边形, 封装 Point2D[]
│   │   │   └── AABB.cs                                轴对齐包围盒
│   │   │
│   │   └── Document/                               【目录】业务模型（扁平化）
│   │       ├── CanvasDocument.cs                      画布文档（根对象）
│   │       ├── Metadata.cs                            元数据
│   │       ├── Outline.cs                             可视化底图
│   │       ├── Wall.cs                                墙体轮廓
│   │       ├── Opening.cs                             门窗
│   │       ├── Room.cs                                物理房间（对应 Revit Room）
│   │       ├── RoomType.cs                            房间类型枚举
│   │       ├── Zone.cs                                设计区域
│   │       ├── ZoneTag.cs                             区域功能标签枚举
│   │       ├── ExclusionArea.cs                       禁止布置区
│   │       ├── WallFinish.cs                          墙面完成面
│   │       ├── FinishSource.cs                        完成面来源枚举
│   │       ├── Module.cs                              布置模块
│   │       ├── ModuleItem.cs                          模块内部家具
│   │       ├── Facing.cs                              朝向（联合类型封装）
│   │       └── FacingDirection.cs                     朝向方向枚举
│   │
│   ├── Algorithms/                              【目录】空间计算
│   │   ├── Geometry/                               【目录】简单数学运算
│   │   │   ├── GeometryHelper.cs                      AABB 计算、中心点、旋转
│   │   │   └── NtsAdapter.cs                          internal: Polygon2D ↔ NTS 转换
│   │   │
│   │   └── Spatial/                                【目录】空间业务逻辑
│   │       ├── CollisionDetector.cs                   碰撞检测（调用 NTS）
│   │       ├── FacingHelper.cs                        方向语义 ↔ Vec2D
│   │       ├── GeometryNormalizer.cs                  AI 意图 → Polygon2D
│   │       ├── PlacementValidator.cs                  布置验证（只验证，不修正）
│   │       └── FinishRules.cs                         特殊完成面规则表
│   │
│   ├── Converters/                              【目录】转换器
│   │   ├── UnitConverter.cs                        单位转换（feet↔mm, rad↔deg）
│   │   ├── Json/                                   【目录】自定义序列化器
│   │   │   ├── Point2DConverter.cs                    [x, y] 格式
│   │   │   └── FacingConverter.cs                     "north" | [dx, dy] 格式
│   │   │
│   │   └── Revit/                                  【目录】Revit 数据转换
│   │       ├── RevitToJsonConverter.cs                Revit数据 → JSON
│   │       └── JsonToRevitConverter.cs                JSON → Revit数据
│   │
│   └── Validation/                              【目录】验证基础设施
│       └── Result.cs                               Result<T, TError> 类型
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
├── BIMCanvas.Server/                         【项目】统一后端服务 (.NET 6+)
│   ├── BIMCanvas.Server.csproj
│   ├── Program.cs                               入口（同时启动 MCP + Web Host）
│   ├── Mcp/                                     【目录】MCP 协议相关
│   │   └── McpHost.cs                              MCP Server 宿主
│   ├── McpTools/                                【目录】MCP 工具实现
│   │   ├── CanvasTools.cs                          画布管理工具
│   │   ├── ModuleTools.cs                          模块操作工具
│   │   ├── PlacementTools.cs                       布置工具
│   │   └── QueryTools.cs                           查询工具
│   ├── Controllers/                             【目录】REST API
│   │   ├── CanvasController.cs                     画布 API
│   │   └── PlacementController.cs                  布置 API
│   ├── Hubs/                                    【目录】SignalR Hub
│   │   └── CanvasHub.cs                            画布实时通信
│   └── Services/                                【目录】业务服务
│       ├── CanvasStateManager.cs                   画布状态管理
│       ├── ZoneCalculator.cs                       Zone 计算
│       ├── PlacementService.cs                     布置逻辑核心
│       ├── PlacementAgentBridge.cs                 AI Agent 桥接
│       ├── ScreenshotService.cs                    截图服务
│       └── ChangeSetService.cs                     变更集服务
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
├── BIMCanvas.Agent/                          【项目】PlacementAgent (Python 3.10+)
│   ├── pyproject.toml                            依赖管理 (Poetry)
│   ├── requirements.txt                          依赖清单
│   │
│   └── src/
│       ├── __init__.py
│       ├── main.py                               入口（启动 Agent + EventListener）
│       │
│       ├── agent/                             【目录】Agent 核心
│       │   ├── __init__.py
│       │   ├── placement_agent.py                PlacementAgent 实现
│       │   └── prompts.py                        系统提示词
│       │
│       ├── events/                            【目录】事件处理
│       │   ├── __init__.py
│       │   ├── listener.py                       SSE 事件监听器
│       │   ├── handlers.py                       事件处理器
│       │   └── models.py                         事件数据模型
│       │
│       ├── mcp/                               【目录】MCP 工具集成
│       │   ├── __init__.py
│       │   └── canvas_client.py                  Canvas-MCP 客户端
│       │
│       └── config/                            【目录】配置
│           ├── __init__.py
│           └── settings.py                       配置管理
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
| **BIMCanvas.Server** | Web 应用 | .NET 6+ | **统一后端服务**（MCP 工具 + REST API + SignalR + EventBus） |
| **BIMCanvas.MCP.Library** | 控制台 | .NET 6+ | 族库 MCP Server |
| **BIMCanvas.Agent** | Python 应用 | **Python 3.10+** | **PlacementAgent 服务**（基于 Agent SDK，SSE 事件驱动） |
| **BIMCanvas.Web** | 前端应用 | Node.js | Vue 3 + TypeScript + Vite |

> **注**：
> - 原 `BIMCanvas.MCP.Canvas` 和 `BIMCanvas.Web.Server` 已合并为 `BIMCanvas.Server`
> - v2.6 新增 `BIMCanvas.Agent`，基于 Anthropic Agent SDK 实现 AI 布置规划

### 3.3 项目依赖关系

```
                    ┌─────────────────────────┐
                    │    BIMCanvas.Core       │
                    │  (.NET Standard 2.0)    │
                    │  Newtonsoft.Json + NTS  │
                    │                         │
                    │  Models/                │
                    │  Algorithms/            │
                    │  Converters/            │
                    │  Validation/            │
                    └─────────────────────────┘
                              ▲
                              │ 引用
          ┌───────────────────┼───────────────────┬───────────────────┐
          │                   │                   │                   │
          ▼                   ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────────────────────┐
│ BIMCanvas.MCP   │ │ BIMCanvas.Revit │ │       BIMCanvas.Server          │
│ .Library        │ │                 │ │         (.NET 6+)               │
│ (.NET 6+)       │ │ (.NET FW 4.7.2) │ │                                 │
│                 │ │                 │ │  MCP 工具 + REST API + SignalR  │
└─────────────────┘ └─────────────────┘ └─────────────────────────────────┘
                                                    │
                                                    │ HTTP / WebSocket
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

## 3.5 AI 交互层架构

### 核心隐喻：AI 是 "OBB 规划师"

> **AI 不计算几何，只决策位置。**

AI 的职责被限定为**定向包围盒 (OBB) 规划**：
- **输入**：空间状态 (Polygon2D + 计算属性)
- **输出**：意图指令 (moduleId + params + center + facing)
- **转换**：Core 层负责 Intent → Polygon2D

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           AI 交互层架构                                      │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   【AI 视图】                                                                │
│   世界由无数个 OBB (矩形盒子) 组成。无论家具是 L 型还是圆形，                  │
│   AI 只操作其外接矩形。AI 仅保证 OBB 不重叠。                                 │
│                                                                             │
│   【职责边界】                                                               │
│   • AI：选择模块 + 确定包围盒位置/朝向                                        │
│   • Library-MCP：提供模块的精确轮廓定义                                       │
│   • Core (Normalizer)：根据位置/朝向计算精确轮廓的 Polygon2D                  │
│   • Web：渲染精确轮廓                                                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 数据流架构

```
模块库 (Canonical Polygon + Parameters)
                    ↓
AI 输出 (Intent: moduleId + params + center + facing)
                    ↓
Core Normalizer (Intent → Polygon2D)
                    ↓
JSON 存储 (bounds: Polygon2D + facing + moduleId)
                    ↓
Web 渲染 (Polygon2D → SVG)
```

### 多样化输出策略 (Polymorphic Output)

AI 输出采用混合策略，兼顾 Token 效率和场景覆盖率：

| 场景 | 推荐输出格式 | 示例 | 占比 |
|------|--------------|------|------|
| **标准正交** | Semantic | `{ center: [x,y], facing: "north" }` | 90% |
| **任意倾斜** | Vec2D | `{ center: [x,y], facing: [0.866, 0.5] }` | 10% |
| **特殊微调** | Polygon2D | `{ bounds: [[x1,y1]...] }` | <1% |

**Core 层作为归一化器 (Normalizer)**，将上述所有格式统一转换为 `Polygon2D` 进行存储和计算。

### AI 输入格式

AI 接收的空间状态数据包含：

| 数据类别 | 格式 | 说明 |
|----------|------|------|
| **Zone (设计区)** | `innerBoundary: Polygon2D` | 可用空间边界 |
| **ExclusionArea (禁区)** | `boundary: Polygon2D` | 禁止布置区域 |
| **Walls (墙体)** | `polygon: Polygon2D` | 墙体轮廓多边形 |
| **Openings (门窗)** | `line: Line2D` | 门窗线段 |
| **Modules (已有家具)** | `bounds: Polygon2D` + `_computed` | 精确边界 + 计算属性 |

**计算属性 `_computed`**（动态生成，不持久化）：

```json
{
  "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
  "facing": "north",
  "_computed": {
    "center": [3000, 3250],
    "size": [3000, 2500]
  }
}
```

---

## 4. BIMCanvas.Server 统一后端架构

### 4.1 架构概述

`BIMCanvas.Server` 是统一的后端服务，整合了 MCP 工具、REST API 和 SignalR 实时通信：

```
┌─────────────────────────────────────────────────────────────┐
│                      BIMCanvas.Server                        │
│                        (.NET 6+)                             │
├─────────────────────────────────────────────────────────────┤
│                                                             │
│   ┌─────────────────┐      ┌─────────────────────────────┐  │
│   │  McpTools/      │ ←──→ │  CanvasStateManager         │  │
│   │  MCP 工具实现    │      │  (内存中的画布状态)          │  │
│   └─────────────────┘      └─────────────────────────────┘  │
│            ↑                           ↑                    │
│            │                           │                    │
│   ┌────────┴────────┐      ┌───────────┴───────────────┐    │
│   │  MCP Protocol   │      │  SignalR Hub + REST API   │    │
│   │  (stdio)        │      │  (WebSocket + HTTP)       │    │
│   └─────────────────┘      └───────────────────────────┘    │
│                                                             │
│            ↑                           ↑                    │
│   ┌────────┴────────┐      ┌───────────┴───────────────┐    │
│   │  Services/      │      │  PlacementService         │    │
│   │  ZoneCalculator │      │  PlacementAgentBridge     │    │
│   └─────────────────┘      └───────────────────────────┘    │
│                                                             │
└─────────────────────────────────────────────────────────────┘
         ↑                               ↑
         │                               │
    Claude Code                     Web Browser
```

**架构优点：**
- 无跨进程通信，状态天然一致
- 单一部署单元，运维简单
- MCP 工具与 REST API 共享业务逻辑

> **历史说明**：原设计将 Canvas-MCP 和 Web.Server 设计为同进程运行，现已正式合并为单一项目 `BIMCanvas.Server`。

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
│  Claude Code  │         │  BIMCanvas.Server   │         │    用户       │
│  (AI Agent)   │         │                     │         │  Web Browser  │
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

> **核心定位**：「薄」数据契约 + 语义桥梁
> - ✅ 定义通用数据模型（CanvasDocument 及其子结构）
> - ✅ 实现 AI 语义 → 几何数据的转换
> - ✅ 提供单位转换（feet↔mm, rad↔deg）
> - ❌ 不做复杂几何运算（委托给 NetTopologySuite）
> - ❌ 不做浮点精度处理（由调用方负责）

**NuGet 依赖**：
- `Newtonsoft.Json` (13.0.3) - JSON 序列化
- `NetTopologySuite` (2.x) - 几何运算

#### 6.1.1 数据模型

详细定义见：[Schema-JSON.md](./Schema-JSON.md)

**几何基元 (Models/Primitives/)**：

```csharp
// Point2D - 坐标点（readonly struct，类型安全）
public readonly struct Point2D
{
    public double X { get; }
    public double Y { get; }

    public Point2D(double x, double y) => (X, Y) = (x, y);
}

// Vec2D - 向量（结构同 Point2D，语义不同）
public readonly struct Vec2D
{
    public double X { get; }
    public double Y { get; }

    public Vec2D(double x, double y) => (X, Y) = (x, y);
    public Vec2D Normalize() => ...;
}

// Polygon2D - 多边形（封装 Point2D[]）
public class Polygon2D
{
    public Point2D[] Vertices { get; }

    public Polygon2D(Point2D[] vertices) => Vertices = vertices;
    public AABB ComputeAABB() => ...;
    public Point2D ComputeCenter() => ...;
}

// FacingDirection - 朝向方向枚举
public enum FacingDirection
{
    [EnumMember(Value = "north")] North,
    [EnumMember(Value = "south")] South,
    [EnumMember(Value = "east")] East,
    [EnumMember(Value = "west")] West,
    [EnumMember(Value = "northeast")] Northeast,
    [EnumMember(Value = "northwest")] Northwest,
    [EnumMember(Value = "southeast")] Southeast,
    [EnumMember(Value = "southwest")] Southwest
}

// Facing - 朝向（联合类型封装）
public readonly struct Facing
{
    public bool IsSemantic { get; }
    public FacingDirection? Semantic { get; }  // 枚举类型
    public Vec2D? Vector { get; }              // 单位向量

    public static implicit operator Facing(FacingDirection d) => ...;
    public static implicit operator Facing(Vec2D v) => ...;
    public double ToAngleRadians() => ...;
}
```

**业务模型 (Models/Document/)**：

```csharp
// RoomType - 房间类型枚举
public enum RoomType
{
    LivingRoom, DiningRoom, MasterBedroom, Bedroom, Study,
    Kitchen, Bathroom, Entrance, Balcony, Corridor, Storage
}

// Room - 物理房间（对应 Revit Room）
public class Room
{
    public string Id { get; set; }
    public string Name { get; set; }
    public RoomType Type { get; set; }
    public Polygon2D? Boundary { get; set; }
}

// ZoneTag - 区域功能标签枚举（细粒度）
public enum ZoneTag
{
    // 多媒体/视听
    TvMedia, AudioVideo,
    // 休息/睡眠
    Sleep, Rest, Reading,
    // 工作/学习
    Work, Study,
    // 收纳
    WardrobeStorage, ShoeStorage, GeneralStorage,
    // 餐饮
    Dining, Cooking, FoodPrep, Bar,
    // 卫浴
    Shower, Bathtub, Toilet, Washing, Laundry,
    // 其他
    Vanity, Entry, Passage, Display, Plants
}

// FinishSource - 完成面来源
public enum FinishSource
{
    RoomDefault,   // 房间类型默认值
    ZoneOverride,  // 工作区标签覆盖
    UserOverride   // 用户手动设置
}

// WallFinish - 墙面完成面
// 设计意图：完成面是一种禁区机制，与门扇禁区类似
// 核心逻辑链：三层来源机制(Source) → 完成面类型(FinishModuleId) → 厚度(Thickness)
//   - RoomDefault: Room.Type 查配置 → 默认完成面类型 → 厚度
//   - ZoneOverride: Zone.Tags 匹配规则 → 特殊完成面类型 → 厚度（如 tv_media → 护墙板 → 80mm）
//   - UserOverride: 用户手动选择完成面类型 → 厚度
public class WallFinish
{
    public string Id { get; set; }
    public Line2D? LocationLine { get; set; }      // 位置：定位线（靠墙侧，方向顺房间）
    public string? FinishModuleId { get; set; }    // 类型：完成面模块库 ID，决定做法和厚度
    public double Thickness { get; set; }          // 厚度（mm），由 FinishModuleId 查模块库获得
    public Polygon2D? ExclusionBoundary { get; set; }  // 禁区轮廓（由 LocationLine + Thickness 计算）
    public string WallId { get; set; }             // 关联墙体 ID
    public string RoomId { get; set; }             // 关联房间 ID（决定是墙的哪一侧）
    public FinishSource Source { get; set; }       // 来源（决定 FinishModuleId 的优先级）
}

// CanvasDocument - 画布文档根对象
public class CanvasDocument
{
    public string Id { get; set; }
    public int Version { get; set; }
    public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";
    public Metadata Metadata { get; set; }
    public Outline Outline { get; set; }
    public List<Room> Rooms { get; set; }           // 新增：物理房间列表
    public List<Zone> Zones { get; set; }
    public List<WallFinish> WallFinishes { get; set; }  // 新增：墙面完成面列表
    public List<Module> Modules { get; set; }
}

// Zone - 设计区域
public class Zone
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string RoomId { get; set; }              // 新增：所属房间 ID
    public List<ZoneTag> Tags { get; set; }         // 替代 Function，支持多标签
    public Polygon2D? RawBoundary { get; set; }     // 新增：原始边界（未扣除完成面）
    public Polygon2D InnerBoundary { get; set; }    // 可用空间（已扣除完成面）
    public List<ExclusionArea> ExclusionAreas { get; set; }
    public List<string> Openings { get; set; }
}

// Module - 布置模块
public class Module
{
    public string Id { get; set; }
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public Polygon2D Bounds { get; set; }  // 精确边界（矩形 4 顶点）
    public Facing Facing { get; set; }     // 类型安全的朝向
    public string ZoneId { get; set; }
    public List<ModuleItem> Items { get; set; }
}
```

#### 6.1.2 空间计算

**Geometry/ - 简单数学运算**：

```csharp
// GeometryHelper - 基础几何运算（不依赖 NTS）
public static class GeometryHelper
{
    public static AABB ComputeAABB(Polygon2D polygon) => ...;
    public static Point2D ComputeCenter(Polygon2D polygon) => ...;
    public static Polygon2D RotatePolygon(Polygon2D polygon, double angleRadians, Point2D center) => ...;
}

// NtsAdapter - 内部适配器（Polygon2D ↔ NTS 转换）
internal static class NtsAdapter
{
    internal static NetTopologySuite.Geometries.Polygon ToNtsPolygon(Polygon2D polygon) => ...;
    internal static Polygon2D FromNtsPolygon(NetTopologySuite.Geometries.Polygon nts) => ...;
}
```

**Spatial/ - 空间业务逻辑**：

```csharp
// CollisionDetector - 碰撞检测（委托给 NTS）
public static class CollisionDetector
{
    /// <summary>检查两个多边形是否相交</summary>
    public static bool Intersects(Polygon2D a, Polygon2D b)
    {
        var ntsA = NtsAdapter.ToNtsPolygon(a);
        var ntsB = NtsAdapter.ToNtsPolygon(b);
        return ntsA.Intersects(ntsB);
    }

    /// <summary>检查多边形 a 是否完全在 b 内部</summary>
    public static bool IsWithin(Polygon2D inner, Polygon2D outer)
    {
        var ntsInner = NtsAdapter.ToNtsPolygon(inner);
        var ntsOuter = NtsAdapter.ToNtsPolygon(outer);
        return ntsOuter.Contains(ntsInner);
    }
}

// FacingHelper - 方向语义 ↔ Vec2D 转换
public static class FacingHelper
{
    /// <summary>语义方向 → 单位向量</summary>
    public static Vec2D SemanticToVector(string semantic) => semantic.ToLower() switch
    {
        "north"     => new Vec2D(0, 1),
        "south"     => new Vec2D(0, -1),
        "east"      => new Vec2D(1, 0),
        "west"      => new Vec2D(-1, 0),
        "northeast" => new Vec2D(1, 1).Normalize(),
        // ... 其他方向
        _ => throw new ArgumentException($"Unknown facing: {semantic}")
    };

    /// <summary>角度（度）→ 单位向量</summary>
    public static Vec2D AngleToVector(double degrees) => ...;
}
```

#### 6.1.3 几何归一化与布置验证

**职责分层**：
- `GeometryNormalizer`：纯几何转换（AI 意图 → Polygon2D）
- `PlacementValidator`：布置验证（只验证，不修正）

```csharp
// GeometryNormalizer - AI 布置意图 → Polygon2D
public static class GeometryNormalizer
{
    /// <summary>
    /// 根据 center + size + facing 创建矩形 Polygon2D
    /// </summary>
    public static Polygon2D CreateRectangle(Point2D center, Vec2D size, Facing facing)
    {
        var halfW = size.X / 2;
        var halfH = size.Y / 2;

        // 本地坐标（未旋转）
        var localCorners = new[]
        {
            new Point2D(-halfW, -halfH),
            new Point2D(halfW, -halfH),
            new Point2D(halfW, halfH),
            new Point2D(-halfW, halfH)
        };

        // 根据 facing 计算旋转角度
        var angle = facing.ToAngleRadians();

        // 旋转并平移到世界坐标
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var worldCorners = localCorners.Select(p => new Point2D(
            center.X + p.X * cos - p.Y * sin,
            center.Y + p.X * sin + p.Y * cos
        )).ToArray();

        return new Polygon2D(worldCorners);
    }
}

// PlacementValidator - 布置验证（只验证，不修正）
public static class PlacementValidator
{
    /// <summary>
    /// 验证模块布置是否合法
    /// </summary>
    /// <returns>Result&lt;bool, List&lt;Violation&gt;&gt;</returns>
    public static ValidationResult Validate(
        Polygon2D moduleBounds,
        Zone zone,
        IEnumerable<Module> existingModules)
    {
        var violations = new List<Violation>();

        // 约束1: 必须在 innerBoundary 内
        if (!CollisionDetector.IsWithin(moduleBounds, zone.InnerBoundary))
            violations.Add(new Violation("超出设计区域边界"));

        // 约束2: 不能与禁区重叠
        foreach (var exclusion in zone.ExclusionAreas ?? Enumerable.Empty<ExclusionArea>())
        {
            if (CollisionDetector.Intersects(moduleBounds, exclusion.Boundary))
                violations.Add(new Violation($"与禁区 {exclusion.Id} 重叠"));
        }

        // 约束3: 不能与其他模块重叠
        foreach (var existing in existingModules)
        {
            if (CollisionDetector.Intersects(moduleBounds, existing.Bounds))
                violations.Add(new Violation($"与模块 {existing.Id} 重叠"));
        }

        return violations.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(violations);
    }
}
```

**关键设计原则**：
- `PlacementValidator` **只做 Validation**，返回验证结果
- **不做 Correction**：「床头靠墙」是 AI 的规划职责，不是 Core 的修正职责
- 未来如需吸附功能，单独创建 `SnapHelper` 或 `ConstraintSolver`

#### 6.1.4 核心转换器 (UnitConverter)

**核心原则**：Core 层是单位转换的**唯一真理来源**。

```csharp
// BIMCanvas.Core/Converters/UnitConverter.cs
namespace BIMCanvas.Core.Converters
{
    /// <summary>
    /// 单位转换器 - 保留原始 Double 精度，无舍入
    /// </summary>
    public static class UnitConverter
    {
        // 长度转换常量
        public const double FeetToMm = 304.8;
        public const double MmToFeet = 1.0 / 304.8;

        // 角度转换常量
        public const double RadToDeg = 180.0 / Math.PI;
        public const double DegToRad = Math.PI / 180.0;

        // 长度转换（无舍入）
        public static double ToMillimeters(double feet) => feet * FeetToMm;
        public static double ToFeet(double mm) => mm * MmToFeet;

        // 角度转换（无舍入）
        public static double ToDegrees(double radians) => radians * RadToDeg;
        public static double ToRadians(double degrees) => degrees * DegToRad;

        /// <summary>
        /// 从 BasisX 向量计算旋转角度（度），范围 0-360
        /// </summary>
        public static double GetRotationFromBasisX(double basisX_X, double basisX_Y)
        {
            double radians = Math.Atan2(basisX_Y, basisX_X);
            double degrees = ToDegrees(radians);
            return degrees < 0 ? degrees + 360 : degrees;
        }
    }
}
```

**数据流中的调用时机**：

```
┌─────────────────────────────────────────────────────────────┐
│  Revit API (feet, radians)                                  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼  [BIMCanvas.Revit 调用]
┌─────────────────────────────────────────────────────────────┐
│  BIMCanvas.Core.Converters.UnitConverter                    │
│  ToMillimeters() / ToDegrees()                              │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼  JSON (mm, degrees)
┌─────────────────────────────────────────────────────────────┐
│  CanvasDocument                                             │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼  [回写 Revit 时调用]
┌─────────────────────────────────────────────────────────────┐
│  BIMCanvas.Core.Converters.UnitConverter                    │
│  ToFeet() / ToRadians()                                     │
└─────────────────────────────────────────────────────────────┘
```

### 6.2 BIMCanvas.Server

**职责**：统一后端服务，整合 MCP 工具、REST API、SignalR 实时通信、业务服务

#### 6.2.1 MCP 工具列表（McpTools/）

| 工具名 | 功能 | 参数 |
|--------|------|------|
| **画布管理** |
| `canvas_create` | 创建画布 | `revitViewId`, `levelId`, `outline`, `zones` |
| `canvas_describe` | 获取画布描述（AI 友好） | `canvasId` |
| `canvas_get_state` | 获取完整 JSON 状态 | `canvasId` |
| `canvas_screenshot` | 获取画布截图 | `canvasId`, `format?` |
| `canvas_export` | 导出 JSON 文件 | `canvasId`, `filePath` |
| **模块操作** |
| `module_add` | 添加模块 | `canvasId`, `expectedVersion`, `moduleId`, `bounds`, `facing`, `zoneId`, `items?` |
| `module_move` | 移动模块 | `canvasId`, `expectedVersion`, `id`, `bounds` |
| `module_rotate` | 旋转模块 | `canvasId`, `expectedVersion`, `id`, `facing` |
| `module_delete` | 删除模块 | `canvasId`, `expectedVersion`, `id` |
| `module_list` | 列出模块 | `canvasId`, `zoneId?` |
| **版本控制** |
| `canvas_get_changes` | 获取待处理变更 | `canvasId` |
| `canvas_ack_commits` | 确认已处理变更 | `canvasId`, `changeSetIds` |
| **查询分析** |
| `module_at` | 查询位置模块 | `canvasId`, `position` |
| `space_analyze` | 空间分析（检查可用空间） | `canvasId`, `zoneId` |

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

#### 6.2.3 业务服务层（Services/）

| 服务 | 职责 | 说明 |
|------|------|------|
| **CanvasStateManager** | 画布状态管理 | 内存中的 CanvasDocument 状态，支持版本控制 |
| **ZoneCalculator** | Zone 计算 | 从 Room 生成 Zone，计算 innerBoundary、exclusionAreas |
| **PlacementService** | 布置逻辑核心 | GenerateLayout、AdjustModule、ValidateLayout、AutoFix |
| **PlacementAgentBridge** | AI Agent 桥接 | 复杂决策时调用 Claude API（可选） |
| **ScreenshotService** | 截图服务 | JSON → SVG → PNG 渲染 |
| **ChangeSetService** | 变更集服务 | 用户修改的版本追踪与同步 |

#### 6.2.4 PlacementService 触发方式

| 触发方式 | 触发源 | 调用路径 | 场景 |
|----------|--------|----------|------|
| **AI 对话触发** | Claude Code | MCP: `generate_layout` → PlacementService | 用户对话指示 AI 布置 |
| **Web 按钮触发** | Web 前端 | REST: `POST /api/canvas/{id}/generate` | 用户点击"一键布置" |
| **自动修正触发** | Server 内部 | CanvasHub.OnLayoutError() → PlacementService.AutoFix() | 检测到布置错误 |

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

### 6.4 PlacementAgent 与 Agent SDK 集成

> **架构决策**：PlacementAgent 从 Server 内部服务迁移至基于 Anthropic Agent SDK 的独立 Python Agent，实现事件驱动的智能布置能力。

#### 6.4.1 架构设计

```
【PlacementAgent 架构】

┌─────────────────────────────────────────────────────────────────────────────┐
│                        BIMCanvas.Agent (Python 3.10+)                        │
│                         基于 Anthropic Agent SDK                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                               │
│   ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐         │
│   │ PlacementAgent  │    │  EventListener  │    │   MCP Client    │         │
│   │   布置决策核心   │◄───│   SSE 事件监听   │    │   工具调用接口   │         │
│   │                 │    │                 │    │                 │         │
│   │ • 理解布置需求   │    │ • 连接 /events  │    │ • canvas_*      │         │
│   │ • 调用 MCP 工具  │    │ • 解析事件类型   │    │ • zone_*        │         │
│   │ • 验证布置结果   │    │ • 触发 Agent    │    │ • module_*      │         │
│   └─────────────────┘    └─────────────────┘    └─────────────────┘         │
│            │                      ▲                      │                   │
│            │                      │ SSE                  │ MCP/HTTP          │
│            │                      │                      │                   │
└────────────┼──────────────────────┼──────────────────────┼───────────────────┘
             │                      │                      │
             ▼                      │                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BIMCanvas.Server (.NET 6+)                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                               │
│   ┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐         │
│   │   EventBus      │    │ EventsController│    │   McpTools/     │         │
│   │   事件总线       │───►│   SSE 端点       │    │   Canvas-MCP    │         │
│   │                 │    │   /api/events   │    │                 │         │
│   │ • 发布事件       │    │ • 推送事件流     │    │ • 模块操作       │         │
│   │ • 订阅事件       │    │ • 客户端管理     │    │ • 状态查询       │         │
│   └─────────────────┘    └─────────────────┘    └─────────────────┘         │
│            ▲                                                                  │
│            │                                                                  │
│   ┌────────┴────────┐    ┌─────────────────┐                                 │
│   │ 事件触发源       │    │ SignalR Hub     │                                 │
│   │ • Web 按钮       │    │ 前端通信         │                                 │
│   │ • 自动检测       │    │                 │                                 │
│   └─────────────────┘    └─────────────────┘                                 │
│                                   │                                           │
└───────────────────────────────────┼───────────────────────────────────────────┘
                                    │ WebSocket
                                    ▼
                         ┌─────────────────────┐
                         │   BIMCanvas.Web     │
                         │   「一键布置」按钮   │
                         └─────────────────────┘
```

**设计原则**：

| 原则 | 说明 |
|------|------|
| 独立部署 | Agent 作为独立进程运行，不依赖 Server 生命周期 |
| 事件驱动 | 通过 SSE 接收事件，避免轮询开销 |
| 工具复用 | 复用现有 Canvas-MCP 工具，不重复实现逻辑 |
| 长期运行 | Agent 持续监听，响应任意时刻的事件 |

#### 6.4.2 事件驱动机制

**EventBus 实现 (C#)**：

```csharp
// BIMCanvas.Server/Services/EventBus.cs
public interface IEventBus
{
    void Publish<T>(T evt) where T : CanvasEvent;
    IDisposable Subscribe<T>(Action<T> handler) where T : CanvasEvent;
    IAsyncEnumerable<CanvasEvent> GetEventStream(CancellationToken ct);
}

public abstract class CanvasEvent
{
    public string EventId { get; } = Guid.NewGuid().ToString();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}

public class PlacementRequestEvent : CanvasEvent
{
    public override string EventType => "placement_request";
    public string CanvasId { get; init; }
    public string ZoneId { get; init; }
    public string TriggerSource { get; init; }  // "web_button" | "auto_fix"
    public Dictionary<string, object> Context { get; init; }
}

public class ValidationFailedEvent : CanvasEvent
{
    public override string EventType => "validation_failed";
    public string CanvasId { get; init; }
    public string ModuleId { get; init; }
    public string ViolationType { get; init; }  // "out_of_bounds" | "collision"
    public string Description { get; init; }
}
```

**SSE 端点 (C#)**：

```csharp
// BIMCanvas.Server/Controllers/EventsController.cs
[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly IEventBus _eventBus;

    [HttpGet]
    public async Task GetEvents(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        await foreach (var evt in _eventBus.GetEventStream(ct))
        {
            var json = JsonSerializer.Serialize(evt);
            await Response.WriteAsync($"event: {evt.EventType}\n");
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
        }
    }
}
```

**SSE 监听器 (Python)**：

```python
# BIMCanvas.Agent/src/events/listener.py
import asyncio
import aiohttp
from dataclasses import dataclass
from typing import Callable, Awaitable

@dataclass
class CanvasEvent:
    event_id: str
    event_type: str
    timestamp: str
    data: dict

class EventListener:
    def __init__(self, server_url: str):
        self.server_url = server_url
        self.handlers: dict[str, list[Callable[[CanvasEvent], Awaitable[None]]]] = {}

    def on(self, event_type: str):
        """事件处理器装饰器"""
        def decorator(func: Callable[[CanvasEvent], Awaitable[None]]):
            if event_type not in self.handlers:
                self.handlers[event_type] = []
            self.handlers[event_type].append(func)
            return func
        return decorator

    async def listen(self):
        """持续监听 SSE 事件流"""
        async with aiohttp.ClientSession() as session:
            async with session.get(f"{self.server_url}/api/events") as response:
                async for line in response.content:
                    line = line.decode('utf-8').strip()
                    if line.startswith('data:'):
                        data = json.loads(line[5:])
                        event = CanvasEvent(
                            event_id=data['eventId'],
                            event_type=data['eventType'],
                            timestamp=data['timestamp'],
                            data=data
                        )
                        await self._dispatch(event)

    async def _dispatch(self, event: CanvasEvent):
        """分发事件到处理器"""
        handlers = self.handlers.get(event.event_type, [])
        for handler in handlers:
            await handler(event)
```

**事件格式**：

```json
{
  "eventId": "evt_abc123",
  "eventType": "placement_request",
  "timestamp": "2025-12-05T10:30:00Z",
  "canvasId": "canvas_001",
  "zoneId": "z1",
  "triggerSource": "web_button",
  "context": {
    "userId": "user_123",
    "sessionId": "sess_456"
  }
}
```

#### 6.4.3 三种触发方式实现

| 触发方式 | 触发源 | 数据流 | 适用场景 |
|----------|--------|--------|----------|
| AI 对话 | 用户输入 | 用户 → Agent Chat → PlacementAgent.run() | 设计讨论中的布置请求 |
| Web 按钮 | 前端 UI | Web → Server EventBus → SSE → Agent | 用户点击「一键布置」 |
| 自动修正 | Server 检测 | Server 验证 → EventBus → SSE → Agent | 模块超界/碰撞自动修复 |

**触发方式 1：AI 对话**

```python
# BIMCanvas.Agent/src/agent/placement_agent.py
from anthropic import Agent, tool

class PlacementAgent(Agent):
    """PlacementAgent - 智能家具布置助手"""

    system_prompt = """你是 BIMCanvas 的智能布置助手。
    你可以理解用户的布置需求，调用 MCP 工具完成家具布置。

    工作流程：
    1. 理解用户需求（房间类型、功能区域、风格偏好）
    2. 查询当前画布状态
    3. 搜索合适的家具模块
    4. 计算最佳布置方案
    5. 执行布置并验证结果
    """

    @tool
    async def place_module(self, zone_id: str, module_id: str,
                          position: list, facing: str) -> dict:
        """在指定区域放置家具模块"""
        result = await self.mcp_client.call(
            "canvas_module_add",
            zoneId=zone_id,
            moduleId=module_id,
            position=position,
            facing=facing
        )
        return result
```

**触发方式 2：Web 按钮**

```typescript
// BIMCanvas.Web 前端
async function onQuickPlaceClick(zoneId: string) {
  await fetch('/api/events/trigger', {
    method: 'POST',
    body: JSON.stringify({
      eventType: 'placement_request',
      zoneId: zoneId,
      triggerSource: 'web_button'
    })
  });
}
```

```python
# Agent 端事件处理
@listener.on('placement_request')
async def handle_placement_request(event: CanvasEvent):
    zone_id = event.data['zoneId']
    await agent.run(f"请为区域 {zone_id} 自动布置家具")
```

**触发方式 3：自动修正**

```csharp
// Server 端验证服务
public class PlacementValidator
{
    private readonly IEventBus _eventBus;

    public void ValidateModule(Module module, Zone zone)
    {
        var result = CollisionDetector.Check(module.Bounds, zone);

        if (!result.IsValid)
        {
            _eventBus.Publish(new ValidationFailedEvent
            {
                CanvasId = zone.CanvasId,
                ModuleId = module.Id,
                ViolationType = result.ViolationType,
                Description = result.Description
            });
        }
    }
}
```

```python
# Agent 端自动修正处理
@listener.on('validation_failed')
async def handle_validation_failed(event: CanvasEvent):
    module_id = event.data['moduleId']
    violation = event.data['violationType']

    if violation == 'out_of_bounds':
        await agent.run(f"模块 {module_id} 超出区域边界，请重新调整位置")
    elif violation == 'collision':
        await agent.run(f"模块 {module_id} 与其他模块碰撞，请解决冲突")
```

#### 6.4.4 MCP 工具定义

PlacementAgent 通过 MCP 协议调用 Canvas-MCP 工具：

| 工具名称 | 功能 | 参数 |
|----------|------|------|
| `canvas_get` | 获取画布状态 | canvasId |
| `canvas_zone_list` | 列出所有区域 | canvasId |
| `canvas_zone_get` | 获取区域详情 | canvasId, zoneId |
| `canvas_module_add` | 添加模块 | zoneId, moduleId, position, facing |
| `canvas_module_move` | 移动模块 | moduleId, newPosition |
| `canvas_module_delete` | 删除模块 | moduleId |
| `canvas_validate` | 验证布置 | canvasId |
| `library_search` | 搜索家具 | query, filters |
| `library_get_module` | 获取模块详情 | moduleId |

#### 6.4.5 Agent 配置

```python
# BIMCanvas.Agent/src/config/settings.py
from pydantic import BaseSettings

class AgentSettings(BaseSettings):
    # Anthropic API
    anthropic_api_key: str
    model: str = "claude-sonnet-4-20250514"

    # Server 连接
    server_url: str = "http://localhost:5000"
    sse_endpoint: str = "/api/events"
    mcp_endpoint: str = "/mcp"

    # Agent 行为
    max_retries: int = 3
    validation_enabled: bool = True
    auto_fix_enabled: bool = True

    class Config:
        env_file = ".env"
        env_prefix = "BIMCANVAS_"
```

**启动流程**：

```python
# BIMCanvas.Agent/src/main.py
import asyncio
from agent.placement_agent import PlacementAgent
from events.listener import EventListener
from config.settings import AgentSettings

async def main():
    settings = AgentSettings()

    # 初始化组件
    agent = PlacementAgent(api_key=settings.anthropic_api_key)
    listener = EventListener(settings.server_url)

    # 注册事件处理器
    @listener.on('placement_request')
    async def on_placement(event):
        await agent.run(f"处理布置请求: {event.data}")

    @listener.on('validation_failed')
    async def on_validation_failed(event):
        if settings.auto_fix_enabled:
            await agent.run(f"自动修复: {event.data}")

    # 启动监听
    print(f"PlacementAgent 已启动，监听 {settings.server_url}")
    await listener.listen()

if __name__ == "__main__":
    asyncio.run(main())
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

| 组件 | 技术选择 | 版本 | 备注 |
|------|----------|------|------|
| **Core 类库** | .NET Standard | 2.0 | 兼容 .NET FW 4.7.2 和 .NET 6+ |
| **JSON 序列化** | Newtonsoft.Json | 13.0.3 | Revit 内置，生态成熟 |
| **几何运算** | NetTopologySuite | 2.x | MVP 立即引入，处理碰撞检测 |
| **测试框架** | xUnit + FluentAssertions | - | 断言可读性好 |
| **MCP Server** | .NET | 6.0+ | - |
| **Web 后端** | ASP.NET Core | 6.0+ | - |
| **实时通信** | SignalR | - | - |
| **Web 前端** | Vue 3 + TypeScript | 3.x | - |
| **前端构建** | Vite | 5.x | - |
| **状态管理** | Pinia | 2.x | - |
| **数据格式** | JSON | - | - |
| **渲染格式** | SVG | - | - |

### 8.1 Core 层关键设计决策

| 决策点 | 结论 | 理由 |
|--------|------|------|
| **Polygon2D 表示** | `Point2D[]` + JsonConverter | 类型安全，JSON 输出保持数组格式 |
| **Facing 实现** | 封装 `readonly struct`，支持隐式转换 | 统一处理语义字符串和向量 |
| **ICanvasDocument 接口** | 不需要 | YAGNI，CanvasDocument 是数据契约非可替换服务 |
| **MathHelper/Epsilon** | 不需要 | Core 层保持简单，精度问题由 NTS 处理 |
| **PolygonOperations** | 不实现 | 复杂几何运算委托给 NTS |
| **NtsAdapter 可见性** | `internal` | 不污染公共 API |
| **PlacementValidator** | 只验证，不修正 | 「修正」是 AI 的规划职责 |

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
| v2.1 | 2025-12-02 | 添加程序执行流程章节，更新数据模型为 v2.0 极简版（outline + zones + modules），element 改为 module |
| v2.2 | 2025-12-03 | 新增 §6.1.4 核心转换器 (UnitConverter)，明确单位换算职责和精度原则 |
| v2.4 | 2025-12-04 | **同步 Core 评审共识**：更新 §3.1 目录结构（Primitives/Document 扁平化、Geometry/Spatial 分层）；更新 §6.1 Core 详细设计（添加 NTS 依赖、分拆 PlacementValidator）；更新 §8 技术选型（Newtonsoft.Json、NTS、xUnit） |
| v2.3 | 2025-12-03 | **几何类型架构升级**：新增 §3.5 AI 交互层架构（"AI = OBB 规划师"隐喻、数据流、多样化输出策略）；新增 §6.1.3 PlacementNormalizer；Module.Bounds/ExclusionArea.Boundary 改为 Polygon2D；Facing 支持联合类型 |
| v2.5 | 2025-12-04 | **数据模型增强**：新增 Room/RoomType（物理房间概念）；新增 ZoneTag 替代 Zone.Function（多标签系统）；新增 WallFinish/FinishSource（墙面完成面禁区）；Facing.Semantic 改为枚举类型 |
| v2.6 | 2025-12-05 | **后端项目合并**：将 BIMCanvas.MCP.Canvas 和 BIMCanvas.Web.Server 合并为单一项目 BIMCanvas.Server；更新 §2.1 整体架构图、§3.1-3.3 项目结构、§4 统一后端架构、§6.2 Server 详细设计；新增 PlacementService/ZoneCalculator 等业务服务说明 |
| v2.6.1 | 2025-12-05 | **数据流修正**：修正 §0.2 MVP 执行流程和 §2.2 数据流向，明确 Revit 层只提取原始数据（rooms），Zone/WallFinish 计算由 Server 层 ZoneCalculator 负责 |
| v2.7 | 2025-12-05 | **PlacementAgent 架构升级**：PlacementAgent 从 Server 内部服务迁移至独立 Python Agent（基于 Anthropic Agent SDK）；新增 BIMCanvas.Agent 项目（§2.1/§3.1/§3.2）；新增 §6.4 PlacementAgent 与 Agent SDK 集成（事件驱动机制、三种触发方式、MCP 工具定义） |

