# 计划：补充 Server-Agent 模块库协作工作流（MVP + 完整版）

## 背景

在 `Agent_Design_Spec.md` 中补充新章节"Server-Agent 模块库协作规范"，包含：
- **MVP 版本**：Agent 直接读写文件，Server 事后验证，无需 MCP 工具
- **完整版**：Agent 通过 MCP 工具与 Server 实时交互

## 修改文件

`docs/Agent_Design_Spec.md` - 新增章节（建议作为 §8.7 或独立新章节）

---

## 新增内容：Server-Agent 模块库协作规范

### 核心设计原则

```
Server = 约束管理者 + 验证者（不做布置决策）
Agent = 智能决策者 + 规划者（不持有状态、不持有映射逻辑）
```

**关键职责边界**：
- **Server 职责**：房间类型→功能标签映射、约束预计算、验证
- **Agent 职责**：读取预计算数据、智能决策、布置规划

---

## 版本对比

| 维度 | MVP 版本 | 完整版 |
|------|----------|--------|
| **交互方式** | Agent 直接读写文件 | Agent 通过 MCP 工具调用 Server |
| **模块库访问** | 直接读取 `module_library.json` | Server 提供 `list_modules` 工具 |
| **功能标签** | Server 预计算写入 `room_zones.json` | Server 实时计算 |
| **约束数据** | 读取 `computed/*.json` 静态文件 | Server 实时计算并返回 |
| **验证时机** | 事后验证（Agent 提交后 Server 检查） | 实时验证（每次放置前检查） |
| **失败处理** | Server 通知 Agent 整体重做 | Server 返回冲突详情，Agent 局部调整 |
| **适用场景** | 快速验证、单机开发 | 生产环境、多 Agent 并行 |

---

## MVP 版本：文件驱动工作流

### 设计理念

> Agent 是"独立设计师"，Server 是"事后审核员"
> **关键**：Server 在数据准备阶段完成所有映射和预计算，Agent 只负责读取和决策

```
Server 预计算（含标签分配）→ Agent 读取数据 → Agent 独立决策 → Server 事后验证
```

### 三阶段工作流

