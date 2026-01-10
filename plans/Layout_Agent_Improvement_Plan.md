# Layout-Agent 分阶段改造方案

**创建日期**：2026-01-10
**适用范围**：BIMCanvas Layout-Agent 配置优化
**目标**：修复7个关键缺陷，实现完整的文件驱动家具布置工作流

---

## 执行摘要

本方案针对当前 layout-agent 配置存在的 7 个关键缺陷，设计了分 3 阶段的渐进式改造路径：

| 阶段 | 目标 | 工作量 | 优先级 | 预期成果 |
|------|------|--------|--------|----------|
| **阶段 1** | 修复 P0 严重缺陷 | 2-3 小时 | 🔴 必须 | Agent 可正确选择家具并生成合法数据 |
| **阶段 2** | 补充 P1 功能 | 4-5 小时 | 🟡 重要 | 支持查看/编辑场景，提升布置质量 |
| **阶段 3** | 完善生态系统 | 待定 | 🟢 可选 | 高级功能和工具支持 |

**关键原则**：
- ✅ 每个阶段独立交付，可单独验证
- ✅ 优先修复最严重的问题（标签驱动、数据结构）
- ✅ 保持向后兼容，避免破坏现有流程
- ✅ 基于实际项目数据验证（demo_1）

---

## 现状分析

### 当前配置的 7 个关键缺陷

| # | 缺陷 | 严重性 | 影响 | 现象 |
|---|------|--------|------|------|
| 1 | **缺少标签驱动逻辑** | 🔴 严重 | 无法正确选择家具 | Agent 不知道卧室要选"sleep"标签的床 |
| 2 | **数据结构不一致** | 🔴 严重 | 生成数据无法识别 | 使用虚构的 templateId 而非 moduleId |
| 3 | **缺少 exclusions.json 读取** | 🟡 中等 | 无法避让禁区 | 可能把家具放在门口 |
| 4 | **Git 提交步骤缺失** | 🟡 中等 | 不符合 MVP 规范 | 无版本历史记录 |
| 5 | **设计规范不详细** | 🟡 中等 | 布置质量不高 | 不知道具体尺寸要求 |
| 6 | **缺少编辑场景支持** | 🟡 中等 | 无法增量修改 | 只能重新布置整个房间 |
| 7 | **文件系统理解不全** | 🟢 轻微 | 缺少上下文 | 不知道其他辅助文件 |

### demo_1 项目实际状况

**已有数据**：
- ✅ baseline/rooms.json - 6个房间（次卧×2、主卧、主卫、公卫、公共空间）
- ✅ baseline/openings.json - 8个门窗
- ✅ baseline/architecture.json - 建筑几何
- ✅ baseline/location_lines.json - 完成面定位线

**缺失数据**：
- ❌ computed/room_zones.json - 需要 Server 生成
- ❌ computed/exclusions.json - 需要 Server 生成
- ❌ modules/module_library.json - 需要预先准备

**关键发现**：layout-agent.md 的工作流步骤 1-3 在 demo_1 中无法执行。

---

## 阶段 1：修复 P0 严重缺陷（2-3 小时）

### 目标

✅ 修复标签驱动逻辑缺失
✅ 修正 modules.json 数据结构
✅ 确保 Agent 生成的数据可被 Server 识别

### 1.1 添加标签驱动过滤逻辑

**修改文件**：`C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md`

**修改位置**：工作流程章节（第 79-92 行）

**修改内容**：

```markdown
## 工作流程
1. 使用 Read 工具读取 computed/room_zones.json
2. 使用 Read 工具读取 modules/module_library.json
3. **【关键】根据 zone.tags 过滤 module.tags**
   - 读取每个 zone 的 tags 字段（Server 已预计算）
   - 过滤 module_library 中 tags 有交集的模块
   - 只使用匹配的模块进行布置
4. 使用 Read 工具读取 computed/exclusions.json（如果存在）
5. 使用 Read 工具读取 baseline/openings.json
6. 分析每个房间的空间特点和约束
7. 按布置优先级为每个房间选择和布置家具
8. 使用 Write 工具将结果写入 schemes/modules.json
9. Git 提交（可选，在明确指示时执行）
```

