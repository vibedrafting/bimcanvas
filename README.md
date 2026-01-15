# BIMCanvas

基于 AI CLI 的室内装修平面方案设计助手，实现 Revit 与 AI 之间的人机协作设计。

> **当前版本**: v3.0 | **数据模型**: File-Driven Architecture + .bcp 项目格式 | **架构**: 三层汉堡模型 (baseline/schemes/computed)

## 解决的问题

| 问题 | 现状 | BIMCanvas 方案 |
|------|------|----------------|
| AI 理解门槛高 | Revit 格式复杂 | JSON 结构清晰，AI 可直接理解 |
| AI 设计是"空想" | 输出无法对应真实产品 | 族库提供真实家具 + Revit 模型 |
| 设计迭代慢 | 每次修改需打开 Revit | Web 画布实时协作 |

---

## 核心设计理念

### v3.0 三层汉堡模型

| 层 | 目录 | 内容 | 权限 |
|---|---|---|---|
| 顶层 | `computed/` | exclusions (禁区) | 自动生成 |
| 中层 | `schemes/{id}/` | zones, finishes, modules | AI/Server 可写 |
| 底层 | `baseline/` | walls, columns, openings, rooms | 只读 |

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
├──────────────────────────────┴──────────────────────────────────┤
│   Revit-MCP (.NET FW 4.7.2)   提取建筑结构、创建 Revit 元素       │
└─────────────────────────────────────────────────────────────────┘
                               │ 引用
              ┌────────────────┼─────────────────────────┐
              ▼                ▼                         ▼
┌──────────────────┐  ┌───────────────────────┐  ┌────────────────────┐
│  BIMCanvas.Core  │  │  BIMCanvas.Server     │  │  BIMCanvas.Web     │
│  (.NET Std 2.0)  │  │  (.NET 6+)            │  │  (Vue 3 + TS)      │
│  数据模型+算法    │  │  MCP + REST + SignalR │  │  JSON → SVG 渲染   │
└──────────────────┘  └───────────┬───────────┘  └────────────────────┘
                                  │ SSE 事件流
                                  ▼
                      ┌───────────────────────┐
                      │  BIMCanvas.Agent      │
                      │  (Python 3.10+)       │
                      │  PlacementAgent       │
                      │  基于 Agent SDK       │
                      └───────────────────────┘
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
| Server 后端 | ASP.NET Core | 6+ | MCP + REST + SignalR + SSE |
| Agent 服务 | Python + Agent SDK | 3.10+ | 基于 Anthropic Agent SDK 的 PlacementAgent |
| Web 前端 | Vue 3 + TypeScript | 3.x | 响应式 + 类型安全 |
| 构建工具 | Vite | 5.x | 快速开发体验 |
| 状态管理 | Pinia | 2.x | Vue 3 官方推荐 |

---

## 项目结构（规划）

```
BIMCanvas/
├── BIMCanvas.Core/              核心类库 (.NET Standard 2.0)
│   ├── Models/                  数据模型 (CanvasDocument, Zone, Module...)
│   └── Algorithms/              空间算法 (碰撞检测, 朝向转换)
│
├── BIMCanvas.Server/            统一后端服务 (.NET 6+)
│   ├── McpTools/                Canvas-MCP + Library-MCP 工具
│   ├── Controllers/             REST API + SSE 事件端点
│   ├── Hubs/                    SignalR Hub
│   └── Services/                EventBus、状态管理、业务服务
│
├── BIMCanvas.Agent/             PlacementAgent 服务 (Python 3.10+)
│   ├── src/agent/               Agent SDK 实现
│   ├── src/events/              SSE 事件监听器
│   └── src/mcp/                 MCP 工具客户端
│
├── BIMCanvas.Revit/             Revit 插件 (.NET FW 4.7.2)
│   ├── Commands/                Ribbon 按钮命令
│   ├── Views/                   WPF 配置窗口
│   └── Adapters/                Revit 元素适配器
│
├── BIMCanvas.Web/               Web 前端 (Vue 3)
│   └── src/
│       ├── components/Canvas/   SVG 画布组件
│       ├── stores/              Pinia 状态
│       └── services/            SignalR 客户端、渲染器
│
├── docs/                        文档 ✅
└── external/Revit-MCP/          已有 Revit-MCP 项目
```

