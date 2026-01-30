# {PROJECT_NAME} - BIMCanvas 项目工作区

> 本文档帮助 AI 快速理解项目结构。详细数据格式请直接读取对应文件。
>
> **生成时间**: {EXPORT_DATE} | **数据版本**: v3.2（统一文件）

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
│   ├── room_zones.json             设计区域（从房间派生）
│   └── exclusions.json             禁区（门扇开启区等）
│
├── context/                        设计上下文
│   └── requirements.md             [读写] 用户设计需求
│
└── schemes/                        【读写层】AI 操作的策略数据
    ├── strategy.json               方案配置、策略参数
    ├── zones.json                  设计分区定义
    ├── finishes.json               完成面配置
    └── modules.json                ⭐ 家具布置（统一文件）
                                       包含所有区域的模块，通过 zoneId 区分
```

**多策略**: 通过 Git 分支实现，AI 只需操作当前 `schemes/`，分支切换由 Server 管理

---

## 2. 坐标系统

- **原点**: 左下角 | **X**: 向右 | **Y**: 向上 | **单位**: mm

---

## 3. 布置约束

```
对于每个要放置的模块:
1. bounds 必须完全在 zones[].rawBoundary 内
2. bounds 不能与任何 exclusions[].rawBoundary 重叠
3. bounds 不能与其他已放置 modules[] 重叠
```

---

## 4. 重要：modules.json 路径规范

**数据模型版本**：v3.2（统一文件）

**正确路径**：
- ✅ `schemes/modules.json`（所有区域的模块在一个文件中）

**错误路径**：
- ❌ `schemes/rz_1/modules.json`（此路径不存在）
- ❌ `schemes/rz_2/modules.json`（此路径不存在）
- ❌ `schemes/default/modules.json`（此路径不存在）

**区分方式**：
通过 `zoneId` 字段区分模块所属的区域：

```json
[
  {
    "id": "m_1",
    "zoneId": "rz_3",  // ← 主卧
    "moduleId": "bed_king",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": "east",
    "items": []
  },
  {
    "id": "m_2",
    "zoneId": "rz_1",  // ← 客厅
    "moduleId": "sofa_main",
    "bounds": [[2000, 3000], [5000, 3000], [5000, 4500], [2000, 4500]],
    "facing": "east",
    "items": []
  }
]
```

**操作流程**：
- layout-agent 的具体操作流程由 `mcp__canvas__get_workflow_guide` 工具定义
- 本 README 仅提供数据格式说明，不包含工作流程

---

## 5. 快速路径参考

| 用途 | 文件路径 | 读写 |
|------|---------|------|
| 项目入口 | `project.json` | 读 |
| 房间区域 | `computed/room_zones.json` | 读 |
| 门窗数据 | `baseline/openings.json` | 读 |
| 禁区信息 | `computed/exclusions.json` | 读 |
| 家具库 | `modules/module_library.json` | 读 |
| **布置结果** | `schemes/modules.json` | **写** |

---

## 6. 添加布置模块示例

在 `schemes/modules.json` 中添加模块（注意不是 rz_*/modules.json）：

```json
[
  {
    "id": "m_1",
    "moduleId": "bed_king",
    "moduleName": "King Bed",
    "bounds": [[9100, 1750], [11100, 1750], [11100, 3750], [9100, 3750]],
    "facing": "east",
    "zoneId": "rz_3",
    "items": []
  }
]
```

**字段说明**: `id`=实例ID, `moduleId`=模块类型, `bounds`=矩形4顶点, `facing`=朝向(north/south/east/west), `zoneId`=所属分区

**常用 moduleId**: bed_king, bed_queen, nightstand, wardrobe, sofa_main, tv_unit, dining_table, toilet, vanity_sink

---

## 7. 常见问题

| 问题 | 答案 |
|------|------|
| 模块放哪个分区？ | 查看 `schemes/zones.json` 找到房间对应的 `rz_*` ID |
| 如何避免与禁区冲突？ | 读取 `computed/exclusions.json`，确保 bounds 不重叠 |
| bounds 顶点顺序？ | 矩形连续顺序：左下→右下→右上→左上 |
| items 可以为空？ | 是，`items: []` 有效，后续由 Server 填充 |

---

*本文档由 BIMCanvas Server 自动生成*