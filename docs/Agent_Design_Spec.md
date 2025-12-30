# PlacementAgent 完整指南

> BIMCanvas AI Agent 的完整理论文档：定位、能力、工作流程、并行架构、设计规则与工具体系

---

## 一、Agent 定位与职责边界

### 1.1 核心定位

PlacementAgent 是 BIMCanvas 系统中的「设计师」角色，负责智能决策，但不持有状态。

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **Agent** | 大脑 | 智能决策、理解意图、规划方案 |
| **Server** | 心脏 + 神经系统 | 状态管理、几何计算、验证、通信 |
| **Core** | 骨骼 | 数据结构、基础算法、类型定义 |
| **Web** | 皮肤 + 眼睛 | 渲染展示、用户交互 |

### 1.2 职责边界

```
Server 是「指挥中心」：协调各方、管理状态、执行验证，但不做布置决策
Agent 是「设计师」：理解需求、做出决策、发出指令，但不持有状态
```

| 维度 | Server（指挥中心） | Agent（设计师） |
|------|-------------------|----------------|
| **状态管理** | ✅ 管理项目文件夹 | ❌ 无状态 |
| **几何计算** | ✅ Zone生成/禁区/innerBoundary | ❌ 不做几何计算 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| **约束验证** | ✅ 边界/碰撞检查 | ❌ 依赖 Server |
| **Git 操作** | ✅ Worktree 创建/合并 | ✅ 在 Worktree 中工作 |
| **通信中枢** | ✅ REST/WebSocket/SSE/MCP | ❌ 只通过 MCP/SSE |

### 1.3 Agent 专属职责

**Agent 负责（智能决策）：**
- 理解用户意图和设计需求
- 分析房间功能，推断功能标签 (tags)
- 选择合适的家具模块
- 决策家具摆放位置和朝向
- 遵循设计原则进行布置
- 策略参数化（根据不同策略调整决策权重）
- 生成设计说明文档（解释设计决策）
- 语义化 Commit（提交时说明设计意图）

**Agent 绝不做的事：**
- ❌ Zone 生成（读取 Server 预计算的 `computed/zones.json`）
- ❌ 禁区计算（读取 `computed/exclusions.json`）
- ❌ InnerBoundary 计算（读取 Server 预计算结果）
- ❌ 约束验证（Server 负责，验证失败会通知 Agent 修正）
- ❌ Git 仓库管理（Server 负责创建/合并 Worktree）

### 1.4 AI 作为 OBB 规划师

> AI 只操作「方向包围盒」(Oriented Bounding Box)，不处理复杂几何。

```
AI 视角：
┌─────────────┐
│   bounds    │  ← AI 操作的是矩形包围盒
│  [4 顶点]   │
│   facing    │  ← 语义朝向（north/south/east/west）
└─────────────┘

Core 层：
bounds + facing → 精确几何位置 + 旋转角度
```

---

## 二、核心工作场景

> 以下场景展示了 Agent 在不同情境下的工作模式。

### 场景 A：策略分叉 (Strategy Fork)

**目标**：风格与策略的 A/B 测试

**用户指令**："给我的客厅出三个方案：一个是'极致收纳'，一个是'动线优先'，还有一个'极简留白'。"

**系统行为**：
1. **分支裂变**：Server 基于 `main` 创建三个 Worktree：
   - `.worktrees/ai-living-storage`
   - `.worktrees/ai-living-flow`
   - `.worktrees/ai-living-minimal`
2. **并发执行**：三个 AI Agent 实例同时启动，加载同一份 `baseline/` 数据，但注入不同的**策略参数**。
3. **独立产出**：
   - AI-1 (收纳)：生成满墙柜体，牺牲部分通道宽度
   - AI-2 (动线)：保留宽敞回游动线，减少非必要家具
   - AI-3 (极简)：只保留核心家具，大量留白

**对 Agent 的能力要求**：
- **策略参数化**：支持配置权重（如 `storage_weight=0.9`, `flow_weight=0.2`）
- **自我辩护**：提交方案时附带 Markdown 设计说明，解释设计权衡

### 场景 B：布局求解器 (Layout Solver)