```
┌─────────────────────────────────────────────────────────────────┐
│              MVP 版本：文件驱动工作流（无 MCP）                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【阶段1】数据准备（Server 预计算，含功能标签分配）               │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 在项目初始化时生成：                                  │
│  │                                                             │
│  │ • computed/room_zones.json  - 房间区域数据（含 tags 字段）  │
│  │   ┌─────────────────────────────────────────────────────┐   │
│  │   │ Server 根据"房间类型→功能标签对照表"                 │   │
│  │   │ 自动为每个 Zone 分配功能标签 tags[]                  │   │
│  │   │                                                     │   │
│  │   │ 示例：                                               │   │
│  │   │ {                                                   │   │
│  │   │   "id": "z1",                                       │   │
│  │   │   "roomId": "r1",                                   │   │
│  │   │   "reason": "room:MasterBedroom",                   │   │
│  │   │   "tags": ["sleep", "storage", "dressing", "lighting"], │   │
│  │   │   "innerBoundary": [[...]]                          │   │
│  │   │ }                                                   │   │
│  │   └─────────────────────────────────────────────────────┘   │
│  │                                                             │
│  │ • computed/exclusions.json     - 禁区集合                   │
│  │ • computed/constraints.json    - 约束规则（可选）           │
│  │                                                             │
│  │ Agent 直接读取：                                             │
│  │ • modules/module_library.json  - 模块库元数据               │
│  │ • baseline/openings.json       - 门窗数据                   │
│  │ • strategy.json                - 策略配置（Server 注入）    │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段2】独立决策（Agent 自主完成）                             │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent 完全独立工作：                                         │
│  │                                                             │
│  │ 1. 读取设计区数据（已包含功能标签）                          │
│  │    zone = load_json("computed/room_zones.json")[zone_id]    │
│  │    tags = zone["tags"]  # Server 已预计算好                 │
│  │                                                             │
│  │ 2. 读取模块库                                                │
│  │    library = load_json("modules/module_library.json")       │
│  │                                                             │
│  │ 3. 根据功能标签过滤模块                                      │
│  │    modules = [m for m in library["modules"]                 │
│  │               if any(t in m["tags"] for t in tags)]         │
│  │                                                             │
│  │ 4. 读取禁区数据，自行规避                                    │
│  │    exclusions = load_json("computed/exclusions.json")       │
│  │                                                             │
│  │ 5. 执行布置决策（AI 推理）                                   │
│  │    • 确定锚点家具位置                                        │
│  │    • 围绕锚点布置主要家具                                    │
│  │    • 检查是否与禁区重叠（简单矩形检测）                      │
│  │                                                             │
│  │ 6. 直接写入结果文件                                          │
│  │    write_json("schemes/{s}/modules.json", modules)          │
│  │    write_json("schemes/{s}/zones.json", zones)              │
│  │                                                             │
│  │ 7. Git 提交                                                  │
│  │    git add . && git commit -m "feat(layout): ..."           │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段3】事后验证（Server 检查）                                │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 读取 Agent 提交的 modules.json，执行验证：            │
│  │                                                             │
│  │ 1. moduleId 有效性检查                                       │
│  │    - 每个 moduleId 必须存在于 module_library.json            │
│  │                                                             │
│  │ 2. 标签兼容性检查（新增）                                    │
│  │    - 模块的 tags 必须与 zone.tags 有交集                    │
│  │                                                             │
│  │ 3. 空间约束检查                                              │
│  │    - bounds 完全在 zone.innerBoundary 内                    │
│  │    - bounds 不与 exclusions 重叠                            │
│  │    - bounds 不与其他模块重叠                                 │
│  │                                                             │
│  │ 4. 验证结果处理                                              │
│  │    ✓ 通过：通知前端展示                                     │
│  │    ✗ 失败：SSE 通知 Agent 重做                              │
│  └─────────────────────────────────────────────────────────────┘
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 房间类型 → 功能标签对照表（Server 持有）

**重要**：此对照表由 Server 持有，Agent 不需要了解此映射逻辑。
Server 在生成 `computed/room_zones.json` 时根据此表自动分配 tags。

| 房间类型 (reason) | 功能标签 (tags) | 说明 |
|-------------------|-----------------|------|
| `room:LivingRoom` | `seating`, `media`, `storage`, `lighting` | 客厅：沙发、电视柜、茶几、落地灯 |
| `room:MasterBedroom` | `sleep`, `storage`, `dressing`, `lighting` | 主卧：床、衣柜、梳妆台、床头柜 |
| `room:Bedroom` | `sleep`, `storage`, `work`, `lighting` | 次卧：床、衣柜、书桌 |
| `room:DiningRoom` | `dining`, `storage`, `lighting` | 餐厅：餐桌、餐椅、餐边柜 |
| `room:Kitchen` | `appliance`, `storage` | 厨房：冰箱、储物柜 |
| `room:Bathroom` | `appliance` | 卫生间：洗衣机 |
| `room:Study` | `work`, `storage`, `seating`, `lighting` | 书房：书桌、书柜、椅子 |
| `room:Balcony` | `appliance`, `seating` | 阳台：洗衣机、休闲椅 |

```python
# Server 端代码：生成 room_zones.json 时使用
# Agent 不需要这段代码

ROOM_TYPE_TAGS = {
    "room:LivingRoom":     ["seating", "media", "storage", "lighting"],
    "room:MasterBedroom":  ["sleep", "storage", "dressing", "lighting"],
    "room:Bedroom":        ["sleep", "storage", "work", "lighting"],
    "room:DiningRoom":     ["dining", "storage", "lighting"],
    "room:Kitchen":        ["appliance", "storage"],
    "room:Bathroom":       ["appliance"],
    "room:Study":          ["work", "storage", "seating", "lighting"],
    "room:Balcony":        ["appliance", "seating"],
}

def generate_room_zones(zones: list[dict]) -> list[dict]:
    """Server 生成 room_zones.json 时，自动分配功能标签"""
    for zone in zones:
        room_type = zone.get("reason", "")
        zone["tags"] = ROOM_TYPE_TAGS.get(room_type, [])
    return zones
```

### computed/room_zones.json 数据结构

```json
{
  "version": "1.0",
  "zones": [
    {
      "id": "z1",
      "roomId": "r1",
      "reason": "room:MasterBedroom",
      "tags": ["sleep", "storage", "dressing", "lighting"],
      "innerBoundary": [[0, 0], [4000, 0], [4000, 3500], [0, 3500]],
      "area_mm2": 14000000
    },
    {
      "id": "z2",
      "roomId": "r2",
      "reason": "room:LivingRoom",
      "tags": ["seating", "media", "storage", "lighting"],
      "innerBoundary": [[0, 0], [6000, 0], [6000, 4000], [0, 4000]],
      "area_mm2": 24000000
    }
  ]
}
```

### MVP 版本的 Agent 工具（Python 本地函数）

```python
# MVP 版本：Agent 内置的本地工具（非 MCP）
# 注意：Agent 不需要 ROOM_TYPE_TAGS 映射表，直接读取 zone.tags

