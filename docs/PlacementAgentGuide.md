# PlacementAgent 工作指南

> BIMCanvas AI Agent 的工作流程、职责边界与注意事项

---

## 零、Server vs Agent 职责边界（重要）

### 核心原则

```
Server 是「指挥中心」：协调各方、管理状态、执行验证，但不做布置决策
Agent 是「设计师」：理解需求、做出决策、发出指令，但不持有状态
```

### 职责对比

| 维度 | Server（指挥中心） | Agent（设计师） |
|------|-------------------|----------------|
| **状态管理** | ✅ 管理项目文件夹 | ❌ 无状态 |
| **几何计算** | ✅ Zone生成/禁区/innerBoundary | ❌ 不做几何计算 |
| **智能决策** | ❌ 不决定"放哪里" | ✅ 规划布置方案 |
| **约束验证** | ✅ 边界/碰撞检查 | ❌ 依赖 Server |
| **通信中枢** | ✅ REST/WebSocket/SSE/MCP | ❌ 只通过 MCP/SSE |

### Agent 绝不做的事

- ❌ **Zone 生成**：读取 Server 预计算的 `computed/zones.json`
- ❌ **禁区计算**：读取 `computed/exclusions.json` 或使用 openings 避让
- ❌ **InnerBoundary 计算**：读取 Server 预计算结果
- ❌ **约束验证**：Server 负责，验证失败会通知 Agent 修正
- ❌ **文件系统管理**：通过 MCP 工具间接操作

### 数据依赖关系

| 数据 | 来源 | Agent 操作 |
|------|------|-----------|
| Room Zone | Server 预计算 → `computed/zones.json` | 读取 |
| 门窗数据 | Revit 导出 → `baseline/openings.json` | 读取 |
| 禁区 | Server 预计算 → `computed/exclusions.json` | 读取（如有） |
| 素材库 | 项目 → `modules/*.svg` | 读取 |
| Designable Zone | Agent 生成 → `schemes/{s}/zones.json` | 写入 |
| 布置结果 | Agent 生成 → `schemes/{s}/modules.json` | 写入 |

---

## 一、Agent 定位与职责

### 1.1 核心定位

PlacementAgent 是 BIMCanvas 系统中的「设计师」角色：

| 组件 | 比喻 | 核心职责 |
|------|------|----------|
| **Agent** | 大脑 | 智能决策、理解意图、规划方案 |
| **Server** | 心脏 + 神经系统 | 状态管理、几何计算、验证、通信 |
| **Core** | 骨骼 | 数据结构、基础算法、类型定义 |
| **Web** | 皮肤 + 眼睛 | 渲染展示、用户交互 |

### 1.2 Agent 专属职责

**Agent 负责（智能决策）：**
- 理解用户意图和设计需求
- 分析房间功能，推断功能标签 (tags)
- 选择合适的家具模块
- 决策家具摆放位置和朝向
- 遵循设计原则进行布置

**Agent 不负责（由 Server 处理）：**
- Zone 生成和几何计算（Server ZoneCalculator）
- 禁区计算（Server ComputedDataService）
- 验证布置结果（Server PlacementValidator）
- 管理系统状态（Server 文件驱动）

### 1.3 AI 作为 OBB 规划师

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

## 二、工作流程

### 2.1 两阶段工作流

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
│  │   • 用户需求（可选）                                      │   │
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 分析户型结构（几室几厅几卫）                         │   │
│  │   2. 为每个 Room Zone 推断功能标签 (tags)                 │   │
│  │   3. 生成 Designable Zone                                │   │
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
│  │                                                          │   │
│  │ AI 任务：                                                 │   │
│  │   1. 根据 tags 选择合适的模块                            │   │
│  │   2. 确定摆放位置（遵循设计原则）                        │   │
│  │   3. 确定朝向                                            │   │
│  │                                                          │   │
│  │ 输出：                                                    │   │
│  │   • schemes/{s}/modules.json (布置结果)                  │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Phase A 详细说明

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

#### Step 2: 素材库过滤（MVP 简化）

- MVP 阶段：直接使用 `modules/` 下所有 SVG
- 完整实现：根据 tags 过滤合适的模块

