# BIMCanvas Agent MVP 实施计划

> 日期：2025-12-29
> 目标：实现最小可行的 AI 布置 Agent

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

### Agent 专属职责（MVP 范围）

1. **语义理解**：理解房间名称/类型，推断功能标签 (tags)
2. **空间规划**：决定家具放在哪个位置、选择合适的模块
3. **朝向决策**：确定家具面向方向
4. **设计原则应用**：靠墙、居中、避门窗等规则

### Agent 绝不做的事

- ❌ Zone 生成（读取 Server 预计算的 `computed/zones.json`）
- ❌ 禁区计算（读取 `computed/exclusions.json` 或使用 openings 避让）
- ❌ InnerBoundary 计算（读取 Server 预计算结果）
- ❌ 约束验证（Server 负责，验证失败会通知 Agent 修正）
- ❌ 文件系统管理（通过 MCP 工具间接操作）

### MVP 阶段的数据依赖

| 数据 | 来源 | Agent 操作 |
|------|------|-----------|
| Room Zone | Server 预计算 → `computed/zones.json` | 读取 |
| 门窗数据 | Revit 导出 → `baseline/openings.json` | 读取 |
| 禁区 | Server 预计算 → `computed/exclusions.json` | 读取（如有） |
| 素材库 | 项目 → `modules/*.svg` | 读取 |
| Designable Zone | Agent 生成 → `schemes/{s}/zones.json` | 写入 |
| 布置结果 | Agent 生成 → `schemes/{s}/modules.json` | 写入 |

---

## 一、MVP 核心目标

**验证假设**：AI Agent 能够理解房间功能，并在设计区内完成符合设计常识的家具布置。

### 范围界定