**目标**：硬约束下的局部最优解搜索

**用户指令**："这个卫生间太小了，帮我看看能不能塞进一个浴缸和淋浴房。"

**系统行为**：
1. **沙盒模式**：Server 创建临时 Worktree `.worktrees/ai-bathroom-solver`
2. **迭代搜索**：Agent 在沙盒中进行高频迭代
   - 尝试 1：失败（浴缸挡门）
   - 尝试 2：失败（淋浴房与马桶重叠）
   - ...
   - 尝试 100：**成功**（找到唯一可行的极限布局）
3. **结果交付**：只有验证成功的方案会被提交

**对 Agent 的能力要求**：
- **沙盒模拟**：在不污染主分支的情况下进行"试错-回滚"
- **失败感知**：读懂 Server 的验证错误，转化为下一次尝试的约束条件

### 场景 C：主编式合并 (Editorial Merge)

**目标**：用户作为总设计师的方案融合

**用户指令**：用户看着三个平行方案，觉得"方案 A 的沙发摆得好，但方案 B 的电视柜设计更合理"。

**系统行为**：
1. **可视化对比**：前端通过"三联屏"展示不同 Worktree 的渲染结果
2. **区域级选择**：用户勾选方案 A 的 `Zone: SofaArea` 和方案 B 的 `Zone: TVArea`
3. **Cherry-pick**：Server 执行精确的 JSON 合并

**对 Agent 的能力要求**：
- **解耦设计**：生成的方案应高度模块化，避免强耦合
- **依赖标记**：强关联的家具需标记 `DependencyGroup`，提示用户成套采纳

---

## 三、Git Worktree 并行架构

> 核心技术：使用 Git Worktree 实现物理隔离的并行开发

### 3.1 架构原理

传统认知：`1 个 Git 仓库 = 1 个文件夹 = 1 个当前分支`
实际上：`1 个 Git 仓库 = N 个文件夹 = N 个并行分支`

**`git worktree`** 允许从同一个 `.git` 仓库中，"映射"出多个独立的文件夹，每个文件夹对应不同的分支。

### 3.2 架构方案对比

| 架构方案 | 物理结构 | 适用场景 | 结论 |
|----------|----------|----------|------|
| 多分支 (Multi-Branch) | 1 个文件夹，内容切换 | 单人串行工作 | ❌ 无法并行 |
| 多仓库 (Multi-Repo) | N 个文件夹，独立历史 | 完全独立的项目 | ❌ 合并困难 |
| **多工作树 (Multi-Worktree)** | **N 个文件夹，共享历史** | **单机并行工作** | **✅ 最佳选择** |

### 3.3 混合架构

```
项目根目录/
├── .git/                      # 共享的 Git 历史
├── baseline/                  # main 分支的基础数据
├── schemes/                   # main 分支的方案
├── .worktrees/                # 临时工作树目录
│   ├── ai-job-1/              # Worktree 1 → feat/ai-storage
│   │   ├── baseline/          # 继承 main 的基础数据
│   │   └── schemes/           # AI-1 的独立方案
│   ├── ai-job-2/              # Worktree 2 → feat/ai-flow
│   │   ├── baseline/
│   │   └── schemes/
│   └── ai-job-3/              # Worktree 3 → feat/ai-minimal
│       ├── baseline/
│       └── schemes/
```

**存储层**：
- 使用单仓库 + 多分支
- `main` 分支：用户当前状态
- `scheme/{id}` 分支：保存的设计方案
- `feat/ai-{jobId}-{name}` 分支：AI 的临时提案

**执行层**：
- 使用 Git Worktree 处理临时任务
- 当 AI 启动时：`git worktree add .worktrees/ai-job-1 feat/ai-proposal`
- 当 AI 完成时：`git worktree remove .worktrees/ai-job-1`