#### Step 3: 设计区划分（MVP 简化）

- MVP 阶段：Room Zone boundary → Designable Zone boundary（1:1 复制）
- 完整实现：可能进一步细分（如客厅分为沙发区、电视区）

### 2.3 Phase B 详细说明

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

---

## 三、数据格式

### 3.1 输入数据

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

### 3.2 输出数据

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

支持两种格式：

**格式 A: bounds 四顶点**
```json
{
  "id": "m_1",
  "moduleId": "bed_king",
  "moduleName": "King Bed",
  "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
  "facing": "east",
  "zoneId": "dz_1",
  "svgPath": "modules/床_双人_2000x1800.svg"
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

### 3.3 Facing 类型

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

## 四、模块素材库

### 4.1 文件组织

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

### 4.2 命名规范

```
{名称}_{规格}_{宽}x{高}.svg

名称：家具中文名
规格：可选的规格描述（如三人、双门）
宽x高：模块尺寸（mm），宽度 x 高度（深度）
```

### 4.3 解析逻辑

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

## 五、布置约束

### 5.1 核心约束规则

```
对于每个要放置的模块：
1. bounds 必须完全在 Designable Zone boundary 内
2. bounds 不能与任何已放置 modules 重叠
3. 不能阻挡门的开启范围
```

### 5.2 门窗避让

利用 `openings.json` 中的数据：

- **门 (type=0)**：需要预留开启空间
  - `handDirections` 表示门扇开启方向
  - 开启空间约为门宽度的 90° 扇形区域

- **窗 (type=1)**：通常不阻挡
  - 床头不靠窗
  - 淋浴房可以靠窗

### 5.3 靠墙判断

根据 boundary 边缘判断哪些边是墙：

```python
def find_wall_edges(boundary, openings):
    """找出可靠墙的边缘（排除门窗位置）"""
    wall_edges = []
    for i in range(len(boundary)):
        edge = (boundary[i], boundary[(i+1) % len(boundary)])
        if not has_opening(edge, openings):
            wall_edges.append(edge)
    return wall_edges
```

---

## 六、触发方式

Agent 支持三种触发方式：

| 触发方式 | 触发源 | 数据流 |
|----------|--------|--------|
| AI 对话 | 用户输入 | 用户 → Agent Chat → PlacementAgent.run() |
| Web 按钮 | 前端 UI | Web → Server EventBus → SSE → Agent |
| 自动修正 | Server 检测 | Server 验证 → EventBus → SSE → Agent |

---

## 七、注意事项

### 7.1 坐标系统

- **坐标系**：CAD 标准（原点左下角，Y 轴向上）
- **单位**：毫米 (mm)
- **精度**：整数即可，无需小数

### 7.2 ID 命名约定

| 类型 | 前缀 | 示例 |
|------|------|------|
| Room Zone | rz_ | rz_1, rz_2 |
| Designable Zone | dz_ | dz_1, dz_2 |
| Module | m_ | m_1, m_2 |
| Door/Window | d_ | d_1, d_2 |
| Room | r_ | r_1, r_2 |

### 7.3 常见陷阱

1. **bounds 顺序**：四顶点按逆时针或顺时针连续排列
2. **facing 方向**：指模块的「正面」朝向，不是背面
3. **重叠检测**：由 Server 执行，Agent 无需自行验证
4. **边界越界**：由 Server 执行，Agent 无需自行验证

### 7.4 文件驱动原则

> 文件是唯一真理源 (Single Source of Truth)

- Agent 直接读写 JSON 文件
- 不需要经过 MCP Server
- 修改立即生效

---

## 八、技术栈

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

## 九、后续扩展

MVP 完成后的扩展方向：

1. **Phase A 完整实现**
   - Step2: 根据 tags + 风格过滤模块
   - Step3: 进一步划分设计区

2. **多策略支持**
   - 并行生成多个布置策略
   - 策略对比和评分

3. **Git 集成**
   - 策略作为 Git 分支
   - 版本管理和回滚

4. **知识库集成**
   - 设计规范检索
   - 案例参考
