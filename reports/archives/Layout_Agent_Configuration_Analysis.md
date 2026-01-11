# Layout-Agent 配置分析与改进计划

**分析日期**：2026-01-10
**分析对象**：`C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md`
**参考项目**：`C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1`

---

## 执行摘要

当前的 layout-agent 配置存在**7个关键缺陷**，导致其无法正确执行文件驱动架构下的家具布置任务。本文档详细分析了这些问题，并提出系统性改进方案。

**关键发现**：
- 🔴 严重缺陷 2 项（标签驱动逻辑、数据结构不一致）
- 🟡 中等缺陷 4 项（exclusions.json、Git提交、设计规范、编辑场景）
- 🟢 轻微缺陷 1 项（文件系统理解不全）

---

## 五个核心问题分析

### 问题1：工作流程是否正确？

**当前状态**：⚠️ 部分正确，但缺少关键步骤

#### ✅ 已知正确的部分
- 知道读取 `computed/room_zones.json` 和 `baseline/openings.json`
- 知道从 `modules/` 目录获取家具素材
- 知道写入 `schemes/modules.json`
- 知道基础布置规则（靠墙、观看距离、动线宽度）

#### ❌ 缺失的关键步骤

**1. 标签驱动的模块过滤逻辑**（最严重）

当前配置：**没有提到要根据 `zone.tags` 过滤 `module.tags`**

正确做法：
```python
# 读取zone的功能标签
zone = load_json("computed/room_zones.json")[0]  # rz_1: 次卧一
tags = zone["tags"]  # ["sleep", "wardrobeStorage"]

# 读取模块库
library = load_json("modules/module_library.json")

# 根据标签过滤模块
modules = [m for m in library["modules"]
           if any(t in m["tags"] for t in tags)]
# 结果：只选择tags包含"sleep"或"wardrobeStorage"的模块
```

实际项目数据示例：
- 次卧一（rz_1）：tags = `["sleep", "wardrobeStorage"]`
- 主卧（rz_3）：tags = `["sleep", "wardrobeStorage", "vanity"]`
- 客厅（rz_6）：tags = `["tvMedia", "rest", "display"]`

**2. 缺少 computed/exclusions.json 读取**

- Server_Agent_Collaboration_Plan.md 明确要求读取禁区数据
- 当前配置只提到 `room_zones.json` 中的 `exclusionAreas` 字段
- 实际项目中没有找到 `computed/exclusions.json` 文件（可能 Server 端未实现）

**3. Git 提交步骤缺失**

- MVP 版本要求：布置完成后执行 `git add . && git commit -m "feat(layout): ..."`
- 当前配置：**完全没有提到 Git 操作**

#### 规范工作流程（应该是）

```
1. 读取 computed/room_zones.json（包含 tags 字段）
2. 读取 modules/module_library.json（模块库元数据）
3. 【关键】根据 zone.tags 过滤 module.tags
4. 读取 computed/exclusions.json（禁区集合，如果存在）
5. 读取 baseline/openings.json（门窗数据，避开开启范围）
6. 执行布置决策（AI 推理：选择模块、确定位置、计算朝向）
7. 直接写入 schemes/modules.json
8. Git 提交（MVP 版本要求）
```

---

### 问题2：设计原则是否充分？

**当前状态**：⚠️ 部分了解，但不够系统

#### ✅ 已有的设计原则
- 基础规则：靠墙、观看距离（2.5-4m）、床头不靠窗
- 布置优先级：锚点家具 → 主要家具 → 辅助家具
- 经验法则：沙发正对电视、不阻挡门、保持动线畅通（800mm）

#### ❌ 缺失的设计原则

**1. 具体的设计尺寸规范**

缺少：
- 卧室：床两侧通道 ≥ 600mm，床尾通道 ≥ 900mm
- 客厅：沙发与茶几距离 400-500mm，茶几与电视柜距离 ≥ 600mm
- 餐厅：餐椅拉出空间 ≥ 750mm
- 衣柜：开门空间 ≥ 650mm（平开门）

