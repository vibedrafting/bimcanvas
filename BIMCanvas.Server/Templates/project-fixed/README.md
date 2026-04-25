# {PROJECT_NAME} - BIMCanvas 项目工作区

> 本文档帮助 AI 快速理解项目结构。详细数据格式请直接读取对应文件。
>
> **生成时间**: {EXPORT_DATE} | **数据版本**: v3.4（分区文件 + _unzoned）

---

## 1. 项目结构

```
{PROJECT_FOLDER}/
├── project.json                    [读写] 项目元数据
│
├── baseline/                       【只读层】Revit 导出的建筑数据
│   ├── architecture.json           墙体 + 柱子轮廓
│   ├── openings.json               门窗定位线、朝向
│   ├── rooms.json                  房间边界、类型
│   └── location_lines.json         完成面定位线
│
├── computed/                       【自动层】Server 计算的派生数据
│   ├── room_zones.json             房间分区（从房间派生，分区方案的依据）
│   └── exclusions.json             禁区（门扇开启区等）
│
├── references/                     项目级运行时参考规则
│   ├── design_principles.md        通用设计原则
│   ├── design_evaluation.md        设计评价框架
│   └── *.md                        房间策略 / 可选家具规则
│
├── modules/                        家具库
│   └── module_library.json         可选家具模块定义
│
└── schemes/                        【读写层】AI 操作的策略数据
    ├── strategy.json               方案配置、策略参数
    ├── zones.json                  分区方案（当前策略对应的分区设计，rz_1、rz_2等分区文件夹创建的依据）
    ├── finishes.json               完成面配置
    ├── rz_1/                       ⭐ 分区 1 的布置数据
    │   └── modules.json            该分区的家具布置
    ├── rz_2/                       ⭐ 分区 2 的布置数据
    │   └── modules.json
    ├── rz_3/                       容器分区（当存在 subZones 时）
    │   ├── dz_1/
    │   │   └── modules.json        叶子子分区 dz_1 的家具布置
    │   └── dz_2/
    │       └── modules.json        叶子子分区 dz_2 的家具布置
    ├── _unzoned/                   未分区模块（bounds 不在任何分区内）
    │   └── modules.json
    └── ...                         其他分区
```

---

## 2. 坐标系统

- **原点**: 左下角 | **X**: 向右 | **Y**: 向上 | **单位**: mm

---

## 3. 布置约束

```
对于每个要放置的模块:
1. bounds 必须完全在对应 zone 的 rawBoundary 内
2. bounds 不能与任何 exclusions[].rawBoundary 重叠
3. bounds 不能与同分区内其他已放置 modules[] 重叠
```

---

## 4. 重要：modules.json 路径规范

**数据模型版本**：v3.4（分区文件 + _unzoned）

**正确路径**：
- ✅ `schemes/rz_1/modules.json`（rz_1 无 `subZones` 时的家具布置）
- ✅ `schemes/rz_3/dz_1/modules.json`（rz_3 有子分区时，dz_1 的家具布置）
- ✅ `schemes/rz_3/dz_2/modules.json`（rz_3 有子分区时，dz_2 的家具布置）
- ✅ `schemes/_unzoned/modules.json`（不在任何分区内的模块，Server 自动归类）

**错误路径**：
- ❌ `schemes/modules.json`（此路径不存在，已废弃）
- ❌ `schemes/rz_3/modules.json`（当 rz_3 有 `subZones` 时，rz_3 是容器分区，不承载家具）

**查找分区**：
1. 读取 `schemes/zones.json` 获取所有分区 ID（如 rz_1, rz_2, rz_3...）
2. 若分区无 `subZones`，定位 `schemes/{zoneId}/modules.json`
3. 若分区有 `subZones`，模块写入对应叶子子分区：`schemes/{parentZoneId}/{childZoneId}/modules.json`
4. `_unzoned` 目录由 Server 自动管理，存放 bounds 中心点不在任何 Room Zone 内的模块

**文件格式示例** (`schemes/rz_3/dz_2/modules.json`)：

