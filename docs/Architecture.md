# BIMCanvas 系统架构文档

> 版本：v2.3
> 更新日期：2025-12-03
> 状态：已定稿（基于几何数据类型架构专家评审）

---

## 0. 程序执行流程

> **核心原则**：KISS - Keep It Simple, Stupid

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
Phase 1: 数据提取
═══════════════════════════════════════════════════════════════════════════════
1. 用户在 Revit 中激活目标平面视图
2. 点击"开始设计"按钮
3. 插件端提取并计算：
   - outline.walls: 墙体轮廓多边形
   - outline.openings: 门窗线段
   - zones[].innerBoundary: 可用空间（已扣除完成面）
   - zones[].exclusionAreas: 门扇禁区（简化矩形）
4. 生成 CanvasDocument JSON
5. Web 端显示户型底图


Phase 2: 区域确认
═══════════════════════════════════════════════════════════════════════════════
6. AI 根据 Revit 房间名称初步填写 zones[].function
7. Web 端显示功能标签
8. 用户确认/修改各区域功能


Phase 3: 方案生成
═══════════════════════════════════════════════════════════════════════════════
9. AI 直接生成布置方案（不做候选清单）
   - 遵循约束：innerBoundary 内、避开 exclusionAreas
10. Web 端显示完整平面布置图


Phase 4: 交互修改
═══════════════════════════════════════════════════════════════════════════════
11. 用户可以：
    a) 在 Web 端拖拽调整家具位置
    b) 通过对话指导 AI 修改（如"把床转90度"）
12. 循环迭代直到满意


Phase 5: 回写 Revit
═══════════════════════════════════════════════════════════════════════════════
13. 用户确认最终方案
14. 调用 Revit-MCP：
    - load_family_from_library 加载族
    - create_element 创建家具（基于 levelId + position）
15. Revit 中显示布置结果
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
    → [Revit-MCP: ai_element_filter] 提取墙/门/窗/房间元素
    → [Revit-MCP: capture_view] 获取视图范围
    → [BIMCanvas.Core: RevitToJsonConverter] 转换为 CanvasDocument (JSON)
        - 墙体 → outline.walls (轮廓多边形)
        - 门窗 → outline.openings (线段)
        - 房间 → zones (含 innerBoundary, exclusionAreas)
    → [Canvas-MCP: canvas_create] 创建画布
    → [WebSocket] 推送到 Web
    → [BIMCanvas.Web] JSON → SVG 渲染显示

【AI 布置方案】
AI 理解用户需求
    → [Library-MCP: module_search] 搜索合适的模块/家具
    → [Canvas-MCP: module_add] 修改 JSON 数据
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
    → [Canvas-MCP: canvas_get_changes] AI 查询变更
    → AI 感知变化并响应

【同步回 Revit】
设计方案确定
    → [Canvas-MCP: canvas_export] 导出 JSON
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

#### 6.1.1 数据模型（v2.0 极简版）

详细定义见：[Schema-JSON.md](./Schema-JSON.md)

```csharp
// CanvasDocument - 画布文档根对象
public class CanvasDocument
{
    public string Id { get; set; }
    public int Version { get; set; }
    public string CoordinateSystem { get; set; } = "cartesian_mm_yUp";
    public Metadata Metadata { get; set; }
    public Outline Outline { get; set; }
    public List<Zone> Zones { get; set; }
    public List<Module> Modules { get; set; }
}

// Metadata - 元数据
public class Metadata
{
    public int RevitViewId { get; set; }
    public int LevelId { get; set; }
    public int GridSize { get; set; } = 500;
}

// Outline - 可视化底图
public class Outline
{
    public List<Wall> Walls { get; set; }
    public List<Opening> Openings { get; set; }
}

// Wall - 墙体轮廓
public class Wall
{
    public string Id { get; set; }
    public List<double[]> Polygon { get; set; }  // [[x,y], [x,y], ...]
}

// Opening - 门窗
public class Opening
{
    public string Id { get; set; }
    public string Type { get; set; }  // "door" | "window"
    public double[][] Line { get; set; }  // [[x1,y1], [x2,y2]]
}

// Zone - 设计区域
public class Zone
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Function { get; set; }
    public List<double[]> InnerBoundary { get; set; }  // 可用空间（已扣除完成面）
    public List<ExclusionArea> ExclusionAreas { get; set; }
    public List<string> Openings { get; set; }
}

// ExclusionArea - 禁止布置区
public class ExclusionArea
{
    public string Id { get; set; }
    public string Type { get; set; }  // "door_swing" | "passage" | "other"
    public List<double[]> Boundary { get; set; }  // Polygon2D [[x,y], ...]
}

// Module - 布置模块
public class Module
{
    public string Id { get; set; }
    public string ModuleId { get; set; }
    public string ModuleName { get; set; }
    public List<double[]> Bounds { get; set; }  // Polygon2D [[x,y], ...] 精确边界
    public object Facing { get; set; }  // string ("north"...) 或 double[] (Vec2D)
    public string ZoneId { get; set; }
    public List<ModuleItem> Items { get; set; }
}

// ModuleItem - 模块内部家具
public class ModuleItem
{
    public string FamilyId { get; set; }
    public double[] Offset { get; set; }  // [dx, dy]
    public string Role { get; set; }
}
```