def list_modules_by_zone(zone: dict) -> list[dict]:
    """根据 Zone 的功能标签过滤模块"""
    library = load_json("modules/module_library.json")
    modules = library["modules"]

    # 直接读取 Server 预计算好的 tags
    tags = zone.get("tags", [])

    # 过滤包含任一标签的模块
    return [m for m in modules if any(t in m["tags"] for t in tags)]

def list_modules(tags: list[str] = None) -> list[dict]:
    """直接按标签过滤模块"""
    library = load_json("modules/module_library.json")
    modules = library["modules"]
    if tags:
        modules = [m for m in modules if any(t in m["tags"] for t in tags)]
    return modules

def get_zone(zone_id: str) -> dict:
    """读取设计区数据（含预计算的 tags）"""
    zones = load_json("computed/room_zones.json")["zones"]
    return next((z for z in zones if z["id"] == zone_id), None)

def get_exclusions(zone_id: str) -> list[dict]:
    """读取禁区数据"""
    exclusions = load_json("computed/exclusions.json")
    return [e for e in exclusions if e.get("zoneId") == zone_id]

def check_overlap_simple(bounds1, bounds2) -> bool:
    """简单矩形重叠检测（Agent 自行实现）"""
    # AABB 碰撞检测
    aabb1 = compute_aabb(bounds1)
    aabb2 = compute_aabb(bounds2)
    return not (aabb1.max_x < aabb2.min_x or aabb1.min_x > aabb2.max_x or
                aabb1.max_y < aabb2.min_y or aabb1.min_y > aabb2.max_y)

def write_modules(scheme_id: str, modules: list[dict]):
    """直接写入布置结果"""
    write_json(f"schemes/{scheme_id}/modules.json", modules)
```

### MVP 版本的约束

| 约束 | 说明 |
|------|------|
| **无实时反馈** | Agent 不知道某个位置是否合法，只能提交后等待 Server 验证 |
| **简化碰撞检测** | Agent 只能做 AABB 检测，精确 Polygon 检测由 Server 完成 |
| **整体重做** | 验证失败时，Agent 需要重新生成整个方案 |
| **无并行感知** | 多 Agent 可能产生冲突（放在同一位置） |

---

## 完整版：MCP 工具驱动工作流

### 设计理念

> Agent 是"协作设计师"，Server 是"实时顾问"

```
Agent 通过 MCP 查询 → Server 实时响应 → Agent 决策 → Server 即时验证
```

### 五阶段工作流

```
┌─────────────────────────────────────────────────────────────────┐
│              完整版：MCP 工具驱动工作流                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  【阶段1】Server 预计算约束数据                                  │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Server 在项目初始化时生成（与 MVP 相同）：                    │
│  │ • computed/room_zones.json    （含 tags 字段）              │
│  │ • computed/exclusions.json                                  │
│  │ • computed/module_index.json    ← 新增：模块库索引缓存       │
│  │ • computed/compatibility.json   ← 新增：房间-模块兼容性矩阵  │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段2】Agent 查询可用模块（MCP 调用）                         │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent:                                                       │
│  │   result = mcp_call("list_compatible_modules", {             │
│  │     "zoneId": "z1",                                         │
│  │     "preferences": { "style": "modern" }                    │
│  │   })                                                         │
│  │                                                              │
│  │ Server 返回（实时计算，基于 zone.tags）：                     │
│  │   {                                                          │
│  │     "zone_tags": ["sleep", "storage", "dressing", "lighting"],│
│  │     "available": [                                           │
│  │       { "moduleId": "mod_bed_001", "score": 0.95 },         │
│  │       { "moduleId": "mod_bed_002", "score": 0.80 }          │
│  │     ],                                                       │
│  │     "constraints": {                                         │
│  │       "available_area_mm2": 28500000,                       │
│  │       "exclusions_count": 2,                                │
│  │       "max_module_size": [3000, 2500]                       │
│  │     }                                                        │
│  │   }                                                          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段3】Agent 做出布置决策（AI 推理）                          │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent 考虑：                                                 │
│  │ • 设计规则（床头靠墙、与背景墙对齐）                          │
│  │ • Server 返回的约束信息                                      │
│  │ • 用户偏好和策略参数                                         │
│  │                                                              │
│  │ 决策输出：                                                   │
│  │   moduleId = "mod_bed_001"                                  │
│  │   center = [3000, 3250]                                     │
│  │   facing = "north"                                          │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段4】Server 实时验证（MCP 调用）                            │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ Agent:                                                       │
│  │   result = mcp_call("place_module", {                        │
│  │     "moduleId": "mod_bed_001",                              │
│  │     "zoneId": "z1",                                         │
│  │     "center": [3000, 3250],                                 │
│  │     "facing": "north"                                       │
│  │   })                                                         │
│  │                                                              │
│  │ Server 执行验证（含标签兼容性检查）：                         │
│  │   1. 基础验证：moduleId 存在、zoneId 有效                    │
│  │   2. 标签验证：module.tags 与 zone.tags 有交集              │
│  │   3. 空间验证：bounds 在边界内、不与禁区重叠                  │
│  │   4. 碰撞检测：不与已放置模块重叠                             │
│  │                                                              │
│  │ 成功返回：                                                    │
│  │   { "success": true, "moduleId": "m1",                       │
│  │     "bounds": [[...]], "status": "placed" }                 │
│  │                                                              │
│  │ 失败返回：                                                    │
│  │   { "success": false, "conflicts": [                         │
│  │       { "type": "overlap", "with": "exclusion_door_1" }     │
│  │     ],                                                       │
│  │     "suggested_positions": [[3500, 3250], [2500, 3250]]     │
│  │   }                                                          │
│  │                                                              │
│  │ Agent 可根据 suggested_positions 调整后重试                  │
│  └─────────────────────────────────────────────────────────────┘
│                              ↓                                  │
│  【阶段5】Server 广播更新 + Agent 提交                           │
│  ┌─────────────────────────────────────────────────────────────┐
│  │ 每次 place_module 成功后：                                   │
│  │ • Server 通过 WebSocket 推送更新到 Web 前端                  │
│  │ • 用户实时看到布置变化                                       │
│  │                                                              │
│  │ Agent 完成所有布置后：                                        │
│  │ • git add . && git commit                                   │
│  │ • Server 无需再次验证（已实时验证通过）                       │
│  └─────────────────────────────────────────────────────────────┘
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 完整版的 MCP 工具定义