---

## schemes 目录结构 (v3.0)

v3.0 采用分区级目录结构，每个分区独立存储，支持并行编辑和选择性合并。

### 目录结构

```
schemes/
├── {zoneId}/           # 分区目录（rz_* 或 dz_*）
│   └── modules.json    # 该分区的布置模块
├── zones.json          # 所有分区定义
└── finishes.json       # 完成面分段
```

### 分区命名规则

| 前缀 | 含义 | 示例 |
|------|------|------|
| `rz_` | Room Zone（房间区域） | `rz_master_bedroom_01` |
| `dz_` | Design Zone（设计区域） | `dz_living_area` |

### modules.json 结构

每个分区的 `modules.json` 是一个数组（非对象包装）：

```json
[
  {
    "id": "m001",
    "moduleId": "bed_king",
    "zoneId": "rz_master_bedroom_01",
    "bounds": {
      "vertices": [[1000, 2000], [3000, 2000], [3000, 4000], [1000, 4000]]
    },
    "facing": "north",
    "items": []
  }
]
```

### 关键字段说明

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 模块实例 ID（前缀 `m`） |
| `moduleId` | string | 模块库中的类型 ID |
| `zoneId` | string | 所属分区 ID |
| `bounds` | Polygon2D | 矩形边界（4 顶点，逆时针） |
| `facing` | string/Vec2D | 朝向（8 方向字符串或单位向量） |

### 设计优势

- **并行编辑**：不同分区可同时编辑（通过 Git Worktree 隔离）
- **选择性合并**：可按分区选择合并 AI 生成的方案
- **冲突减少**：分区独立文件降低合并冲突概率

详细 Schema 见：[docs/Schema-JSON-v3.md](./docs/Schema-JSON-v3.md)

---

## v3.0 项目数据结构

v3.0 采用 `.bcp` ZIP 格式，包含多个 JSON 文件：

```json
{
  "id": "project_001",
  "name": "Sample Project",
  "coordinateSystem": "cartesian_mm_yUp",
  "metadata": { "placementElevation": 0, "origin": [0, 0, 0], "rotation": 0 },

  "walls": [{ "id": "wall_001", "elementId": 12345, "polygon": [[0,0], [6000,0], [6000,200], [0,200]] }],
  "columns": [{ "id": "col_001", "elementId": 23456, "isStructural": true, "polygon": [[3000,0], [3500,0], [3500,500], [3000,500]] }],
  "openings": [{ "id": "d1", "type": "door", "line": [[2000,0], [2900,0]] }],
  "finishLocationBoundaries": [{ "id": "flb_001", "elementIds": [12345, 23456], "polygon": [[...]] }],

  "rooms": [{
    "id": "r1",
    "name": "主卧",
    "type": "master_bedroom",
    "boundary": [[0,0], [6000,0], [6000,5000], [0,5000]]
  }],

  "zones": [{
    "id": "z1",
    "name": "主卧睡眠区",
    "tags": ["sleep", "master_bedroom"],
    "roomId": "r1",
    "innerBoundary": [[50,50], [5950,50], ...],
    "exclusionAreas": [{ "id": "ex1", "type": "door_swing", "boundary": [[2000,0], [2900,0], [2900,900], [2000,900]] }],
    "openings": ["d1"]
  }],

  "wallFinishes": [{
    "id": "wf1",
    "locationLine": [[200, 200], [200, 5800]],
    "thickness": 20,
    "exclusionBoundary": [[200, 200], [220, 200], [220, 5800], [200, 5800]]
  }],

  "modules": [{
    "id": "m1",
    "moduleId": "sleep_master_01",
    "moduleName": "主卧睡眠模块",
    "bounds": [[1500, 2000], [4500, 2000], [4500, 4500], [1500, 4500]],
    "facing": "north",
    "zoneId": "z1",
    "items": [{ "familyId": "bed_double_01", "offset": [0,0], "role": "主体" }]
  }]
}
```

