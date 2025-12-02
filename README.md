# BIMCanvas

基于 AI CLI 的室内装修平面方案设计助手，实现 Revit 与 AI 之间的人机协作设计。

> **当前版本**: v2.0 极简版 | **数据模型**: outline + zones + modules

## 解决的问题

| 问题 | 现状 | BIMCanvas 方案 |
|------|------|----------------|
| AI 理解门槛高 | Revit 格式复杂 | JSON 结构清晰，AI 可直接理解 |
| AI 设计是"空想" | 输出无法对应真实产品 | 族库提供真实家具 + Revit 模型 |
| 设计迭代慢 | 每次修改需打开 Revit | Web 画布实时协作 |

---

## 核心设计理念

### v2.0 极简数据分层

| 层面 | 内容 | 用途 |
|------|------|------|
| Layer 1 (AI 上下文) | outline + zones + modules | AI 布置计算、前端渲染 |
| Layer 2 (Revit 详情) | revitElementId、厚度等 | Phase 1 暂缓 |

### JSON 为骨，SVG 为皮

| 层面 | 格式 | 职责 |
|------|------|------|
| 数据层（骨） | JSON | 存储、传输、AI 交互、业务逻辑 |
| 视图层（皮） | SVG | 渲染、显示、视觉反馈 |

**数据流**：AI 修改 JSON → WebSocket 推送 → 前端生成 SVG → 用户看到画布

**选择理由**：
- JSON Token 消耗远低于 SVG（约 1/10）
- JSON 结构化数据 AI 更易推理
- SVG 仅在渲染时生成，避免解析开销

### 坐标系统

采用 **CAD 标准坐标系**（非 Web 屏幕坐标系）：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | 向上为正 | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

**选择理由**：
- 数据层符合数学直觉
- 与 Revit 坐标系一致，减少转换
- 空间关系语义自洽（above = Y 值更大）

---

## 技术架构

### 架构总览

```
┌─────────────────────────────────────────────────────────────────┐
│                      Claude Code (AI CLI)                        │
│                    用户与 AI 的对话交互入口                        │
└──────────────────────────────┬──────────────────────────────────┘
                               │ MCP Protocol
┌──────────────────────────────┼──────────────────────────────────┐
│                         MCP Server 集群                          │
├──────────────────┬───────────┴───────────┬──────────────────────┤
│   Revit-MCP      │     Canvas-MCP        │    Library-MCP       │
│   提取建筑结构    │     操作 JSON 数据     │    搜索族资源         │
│   创建 Revit 元素 │     版本控制          │    获取族信息         │
│   .NET FW 4.7.2  │     .NET 6+           │    .NET 6+           │
└──────────────────┴───────────────────────┴──────────────────────┘
                               │ 引用
              ┌────────────────┼────────────────┐
              ▼                ▼                ▼
┌──────────────────┐  ┌────────────────┐  ┌────────────────────┐
│  BIMCanvas.Core  │  │ Web.Server     │  │  BIMCanvas.Web     │
│  (.NET Std 2.0)  │  │ (.NET 6+)      │  │  (Vue 3 + TS)      │
│  数据模型+算法    │  │ SignalR + API  │  │  JSON → SVG 渲染   │
└──────────────────┘  └────────────────┘  └────────────────────┘
```

### 数据流向

```
【Revit → 画布】
Revit 模型 → ai_element_filter 提取 → Core 转换 JSON → Canvas-MCP 创建 → Web 渲染

【AI 设计】
AI 理解需求 → Library-MCP 搜索家具 → Canvas-MCP 修改 JSON → WebSocket 推送 → Web 渲染

【用户修改】
Web 拖拽 → 修改本地 JSON → 点击 Commit → 生成 change_set → AI 感知并响应

【同步回 Revit】
导出 JSON → Core 解析 → Revit-MCP 加载族 → 创建 Revit 元素
```

---

## 技术栈

| 组件 | 技术 | 版本 | 选型理由 |
|------|------|------|----------|
| Core 类库 | .NET Standard | 2.0 | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |
| Revit 插件 | .NET Framework | 4.7.2 | Revit API 限制 |
| MCP Server | .NET | 6+ | 现代运行时 |
| Web 后端 | ASP.NET Core | 6+ | SignalR 支持 |
| Web 前端 | Vue 3 + TypeScript | 3.x | 响应式 + 类型安全 |
| 构建工具 | Vite | 5.x | 快速开发体验 |
| 状态管理 | Pinia | 2.x | Vue 3 官方推荐 |

---

## 项目结构