**2. 家具组合规则**

缺少：
- 床（sleep）→ 必须配 床头柜（storage）× 2
- 沙发（seating）→ 建议配 茶几（storage）
- 梳妆台（dressing）→ 建议配 凳子（seating）

**3. 空间分析方法**

缺少：
- 如何识别电视墙（客厅最长墙/无门窗墙）
- 如何识别床头墙（卧室最长墙/避开窗户）
- 如何计算可用空间（rawBoundary - exclusions - openings.swingArc）

**4. 标签驱动的设计逻辑**（最关键）

缺少：
- 根据 zone.tags 确定设计目标
- 根据 module.tags 选择合适的家具类型
- 优先级：匹配度高的模块 > 匹配度低的模块

示例（主卧 rz_3）：
- zone.tags = ["sleep", "wardrobeStorage", "vanity"]
- 必选：mod_bed_* (tags: ["sleep"])
- 必选：mod_cabinet_006 (tags: ["storage"]) - 衣柜
- 可选：mod_table_006 (tags: ["dressing", "storage"]) - 梳妆台

#### 建议补充的设计规范

参考 Server_Agent_Collaboration_Plan.md §144-158 的房间类型→功能标签对照表：

| 房间类型 | zone.tags | 必选模块 | 可选模块 |
|---------|-----------|---------|---------|
| room:MasterBedroom | sleep, wardrobeStorage, vanity | 床、衣柜 | 梳妆台、床头柜、床尾凳 |
| room:Bedroom | sleep, wardrobeStorage | 床、衣柜 | 书桌、床头柜 |
| room:LivingRoom | tvMedia, rest, display | 沙发、电视柜 | 茶几、边几、落地灯 |

---

### 问题3：文件系统理解是否正确？

**当前状态**：⚠️ 部分知道，信息不完整

#### ✅ 已知的文件

| 文件 | 读写属性 | 理解状态 |
|------|---------|---------|
| computed/room_zones.json | 只读 | ✅ 正确 |
| baseline/openings.json | 只读 | ✅ 正确 |
| modules/ | 只读 | ✅ 正确 |
| schemes/modules.json | 可写 | ✅ 正确 |

#### ❌ 缺失的文件认知