| 功能 | MVP 包含 | 说明 |
|------|----------|------|
| Phase A Step1: 功能标签推断 | ✅ | AI 根据房间名称/类型推断 tags |
| Phase A Step2: 素材库过滤 | ❌ | 简化：使用全部 modules/*.svg |
| Phase A Step3: 设计区划分 | ❌ | 简化：Room Zone = Designable Zone |
| Phase B: 布置决策 | ✅ 完整实现 | 包含设计原则和布置逻辑 |
| 多策略并行 | ❌ | 后续扩展 |
| Git 版本管理 | ❌ | 后续扩展 |

---

## 二、数据流设计

```
┌─────────────────────────────────────────────────────────────────────┐
│                          MVP 数据流                                  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  【Phase A: 分区设计】                                               │
│                                                                     │
│  computed/zones.json ──────────────────────────────────────┐        │
│  (Room Zone, tags=[])                                      │        │
│                                                            ▼        │
│                                                   ┌───────────────┐ │
│                                                   │ AI 推断 tags  │ │
│                                                   │               │ │
│  baseline/rooms.json ─────────────────────────────│ • name        │ │
│  (房间名称)                                        │ • reason      │ │
│                                                   │ • 户型分析    │ │
│                                                   └───────┬───────┘ │
│                                                           │         │
│                                                           ▼         │
│                                         schemes/{s}/zones.json      │
│                                         (Designable Zone, 含 tags)  │
│                                                           │         │
│  ─────────────────────────────────────────────────────────┼─────    │
│                                                           │         │
│  【Phase B: 布置决策】                                     │         │
│                                                           ▼         │
│  modules/*.svg ─────────────────────────────────┐ ┌───────────────┐ │
│  (素材库)                                        │ │ AI 布置决策  │ │
│                                                  ├─│               │ │
│  baseline/openings.json ─────────────────────────┤ │ • 选择模块   │ │
│  (门窗位置)                                       │ │ • 计算位置   │ │
│                                                  │ │ • 确定朝向   │ │
│                                                  └─└───────┬───────┘ │
│                                                           │         │
│                                                           ▼         │
│                                         schemes/{s}/modules.json    │
│                                         (布置结果)                   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## 三、技术架构

### 3.1 技术栈

- **语言**：Python 3.10+
- **框架**：Anthropic Agent SDK
- **模型**：Claude Sonnet 4
- **依赖**：anthropic

### 3.2 项目结构

```
BIMCanvas.Agent/
├── pyproject.toml              # Python 项目配置
├── src/
│   ├── __init__.py
│   ├── main.py                 # 入口
│   ├── agent/
│   │   ├── __init__.py
│   │   ├── placement_agent.py  # 主 Agent
│   │   ├── zone_designer.py    # Phase A: 分区设计
│   │   └── layout_planner.py   # Phase B: 布置决策
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── file_tools.py       # 读取 Server 数据 + 写入方案
│   │   └── svg_parser.py       # SVG 文件名解析
│   └── config/
│       ├── __init__.py
│       └── settings.py         # 配置
```

### 3.3 工具定义

Agent 需要的工具（读取 Server 预计算数据，写入方案文件）：

| 工具 | 功能 | 说明 |
|------|------|------|
| `read_room_zones` | 读取 Room Zone | 读取 `computed/zones.json`（Server 预计算） |
| `read_openings` | 读取门窗数据 | 读取 `baseline/openings.json` |
| `read_exclusions` | 读取禁区数据 | 读取 `computed/exclusions.json`（Server 预计算） |
| `list_modules` | 列出素材库 | 读取 `modules/*.svg` 文件名 |
| `write_design_zones` | 写入 Designable Zone | 写入 `schemes/{s}/zones.json`（Agent 决策结果） |
| `write_modules` | 写入模块布置 | 写入 `schemes/{s}/modules.json`（Agent 决策结果） |

**关键区分**：
- **读取工具**：获取 Server 预计算的几何数据，Agent 不重复计算
- **写入工具**：保存 Agent 的决策结果（tags、布置位置），不涉及几何计算

---

## 四、实施步骤

### Phase 1: 基础设施

**目标**：搭建项目框架，准备测试数据

**任务清单**：

- [ ] 创建 `BIMCanvas.Agent/` 文件夹
- [ ] 创建 `pyproject.toml` 配置文件
- [ ] 创建项目目录结构
- [ ] 安装依赖 (`pip install anthropic`)
- [ ] 在 demo_1 项目创建 `modules/` 文件夹
- [ ] 准备基础 SVG 模块文件（至少 10 个）
- [ ] 实现 `file_tools.py`：读写 JSON 文件
- [ ] 实现 `svg_parser.py`：解析 SVG 文件名

**关键文件**：

```python
# pyproject.toml
[project]
name = "bimcanvas-agent"
version = "0.1.0"
requires-python = ">=3.10"
dependencies = [
    "anthropic>=0.40.0",
]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"
```

```python
# src/tools/svg_parser.py
def parse_svg_filename(filename: str) -> dict:
    """解析 SVG 文件名获取模块信息"""
    name = filename.replace(".svg", "")
    parts = name.rsplit("_", 1)

    size_str = parts[-1]
    width, height = map(int, size_str.split("x"))
    name_part = parts[0]

    return {
        "templateId": name,
        "name": name_part,
        "size": [width, height],
        "svgPath": f"modules/{filename}"
    }
```

### Phase 2: Phase A 实现

**目标**：实现分区设计功能（功能标签推断）

**任务清单**：

- [ ] 实现 `zone_designer.py` 基本框架
- [ ] 实现 Room Zone 读取功能
- [ ] 实现 tags 推断逻辑（调用 Claude）
- [ ] 实现 Designable Zone 生成
- [ ] 实现 zones.json 写入功能
- [ ] 使用 demo_1 项目测试
- [ ] 验证 tags 推断合理性

**tags 推断规则**：

| reason | name 关键词 | 推荐 tags |
|--------|-------------|-----------|
| room:LivingRoom | 客厅 | sitting, entertainment, tv_media |
| room:MasterBedroom | 主卧 | sleeping, rest, storage, dressing |
| room:Bedroom | 次卧/卧室 | sleeping, rest |
| room:Bathroom | 卫生间 | bathing, toilet |
| room:Kitchen | 厨房 | cooking, storage |
| room:DiningRoom | 餐厅 | dining |

**输出格式**：

```json
{
  "id": "dz_1",
  "type": "Designable",
  "parentRoomZoneId": "rz_3",
  "name": "主卧",
  "tags": ["sleeping", "rest", "storage", "dressing"],
  "boundary": [[...], ...],
  "openings": ["d_3", "d_7"]
}
```

### Phase 3: Phase B 实现

**目标**：实现布置决策功能

**任务清单**：

- [ ] 实现 `layout_planner.py` 基本框架
- [ ] 实现 Designable Zone 读取
- [ ] 实现素材库加载（list_modules）
- [ ] 实现门窗数据读取
- [ ] 实现设计原则：
  - [ ] 靠墙规则
  - [ ] 居中规则
  - [ ] 顶角规则
  - [ ] 朝向规则
  - [ ] 对位规则
  - [ ] 避门窗规则
- [ ] 实现布置优先级逻辑
- [ ] 实现 modules.json 写入
- [ ] 逐房间测试布置
- [ ] 验证布置合理性

**设计原则**：

| 类型 | 规则 | 示例 |
|------|------|------|
| **靠墙规则** | 大型家具尽量靠墙 | 衣柜、床、沙发 |
| **居中规则** | 某些家具居中于墙面 | 电视柜居中于电视墙 |
| **顶角规则** | 某些家具顶墙角 | 衣柜、书柜 |
| **朝向规则** | 模块背对墙 | 沙发背墙，面向中心 |
| **对位规则** | 家具对位关系 | 沙发正对电视 |
| **避窗规则** | 除淋浴外避免靠窗 | 床头不靠窗 |
| **避门规则** | 不阻挡门开启范围 | 利用 openings 数据 |

**布置优先级**：

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

### Phase 4: 集成测试

**目标**：端到端验证

**任务清单**：

- [ ] 完整流程测试：Phase A → Phase B
- [ ] 检查输出文件格式正确性
- [ ] 验证 modules.json 可被 Web 正确渲染
- [ ] 验证 SVG 加载和定位正确
- [ ] 修复发现的问题

---

## 五、模块素材库设计

### 5.1 文件夹结构

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

### 5.2 命名规范

```
{名称}_{规格}_{宽}x{高}.svg

名称：家具中文名
规格：可选的规格描述（如三人、双门）
宽x高：模块尺寸（mm），宽度 x 高度（深度）
```

---

## 六、关键文件清单

### 需要创建的文件

| 路径 | 说明 | 优先级 |
|------|------|--------|
| `BIMCanvas.Agent/pyproject.toml` | Python 项目配置 | P1 |
| `BIMCanvas.Agent/src/main.py` | Agent 入口 | P1 |
| `BIMCanvas.Agent/src/tools/file_tools.py` | 文件读写工具 | P1 |
| `BIMCanvas.Agent/src/tools/svg_parser.py` | SVG 解析 | P1 |
| `BIMCanvas.Agent/src/agent/placement_agent.py` | 主 Agent | P2 |
| `BIMCanvas.Agent/src/agent/zone_designer.py` | Phase A: 语义理解 | P2 |
| `BIMCanvas.Agent/src/agent/layout_planner.py` | Phase B: 空间规划 | P3 |
| `BIMCanvas.Agent/src/config/settings.py` | 配置 | P1 |
| `demo_1/modules/*.svg` | 测试用模块 SVG | P1 |

> **注意**：不创建 geometry_tools.py，所有几何计算由 Server 完成。

### Agent 读取的文件（Server/Revit 预生成）

| 路径 | 内容 | 生成者 |
|------|------|--------|
| `computed/zones.json` | Room Zone（含 boundary） | Server |
| `baseline/openings.json` | 门窗数据 | Revit 导出 |
| `computed/exclusions.json` | 禁区数据 | Server |
| `modules/*.svg` | 模块素材 | 手动准备 |

### Agent 写入的文件（决策结果）

| 路径 | 内容 | 职责 |
|------|------|------|
| `schemes/{s}/zones.json` | Designable Zone（含 tags） | Agent 语义理解 |
| `schemes/{s}/modules.json` | 布置结果 | Agent 空间规划 |

### 需要修改的文件

| 路径 | 修改内容 |
|------|----------|
| `demo_1/schemes/default/zones.json` | Phase A 输出 |
| `demo_1/schemes/default/modules.json` | Phase B 输出 |

---

## 七、测试数据

### 测试项目路径

```
C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1
```

### 项目结构

```
demo_1/
├── project.json              # 项目元数据
├── baseline/
│   ├── rooms.json            # 房间数据
│   └── openings.json         # 门窗数据
├── computed/
│   └── zones.json            # Room Zone（输入）
├── schemes/
│   └── default/
│       ├── zones.json        # Designable Zone（输出）
│       └── modules.json      # 布置结果（输出）
└── modules/                  # SVG 素材库（待创建）
```

### 现有 Room Zone 数据

| id | name | reason | 说明 |
|----|------|--------|------|
| rz_1 | 次卧一 | room:Bedroom | 卧室 |
| rz_2 | 次卧二 | room:Bedroom | 卧室 |
| rz_3 | 主卧 | room:MasterBedroom | 主卧室 |
| rz_4 | 主卫 | room:Bathroom | 卫生间 |
| rz_5 | 公卫 | room:Bathroom | 卫生间 |
| rz_6 | 公共空间 | room:LivingRoom | 客餐厅 |

---

## 八、验收标准

MVP 完成时，应能执行以下流程：

```bash
# 1. 运行 Agent
cd BIMCanvas.Agent
python -m src.main --project "C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1" \
                   --scheme "default" \
                   --prompt "帮我设计这个户型"

# 2. 预期输出
[Phase A] 读取 6 个 Room Zone
[Phase A] 推断功能标签...
  - 次卧一 → tags: [sleeping, rest]
  - 次卧二 → tags: [sleeping, rest]
  - 主卧 → tags: [sleeping, rest, storage, dressing]
  - 主卫 → tags: [bathing, toilet]
  - 公卫 → tags: [bathing, toilet]
  - 公共空间 → tags: [sitting, entertainment, dining]
[Phase A] 写入 schemes/default/zones.json

[Phase B] 读取 6 个 Designable Zone
[Phase B] 加载 15 个模块素材
[Phase B] 布置主卧...
  - 放置：床_双人 @ (10100, 2750), facing=east
  - 放置：衣柜_三门 @ (12650, 5450), facing=south
  - 放置：床头柜 @ (9350, 4000), facing=east
[Phase B] 布置公共空间...
  - 放置：电视柜 @ (8550, 3325), facing=west
  - 放置：沙发_三人 @ (5400, 3700), facing=east
  ...
[Phase B] 写入 schemes/default/modules.json

完成！共布置 19 个模块
```

### 验收检查项

- [ ] Agent 能正确读取 computed/zones.json
- [ ] Agent 能正确推断各房间的功能标签
- [ ] Agent 能正确生成 schemes/default/zones.json
- [ ] Agent 能正确读取 modules/ 下的 SVG 文件
- [ ] Agent 能正确读取 baseline/openings.json
- [ ] Agent 能为每个房间生成合理的家具布置
- [ ] 布置结果遵循设计原则
- [ ] 家具不阻挡门的开启
- [ ] 输出的 modules.json 格式正确
- [ ] Web 端能正确渲染布置结果

---

## 九、MVP 前置条件

> **重要**：Agent MVP 依赖 Server 预计算的数据，需确保以下条件满足：

### 必需的预计算数据

| 数据 | 路径 | 生成方式 | 当前状态 |
|------|------|----------|----------|
| Room Zone | `computed/zones.json` | Server ZoneCalculator | ✅ demo_1 已有 |
| 门窗数据 | `baseline/openings.json` | Revit 导出 | ✅ demo_1 已有 |
| 禁区（可选） | `computed/exclusions.json` | Server ComputedDataService | ⬜ 待生成 |

### MVP 阶段的简化处理

由于 Server 尚未完全开发，MVP 阶段采用以下简化：

1. **禁区计算**：如果 `exclusions.json` 不存在，Agent 使用 `openings.json` 中的门窗位置进行简单避让
2. **InnerBoundary**：如果 Zone 没有 `innerBoundary` 字段，Agent 使用 `rawBoundary` 作为布置边界
3. **验证**：MVP 阶段不做实时验证，依赖人工检查布置结果

### Server 完善后的增强

当 Server 完善后，Agent 可以获得更精确的数据：
- `innerBoundary`：考虑完成面厚度后的可用空间
- `exclusions`：精确的门扇开启禁区
- 实时验证：Server 验证 → 通知 Agent 修正

---

## 十、后续扩展（MVP 后）

MVP 完成后的扩展方向：

1. **Phase A 完整实现**
   - Step2: 根据 tags + 风格过滤模块
   - Step3: 进一步划分设计区（如客厅分为沙发区、电视区）

2. **多策略支持**
   - 并行生成多个布置策略
   - 策略对比和评分

3. **Git 集成**
   - 策略作为 Git 分支
   - 版本管理和回滚

4. **知识库集成**
   - 设计规范检索
   - 案例参考

5. **SSE 事件触发**
   - Web 按钮触发布置
   - Server 检测自动修正

---

## 十一、风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| Claude API 限流 | 布置速度慢 | 批量处理房间，减少 API 调用 |
| SVG 文件缺失 | 无法布置 | 提前准备完整的模块库 |
| 布置结果不合理 | 用户体验差 | 完善设计原则，增加约束 |
| 边界计算错误 | 家具越界 | Server 验证兜底 |
