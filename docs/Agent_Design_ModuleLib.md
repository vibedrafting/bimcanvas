# BIMCanvas 模块素材库规范

> **版本**：v1.0 | **更新日期**：2026-01-13
> **目的**：定义模块素材库的数据结构、标签体系、Agent 集成方式及 SVG 资源规范
> **关联文档**：[Agent_Design.md](./Agent_Design.md)

---

## 一、库结构概览

### 1.1 目录结构

模块素材库采用 JSON 元数据 + SVG 资源分离的架构：

```
{项目根目录}/modules/
├── module_library.json      # 模块元数据定义（37个模块）
├── SVG_Generation_Prompt.md # SVG 生成指南
└── assets/                  # SVG 俯视图资源
    ├── mod_bed_001.svg      # 双人床 (1800×2000mm)
    ├── mod_bed_002.svg      # 大双人床 (2230×2500mm)
    ├── mod_sofa_001.svg     # 单人沙发 (760×660mm)
    └── ...                  # 共 37 个 SVG 文件
```

### 1.2 module_library.json 数据格式

```json
{
  "version": "1.0",
  "modules": [
    {
      "id": "mod_bed_001",
      "name": "双人床",
      "tags": ["sleep"],
      "size": { "width": 1800, "depth": 2000 },
      "description": "标准双人床",
      "svgPath": "modules/assets/mod_bed_001.svg"
    }
  ]
}
```

**字段说明**：

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | string | 唯一标识，格式 `mod_{类型}_{编号}`，Agent 通过此 ID 引用模块 |
| `name` | string | 中文名称，用于显示和日志 |
| `tags` | string[] | 功能标签数组，用于按功能过滤 |
| `size.width` | number | 宽度（mm），俯视图水平方向 |
| `size.depth` | number | 深度（mm），俯视图垂直方向 |
| `description` | string | 模块描述 |
| `svgPath` | string | SVG 资源相对路径 |

---

## 二、标签体系

### 2.1 标签分类

模块使用功能标签标记用途，Agent 根据房间类型和策略过滤合适的模块：

| 标签 | 说明 | 适用房间 | 示例模块 |
|------|------|----------|----------|
| `sleep` | 睡眠相关 | 卧室 | 床、榻榻米 |
| `seating` | 座位相关 | 客厅、卧室 | 沙发、单椅 |
| `dining` | 餐饮相关 | 餐厅、厨房 | 餐桌、餐椅、吧椅 |
| `storage` | 存储相关 | 全屋 | 衣柜、储物柜 |
| `work` | 工作相关 | 书房、卧室 | 书桌、电脑桌 |
| `media` | 媒体相关 | 客厅 | 电视柜 |
| `appliance` | 家电类 | 厨房、阳台 | 冰箱、洗衣机、空调 |
| `lighting` | 照明类 | 全屋 | 落地灯、台灯 |
| `dressing` | 梳妆相关 | 卧室 | 梳妆台 |

### 2.2 标签权重机制

Agent 根据房间类型读取 `zone.tags`（Server 预计算），然后过滤模块库：

```python
# 示例：获取卧室适用的模块
bedroom_tags = zone.tags  # ["sleep", "storage", "dressing", "lighting"]
matching_modules = [m for m in library.modules if any(t in m.tags for t in bedroom_tags)]
```

### 2.3 房间类型 → 功能标签对照表

> **注意**：此映射由 Server 持有并预计算到 `room_zones.json`，Agent 无需了解映射逻辑，只需读取 `zone.tags`。

| 房间类型 (reason) | 功能标签 (tags) |
|-------------------|-----------------|
| `room:LivingRoom` | `seating`, `media`, `storage`, `lighting` |
| `room:MasterBedroom` | `sleep`, `storage`, `dressing`, `lighting` |
| `room:Bedroom` | `sleep`, `storage`, `work`, `lighting` |
| `room:DiningRoom` | `dining`, `storage`, `lighting` |
| `room:Kitchen` | `appliance`, `storage` |
| `room:Bathroom` | `appliance` |
| `room:Study` | `work`, `storage`, `seating`, `lighting` |
| `room:Balcony` | `appliance`, `seating` |