```
BIMCanvas/
├── BIMCanvas.Core/              核心类库 (.NET Standard 2.0) ✅ 已实现
│   ├── Models/                  数据模型
│   │   └── CanvasDocument.cs    CanvasDocument, Zone, Module 等 9 个类
│   └── Algorithms/              空间算法
│       ├── CollisionDetector.cs AABB 碰撞检测、多边形内判断
│       └── FacingHelper.cs      语义朝向 ↔ 角度转换
│
├── BIMCanvas.Revit/             Revit 插件 (.NET FW 4.7.2)
│   ├── Commands/                Ribbon 按钮命令
│   ├── Views/                   WPF 配置窗口
│   └── Adapters/                Revit 元素适配器
│
├── BIMCanvas.MCP.Canvas/        画布 MCP Server (.NET 6+)
│   └── Tools/                   画布管理、模块操作、版本控制
│
├── BIMCanvas.MCP.Library/       族库 MCP Server (.NET 6+)
│   └── Tools/                   族库查询、Visual Fallback
│
├── BIMCanvas.Web.Server/        Web 后端 (.NET 6+)
│   ├── Hubs/                    SignalR Hub
│   └── Services/                状态管理、变更集服务
│
├── BIMCanvas.Web/               Web 前端 (Vue 3)
│   └── src/
│       ├── components/Canvas/   SVG 画布组件
│       ├── stores/              Pinia 状态
│       └── services/            SignalR 客户端、渲染器
│
├── docs/                        文档
└── external/Revit-MCP/          已有 Revit-MCP 项目
```

---

## v2.0 JSON 数据结构

```json
{
  "id": "canvas_001",
  "version": 1,
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": { "revitViewId": 12345, "levelId": 67890, "gridSize": 500 },

  "outline": {
    "walls": [{ "id": "w1", "polygon": [[0,0], [6000,0], ...] }],
    "openings": [{ "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] }]
  },

  "zones": [{
    "id": "z1",
    "name": "主卧",
    "function": "master_bedroom",
    "innerBoundary": [[50,50], [5950,50], ...],
    "exclusionAreas": [{ "id": "ex1", "type": "door_swing", "rect": [2000,0,2900,900] }],
    "openings": ["d1"]
  }],

  "modules": [{
    "id": "m1",
    "moduleId": "sleep_master_01",
    "moduleName": "主卧睡眠模块",
    "bounds": [1500, 2000, 4500, 4500],
    "facing": "north",
    "zoneId": "z1",
    "items": [{ "familyId": "bed_double_01", "offset": [0,0], "role": "主体" }]
  }]
}
```

**核心设计决策**：
| 决策点 | 选择 | 理由 |
|--------|------|------|
| 墙体表示 | 封闭轮廓多边形 | AI 不需要理解墙体结构 |
| 门窗表示 | 简化为线段 | 厚度不影响家具布置 |
| 门扇区域 | 预计算 AABB 禁区 | KISS - AI 只需知道"这里不能放" |
| 房间结构 | 只有 zones | 单一数据源原则 |
| 布置单元 | modules（模块） | 支持单一家具或组合 |
| 模块位置 | AABB 包围盒 | 碰撞检测简单直观 |
| 模块朝向 | 语义化 (north/south/...) | AI 友好，插件端转换角度 |

---

## 开发阶段

### Phase 1: 核心基础（MVP）

**目标**：AI 可以在画布上设计，Web 可以显示

- ✅ 实现 Core 数据模型（CanvasDocument, Zone, Module 等）
- ✅ 实现空间算法（CollisionDetector, FacingHelper）
- ⬜ 实现 Canvas-MCP 基础工具（module_add, module_move, module_delete）
- ⬜ 实现 Web 后端 SignalR + REST API
- ⬜ 实现 Web 前端 JSON → SVG 渲染

### Phase 2: 协作编辑

**目标**：AI 和用户可以实时协作

- 实现 Commit 同步机制
- 实现元素拖拽/旋转交互
- 实现 Library-MCP 族库查询
- 实现 Visual Fallback 占位符

### Phase 3: Revit 集成

**目标**：完整的 Revit 双向同步

- 实现 Revit → JSON 导出
- 实现 Ribbon 面板和配置窗口
- 实现 JSON → Revit 同步

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [Architecture.md](./docs/Architecture.md) | 详细架构设计 |
| [Schema-JSON.md](./docs/Schema-JSON.md) | JSON 数据模型规范 |
| [PRD.md](./docs/PRD.md) | 产品需求文档 |
| [Architecture_Design_Review.md](./docs/Architecture_Design_Review.md) | 专家评审记录 |