**新增章节**：`### 标签驱动模块选择（核心逻辑）`

插入在"工作流程"章节之后：

```markdown
### 标签驱动模块选择（核心逻辑）

**原则**：Server 预计算房间功能标签，Agent 根据标签筛选家具模块。

**实现逻辑**：
\`\`\`python
# 第 1 步：读取房间区域数据（含 tags）
zones = load_json("computed/room_zones.json")
zone = next(z for z in zones if z["id"] == target_zone_id)
zone_tags = zone["tags"]  # 示例：["sleep", "wardrobeStorage"]

# 第 2 步：读取模块库
library = load_json("modules/module_library.json")

# 第 3 步：过滤匹配的模块
compatible_modules = [
    module for module in library["modules"]
    if any(tag in module["tags"] for tag in zone_tags)
]

# 结果：只有 tags 包含 "sleep" 或 "wardrobeStorage" 的模块
# 如：mod_bed_001 (tags: ["sleep"])、mod_cabinet_006 (tags: ["storage"])
\`\`\`

**房间类型对应标签参考**：

| 房间类型 (reason) | 功能标签 (tags) | 必选模块 | 可选模块 |
|------------------|-----------------|---------|---------|
| room:MasterBedroom | sleep, wardrobeStorage, vanity | 床、衣柜 | 梳妆台、床头柜 |
| room:Bedroom | sleep, wardrobeStorage | 床、衣柜 | 书桌、床头柜 |
| room:LivingRoom | tvMedia, rest, display | 沙发、电视柜 | 茶几、边几 |
| room:Bathroom | shower, toilet, washing, vanity | - | 洗衣机 |
```

### 1.2 修正 modules.json 数据结构

**修改位置**：`layout-agent.md` 第 51-68 行（modules.json 输出格式章节）

**修改前**（错误）：
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

**修改后**（正确）：
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
    }
  ]
}
```

**新增字段说明表格**：

```markdown
## 字段说明

| 字段 | 类型 | 说明 | 数据来源 | 示例 |
|------|------|------|----------|------|
| id | string | 布置实例唯一ID（前缀 m_） | Agent 生成 | `"m_1"` |
| moduleId | string | 引用模块库的模块ID | 从 module_library 选择 | `"mod_bed_001"` |
| zoneId | string | 所属房间分区ID | 从 room_zones 选择 | `"rz_3"` |
| bounds.center | array | 中心点坐标 [x, y]（mm） | Agent 计算 | `[12000, 3000]` |
| bounds.size | array | 尺寸 [width, depth]（mm） | **从 module_library 复制** | `[1800, 2000]` |
| bounds.rotation | number | 旋转角度（度） | Agent 计算 | `0` |
| facing | string/array | 朝向（8方向或Vec2D） | Agent 计算 | `"north"` |
| items | array | 子项（预留字段） | 暂时为空 | `[]` |

**重要**：
- 使用 `moduleId` 而非 `templateId`（直接对应 module_library.json 的 id）
- `bounds.size` 必须从 module_library.json 复制（保证渲染性能和历史兼容性）
- `id` 使用 `m_` 前缀（区分布置实例和模块定义）
```

### 1.3 新增数据结构说明章节

**插入位置**：在"字段说明"之后

**新增内容**：

```markdown
## 数据结构说明

### 核心概念区分

在家具布置系统中，有两种不同的 JSON 文件：

| 文件 | 职责 | 类比 | 读写属性 |
|------|------|------|----------|
| **module_library.json** | 设计素材库 | "家具目录" | 只读 |
| **modules.json** | 布置结果 | "装修清单" | 可写 |

**重要**：两者数据结构不同是**正确且必要的**。module_library.json 定义"有什么家具"，modules.json 记录"用了什么家具、放在哪里"。

### module_library.json - 设计素材库

**文件路径**：`modules/module_library.json`

