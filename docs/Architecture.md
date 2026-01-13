# BIMCanvas 系统架构总设计

> 版本：v3.0
> 更新日期：2026-01-13
> 状态：已定稿（File-Driven Architecture + .bcp 项目格式）
>
> **相关文档**：
> - [Schema.md](./Schema.md) - JSON 数据模型规范
> - [Arch_MCP_Tools.md](./Arch_MCP_Tools.md) - MCP 工具接口规范
> - [Arch_Converter.md](./Arch_Converter.md) - 转换器架构专题（转换链路、坐标转换、NTS 中间层）
> - [Arch_DataFlow.md](./Arch_DataFlow.md) - 数据流场景分析专题（典型场景调用链、API 参考）
> - [Flow_Workflows.md](./Flow_Workflows.md) - 端到端业务流程

---

## 1. 项目概述

### 1.1 项目定位

**BIMCanvas 只做一件事：在用户提供的建筑平面内，布置符合设计逻辑的家具组合。**

通过 Claude Code 作为 AI 入口，实现：
- 从 Revit 提取建筑结构（墙/门/窗）
- AI 在 JSON 数据模型上进行家具布置
- 用户在 Web 画布上实时协作编辑
- 将设计方案同步回 Revit

### 1.2 阶段速查表

| 阶段 | 名称 | 执行者 | 输出 |
|------|------|--------|------|
| Phase 1 | 数据准备 | BIMCanvas.Revit | 精简版 CanvasDocument |
| Phase 2 | 数据处理 | BIMCanvas.Server | 完整版 CanvasDocument |
| Phase 3 | 区域确认 | 用户 + AI | zones[].tags + wallFinishes |
| Phase 4 | 方案生成 | MainAgent | modules[] |
| Phase 5 | 交互修改 | 用户 + AI | 更新的 modules[] |
| Phase 6 | 回写 Revit | Revit-MCP | Revit 家具实例 |

**阶段流程视图**：

```
    ┌──────────┐     ┌──────────┐     ┌──────────┐
    │ Phase 1  │ ──► │ Phase 2  │ ──► │ Phase 3  │
    │ Revit 提取│     │ Server   │     │ 区域确认 │
    │          │     │ 计算     │     │          │
    └──────────┘     └──────────┘     └──────────┘
                                            │
    ┌──────────┐     ┌──────────┐     ┌─────▼────┐
    │ Phase 6  │ ◄── │ Phase 5  │ ◄── │ Phase 4  │
    │ 回写 Revit│     │ 交互修改 │     │ AI 布置  │
    │          │     │          │     │          │
    └──────────┘     └──────────┘     └──────────┘
```

### 1.3 核心设计决策

| 决策点 | 选择 | 理由 |
|--------|------|------|
| **数据架构** | File-Driven Architecture | 文件为真理源，Server 作为"文件播放器" |
| **数据分层** | 三层汉堡模型 (baseline/schemes/computed) | 职责清晰，读写权限分明 |
| **项目格式** | `.bcp` ZIP 包 | 多文件夹结构，便于版本控制和传输 |
| **坐标系** | Y-Up (笛卡尔) | 符合 CAD/BIM/数学直觉，只在前端渲染时转换 |
| **墙体表示** | 封闭轮廓多边形 | AI 不需要理解墙体结构，只需知道空间边界 |
| **门扇区域** | 预计算为矩形禁区（AABB） | KISS - AI 只需知道"这里不能放" |
| **布置单元** | modules（模块） | 支持单一家具或组合（如睡眠模块=床+床头柜） |
| **模块朝向** | 语义化方向（north/south/...） | AI 友好，插件端转换为角度 |
| **多方案管理** | Strategy 对象 | 每个方案独立文件夹，支持 Git diff 版本对比 |
| **Core 运行时** | .NET Standard 2.0 | 同时兼容 .NET FW 4.7.2 和 .NET 6+ |

---

## 2. 文件驱动架构 (File-Driven Architecture)

### 2.1 核心理念

> **文件是唯一真理源，Server 是"文件播放器"而非"内存数据库"**

这意味着：
- **持久化优先**：所有业务数据以 JSON 文件形式存储在磁盘
- **Server 无状态**：Server 不"拥有"数据，只负责读取、聚合、分发文件内容
- **变更可追溯**：任何外部进程（Agent、脚本、手工编辑）修改文件后，系统自动感知并同步