```python
# 完整版：Server 提供的 MCP 工具

@mcp_tool("list_compatible_modules")
def list_compatible_modules(zone_id: str, preferences: dict = None) -> dict:
    """
    查询指定区域兼容的模块列表

    Server 内部：根据 zone.tags 过滤模块库
    返回：按适合度排序的模块列表 + 区域约束信息
    """
    pass

@mcp_tool("place_module")
def place_module(module_id: str, zone_id: str, center: list, facing: str) -> dict:
    """
    放置模块并验证

    Server 内部：验证 module.tags 与 zone.tags 的兼容性
    返回：成功时返回精确 bounds，失败时返回冲突详情 + 建议位置
    """
    pass

@mcp_tool("check_placement_validity")
def check_placement_validity(module_id: str, zone_id: str, bounds: list) -> dict:
    """
    检查某个位置是否合法（不实际创建）

    用于 Agent 预判位置可行性
    """
    pass

@mcp_tool("get_zone_heatmap")
def get_zone_heatmap(module_id: str, zone_id: str) -> dict:
    """
    获取可放置热图

    返回栅格化的可放置区域，帮助 Agent 快速找到合法位置
    """
    pass
```

---

## 两版本的选择建议

| 场景 | 推荐版本 | 原因 |
|------|----------|------|
| 快速原型验证 | MVP | 无需实现 Server MCP 接口 |
| 单机本地开发 | MVP | 简单直接，易于调试 |
| Claude Code 集成 | MVP | Claude Code 直接操作文件 |
| 生产环境部署 | 完整版 | 实时验证，用户体验好 |
| 多 Agent 并行 | 完整版 | Server 协调避免冲突 |
| Web 实时预览 | 完整版 | WebSocket 推送更新 |

---

## 实施步骤

1. 在 `Agent_Design_Spec.md` 第八节后新增 §8.7 "Server-Agent 模块库协作规范"
2. 包含：核心设计原则、版本对比表、MVP 三阶段流程图、完整版五阶段流程图
3. 包含：房间类型→功能标签对照表（标注为 Server 持有）
4. 包含：room_zones.json 数据结构示例
5. 包含：MVP 工具代码示例、完整版 MCP 工具定义
6. 包含：选择建议表

## 验证方式

1. 检查文档结构完整性
2. 确认 MVP 版本：Agent 只读取 zone.tags，不做房间类型映射
3. 确认完整版：Server 在 MCP 工具内部处理标签兼容性
4. 确认职责边界清晰：Server 管理映射，Agent 专注决策