**数据结构**：
\`\`\`json
{
  "version": "1.0",
  "modules": [
    {
      "id": "mod_bed_001",
      "name": "双人床",
      "tags": ["sleep"],
      "size": {"width": 1800, "depth": 2000},
      "description": "标准双人床",
      "svgPath": "modules/assets/mod_bed_001.svg"
    }
  ]
}
\`\`\`

### modules.json - 布置结果

**文件路径**：`schemes/modules.json` 或 `schemes/{schemeId}/modules.json`

**数据结构**：见上文"modules.json 输出格式"章节。

### 数据流转关系

\`\`\`
module_library.json (只读)
    ↓ Agent 根据 zone.tags 过滤
    ↓ Agent 选择合适的 moduleId
modules.json (可写)
    ↓ Agent 写入布置实例（moduleId + bounds + facing）
Server 验证
    ↓ 检查 moduleId 有效性
    ↓ 检查 module.tags 与 zone.tags 兼容性
    ↓ 检查空间约束
前端渲染
    ↓ 读取 modules.json 获取位置
    ↓ 通过 moduleId 查询 module_library 获取 SVG
\`\`\`

### 关键设计决策

1. **为什么 bounds.size 需要从 module_library 复制？**
   - 渲染性能（避免多次查询）
   - 数据完整性（历史方案不受模块库更新影响）
   - 支持未来的尺寸微调

2. **为什么使用 moduleId 而不是 templateId？**
   - 语义清晰，直接对应 module_library.json 的 id
   - 避免与"模板系统"概念混淆

3. **为什么 id 使用 "m_" 前缀？**
   - 区分布置实例（m_1）和模块定义（mod_bed_001）
```

### 1.4 验证测试

**测试项目**：demo_1

**前置条件**（需要 Server 生成）：
1. `computed/room_zones.json` - 包含 tags 字段
2. `modules/module_library.json` - 家具模板库

**测试用例**：布置主卧（rz_3）

**输入数据**：
```json
// computed/room_zones.json
[
  {
    "id": "rz_3",
    "name": "主卧",
    "roomId": "r_3",
    "reason": "room:MasterBedroom",
    "tags": ["sleep", "wardrobeStorage", "vanity"],
    "rawBoundary": [[...]]
  }
]

// modules/module_library.json
{
  "modules": [
    {"id": "mod_bed_001", "name": "双人床", "tags": ["sleep"], "size": {"width": 1800, "depth": 2000}},
    {"id": "mod_cabinet_006", "name": "衣柜", "tags": ["storage"], "size": {"width": 2000, "depth": 650}},
    {"id": "mod_table_006", "name": "梳妆台", "tags": ["dressing", "storage"], "size": {"width": 1200, "depth": 1000}}
  ]
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
      "bounds": {"center": [12000, 3000], "size": [1800, 2000], "rotation": 0},
      "facing": "north",
      "items": []
    },
    {
      "id": "m_2",
      "moduleId": "mod_cabinet_006",
      "zoneId": "rz_3",
      "bounds": {"center": [10000, 5000], "size": [2000, 650], "rotation": 0},
      "facing": "south",
      "items": []
    }
  ]
}
```

**验证点**：
- ✅ 所有 moduleId 都来自 module_library.json
- ✅ 所有 moduleId 的 tags 与 zone.tags 有交集
- ✅ bounds.size 与 module_library 中的 size 一致
- ✅ 使用 moduleId 而非 templateId
- ✅ 使用 m_ 前缀的布置实例 id

### 1.5 阶段 1 交付物

- [ ] 修改后的 `layout-agent.md`（包含标签驱动逻辑和正确数据结构）
- [ ] 测试数据（room_zones.json 和 module_library.json）
- [ ] 测试报告（验证 5 个验证点）

---

## 阶段 2：补充 P1 重要功能（4-5 小时）

### 目标

✅ 补充详细的设计规范（尺寸、组合规则）
✅ 添加 Git 提交步骤
✅ 添加编辑场景支持
✅ 添加查看/报告功能

### 2.1 补充详细设计规范

**新增章节**：`## 详细设计规范`

**插入位置**：在"布置规则"章节之后

**内容**：