**1. computed/** 目录下的其他文件
- `exclusions.json` - 禁区集合（文档要求，但实际项目未找到）

**2. baseline/** 目录下的其他文件
- `walls.json` - 墙体轮廓（只读，理解空间形状）
- `columns.json` - 柱子轮廓（只读，识别禁区）
- `rooms.json` - 物理房间（只读，理解户型）
- `location_lines.json` - 完成面定位线（只读，可能影响布置）

**3. schemes/** 目录下的其他文件
- `zones.json` - 设计区域（混合读写）
- `finishes.json` - 完成面分段（混合读写）
- `strategy.json` - 策略配置（只读，Server 注入）

**4. project.json** - 项目元数据
- 当前项目：activeSchemeId = "default"
- 方案路径：`./schemes`（不是 `./schemes/default`）

#### 正确的文件驱动架构理解

参考 FileDrivenArchitecture.md 和实际项目结构：

```
C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1\
├── project.json                # 项目元数据（只读）
├── baseline/                   # 【底层】建筑基础数据（只读，Revit 导出）
│   ├── walls.json              # 墙体轮廓
│   ├── columns.json            # 柱子轮廓
│   ├── openings.json           # 门窗 ✅ 当前已知
│   ├── rooms.json              # 物理房间
│   └── location_lines.json     # 完成面定位线
├── computed/                   # 【中层】计算派生数据（只读，Server 自动生成）
│   ├── room_zones.json         # 房间区域（含 tags）✅ 当前已知
│   └── exclusions.json         # 禁区（❌ 实际项目中缺失）
├── modules/                    # 家具素材库（只读）
│   ├── module_library.json     # 模块库元数据 ✅ 当前已知
│   └── assets/                 # SVG 资源文件
├── schemes/                    # 【顶层】方案设计数据（可写）
│   ├── modules.json            # 布置模块 ✅ 当前已知
│   ├── zones.json              # 设计区域
│   └── finishes.json           # 完成面分段
└── context/                    # 上下文数据（Server 生成）
```

---

### 问题4：数据结构是否正确？

**当前状态**：❌ 数据结构不一致

#### 当前配置中的 modules.json 结构

```json
{
  "modules": [
    {
      "id": "mod_1",
      "templateId": "sofa_3seat",
      "bounds": {
        "center": [x, y],
        "size": [width, height],
        "rotation": 0
      },
      "facing": "north",
      "zoneId": "rz_1"
    }
  ]
}
```

#### 实际项目中的 module_library.json 结构

```json
{
  "modules": [
    {
      "id": "mod_bed_001",
      "name": "双人床",
      "tags": ["sleep"],
      "size": { "width": 1800, "depth": 2000 },
      "description": "标准双人床",
      "svgPath": "E:/工作文档/开发类/MyCode/BIMCanvas/modules/assets/mod_bed_001.svg"
    }
  ]
}
```

#### 不一致的地方

**1. templateId 字段混淆**
- 配置中：`"templateId": "sofa_3seat"`（虚构的ID）
- 实际：应该使用 `"moduleId": "mod_bed_001"`（module_library.json 中的 id）

**2. size 字段格式不一致**
- 配置中：`"size": [width, height]`（数组）
- 实际库中：`"size": { "width": 1800, "depth": 2000 }`（对象，且使用 depth 而非 height）
- 建议：在 modules.json 中使用数组 `[width, depth]`，从 module_library 复制数值

**3. 缺少关键字段**
- 配置中没有提到 `items[]` 字段（Server_Agent_Collaboration_Plan.md 提到）

#### 正确的 modules.json 结构（推断）

基于 Server_Agent_Collaboration_Plan.md 和实际数据：

```json
{
  "modules": [
    {
      "id": "m_1",                        // 布置实例的唯一ID（不是moduleId）
      "moduleId": "mod_bed_001",          // 引用 module_library.json 中的 id
      "zoneId": "rz_3",                   // 所属房间分区
      "bounds": {
        "center": [12000, 3000],          // 中心点坐标（mm）
        "size": [1800, 2000],             // 尺寸 [width, depth]，从库复制
        "rotation": 0                     // 旋转角度（度）
      },
      "facing": "north",                  // 朝向（语义字符串或 Vec2D）
      "items": []                         // 子项（暂时为空，未来扩展）
    }
  ]
}
```

#### 字段说明修正

| 字段 | 类型 | 说明 | 当前配置 | 应该是 |
|------|------|------|---------|--------|
| id | string | 布置实例ID | `"mod_1"` | ✅ 正确（但建议改为 `"m_1"` 以区分） |
| moduleId | string | 模块库ID | ~~templateId~~ | `"mod_bed_001"` |
| bounds.size | array | 模块尺寸 | `[width, height]` | `[1800, 2000]`（从 module_library 复制） |
| zoneId | string | 所属房间 | `"rz_1"` | ✅ 正确 |
| facing | string/array | 朝向 | `"north"` | ✅ 正确（支持 8 方向或 Vec2D） |
| items | array | 子项 | 缺失 | `[]`（暂时为空） |

---

### 问题5：三大场景支持情况

#### 场景A：查看/了解当前项目状态

**需求**：查看原建筑状态（户型、房间）+ 各房间内家具布置信息

**当前支持度**：⚠️ 40%

| 能力 | 状态 | 说明 |
|------|------|------|
| 查看房间信息 | ✅ | 可读取 computed/room_zones.json |
| 查看门窗信息 | ✅ | 可读取 baseline/openings.json |
| 查看布置信息 | ⚠️ | 配置中没有明确说明"查看"场景 |
| 查看原建筑数据 | ❌ | 不知道 baseline/walls.json、rooms.json |
| 输出项目状态报告 | ❌ | 缺少交互指令 |

**改进建议**：
1. 添加"查看模式"指令
2. 输出格式化报告，包含房间信息、家具数量等

**期望交互示例**：
```
用户：查看当前项目状态

Agent 输出：
项目：demo_1（共 6 个房间）

房间 rz_1（次卧一）：
- 面积：9.8 m²
- 功能标签：sleep, wardrobeStorage
- 门窗：1 扇门（d_5）
- 已布置家具：0 件（未布置）

房间 rz_6（公共空间）：
- 面积：45.2 m²
- 功能标签：tvMedia, rest, display
- 门窗：1 扇门（d_1）+ 2 扇窗（wi_4, wi_5）
- 已布置家具：0 件（未布置）
```

---

#### 场景B：编辑/移动当前的布置信息

**需求**：移动/删除/旋转部分家具（增量修改）

**当前支持度**：❌ 0%

| 能力 | 状态 | 说明 |
|------|------|------|
| 读取现有布置 | ❌ | 配置中只提到"从头布置"场景 |
| 修改单个家具位置 | ❌ | 没有增量修改逻辑 |
| 删除家具 | ❌ | 没有删除操作说明 |
| 旋转家具 | ❌ | 没有旋转操作说明 |
| 保留其他房间布置 | ❌ | 不知道如何只修改部分房间 |

**改进建议**：

1. **增量修改流程**：
```
1. 读取现有 schemes/modules.json
2. 识别要修改的模块
3. 执行修改操作（验证不冲突）
4. 保留其他模块不变
5. 写入更新后的 modules.json
```

2. **示例工作流**：
```python
# 读取现有布置
current = load_json("schemes/modules.json")

# 找到要移动的家具
bed = next(m for m in current["modules"] if m["id"] == "m_1")

# 移动到新位置
bed["bounds"]["center"] = [13000, 3000]

# 验证不冲突（Server 负责，Agent 只需写入）
write_json("schemes/modules.json", current)
```

3. **期望交互示例**：
```
用户：移动主卧的床到房间中心

Agent 行为：
1. 读取 schemes/modules.json
2. 找到 zoneId="rz_3" 且 moduleId 包含 "bed" 的模块
3. 计算 rz_3 的中心坐标
4. 更新床的 bounds.center
5. 写入更新后的文件
6. Git 提交
```

---

#### 场景C：重头设计一个室内设计方案

**需求**：从无到有，全面掌握设计素材、设计规范、文件操作

**当前支持度**：⚠️ 50%

| 能力 | 状态 | 说明 |
|------|------|------|
| 了解设计区域 | ✅ | 可读取 room_zones.json |
| 了解可用模块 | ✅ | 可读取 module_library.json |
| 选择合适模块 | ❌ | **不知道标签驱动逻辑** |
| 了解禁区约束 | ⚠️ | 提到但文件缺失 |
| 了解门窗约束 | ✅ | 可读取 openings.json |
| 应用设计规范 | ⚠️ | 规范不够详细 |
| 生成布置结果 | ⚠️ | 数据结构不一致 |
| Git 提交 | ❌ | 完全缺失 |

**改进建议**：

**1. 标签驱动逻辑（最关键）**

```python
# 第一步：读取房间和模块库
zones = load_json("computed/room_zones.json")
library = load_json("modules/module_library.json")

# 第二步：为每个房间筛选模块
for zone in zones:
    zone_tags = zone["tags"]  # ["sleep", "wardrobeStorage"]

    # 过滤匹配的模块
    compatible_modules = [
        m for m in library["modules"]
        if any(tag in m["tags"] for tag in zone_tags)
    ]

    # 输出：只有 tags 包含 "sleep" 或 "wardrobeStorage" 的模块
    # 如：mod_bed_001, mod_bed_002, mod_cabinet_006（衣柜）
```

**2. 完整工作流**

```
1. 读取所有输入数据（room_zones, openings, module_library）
2. 【关键】根据 zone.tags 过滤每个房间的候选模块
3. 按房间逐个设计：
   a. 识别锚点墙（电视墙/床头墙）
   b. 放置锚点家具（床/电视柜）
   c. 放置主要家具（沙发/衣柜）
   d. 放置辅助家具（茶几/床头柜）
4. 验证不冲突（简单 AABB 检测）
5. 写入 schemes/modules.json
6. Git 提交
```

**3. 数据结构修正示例**

```json
{
  "modules": [
    {
      "id": "m_1",
      "moduleId": "mod_bed_001",  // ← 修正：使用 moduleId
      "zoneId": "rz_3",
      "bounds": {
        "center": [12000, 3000],
        "size": [1800, 2000],     // ← 修正：从 module_library 复制
        "rotation": 0
      },
      "facing": "north",
      "items": []
    }
  ]
}
```

---

## 核心问题总结

### 7个关键缺陷

| # | 问题 | 严重性 | 影响 | 现象 |
|---|------|--------|------|------|
| 1 | **缺少标签驱动逻辑** | 🔴 严重 | 无法正确选择家具模块 | Agent 不知道客厅要选择"tvMedia"标签的电视柜，而不是"sleep"标签的床 |
| 2 | **数据结构不一致** | 🔴 严重 | 生成的文件无法被 Server 识别 | Agent 写入的 templateId 字段 Server 无法解析 |
| 3 | **缺少 exclusions.json** | 🟡 中等 | 无法正确避让禁区 | Agent 可能把家具放在门口或其他禁区 |
| 4 | **Git 提交缺失** | 🟡 中等 | 不符合 MVP 版本规范 | 布置完成后没有版本记录 |
| 5 | **设计规范不详细** | 🟡 中等 | 布置质量不高 | 不知道床两侧需要600mm通道 |
| 6 | **缺少编辑场景** | 🟡 中等 | 无法增量修改 | 只能重头布置，不能只移动一件家具 |
| 7 | **文件系统理解不全** | 🟢 轻微 | 缺少上下文信息 | 不知道 baseline/walls.json 可以帮助理解户型 |

### 优先级排序

**P0（必须修复）**
- 添加标签驱动逻辑
- 修正数据结构（moduleId, size 格式）

**P1（应该修复）**
- 补充详细的设计规范（尺寸、组合规则）
- 添加 Git 提交步骤
- 添加编辑场景支持

**P2（可以延后）**
- 完善文件系统理解
- 添加查看/报告功能
- 实现 computed/exclusions.json（需要 Server 支持）

---

## 改进方案

### 方案 A：最小修正（P0 缺陷）

**修改范围**：仅修正 layout-agent.md 的关键逻辑

**修改点**：
1. 工作流程中添加"根据 zone.tags 过滤 module.tags"步骤
2. 修正 modules.json 数据结构示例（templateId → moduleId）
3. 添加字段映射说明

**工作量**：约 1-2 小时
**优点**：改动最小，快速修复严重问题
**缺点**：设计质量仍不高，缺少编辑功能

---

### 方案 B：系统性重构（P0 + P1）

**修改范围**：重写 layout-agent.md，补充详细规范

**修改点**：
1. 修正所有 P0 缺陷
2. 补充详细设计规范（尺寸、组合、空间分析）
3. 添加三大场景支持（查看、编辑、从头设计）
4. 添加 Git 提交步骤
5. 优化工作流程和交互规范

**工作量**：约 4-6 小时
**优点**：全面改进，支持实际使用场景
**缺点**：改动较大，需要重新测试

---

### 方案 C：分阶段演进（推荐）⭐

**阶段 1（本周）**：修复 P0 缺陷
- 标签驱动逻辑
- 数据结构修正
- 工作量：1-2 小时

**阶段 2（下周）**：补充 P1 功能
- 详细设计规范
- 编辑场景支持
- Git 提交
- 工作量：3-4 小时

**阶段 3（未来）**：完善生态
- 查看/报告功能
- Server 端 exclusions.json 实现
- 高级布置算法（热图、冲突建议）
- 工作量：待定

**优点**：
- 分散风险，逐步验证
- 优先解决最严重问题
- 每个阶段都有可交付成果

---

## 验证方案

### 测试场景：从头设计主卧（rz_3）

**输入数据**：
```json
{
  "id": "rz_3",
  "name": "主卧",
  "type": "room",
  "reason": "room:MasterBedroom",
  "tags": ["sleep", "wardrobeStorage", "vanity"],
  "rawBoundary": [[...]]  // L型房间，约 20m²
}
```

**期望输出**：
```json
{
  "modules": [
    {
      "id": "m_1",
      "moduleId": "mod_bed_001",
      "zoneId": "rz_3",
      "bounds": {
        "center": [12000, 3000],
        "size": [1800, 2000],
        "rotation": 0
      },
      "facing": "north",
      "items": []
    },
    {
      "id": "m_2",
      "moduleId": "mod_cabinet_006",
      "zoneId": "rz_3",
      "bounds": {
        "center": [10000, 5000],
        "size": [2000, 650],
        "rotation": 0
      },
      "facing": "south",
      "items": []
    },
    {
      "id": "m_3",
      "moduleId": "mod_table_006",
      "zoneId": "rz_3",
      "bounds": {
        "center": [13500, 4500],
        "size": [1200, 1000],
        "rotation": 0
      },
      "facing": "west",
      "items": []
    }
  ]
}
```

**验证点**：
- ✅ 所有 moduleId 都来自 module_library.json
- ✅ 所有 moduleId 的 tags 与 zone.tags 有交集：
  - mod_bed_001: tags=["sleep"] ∩ zone.tags → ✅ 匹配
  - mod_cabinet_006: tags=["storage"] ∩ zone.tags → ✅ 匹配（wardrobeStorage）
  - mod_table_006: tags=["dressing", "storage"] ∩ zone.tags → ✅ 匹配（vanity）
- ✅ bounds.size 与 module_library 中的 size 一致
- ✅ 家具组合合理（床 + 衣柜 + 梳妆台）
- ✅ 家具位置不冲突（简单 AABB 检测）

---

## 关键文件清单

### 需要修改的文件
- `C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md` - Agent 配置文件

### 需要参考的文档
| 文档 | 路径 | 用途 |
|------|------|------|
| MVP 版本规范 | `docs/Server_Agent_Collaboration_Plan.md` | 标签驱动逻辑、工作流程 |
| 文件驱动架构 | `docs/FileDrivenArchitecture.md` | 文件系统结构、读写规则 |
| 项目指令 | `CLAUDE.md` | 项目约束、命名规范 |

### 需要参考的实际数据
| 文件 | 路径 | 用途 |
|------|------|------|
| 实际房间数据 | `demo_1/computed/room_zones.json` | 验证 tags 字段格式 |
| 实际模块库 | `demo_1/modules/module_library.json` | 验证 moduleId 和 tags |
| 实际门窗数据 | `demo_1/baseline/openings.json` | 验证门窗数据结构 |

---

## 下一步行动

### 立即行动（用户决策）

**选择改进方案**：
- [ ] 方案 A：最小修正（1-2小时）
- [ ] 方案 B：系统性重构（4-6小时）
- [x] 方案 C：分阶段演进（推荐）

### 阶段 1 实施计划（如选择方案 C）

**第1步：修正标签驱动逻辑**
1. 在 layout-agent.md 工作流程第3步后添加：
   ```markdown
   3. 【关键】根据 zone.tags 过滤 module.tags
      - 读取每个 zone 的 tags 字段
      - 过滤 module_library 中 tags 有交集的模块
      - 只使用匹配的模块进行布置
   ```

**第2步：修正数据结构示例**
1. 将 `templateId` 改为 `moduleId`
2. 将 `bounds.size` 从 `[width, height]` 改为从 module_library 复制
3. 添加 `items: []` 字段

**第3步：验证修正效果**
1. 使用 demo_1 项目测试
2. 检查生成的 modules.json 是否符合规范
3. 验证标签匹配逻辑是否正确

### 后续行动（阶段 2）

1. 补充详细设计规范（尺寸、组合规则）
2. 添加 Git 提交步骤
3. 添加编辑场景支持

### Server 端配合（如需要）

1. 实现 computed/exclusions.json 生成逻辑
2. 确保 room_zones.json 的 tags 字段完整
3. 添加布置结果验证接口

---

## 附录：实际数据示例

### demo_1 项目的 room_zones.json（部分）

```json
[
  {
    "id": "rz_1",
    "name": "次卧一",
    "roomId": "r_1",
    "type": "room",
    "reason": "room:Bedroom",
    "tags": ["sleep", "wardrobeStorage"],
    "rawBoundary": [[...]]
  },
  {
    "id": "rz_3",
    "name": "主卧",
    "roomId": "r_3",
    "type": "room",
    "reason": "room:MasterBedroom",
    "tags": ["sleep", "wardrobeStorage", "vanity"],
    "rawBoundary": [[...]]
  },
  {
    "id": "rz_6",
    "name": "公共空间",
    "roomId": "r_6",
    "type": "room",
    "reason": "room:LivingRoom",
    "tags": ["tvMedia", "rest", "display"],
    "rawBoundary": [[...]]
  }
]
```

### demo_1 项目的 module_library.json（部分）

```json
{
  "version": "1.0",
  "modules": [
    {
      "id": "mod_bed_001",
      "name": "双人床",
      "tags": ["sleep"],
      "size": { "width": 1800, "depth": 2000 },
      "description": "标准双人床"
    },
    {
      "id": "mod_cabinet_006",
      "name": "衣柜",
      "tags": ["storage"],
      "size": { "width": 2000, "depth": 650 },
      "description": "大衣柜"
    },
    {
      "id": "mod_table_006",
      "name": "梳妆台",
      "tags": ["dressing", "storage"],
      "size": { "width": 1200, "depth": 1000 },
      "description": "梳妆台"
    },
    {
      "id": "mod_cabinet_001",
      "name": "电视柜",
      "tags": ["media", "storage"],
      "size": { "width": 1200, "depth": 500 },
      "description": "小型电视柜"
    },
    {
      "id": "mod_sofa_003",
      "name": "多人沙发",
      "tags": ["seating"],
      "size": { "width": 2650, "depth": 960 },
      "description": "三人沙发"
    }
  ]
}
```

### 标签匹配示例

**主卧（rz_3）**：
- zone.tags = `["sleep", "wardrobeStorage", "vanity"]`
- 匹配的模块：
  - mod_bed_001: tags=["sleep"] → ✅ 匹配
  - mod_bed_002: tags=["sleep"] → ✅ 匹配
  - mod_cabinet_006: tags=["storage"] → ✅ 匹配
  - mod_table_006: tags=["dressing", "storage"] → ✅ 匹配
- 不匹配的模块：
  - mod_cabinet_001: tags=["media", "storage"] → ❌ 不匹配（media 不在 zone.tags）
  - mod_sofa_003: tags=["seating"] → ❌ 不匹配

**客厅（rz_6）**：
- zone.tags = `["tvMedia", "rest", "display"]`
- 匹配的模块：
  - mod_cabinet_001: tags=["media", "storage"] → ✅ 匹配（media 可以匹配 tvMedia）
  - mod_sofa_003: tags=["seating"] → ✅ 匹配（seating 可以匹配 rest）
- 不匹配的模块：
  - mod_bed_001: tags=["sleep"] → ❌ 不匹配

---

**报告结束**

如有问题或需要进一步说明，请联系项目团队。