```json
[
  {
    "moduleId": "mod_bed_001",
    "moduleName": "双人床",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": {
      "value": [1, 0],
      "semantic": null
    },
    "items": []
  }
]
```

> **注意**：`id` 由 Server 在 `validate_layout` 时自动生成（格式 `m_xxxxxxxx`），Agent 写入时无需填写。`zoneId` 由 Server 根据 bounds 位置自动计算。

**操作流程**：
- 布置操作流程由工作流 Skills 定义：
  `query-workflow` / `edit-workflow` / `generate-reference-analysis` / `generate-planning` / `generate-placement` / `generate-zoning`
- generate 任务的主链统一为：
  - 无参考图或未形成定稿参考分析 → `generate-planning` → `generate-placement`
  - 有参考图且需要参考消费 → `generate-reference-analysis` → `generate-planning` → `generate-placement`
- 项目级运行时参考规则统一位于 `references/*.md`
- 本 README 仅提供数据格式说明，不包含完整工作流程

---

## 5. 快速路径参考

| 用途 | 文件路径 | 读写 |
|------|---------|------|
| 项目入口 | `project.json` | 读 |
| 房间区域 | `computed/room_zones.json` | 读 |
| 门窗数据 | `baseline/openings.json` | 读 |
| 禁区信息 | `computed/exclusions.json` | 读 |
| 设计规则 | `references/*.md` | 读 |
| 家具库 | `modules/module_library.json` | 读 |
| **布置结果** | 叶子分区 `modules.json` | **写** |

---

## 6. 添加布置模块示例

在叶子分区 `schemes/rz_3/dz_2/modules.json` 中添加模块：

```json
[
  {
    "id": "m_1",
    "moduleId": "mod_bed_001",
    "moduleName": "双人床",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": {
      "value": [1, 0],
      "semantic": null
    },
    "zoneId": "rz_3",
    "items": []
  }
]
```

**字段说明**:
- `id`: 由 Server 自动生成（格式 `m_xxxxxxxx`），Agent 无需填写
- `moduleId`: 模块类型（来自 module_library.json）
- `bounds`: 矩形 4 顶点（左下→右下→右上→左上）
- `facing.value`: 布置方向真理，单位向量 `[x, y]`
- `facing.semantic`: AI 临时输入槽，允许先写 `"north"` 等语义方向，随后由 `validate_layout` 转成 `value` 并清空为 `null`
- `zoneId`: 由 Server 根据 bounds 位置自动计算
- `items`: 子项（可为空数组）

**常用 moduleId**: mod_bed_001(1800床), mod_bed_002(1500床), mod_sofa_001, mod_cabinet_006, mod_table_001

**卧室床模块说明**：`mod_bed_001` 为 `1800×2100`，`mod_bed_002` 为 `1500×2100`。睡眠组空间充足时优先选 `mod_bed_001 + 双床头柜`；空间不足时先切换到 `mod_bed_002 + 双床头柜`，仅在仍不成立时才允许 `mod_bed_002 + 单床头柜`。

---

## 7. 常见问题

| 问题 | 答案 |
|------|------|
| 如何找到分区 ID？ | 读取 `schemes/zones.json`，每个 zone 的 `id` 字段即分区 ID |
| 模块应该写入哪个文件？ | 写入目标叶子分区的 `modules.json`；父 zone 有 `subZones` 时，写入 `schemes/{parentZoneId}/{childZoneId}/modules.json` |
| 如何避免与禁区冲突？ | 读取 `computed/exclusions.json`，确保 bounds 不重叠 |
| bounds 顶点顺序？ | 矩形连续顺序：左下→右下→右上→左上 |
| `facing` 应该怎么写？ | 规范格式是 `{ "value": [x, y], "semantic": null }`；AI 也可临时写 `{ "value": null, "semantic": "north" }`，随后必须调用 `validate_layout` 归一化 |
| items 可以为空？ | 是，`items: []` 有效，后续由 Server 填充 |
| `_unzoned` 目录是什么？ | Server 保存时，bounds 中心不在任何分区内的模块自动归入此目录，避免数据丢失 |

---

*本文档由 BIMCanvas Server 自动生成*