---

## 三、模块统计

### 3.1 按类别统计

当前库包含 **37 个模块**，分为 7 大类：

| 类别 | 数量 | ID 范围 | 宽度范围 | 深度范围 |
|------|------|---------|----------|----------|
| 床类 | 4 | mod_bed_001~004 | 1300-2230mm | 1810-2500mm |
| 沙发 | 7 | mod_sofa_001~007 | 760-4000mm | 660-2100mm |
| 椅子 | 6 | mod_chair_001~006 | 380-1600mm | 380-1380mm |
| 桌台 | 7 | mod_table_001~007 | 900-1600mm | 420-1350mm |
| 柜架 | 7 | mod_cabinet_001~007 | 420-2000mm | 350-650mm |
| 家电 | 4 | mod_appliance_001~004 | 530-900mm | 350-800mm |
| 灯具 | 2 | mod_lighting_001~002 | 300-500mm | 300-500mm |

---

## 四、Agent 集成

### 4.1 工具函数接口

#### 查询模块库

```python
def list_modules(tags: list[str] = None) -> list[dict]:
    """
    查询模块库，可按标签过滤

    Args:
        tags: 功能标签列表，如 ["sleep", "storage"]

    Returns:
        匹配的模块列表
    """
    library = load_json("modules/module_library.json")
    modules = library["modules"]

    if tags:
        # 返回包含任一指定标签的模块
        modules = [m for m in modules if any(t in m["tags"] for t in tags)]

    return modules

# 示例：获取卧室适用的模块
bedroom_modules = list_modules(tags=["sleep", "storage", "dressing"])
```

#### 获取模块详情

```python
def get_module_info(module_id: str) -> dict:
    """
    根据 moduleId 获取完整模块信息

    Args:
        module_id: 模块 ID，如 "mod_bed_001"

    Returns:
        模块完整信息（尺寸、标签、SVG路径等）
    """
    library = load_json("modules/module_library.json")
    for module in library["modules"]:
        if module["id"] == module_id:
            return module
    return None
```

### 4.2 在 modules.json 中引用

布置结果中通过 `moduleId` 引用库中的模块：

```json
{
  "id": "m_1",
  "moduleId": "mod_bed_001",
  "moduleName": "主卧双人床",
  "bounds": [[600, 400], [2400, 400], [2400, 2400], [600, 2400]],
  "facing": "north",
  "zoneId": "dz_1",
  "placementReason": "床头靠北墙居中，与窗户保持距离"
}
```

> **注意**：`moduleId` 必须是 `module_library.json` 中存在的 `id` 值。

### 4.3 过滤与搜索示例

```python
# 场景 1：获取客厅适用的所有家具
living_room_tags = ["seating", "media", "storage", "lighting"]
living_room_modules = list_modules(tags=living_room_tags)

# 场景 2：根据策略进一步过滤
if strategy.approach == "StorageFirst":
    # 优先选择带储物功能的家具
    modules = [m for m in modules if "storage" in m["tags"]]
elif strategy.approach == "MinimalistFirst":
    # 只选择核心家具
    core_tags = ["sleep", "seating", "dining"]
    modules = [m for m in modules if any(t in m["tags"] for t in core_tags)]
```

---

## 五、SVG 资源规范

### 5.1 viewBox 标准

SVG 文件作为模块的可视化表示，遵循以下规范：