### 3.4 并行工作流程

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Agent + Git 完整工作流                               │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  【触发阶段】                                                            │
│  用户请求 ──→ Server 解析意图 ──→ 创建策略配置                           │
│                                                                         │
│  【准备阶段】                                                            │
│  Server ──→ git worktree add .worktrees/ai-job-{id} feat/ai-{name}     │
│         ──→ 将策略配置写入 Worktree                                     │
│         ──→ 启动 Agent 进程，传入 Worktree 路径                         │
│                                                                         │
│  【执行阶段】                                                            │
│  Agent ──→ 在 Worktree 中读取数据                                       │
│        ──→ 执行布置决策                                                  │
│        ──→ 写入 schemes/{s}/*.json                                      │
│        ──→ 写入设计说明 schemes/{s}/README.md                           │
│        ──→ git add . && git commit -m "feat(layout): ..."              │
│                                                                         │
│  【验证阶段】                                                            │
│  Server ──→ 读取 Commit 内容                                            │
│         ──→ 执行约束验证                                                 │
│         ──→ 验证通过：通知前端展示                                       │
│         ──→ 验证失败：通知 Agent 修正                                    │
│                                                                         │
│  【交付阶段】                                                            │
│  用户选择 ──→ Server 执行 git merge feat/ai-{name} 到 main             │
│           ──→ git worktree remove .worktrees/ai-job-{id}               │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 四、两阶段工作流程

### 4.1 流程概览

```
┌─────────────────────────────────────────────────────────────────┐
│                     PlacementAgent 工作流                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【Phase A: 分区设计】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 输入：                                                    │   │
│  │   • computed/zones.json (Room Zone)                      │   │
│  │   • baseline/rooms.json (房间名称、类型)                  │   │
│  │   • 用户需求 + 策略参数                                   │   │
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 分析户型结构（几室几厅几卫）                         │   │
│  │   2. 为每个 Room Zone 推断功能标签 (tags)                 │   │
│  │   3. 根据策略调整标签权重                                 │   │
│  │   4. 生成 Designable Zone                                │   │
│  │   5. 细分设计区（如客厅分为沙发区、电视区）              │   │
│  │                                                          │   │
│  │ 输出：                                                    │   │
│  │   • schemes/{s}/zones.json (Designable Zone)             │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              ↓                                  │
│  【Phase B: 布置决策】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ 输入：                                                    │   │
│  │   • schemes/{s}/zones.json (Designable Zone)             │   │
│  │   • modules/*.svg (素材库)                               │   │
│  │   • baseline/openings.json (门窗位置)                    │   │
│  │   • computed/exclusions.json (禁区)                      │   │
│  │   • 策略参数                                              │   │
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 根据 tags + 策略过滤合适的模块                      │   │
│  │   2. 确定锚点家具位置                                     │   │
│  │   3. 围绕锚点布置主要家具                                 │   │
│  │   4. 填充辅助家具                                         │   │
│  │   5. 确定朝向                                            │   │
│  │                                                          │   │
│  │ 输出：                                                    │   │
│  │   • schemes/{s}/modules.json (布置结果)                  │   │
│  │   • schemes/{s}/README.md (设计说明)                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                              ↓                                  │
│  【Phase C: 提交交付】                                           │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ AI 任务：                                                 │   │
│  │   1. 生成语义化 Commit Message                           │   │
│  │   2. 执行 git add && git commit                          │   │
│  │   3. 通知 Server 验证                                     │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 4.2 Phase A: 分区设计

#### Step 1: 功能标签推断

从 Room Zone 的 `name` 和 `reason` 字段推断功能标签：

| reason | name 关键词 | 推荐 tags |
|--------|-------------|-----------|
| room:LivingRoom | 客厅 | sitting, entertainment, tv_media |
| room:MasterBedroom | 主卧 | sleeping, rest, storage, dressing |
| room:Bedroom | 次卧/卧室 | sleeping, rest |
| room:Bathroom | 卫生间/主卫/公卫 | bathing, toilet |
| room:Kitchen | 厨房 | cooking, storage |
| room:DiningRoom | 餐厅 | dining |

#### Step 2: 素材库过滤

根据 tags + 风格 + 策略参数过滤合适的模块：
- 策略为"极致收纳"时，优先选择储物类家具
- 策略为"动线优先"时，减少大型家具数量
- 策略为"极简留白"时，只选择核心家具

#### Step 3: 设计区划分

将 Room Zone 进一步细分为 Designable Zone：
- 客厅 → 沙发区、电视区、通道区
- 卧室 → 睡眠区、储物区、梳妆区

### 4.3 Phase B: 布置决策

#### 设计原则

| 类型 | 规则 | 示例 |
|------|------|------|
| **靠墙规则** | 大型家具尽量靠墙 | 衣柜、床、沙发 |
| **居中规则** | 某些家具居中于墙面 | 电视柜居中于电视墙 |
| **顶角规则** | 某些家具顶墙角 | 衣柜、书柜 |
| **朝向规则** | 模块背对墙 | 沙发背墙，面向中心 |
| **对位规则** | 家具对位关系 | 沙发正对电视 |
| **避窗规则** | 除淋浴外避免靠窗 | 床头不靠窗 |
| **避门规则** | 不阻挡门开启范围 | 利用 openings 数据 |

#### 布置优先级

```
1. 【锚点家具】确定设计区的"锚点"
   • 客厅: 电视墙位置 → 电视柜
   • 卧室: 床头墙位置 → 床
   • 餐厅: 主位置 → 餐桌

2. 【主要家具】围绕锚点布置
   • 客厅: 沙发（正对电视柜）
   • 卧室: 衣柜、床头柜

3. 【辅助家具】填充剩余空间
   • 茶几、边几、装饰柜等
```

#### 布置约束

```
对于每个要放置的模块：
1. bounds 必须完全在 Designable Zone boundary 内
2. bounds 不能与任何 exclusionAreas 重叠
3. bounds 不能与其他已放置 modules 重叠
4. 不能阻挡门的开启范围
```

---

## 五、策略参数化

### 5.1 策略配置结构

```json
{
  "name": "极致收纳",
  "approach": "StorageFirst",
  "weights": {
    "storage": 0.9,
    "circulation": 0.2,
    "aesthetics": 0.3
  },
  "constraints": {
    "min_aisle_width": 600,
    "max_furniture_count": null
  }
}
```

### 5.2 策略类型

| 策略 | approach | 说明 |
|------|----------|------|
| 极致收纳 | StorageFirst | 最大化储物空间，可牺牲部分通道宽度 |
| 动线优先 | CirculationFirst | 保证宽敞的行走动线，减少家具数量 |
| 极简留白 | MinimalistFirst | 只保留核心家具，大量留白 |
| 舒适优先 | ComfortFirst | 优先考虑使用舒适度，如沙发尺寸 |
| 均衡方案 | Balanced | 各方面均衡考虑 |

### 5.3 策略影响决策

```python
class PlacementAgent:
    def select_furniture(self, zone, strategy):
        if strategy.approach == "StorageFirst":
            # 优先选择带储物功能的家具
            # 增加柜体数量
            # 允许通道略窄
        elif strategy.approach == "CirculationFirst":
            # 减少大型家具
            # 保证通道宽度 >= 900mm
        elif strategy.approach == "MinimalistFirst":
            # 只选择核心家具
            # 跳过辅助家具
```

---

## 六、工具体系 (Tools)

> Agent 通过工具与系统交互，工具分为数据读取、方案写入、Git 操作三类。

### 6.1 数据读取工具

| 工具 | 功能 | 说明 |
|------|------|------|
| `read_room_zones` | 读取 Room Zone | 读取 `computed/zones.json`（Server 预计算） |
| `read_openings` | 读取门窗数据 | 读取 `baseline/openings.json` |
| `read_exclusions` | 读取禁区数据 | 读取 `computed/exclusions.json`（Server 预计算） |
| `list_modules` | 列出素材库 | 读取 `modules/*.svg` 文件名，解析尺寸信息 |
| `read_strategy` | 读取策略配置 | 读取 `strategy.json`（Server 注入） |

### 6.2 方案写入工具

| 工具 | 功能 | 说明 |
|------|------|------|
| `write_design_zones` | 写入 Designable Zone | 写入 `schemes/{s}/zones.json` |
| `write_modules` | 写入模块布置 | 写入 `schemes/{s}/modules.json` |
| `write_readme` | 写入设计说明 | 写入 `schemes/{s}/README.md` |

### 6.3 Git 操作工具

| 工具 | 功能 | 说明 |
|------|------|------|
| `git_add` | 添加文件到暂存区 | `git add .` |
| `git_commit` | 提交变更 | `git commit -m "..."` |
| `git_status` | 查看状态 | 检查当前工作区状态 |

### 6.4 工具调用示例

```python
# 读取房间数据
zones = read_room_zones(project_path)

# 读取策略配置
strategy = read_strategy(project_path)

# 执行布置决策
modules = plan_layout(zones, strategy)

# 写入结果
write_modules(project_path, scheme_id, modules)
write_readme(project_path, scheme_id, design_notes)

# 提交变更
git_add(project_path)
git_commit(project_path, f"feat(layout): {strategy.name} for {zone.name}")
```

---

## 七、数据格式

### 7.1 输入数据

#### computed/zones.json (Room Zone)

```json
{
  "id": "rz_1",
  "name": "次卧一",
  "roomId": "r_1",
  "type": 1,
  "reason": "room:Bedroom",
  "rawBoundary": [[9400, 10500], [6600, 10500], ...],
  "tags": [],
  "computedBoundary": null
}
```

#### baseline/openings.json (门窗)

```json
{
  "id": "d_1",
  "type": 0,
  "roomIds": ["r_6"],
  "line": [[2100, 5600], [2100, 5800]],
  "facingDirection": [-1, 0],
  "handDirections": [[-1, 0]]
}
```

- `type`: 0=门, 1=窗
- `facingDirection`: 门窗面向方向
- `handDirections`: 门扇开启方向

### 7.2 输出数据

#### schemes/{s}/zones.json (Designable Zone)

```json
{
  "id": "dz_1",
  "type": "Designable",
  "parentRoomZoneId": "rz_3",
  "name": "主卧",
  "tags": ["sleeping", "rest", "storage", "dressing"],
  "boundary": [[14100, 5750], [11200, 5750], ...],
  "openings": ["d_3", "d_7"]
}
```

#### schemes/{s}/modules.json (布置结果)

**格式 A: bounds 四顶点**
```json
{
  "id": "m_1",
  "moduleId": "bed_king",
  "moduleName": "King Bed",
  "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
  "facing": "east",
  "zoneId": "dz_1",
  "svgPath": "modules/床_双人_2000x1800.svg",
  "dependencyGroup": "bedroom_core"
}
```

**格式 B: position + size**
```json
{
  "id": "m_1",
  "moduleId": "bed_king",
  "moduleName": "King Bed",
  "position": [10100, 2750],
  "size": [2000, 1800],
  "facing": "east",
  "zoneId": "dz_1",
  "svgPath": "modules/床_双人_2000x1800.svg"
}
```

#### schemes/{s}/README.md (设计说明)

```markdown
# 主卧布置方案 - 极致收纳

## 设计策略
本方案采用"极致收纳"策略，最大化储物空间。

## 设计决策
1. **床位置**：靠东墙居中，为衣柜预留最大空间
2. **衣柜**：选择三门衣柜，占据整面北墙
3. **床头柜**：单侧放置，另一侧用于通道

## 权衡说明
- 牺牲：通道宽度从 900mm 减少到 700mm
- 获得：衣柜容量增加 40%

## 依赖关系
- 床 + 床头柜：建议成套采纳
```

### 7.3 Facing 类型

| 格式 | 示例 | 说明 |
|------|------|------|
| 语义字符串 | `"north"` | 标准 8 方向 |
| Vec2D | `[0.707, 0.707]` | 任意角度单位向量 |

**语义字符串 → 角度转换：**

| 朝向 | 角度 | 朝向 | 角度 |
|------|------|------|------|
| north | 0° | south | 180° |
| east | 90° | west | 270° |
| northeast | 45° | southwest | 225° |
| southeast | 135° | northwest | 315° |

---

## 八、模块素材库规范

### 8.1 文件组织

```
{项目根目录}/modules/
├── 床_双人_2000x1800.svg
├── 床_单人_1200x1900.svg
├── 衣柜_三门_2400x600.svg
├── 衣柜_双门_1800x600.svg
├── 床头柜_500x500.svg
├── 沙发_三人_2400x900.svg
├── 沙发_双人_1800x900.svg
├── 沙发_贵妃_1500x800.svg
├── 茶几_方形_1200x600.svg
├── 茶几_圆形_800x800.svg
├── 电视柜_1800x400.svg
├── 餐桌_六人_1800x900.svg
├── 餐椅_450x450.svg
├── 马桶_400x700.svg
├── 洗手台_600x500.svg
└── 淋浴房_900x900.svg
```

### 8.2 命名规范

```
{名称}_{规格}_{宽}x{高}.svg

名称：家具中文名
规格：可选的规格描述（如三人、双门）
宽x高：模块尺寸（mm），宽度 x 高度（深度）
```

### 8.3 解析逻辑

```python
def parse_svg_filename(filename: str) -> dict:
    """解析 SVG 文件名获取模块信息"""
    name = filename.replace(".svg", "")
    parts = name.rsplit("_", 1)  # 从右边分割

    # 尺寸部分
    size_str = parts[-1]  # "2000x1800"
    width, height = map(int, size_str.split("x"))

    # 名称部分
    name_part = parts[0]  # "床_双人"

    return {
        "templateId": name,
        "name": name_part,
        "size": [width, height],
        "svgPath": f"modules/{filename}"
    }
```

---

## 九、触发方式

Agent 支持三种触发方式：

| 触发方式 | 触发源 | 数据流 |
|----------|--------|--------|
| AI 对话 | 用户输入 | 用户 → Agent Chat → PlacementAgent.run() |
| Web 按钮 | 前端 UI | Web → Server EventBus → SSE → Agent |
| 自动修正 | Server 检测 | Server 验证 → EventBus → SSE → Agent |

---

## 十、技术栈

- **语言**：Python 3.10+
- **框架**：Anthropic Agent SDK
- **模型**：Claude Sonnet 4
- **依赖**：`pip install anthropic`

### Agent SDK 使用示例

```python
from anthropic import Anthropic

client = Anthropic()

# 定义工具
tools = [
    {
        "name": "read_room_zones",
        "description": "读取项目的 Room Zone 数据",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string"}
            },
            "required": ["project_path"]
        }
    },
    {
        "name": "git_commit",
        "description": "提交当前变更到 Git",
        "input_schema": {
            "type": "object",
            "properties": {
                "project_path": {"type": "string"},
                "message": {"type": "string"}
            },
            "required": ["project_path", "message"]
        }
    },
    # ... 更多工具
]

# 运行 Agent
response = client.messages.create(
    model="claude-sonnet-4-20250514",
    max_tokens=4096,
    tools=tools,
    messages=[
        {"role": "user", "content": "请为这个户型设计家具布置"}
    ]
)
```

---

## 十一、注意事项

### 11.1 坐标系统

- **坐标系**：CAD 标准（原点左下角，Y 轴向上）
- **单位**：毫米 (mm)
- **精度**：整数即可，无需小数

### 11.2 ID 命名约定

| 类型 | 前缀 | 示例 |
|------|------|------|
| Room Zone | rz_ | rz_1, rz_2 |
| Designable Zone | dz_ | dz_1, dz_2 |
| Module | m_ | m_1, m_2 |
| Door/Window | d_ | d_1, d_2 |
| Room | r_ | r_1, r_2 |

### 11.3 常见陷阱

1. **bounds 顺序**：四顶点按逆时针或顺时针连续排列
2. **facing 方向**：指模块的「正面」朝向，不是背面
3. **重叠检测**：由 Server 执行，Agent 无需自行验证
4. **边界越界**：由 Server 执行，Agent 无需自行验证

### 11.4 文件驱动原则

> 文件是唯一真理源 (Single Source of Truth)

- Agent 在 Worktree 中直接读写 JSON 文件
- Git Commit 是成果交付的唯一方式
- 修改通过 Commit 记录，可追溯、可回滚

---

## 十二、相关文档

- `docs/AI_Parallel_Design_Patterns.md` - 并行设计模式详细说明
- `docs/Schema-JSON-v3.md` - v3.0 数据模型定义
- `docs/Architecture.md` - 系统架构文档
- `plans/Agent_MVP.md` - MVP 阶段快速启动指南