### 2.2 为什么选择文件驱动

| 传统架构 | 文件驱动架构 |
|---------|-------------|
| 数据库是真理源 | 文件是真理源 |
| 需要数据迁移 | 直接操作 JSON |
| Agent 需要 API 接口 | Agent 直接写文件 |
| 版本控制需要额外方案 | 天然支持 Git |
| 调试需要查数据库 | 调试直接看文件 |

**核心优势**：
1. **Git 原生集成**：项目文件即 Git 仓库，分支/回滚/协作开箱即用
2. **Agent 友好**：AI Agent 无需 API 即可直接修改 JSON 文件
3. **调试透明**：所有状态可直接通过文件系统查看
4. **离线可用**：文件在本地，无需网络即可编辑

### 2.3 三层汉堡模型

```
┌─────────────────────────────────────────────────────────────┐
│  顶层 (Computed) - 自动生成层                                │
│  computed/                                                   │
│  ├── room_zones.json    房间区域（从 rooms 派生）            │
│  └── exclusions.json    禁区（门扇扫过区域等）                │
│  特点：完全派生，可随时重建                                   │
├─────────────────────────────────────────────────────────────┤
│  中层 (Schemes) - 方案设计层（v3.2 简化）                     │
│  schemes/                                                     │
│  ├── strategy.json      策略元数据                           │
│  ├── zones.json         设计区域划分（AI/Server 写入）       │
│  ├── finishes.json      完成面定义（AI/Server 写入）         │
│  └── modules.json       家具模块布置（AI 写入）              │
│  特点：可编辑，多策略通过 Git 分支隔离（非子目录）            │
├─────────────────────────────────────────────────────────────┤
│  底层 (Baseline) - 建筑基础层                                 │
│  baseline/                                                   │
│  ├── metadata.json      坐标转换参数                         │
│  ├── architecture.json  墙体 + 柱子                          │
│  ├── openings.json      门窗数据                             │
│  ├── rooms.json         房间边界                             │
│  └── location_lines.json 完成面定位线                        │
│  特点：只读，来自 Revit 导出                                  │
└─────────────────────────────────────────────────────────────┘
```

**读写权限明细**：

| 层级 | 文件夹 | 读取方 | 写入方 | 流转逻辑 |
|------|--------|--------|--------|----------|
| **底层 (Baseline)** | `baseline/` | Server、Web | Revit 导出 (只读) | 启动加载 → 推送 Web 作为静态背景 |
| **中层 (Schemes)** | `schemes/{s}/zones.json` | Server、Web | AI/Server | Server 读取 → 计算边界 → 推送 Web |
| **中层 (Schemes)** | `schemes/{s}/modules.json` | Server、Web | AI/Web/Server | **双向同步**：文件变动 ↔ Web 渲染 |
| **顶层 (Computed)** | `computed/` | Server、Web | Server (自动) | 根据 openings 计算禁区等派生数据 |

### 2.4 .bcp 项目格式

`.bcp` 是项目的标准交换格式，本质是包含以下结构的 ZIP 文件：

```
project.bcp (ZIP)
├── project.json           项目元数据 + 方案列表
├── baseline/               建筑基础数据（只读）
│   ├── metadata.json
│   ├── architecture.json
│   ├── openings.json
│   ├── rooms.json
│   └── location_lines.json
├── schemes/                方案设计数据
│   └── default/            默认方案
│       ├── strategy.json
│       ├── zones.json
│       ├── finishes.json
│       └── modules.json
└── computed/               计算派生数据
    ├── room_zones.json
    └── exclusions.json
```

详细数据结构定义见：[Schema.md](./Schema.md)

---

## 3. 系统架构

### 3.1 整体架构图

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
│                         │                                                   │
│  .NET FW 4.7.2          │  .NET 6+                                          │
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
│     MainAgent（主控+SubAgent）       │  │       统一后端服务                   │
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

### 3.2 组件角色定位

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **BIMCanvas.Server** | 心脏 + 神经系统 | 状态管理、几何计算、通信中枢、事件分发 |
| **BIMCanvas.Agent** | 大脑 | 智能决策、理解意图、规划布置方案 |
| **BIMCanvas.Core** | 骨骼 | 数据结构、基础算法、类型定义 |
| **BIMCanvas.Web** | 皮肤 + 眼睛 | 渲染展示、用户交互 |
| **BIMCanvas.Revit** | 手臂 | 从 Revit 抓取数据、回写 Revit |