```markdown
## 详细设计规范

### 设计尺寸规范

**卧室**：
- 床两侧通道：≥ 600mm
- 床尾通道：≥ 900mm
- 衣柜前开门空间：≥ 650mm（平开门）
- 床头柜与床的距离：50-100mm

**客厅**：
- 沙发与茶几距离：400-500mm
- 茶几与电视柜距离：≥ 600mm
- 沙发与电视的观看距离：2500-4000mm
- 主要动线宽度：≥ 800mm

**餐厅**：
- 餐椅拉出空间：≥ 750mm
- 餐桌与墙的距离：≥ 800mm
- 餐边柜与餐桌距离：≥ 900mm

### 家具组合规则

**必选组合**：
- 床（sleep）→ 必须配床头柜（storage）× 2
- 沙发（seating）→ 必须配茶几（storage）
- 梳妆台（dressing）→ 建议配凳子（seating）

**可选组合**：
- 床（sleep）→ 可选床尾凳（seating, storage）
- 电视柜（media）→ 可选边柜（storage）
- 书桌（work）→ 可选台灯（lighting）

### 空间分析方法

**识别锚点墙**：
1. **电视墙**（客厅）：
   - 优先选择最长的连续墙面
   - 避开有门窗的墙面
   - 优先选择正对入口的墙面

2. **床头墙**（卧室）：
   - 优先选择最长的连续墙面
   - 避开有窗户的墙面（床头不靠窗）
   - 避开门的正对面

**计算可用空间**：
\`\`\`python
available_area = rawBoundary - exclusions - opening_swing_arcs
\`\`\`

### 布置策略

**锚点家具放置**：
1. 识别锚点墙（电视墙/床头墙）
2. 将锚点家具居中于锚点墙
3. 保持与墙面的合理距离（50-100mm）

**主要家具放置**：
1. 围绕锚点家具布置
2. 保持功能性的距离关系
3. 避让门窗开启范围

**辅助家具放置**：
1. 填充剩余空间
2. 优化动线流畅性
3. 平衡视觉重量
```

### 2.2 添加 Git 提交步骤

**修改位置**：工作流程第 9 步

**修改内容**：

```markdown
9. Git 提交（符合 MVP 版本规范）
   - 执行 `git add .`
   - 执行 `git commit -m "feat(layout): 完成 [房间名称] 家具布置\n\nCo-Authored-By: Claude Sonnet 4.5 <noreply@anthropic.com>"`
   - 提交信息格式：
     - 功能型：`feat(layout): 描述`
     - 修复型：`fix(layout): 描述`
     - 自动存档：`自动存档_[YYYYMMDD_HHMMSS]`
```

**新增章节**：`### Git 提交规范`

```markdown
### Git 提交规范

| 场景 | 提交信息格式 | 示例 |
|------|-------------|------|
| 完成布置 | `feat(layout): 完成 [房间] 家具布置` | `feat(layout): 完成主卧家具布置` |
| 修正布置 | `fix(layout): 修正 [问题]` | `fix(layout): 修正床的位置冲突` |
| 自动存档 | `自动存档_[时间戳]` | `自动存档_20260110_143522` |

**注意**：
- 所有提交必须包含 Co-Authored-By 签名
- 时间戳格式：YYYYMMDD_HHMMSS
- 避免使用 `git commit --amend` 除非明确要求
```

### 2.3 添加编辑场景支持

**新增章节**：`## 编辑场景支持`

**插入位置**：在"工作流程"章节之后

**内容**：