| 规范项 | 要求 |
|--------|------|
| ViewBox | `viewBox="0 0 {width} {depth}"`，单位 mm |
| 主轮廓描边 | 25px，黑色 (#000000) |
| 细节线描边 | 15-20px，黑色 |
| 线型 | 实线，禁止虚线 |
| 风格 | 极简线框，区分家具类型特征 |
| 默认朝向 | 向上（north），床头/沙发靠背在顶部 |

### 5.2 描边与线型

```svg
<!-- 主轮廓 -->
<rect x="0" y="0" width="1800" height="2000"
      fill="none" stroke="#000000" stroke-width="25"/>

<!-- 细节线 -->
<line x1="0" y1="400" x2="1800" y2="400"
      stroke="#000000" stroke-width="15"/>
```

### 5.3 SVG 示例（双人床）

```svg
<svg viewBox="0 0 1800 2000" xmlns="http://www.w3.org/2000/svg">
  <!-- 床框 -->
  <rect x="0" y="0" width="1800" height="2000"
        fill="none" stroke="#000" stroke-width="25"/>
  <!-- 床头（北侧，即 y=0 附近） -->
  <rect x="50" y="50" width="1700" height="100"
        fill="none" stroke="#000" stroke-width="15"/>
  <!-- 床垫区域 -->
  <rect x="50" y="200" width="1700" height="1700"
        fill="none" stroke="#000" stroke-width="15"/>
</svg>
```

---

## 六、Server-Agent 协作规范

### 6.1 核心设计原则

```
Server = 约束管理者 + 验证者（不做布置决策）
Agent = 智能决策者 + 规划者（不持有状态、不持有映射逻辑）
```

**关键职责边界**：
- **Server 职责**：房间类型→功能标签映射、约束预计算、验证
- **Agent 职责**：读取预计算数据、智能决策、布置规划

### 6.2 两种协作模式

| 维度 | MVP 版本 | 完整版 |
|------|----------|--------|
| **交互方式** | Agent 直接读写文件 | Agent 通过 MCP 工具调用 Server |
| **模块库访问** | 直接读取 `module_library.json` | Server 提供 `list_modules` 工具 |
| **功能标签** | Server 预计算写入 `room_zones.json` | Server 实时计算 |
| **验证时机** | 事后验证（Agent 提交后 Server 检查） | 实时验证（每次放置前检查） |
| **适用场景** | 快速验证、单机开发、Claude Code 集成 | 生产环境、多 Agent 并行 |

### 6.3 MVP 版本工作流

```
Server 预计算（含标签分配）→ Agent 读取数据 → Agent 独立决策 → Server 事后验证
```

**三阶段流程**：

1. **数据准备**（Server）：
   - 生成 `computed/room_zones.json`（含 `tags` 字段）
   - 生成 `computed/exclusions.json`

2. **独立决策**（Agent）：
   - 读取 `zone.tags` → 过滤模块 → 布置决策
   - 写入 `schemes/{s}/modules.json`
   - Git 提交

3. **事后验证**（Server）：
   - moduleId 有效性检查
   - 标签兼容性检查
   - 空间约束检查（碰撞、边界）

### 6.4 完整版工作流

```
Agent 请求数据 → Server 实时提供 → Agent 决策 → Server 实时验证 → Agent 调整
```

**实时交互**：

```python
# Agent 调用 MCP 工具获取模块库
modules = await mcp_call("canvas__list_modules", tags=zone.tags)

# Agent 调用 MCP 工具验证布置
result = await mcp_call("canvas__validate_placement", module_bounds=bounds)
if not result.valid:
    # 根据验证结果调整
    ...
```

---

## 附录 A: 模块 ID 命名规范

| 类型 | 前缀 | 示例 |
|------|------|------|
| 床类 | mod_bed_ | mod_bed_001, mod_bed_002 |
| 沙发 | mod_sofa_ | mod_sofa_001, mod_sofa_007 |
| 椅子 | mod_chair_ | mod_chair_001, mod_chair_006 |
| 桌台 | mod_table_ | mod_table_001, mod_table_007 |
| 柜架 | mod_cabinet_ | mod_cabinet_001 |
| 家电 | mod_appliance_ | mod_appliance_001 |
| 灯具 | mod_lighting_ | mod_lighting_001 |

---

## 附录 B: 相关文档

| 文档 | 路径 | 内容 |
|------|------|------|
| Agent 架构设计 | [Agent_Design.md](./Agent_Design.md) | SubAgent 架构、提示词设计 |
| 数据模型 | [Schema.md](./Schema.md) | JSON Schema 定义 |
| MCP 工具规范 | [Arch_MCP_Tools.md](./Arch_MCP_Tools.md) | MCP 工具接口 |