### 3.3 Server vs Agent 职责边界

| 维度 | Server（指挥中心） | Agent（设计师） |
|------|-------------------|-----------------|
| 状态 | 持有 CanvasDocument | 无状态 |
| 几何计算 | Zone/禁区/完成面 | 不做几何 |
| 智能决策 | 不决定"放哪里" | 规划布置方案 |
| 通信 | 连接所有组件 | 只通过 MCP/SSE |
| 验证 | 约束检查 | 依赖 Server |

**关键设计原则**：
- **Server 不做决策**：它不决定"沙发放哪里"，只执行验证和计算
- **Agent 不持有状态**：它只发指令，状态由 Server 管理
- **Server 是通信中枢**：所有组件通过它交换数据（REST/WebSocket/SSE/MCP）

---

## 4. 核心数据流

### 4.1 三条核心数据流

| 数据流 | 方向 | 触发条件 | 关键组件 |
|--------|------|---------|---------|
| **用户编辑流** | Web → Server → 文件 | 用户拖动/修改模块 | canvasStore → REST API → File |
| **文件同步流** | 文件 → Server → Web | 文件变化（Agent/外部编辑） | FileWatcher → SignalR |
| **项目加载流** | 文件 → Server → Web | 上传/切换项目 | REST API → loadProject() |

> 详细数据流场景分析（包括五个典型场景的调用链、API 参考）见：[Arch_DataFlow.md](./Arch_DataFlow.md)

### 4.2 数据流向详解

```
【从 Revit 到画布】
Revit 模型
    → [BIMCanvas.Revit] 提取原始数据
        - 墙体 → architecture.json (轮廓多边形)
        - 门窗 → openings.json (线段)
        - 房间 → rooms.json (边界 + 类型)
    → 生成精简版项目结构 (zones/modules 为空)
    → [POST] 发送到 BIMCanvas.Server
    → [BIMCanvas.Server] 数据处理
        - rooms[] → room_zones.json (生成可设计区)
        - openings[] → exclusions.json (门扇禁区)
    → [WebSocket] 推送到 Web
    → [BIMCanvas.Web] JSON → SVG 渲染

【AI 布置方案】
AI 理解用户需求
    → [Library-MCP] 搜索合适的模块/家具
    → [Canvas-MCP] 修改 modules.json
        - 约束检查：bounds 在可设计区内
        - 避障检查：不与禁区重叠
        - 碰撞检查：不与其他 modules 重叠
    → [WebSocket] 实时推送 JSON 变更
    → [BIMCanvas.Web] 重新渲染 SVG

【用户交互修改】
用户在 Web 画布操作
    → [前端] 修改本地 JSON 状态
    → [REST API] 发送变更到 Server
    → [Server] 写入文件 + 广播更新
    → AI 可感知变化并响应

【同步回 Revit】
设计方案确定
    → [BIMCanvas.Server] 导出 JSON
    → [BIMCanvas.Core] 解析 modules
        - 计算各 item 的世界坐标
        - 转换 facing → 旋转角度
    → [Revit-MCP] 创建 Revit 元素
```

---

## 5. 核心机制

### 5.1 防抖机制 (500ms)

**问题**：Agent 可能在短时间内连续写入多个文件。

**解决方案**：500ms 防抖，只在最后一次写入后触发同步。

```csharp
private const int DebounceMs = 500;

private void ScheduleUpdate(string fileName) {
    _debounceCts?.Cancel();
    _debounceCts = new CancellationTokenSource();
    _ = Task.Run(async () => {
        await Task.Delay(DebounceMs, token);
        if (!token.IsCancellationRequested)
            await BroadcastUpdate(fileName);
    }, token);
}
```

### 5.2 Git 感知机制

**问题**：Git 操作会同时修改多个文件，FileWatcher 可能触发中间状态更新。

**解决方案**：Git 操作锁

```csharp
public bool IsGitOperationInProgress { get; set; }

private void OnFileChanged(...) {
    if (_projectContext.IsGitOperationInProgress) {
        return;  // 跳过
    }
    // 正常处理
}
```

### 5.3 变更源追踪 (ChangeSource)

不同场景需要不同的历史管理策略：