```markdown
## 编辑场景支持

### 增量修改工作流

**场景**：用户需要修改/移动/删除部分家具，而不是重新布置整个房间。

**工作流程**：
1. 使用 Read 工具读取现有 schemes/modules.json
2. 识别要修改的模块（通过 id、moduleId 或 zoneId 筛选）
3. 执行修改操作：
   - **移动家具**：更新 bounds.center
   - **旋转家具**：更新 bounds.rotation
   - **删除家具**：从数组中移除对应项
   - **替换家具**：修改 moduleId，并同步更新 bounds.size
4. 保留其他模块不变
5. 使用 Write 工具写入更新后的 modules.json
6. Git 提交

### 编辑操作示例

**移动家具**：
\`\`\`python
# 用户：将主卧的床移动到房间中心
current = load_json("schemes/modules.json")

# 找到主卧的床
bed = next(m for m in current["modules"]
           if m["zoneId"] == "rz_3" and "bed" in m["moduleId"])

# 计算房间中心
zone = load_json("computed/room_zones.json")
zone_data = next(z for z in zone if z["id"] == "rz_3")
center = calculate_polygon_center(zone_data["rawBoundary"])

# 更新位置
bed["bounds"]["center"] = center

# 写入
write_json("schemes/modules.json", current)
\`\`\`

**删除家具**：
\`\`\`python
# 用户：删除次卧一的书桌
current = load_json("schemes/modules.json")

# 过滤掉要删除的模块
current["modules"] = [
    m for m in current["modules"]
    if not (m["zoneId"] == "rz_1" and "table_003" in m["moduleId"])
]

# 写入
write_json("schemes/modules.json", current)
\`\`\`

**旋转家具**：
\`\`\`python
# 用户：将沙发旋转 90 度
current = load_json("schemes/modules.json")

sofa = next(m for m in current["modules"] if "sofa" in m["moduleId"])
sofa["bounds"]["rotation"] = (sofa["bounds"]["rotation"] + 90) % 360

# 更新 facing 方向
facing_map = {"north": "east", "east": "south", "south": "west", "west": "north"}
if sofa["facing"] in facing_map:
    sofa["facing"] = facing_map[sofa["facing"]]

write_json("schemes/modules.json", current)
\`\`\`

### 编辑注意事项

1. **保留其他房间的布置**：只修改目标房间的模块
2. **验证修改后不冲突**：简单 AABB 检测（Server 会做精确验证）
3. **同步更新相关字段**：如旋转时同步更新 facing
4. **提交有意义的 Git 信息**：描述具体修改内容
```

### 2.4 添加查看/报告功能

**新增章节**：`## 查看和报告功能`

**内容**：

```markdown
## 查看和报告功能

### 项目状态查看

**触发指令**：
- "查看当前项目状态"
- "项目概览"
- "显示所有房间信息"

**输出格式**：
\`\`\`
项目：demo_1
方案：default
共 6 个房间

房间 rz_1（次卧一）：
- 面积：9.8 m²
- 功能标签：sleep, wardrobeStorage
- 门窗：1 扇门（d_5）
- 已布置家具：2 件（床、衣柜）

房间 rz_3（主卧）：
- 面积：20.5 m²
- 功能标签：sleep, wardrobeStorage, vanity
- 门窗：1 扇门（d_7）
- 已布置家具：3 件（床、衣柜、梳妆台）

房间 rz_6（公共空间）：
- 面积：45.2 m²
- 功能标签：tvMedia, rest, display
- 门窗：1 扇门（d_1）+ 2 扇窗（wi_4, wi_5）
- 已布置家具：4 件（沙发、电视柜、茶几、边几）
\`\`\`

**实现逻辑**：
\`\`\`python
# 读取数据
zones = load_json("computed/room_zones.json")
openings = load_json("baseline/openings.json")
modules = load_json("schemes/modules.json")

# 统计每个房间的家具数量
for zone in zones:
    zone_modules = [m for m in modules["modules"] if m["zoneId"] == zone["id"]]
    # 计算面积并输出
    area_m2 = zone.get("area_mm2", 0) / 1_000_000
    print(f"房间 {zone['id']}（{zone['name']}）：")
    print(f"- 面积：{area_m2:.1f} m²")
    print(f"- 功能标签：{', '.join(zone['tags'])}")
    print(f"- 已布置家具：{len(zone_modules)} 件")
\`\`\`

### 房间详情查看

**触发指令**：
- "查看主卧详情"
- "显示次卧一的家具"
- "rz_3 的布置情况"

**输出格式**：
\`\`\`
房间 rz_3（主卧）详情：

基本信息：
- 类型：room:MasterBedroom
- 面积：20.5 m²
- 功能标签：sleep, wardrobeStorage, vanity

已布置家具（3 件）：
1. m_1 - 双人床 (mod_bed_001)
   - 位置：[12000, 3000] mm
   - 尺寸：1800 × 2000 mm
   - 朝向：north

2. m_2 - 衣柜 (mod_cabinet_006)
   - 位置：[10000, 5000] mm
   - 尺寸：2000 × 650 mm
   - 朝向：south

3. m_3 - 梳妆台 (mod_table_006)
   - 位置：[13500, 4500] mm
   - 尺寸：1200 × 1000 mm
   - 朝向：west
\`\`\`
```