#### 6.1.2 空间计算

```csharp
// 碰撞检测（基于 AABB）
public class CollisionDetector
{
    // 检查模块是否可放置
    public bool CanPlace(Module module, Zone zone, List<Module> existingModules);

    // 检查 AABB 是否相交
    public bool AabbIntersects(double[] a, double[] b);

    // 检查 AABB 是否在多边形内
    public bool IsInsidePolygon(double[] bounds, List<double[]> polygon);
}

// 朝向转换
public class FacingHelper
{
    // 语义方向 → 旋转角度
    public double ToRotation(string facing);
    // north → 0°, east → 90°, south → 180°, west → 270°

    // 旋转角度 → 语义方向
    public string FromRotation(double angle);
}

// 网格对齐
public class GridHelper
{
    public double[] SnapToGrid(double[] bounds, int gridSize);
}
```

#### 6.1.3 意图归一化器 (PlacementNormalizer)

**职责**：将 AI 的多样化输出统一转换为 `Polygon2D`。

```csharp
// BIMCanvas.Core/Algorithms/PlacementNormalizer.cs
namespace BIMCanvas.Core.Algorithms
{
    /// <summary>
    /// 将 AI 的布置意图转换为精确几何
    /// </summary>
    public class PlacementNormalizer
    {
        /// <summary>
        /// 将语义化布置意图转换为 Polygon2D
        /// </summary>
        /// <param name="moduleId">模块库 ID</param>
        /// <param name="parameters">参数化驱动（如 width, depth）</param>
        /// <param name="center">中心点 [x, y]</param>
        /// <param name="facing">朝向（string 或 Vec2D）</param>
        /// <returns>精确边界 Polygon2D</returns>
        public Polygon2D ToPolygon(
            string moduleId,
            Dictionary<string, double> parameters,
            double[] center,
            object facing)
        {
            // 1. 从模块库获取 canonical polygon（局部坐标系）
            var canonical = _moduleLibrary.GetCanonicalPolygon(moduleId, parameters);

            // 2. 计算旋转角度
            double angle = FacingHelper.ToAngle(facing);

            // 3. 应用变换：旋转 + 平移
            return canonical.Rotate(angle).Translate(center);
        }

        /// <summary>
        /// 验证布置意图是否有效
        /// </summary>
        public PlacementValidationResult Validate(
            PlacementIntent intent,
            Zone zone,
            List<Module> existingModules)
        {
            var polygon = ToPolygon(intent);

            // 检查是否在 zone 内
            if (!zone.InnerBoundary.Contains(polygon))
                return new PlacementValidationResult(false, "超出设计区域边界");

            // 检查是否与禁区重叠
            foreach (var exclusion in zone.ExclusionAreas)
            {
                if (polygon.Intersects(exclusion.Boundary))
                    return new PlacementValidationResult(false, $"与禁区 {exclusion.Id} 重叠");
            }

            // 检查是否与已有模块重叠
            foreach (var existing in existingModules)
            {
                if (polygon.Intersects(existing.Bounds))
                    return new PlacementValidationResult(false, $"与模块 {existing.Id} 重叠");
            }

            return new PlacementValidationResult(true);
        }
    }
}
```

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

### 6.2 BIMCanvas.MCP.Canvas

**职责**：提供画布操作的 MCP 工具集

#### 6.2.1 MCP 工具列表

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
| v2.1 | 2025-12-02 | 添加程序执行流程章节，更新数据模型为 v2.0 极简版（outline + zones + modules），element 改为 module |
| v2.2 | 2025-12-03 | 新增 §6.1.4 核心转换器 (UnitConverter)，明确单位换算职责和精度原则 |
| v2.3 | 2025-12-03 | **几何类型架构升级**：新增 §3.5 AI 交互层架构（"AI = OBB 规划师"隐喻、数据流、多样化输出策略）；新增 §6.1.3 PlacementNormalizer；Module.Bounds/ExclusionArea.Boundary 改为 Polygon2D；Facing 支持联合类型 |