| 场景 | ChangeSource | 清空历史 | 保留历史 | 保持视图 |
|------|-------------|---------|---------|---------|
| 系统初始化 | SystemInit | 是 | 否 | 否 |
| 上传新项目 | UserUpload | 是 | 否 | 否 |
| Git 切换分支 | GitCheckout | 是 | 否 | 是 |
| Git 放弃修改 | GitDiscard | 是 | 否 | 是 |
| Agent 修改 | AgentModify | 否 | 是 | 是 |
| Server 推送 | ServerSync | 否 | 是 | 是 |
| 协作同步 | CollabSync | 否 | 是 | 是 |
| 用户编辑 | UserEdit | 否 | 是 | - |

### 5.4 撤销/重做机制

在文件驱动模式下，Undo 本质是**逆向写入文件**：

1. 用户移动 A → B，Server 记录逆向操作入栈
2. 用户点击 Undo，Server 执行逆向操作并写入文件
3. **外部干扰规则**：一旦检测到非 Web 端发起的文件变更（如手动编辑），立即清空 Undo 栈

> **外部干扰规则详解**：外部修改切断了 Undo 链条，强行回滚会导致状态不一致。因此系统会主动清空历史栈，确保数据安全。

### 5.5 持久化双层策略

采用 **"磁盘即时同步 + Git 周期存档"** 的双层策略：

| 层级 | 触发时机 | 动作 | 效果 |
|------|---------|------|------|
| **第一层：磁盘同步** | 用户交互结束时（MouseUp） | 立即写入 JSON 文件 | VS Code 等工具能实时看到修改 |
| **第二层：版本存档** | 显式保存 / 每隔 1 分钟 | `git add . && git commit` | 生成 Git 历史节点 |

**第一层去抖动**：禁用。采用阻塞式立即写入，确保文件系统与内存状态毫秒级一致。

### 5.6 PlacementValidator 设计原则

**职责边界**：

| 类 | 职责 | 关键原则 |
|---|------|---------|
| `GeometryNormalizer` | AI 意图 → Polygon2D | 纯几何转换 |
| `PlacementValidator` | 布置验证 | **只验证，不修正** |

**关键设计原则**：

- `PlacementValidator` **只做 Validation**，返回验证结果
- **不做 Correction**：「床头靠墙」是 AI 的规划职责，不是 Core 的修正职责
- 未来如需吸附功能，单独创建 `SnapHelper` 或 `ConstraintSolver`

> 详细转换器架构（包括转换链路、坐标转换公式、NTS 中间层设计）见：[Arch_Converter.md](./Arch_Converter.md)

### 5.7 WallFinish 三层来源机制

完成面（WallFinish）支持三层来源，按优先级从低到高：

| 优先级 | 来源类型 | 说明 | 示例 |
|--------|---------|------|------|
| 1 (最低) | **RoomDefault** | 根据 Room.Type 查配置 | 卧室 → 乳胶漆 → 0mm |
| 2 | **ZoneOverride** | Zone.Tags 匹配规则 | tv_media → 护墙板 → 80mm |
| 3 (最高) | **UserOverride** | 用户手动选择 | 用户指定特定墙面材质 |

**计算规则**：
- 每个完成面分段（FinishSegment）继承最高优先级的来源
- 系统自动合并相邻的同材质分段

---

## 6. Git 工作流

### 6.1 分支与工作树策略

为实现真正的**物理隔离**和**并发读写**，采用混合架构：

- **存储层**：单仓库 + 多分支，所有数据在一个 `.git` 历史中
- **执行层**：使用 Git Worktree 处理临时任务

### 6.2 AI 辅助设计工作流

1. **用户请求**：用户在 Web 端请求"帮我重新布置主卧"
2. **工作树创建**：Server 执行 `git worktree add .temp/ai-job-1 feat/ai-feat-001`
3. **AI 生成**：AI 在临时目录下修改 `modules.json`，提交代码
4. **方案评审**：
   - AI 完成后，Web 端进入"评审模式"
   - Server 同时读取主目录和工作树目录的数据
   - 前端渲染"左右分屏"对比

### 6.3 Visual Merge UI（可视化冲突解决）

这是本架构的核心交互组件，用于 AI 方案与用户方案的融合。

**界面设计**：
- 分屏显示：左侧"我的方案"，右侧"AI 提案"
- 允许按区域选择保存 AI 生成的方案

**颗粒度**：按 **Zone（可设计区）** 进行差异对比