### 2.5 阶段 2 交付物

- [ ] 详细设计规范章节
- [ ] Git 提交规范章节
- [ ] 编辑场景支持章节（含示例代码）
- [ ] 查看和报告功能章节（含示例代码）
- [ ] 测试报告（验证编辑和查看功能）

---

## 阶段 3：完善生态系统（待定）

### 目标

✅ 完善文件系统理解
✅ 实现高级布置算法（可选）
✅ Server 端配合工作

### 3.1 完善文件系统理解

**新增章节**：`## 项目文件系统结构`

**内容**：

```markdown
## 项目文件系统结构

### 完整的文件架构

\`\`\`
项目根目录/
├── project.json                # 项目元数据（只读）
├── baseline/                   # 【底层】建筑基础数据（只读，Revit 导出）
│   ├── walls.json
│   ├── columns.json
│   ├── openings.json           # ✅ 必读
│   ├── rooms.json              # ✅ 必读
│   ├── location_lines.json
│   ├── architecture.json
│   └── metadata.json           # ✅ 必读
├── computed/                   # 【中层】计算派生数据（只读，Server 生成）
│   ├── room_zones.json         # ✅ 必读
│   └── exclusions.json         # ✅ 推荐读取
├── modules/                    # 家具素材库（只读）
│   ├── module_library.json     # ✅ 必读
│   └── assets/                 # SVG 资源
├── schemes/                    # 【顶层】方案设计数据（可写）
│   ├── modules.json            # ✅ 输出文件
│   ├── zones.json
│   └── finishes.json
└── context/
    └── requirements.md         # 设计需求
\`\`\`

### 文件读写权限

| 层级 | 路径 | 权限 | 说明 |
|-----|------|------|------|
| 基准层 | baseline/ | 🔒 只读 | Revit 导出，不可修改 |
| 计算层 | computed/ | 🔒 只读 | Server 自动生成 |
| 素材层 | modules/ | 🔒 只读 | 预先准备的家具资源 |
| 方案层 | schemes/ | ✏️ 可写 | Agent 输出布置结果 |
```

### 3.2 Server 端配合工作

**Server 需要实现的功能**：

1. **生成 computed/room_zones.json**
2. **生成 computed/exclusions.json**
3. **初始化 schemes 目录**
4. **验证布置结果**

详细实现见附录 B。

---

## 实施计划

### 时间线

| 阶段 | 任务 | 工作量 | 依赖 |
|------|------|--------|------|
| **阶段 1** | 修复标签驱动逻辑 | 1 小时 | 无 |
| | 修正数据结构 | 0.5 小时 | 无 |
| | 补充数据结构说明 | 0.5 小时 | 无 |
| | 验证测试 | 1 小时 | Server 生成测试数据 |
| **阶段 2** | 补充设计规范 | 2 小时 | 阶段 1 |
| | 添加 Git 提交 | 0.5 小时 | 阶段 1 |
| | 添加编辑场景 | 1.5 小时 | 阶段 1 |
| | 添加查看功能 | 1 小时 | 阶段 1 |
| | 验证测试 | 1 小时 | 无 |
| **阶段 3** | 完善文件系统理解 | 1 小时 | 阶段 2 |
| | Server 端配合 | 待定 | 阶段 2 |

### 依赖关系

```
阶段 1（P0修复）→ 必须完成
    ↓
阶段 2（P1功能）→ 可选
    ↓
阶段 3（生态完善）
```

---

## 验收标准

### 阶段 1 验收

- [ ] layout-agent.md 包含标签驱动逻辑说明
- [ ] modules.json 数据结构使用 moduleId 而非 templateId
- [ ] 数据结构说明章节完整
- [ ] 使用 demo_1 项目测试通过
- [ ] 生成的 modules.json 可被 Server 正确解析

### 阶段 2 验收

- [ ] 补充了详细设计规范（尺寸、组合、空间分析）
- [ ] 添加了 Git 提交步骤和规范
- [ ] 支持编辑场景（移动、删除、旋转）
- [ ] 支持查看功能（项目状态、房间详情）
- [ ] 所有功能通过测试用例验证