**核心设计决策**：
| 决策点 | 选择 | 理由 |
|--------|------|------|
| 数据架构 | File-Driven + .bcp ZIP | 文件为真理源，多文件夹结构 |
| 墙体/柱子 | 分离存储（walls + columns） | AI 需要区分构件类型做空间理解 |
| 柱子类型 | isStructural 布尔 | 区分结构柱/建筑柱 |
| 完成面定位 | LocationLine + FinishSegment | 定位线 + 分段化完成面 (v3.0) |
| 门窗表示 | 简化为线段 | 厚度不影响家具布置 |
| 门扇区域 | 预计算为禁区 Polygon2D | KISS - AI 只需知道"这里不能放" |
| 房间结构 | rooms + zones 分离 | rooms 对应 Revit 房间，zones 为设计区域 |
| 布置单元 | modules（模块） | 支持单一家具或组合 |
| 模块朝向 | Facing 联合类型 | 语义字符串 or Vec2D 单位向量 |

---

## 开发阶段

### Phase 1: 核心基础（MVP）

**目标**：AI 可以在画布上设计，Web 可以显示

**当前阶段**：v3.0 架构升级完成（Core + Revit + Server + Web 项目加载）

- ✅ 实现 Core 数据模型（CanvasDocument, Zone, Module 等）
- ✅ 实现空间算法（CollisionDetector, PlacementValidator）
- ✅ 实现 v3.0 数据模型（Project, Strategy, LocationLine, ExclusionArea 等）
- ✅ 实现 Server 层 v3.0 项目加载（ProjectService, ManifestService）
- ✅ 实现 Web 层 v3.0 项目数据加载
- ⬜ 实现 Web 前端 JSON → SVG 渲染

### Phase 2: PlacementAgent 集成

**目标**：智能布置助手自动化

- ⬜ 实现 BIMCanvas.Agent 项目结构（Python 3.10+）
- ⬜ 实现 PlacementAgent（基于 Anthropic Agent SDK）
- ⬜ 实现 EventBus + SSE 事件机制
- ⬜ 实现三种触发方式（AI 对话、Web 按钮、自动修正）

### Phase 3: 协作编辑

**目标**：AI 和用户可以实时协作

- ⬜ 实现 Commit 同步机制
- ⬜ 实现元素拖拽/旋转交互
- ⬜ 实现 Library-MCP 族库查询
- ⬜ 实现 Visual Fallback 占位符

### Phase 4: Revit 集成

**目标**：完整的 Revit 双向同步

- ✅ 实现 Revit → JSON 导出（墙体/柱子/门窗/房间）
- ✅ 实现 Ribbon 面板和配置窗口
- ✅ 实现 LocationLine 提取（v3.0）
- ✅ 实现 .bcp 格式导出（v3.0）
- ⬜ 实现 JSON → Revit 同步（回写家具）

---

## 相关文档

| 文档 | 说明 |
|------|------|
| [Architecture.md](./docs/Architecture.md) | 详细架构设计 |
| [Schema-JSON-v3.md](./docs/Schema-JSON-v3.md) | JSON 数据模型规范 (v3.0) |
| [PRD.md](./docs/PRD.md) | 产品需求文档 |
| [Architecture_Design_Review.md](./docs/Architecture_Design_Review.md) | 专家评审记录 |
| [PlacementAgent_Review.md](./reviews/PlacementAgent_Review.md) | PlacementAgent 架构决策记录 |