**交互逻辑**：
1. 即使没有代码冲突，用户也可以进行**选择性合并**
2. 示例：*"主卧采纳 AI 的（勾选右边），但客厅保留我的（勾选左边）"*

**执行结果**：
1. Server 根据用户选择，执行精确的 JSON 合并（Cherry-pick）
2. 生成一个新的 Commit 到 `main` 分支
3. Web 端退出评审模式，显示融合后的新方案

**核心价值**：

| 特性 | 说明 |
|------|------|
| **零数据丢失** | 用户的修改和 AI 的方案都在各自的分支里安全保存 |
| **选择权** | 用户不再被 AI 强制覆盖，而是拥有最终的"采纳权" |
| **可回溯** | 所有的尝试都有 Git 记录，随时可以回退 |

---

## 7. 坐标系统

BIMCanvas 采用 **CAD 标准坐标系**（笛卡尔坐标系）：

| 属性 | BIMCanvas | Web 屏幕 |
|------|-----------|----------|
| 原点 | 左下角 | 左上角 |
| Y 轴 | **向上为正** | 向下为正 |
| 单位 | 毫米 (mm) | 像素 (px) |

### 各层职责

```
┌─────────────────────────────────────────────────────────┐
│  Revit 层 (.NET FW 4.7.2)                               │
│  - 导出原始坐标（Y-up）                                  │
│  - 存入 metadata.json                                   │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Core 层 (.NET Standard 2.0)                            │
│  - 纯笛卡尔坐标运算                                      │
│  - 不做任何坐标系转换                                    │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Web 层 (Vue 3 + TypeScript)                            │
│  - 渲染时转换：y_screen = height - y_model              │
│  - 事件处理时反向转换                                    │
│  - 禁止使用 CSS scaleY(-1)                              │
└─────────────────────────────────────────────────────────┘
```

### 前端坐标转换

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

### 角度语义规范

项目中存在三套角度系统，**混用会导致方向相反的 bug**：

| 系统 | 正方向 | 来源 | 使用场景 |
|------|--------|------|----------|
| **数据模型角** | CCW+ | 2D 数学（Y-up） | `rotatePoint2D()`, JSON 存储 |
| **交互角** | CW+ | `atan2(z, x)` | 鼠标拖动计算 |
| **Three.js 角** | CCW+ | `rotation.y` | 3D 渲染预览 |

**根因**：坐标映射 `y → -z` 是镜像操作，翻转了 CCW/CW。

**转换规则**：

| 转换方向 | 操作 |
|----------|------|
| 交互角 → 数据模型角 | 取反 |
| 交互角 → Three.js | 取反 |
| 用户输入（度数）→ 数据模型角 | 直接使用 |

**代码规范**：

```typescript
// ✗ 错误：交互角直接当模型角
const delta = endAngle - startAngle;
rotatePoint2D(point, center, delta);

// ✓ 正确：交互角取反后传入
const delta = -(endAngle - startAngle);
rotatePoint2D(point, center, delta);
```

> 详见：`BIMCanvas.Web/README.md` §角度语义系统

---

## 8. 项目结构

### 8.1 解决方案目录