### 阶段 3 验收

- [ ] 文件系统结构说明完整
- [ ] Server 端生成所有必需的 computed 文件
- [ ] 完整的端到端工作流可运行

---

## 附录

### A. 测试数据准备（demo_1 项目）

**computed/room_zones.json**：
```json
[
  {"id": "rz_1", "name": "次卧一", "roomId": "r_1", "type": "room", "reason": "room:Bedroom", "tags": ["sleep", "wardrobeStorage"], "rawBoundary": [[...]]},
  {"id": "rz_2", "name": "次卧二", "roomId": "r_2", "type": "room", "reason": "room:Bedroom", "tags": ["sleep", "wardrobeStorage"], "rawBoundary": [[...]]},
  {"id": "rz_3", "name": "主卧", "roomId": "r_3", "type": "room", "reason": "room:MasterBedroom", "tags": ["sleep", "wardrobeStorage", "vanity"], "rawBoundary": [[...]]},
  {"id": "rz_6", "name": "公共空间", "roomId": "r_6", "type": "room", "reason": "room:LivingRoom", "tags": ["tvMedia", "rest", "display"], "rawBoundary": [[...]]}
]
```

**modules/module_library.json**（最小测试集）：
```json
{
  "version": "1.0",
  "modules": [
    {"id": "mod_bed_001", "name": "双人床", "tags": ["sleep"], "size": {"width": 1800, "depth": 2000}},
    {"id": "mod_cabinet_006", "name": "衣柜", "tags": ["storage"], "size": {"width": 2000, "depth": 650}},
    {"id": "mod_table_006", "name": "梳妆台", "tags": ["dressing", "storage"], "size": {"width": 1200, "depth": 1000}},
    {"id": "mod_sofa_003", "name": "三人沙发", "tags": ["seating"], "size": {"width": 2650, "depth": 960}},
    {"id": "mod_cabinet_001", "name": "电视柜", "tags": ["media", "storage"], "size": {"width": 1200, "depth": 500}},
    {"id": "mod_table_002", "name": "茶几", "tags": ["storage"], "size": {"width": 1200, "depth": 760}}
  ]
}
```

### B. Server 端实现参考

**生成 room_zones.json**：
```csharp
public class RoomZoneGenerator
{
    private static readonly Dictionary<int, string[]> RoomTypeTags = new()
    {
        { 0, new[] { "tvMedia", "rest", "display" } },           // 公共空间
        { 2, new[] { "sleep", "wardrobeStorage", "vanity" } },   // 主卧
        { 3, new[] { "sleep", "wardrobeStorage" } },             // 次卧
        { 6, new[] { "shower", "toilet", "washing", "vanity" } } // 卫生间
    };

    public void GenerateRoomZones(string projectPath)
    {
        var rooms = LoadRooms($"{projectPath}/baseline/rooms.json");
        var zones = rooms.Select(room => new RoomZone
        {
            Id = $"rz_{room.Id.Substring(2)}",
            RoomId = room.Id,
            Name = room.Name,
            Type = "room",
            Reason = MapRoomTypeToReason(room.Type),
            Tags = RoomTypeTags.GetValueOrDefault(room.Type, Array.Empty<string>()),
            RawBoundary = room.Boundary
        }).ToList();

        WriteJson($"{projectPath}/computed/room_zones.json", zones);
    }
}
```

### C. 关键文件清单

**需要修改的文件**：
- `C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md`

**参考文档**：
- `E:\工作文档\开发类\MyCode\BIMCanvas\docs\Server_Agent_Collaboration_Plan.md`
- `E:\工作文档\开发类\MyCode\BIMCanvas\docs\FileDrivenArchitecture.md`
- `E:\工作文档\开发类\MyCode\BIMCanvas\CLAUDE.md`
- `E:\工作文档\开发类\MyCode\BIMCanvas\reports\Layout_Agent_Configuration_Analysis.md`

**测试项目**：
- `C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1`

---

**方案版本**：v1.0
**最后更新**：2026-01-10
**状态**：待审批