```
BIMCanvas/                                    【根目录】
│
├── BIMCanvas.slnx                            【解决方案文件】
│
├── BIMCanvas.Core/                           【项目】核心类库 (.NET Standard 2.0)
│   ├── Models/                               【目录】数据模型
│   │   ├── Primitives/                       几何基元
│   │   │   ├── Point2D.cs                      坐标点（readonly struct）
│   │   │   ├── Vec2D.cs                        向量（结构同 Point2D，语义不同）
│   │   │   ├── Polygon2D.cs                    多边形（封装 Point2D[]）
│   │   │   ├── Line2D.cs                       线段
│   │   │   ├── AABB.cs                         轴对齐包围盒
│   │   │   ├── Facing.cs                       朝向（联合类型：语义/向量）
│   │   │   └── FacingDirection.cs              朝向方向枚举
│   │   ├── RevitSource/                      Revit 导出数据
│   │   ├── CanvasData/                       画布数据 (Zone, WallFinish, ExclusionArea)
│   │   └── RevitWriteback/                   回写数据 (Module, ModuleItem)
│   ├── Algorithms/                           【目录】空间计算
│   │   ├── Geometry/                         几何运算
│   │   │   ├── NtsAdapter.cs                   NTS 适配器（internal）
│   │   │   └── CollisionDetector.cs            碰撞检测（调用 NTS）
│   │   └── Spatial/                          空间业务逻辑
│   │       ├── GeometryNormalizer.cs           AI 意图 → Polygon2D
│   │       ├── PlacementValidator.cs           布置验证（只验证，不修正）
│   │       ├── FacingHelper.cs                 方向语义 ↔ Vec2D
│   │       └── FinishRules.cs                  特殊完成面规则表
│   └── Converters/                           【目录】转换器
│       ├── UnitConverter.cs                    单位转换（feet↔mm, rad↔deg）
│       └── NtsConverter.cs                     NTS ↔ Core.Models 类型转换
│
├── BIMCanvas.Revit/                          【项目】Revit 插件 (.NET FW 4.7.2)
│   ├── Commands/                             【目录】Ribbon 命令
│   │   └── ExportCanvasCommand.cs              导出命令
│   ├── Adapters/                             【目录】数据适配器
│   │   ├── BoundaryAdapter.cs                  边界轮廓提取（墙体+柱子几何切割）
│   │   ├── OpeningAdapter.cs                   门窗数据提取
│   │   └── RoomAdapter.cs                      房间边界提取
│   ├── Models/                               【目录】中间模型
│   │   ├── RevitBoundary.cs                    保留元素追溯信息
│   │   ├── RevitOpening.cs                     门窗几何+方向信息
│   │   └── RevitRoom.cs                        房间边界+名称
│   ├── Converters/                           【目录】转换器
│   │   └── RevitNtsConverter.cs                Revit API ↔ NTS 类型转换
│   ├── Services/                             【目录】服务层
│   │   ├── CanvasExportService.cs              画布导出服务（6阶段流程）
│   │   ├── CoordinateTransformer.cs            坐标转换器（有状态）
│   │   └── RoomTypeInferrer.cs                 房间类型推断
│   └── Utilities/                            【目录】工具类
│       ├── OutlineExtractor.cs                 轮廓提取（Boolean 运算）
│       └── OpeningDirectionAnalyzer.cs         门窗方向分析
│
├── BIMCanvas.Server/                         【项目】统一后端 (.NET 6+)
│   ├── McpTools/                             【目录】Canvas-MCP 工具
│   │   ├── ModuleTools.cs                      模块操作（add/move/rotate/delete）
│   │   ├── CanvasTools.cs                      画布管理（create/describe/export）
│   │   └── QueryTools.cs                       查询分析（module_at/space_analyze）
│   ├── Controllers/                          【目录】REST API
│   │   └── ProjectController.cs                项目管理接口
│   ├── Hubs/                                 【目录】SignalR Hub
│   │   └── CanvasHub.cs                        实时通信
│   └── Services/                             【目录】状态管理
│       ├── ProjectContext.cs                   项目上下文（单项目模式）
│       ├── ProjectWatcherService.cs            文件监听服务（500ms 防抖）
│       └── EventBus.cs                         事件总线
│
├── BIMCanvas.Agent/                          【项目】AI Agent (Python 3.10+)
│   └── MainAgent                          主控Agent + SubAgent 架构
│
├── BIMCanvas.Web/                            【项目】Web 前端 (Vue 3 + TS)
│   ├── src/stores/                           Pinia 状态管理
│   │   ├── canvasStore.ts                      画布状态
│   │   └── gitStore.ts                         Git 状态
│   ├── src/services/                         服务层
│   │   ├── SignalRService.ts                   WebSocket 客户端
│   │   └── state/TimelineManager.ts            历史管理器
│   └── src/components/                       Vue 组件
│
└── docs/                                     【文档】
    ├── Schema.md                             数据模型规范
    ├── Architecture.md                       系统架构 (本文档)
    ├── Arch_MCP_Tools.md                     MCP 工具规范
    ├── Arch_Converter.md                     转换器架构专题
    ├── Arch_DataFlow.md                      数据流场景分析专题
    └── Flow_Workflows.md                     业务流程
```

---

## 版本历史

| 版本 | 日期 | 变更内容 |
|------|------|----------|
| v3.0 | 2026-01-13 | 文档重构：合并 Architecture.md + FileDrivenArchitecture.md + Data_Flow_Guide.md；统一 File-Driven Architecture 表述；新增 ChangeSource 机制说明；更新项目文件结构为 Schema v3.0 定义 |
