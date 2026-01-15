# {PROJECT_NAME} - BIMCanvas 项目工作区

> 本文档帮助 AI 快速理解项目结构。详细数据格式请直接读取对应文件。
>
> **生成时间**: {EXPORT_DATE} | **数据版本**: v3.0

---

## 1. 文件导航

| 数据类型 | 文件位置 | 读写 | 说明 |
|----------|----------|:----:|------|
| 项目配置 | `project.json` | 读写 | 项目元数据 |
| 墙柱轮廓 | `baseline/architecture.json` | 只读 | Revit 导出 |
| 门窗开口 | `baseline/openings.json` | 只读 | 门窗定位线 |
| 物理房间 | `baseline/rooms.json` | 只读 | 房间边界 |
| 定位线 | `baseline/location_lines.json` | 只读 | 完成面定位 |
| 设计区域 | `computed/room_zones.json` | 自动 | 派生区域 |
| 禁区 | `computed/exclusions.json` | 自动 | 门扇禁区等 |
| 设计需求 | `context/requirements.md` | 读写 | 用户需求 |
| 方案配置 | `schemes/strategy.json` | 读写 | 策略参数 |
| 区域配置 | `schemes/zones.json` | 读写 | 设计分区 |
| **布置模块** | `schemes/rz_*/modules.json` | **读写** | **家具布置** |

---

## 2. 目录结构

```
{PROJECT_FOLDER}/
├── project.json                # 项目元数据
├── baseline/                   # 【底层】建筑数据（只读）
│   ├── architecture.json       # 墙体 + 柱子
│   ├── openings.json           # 门窗开口
│   ├── rooms.json              # 物理房间
│   └── location_lines.json     # 定位线
├── computed/                   # 【中层】派生数据（自动）
│   ├── room_zones.json         # 设计区域
│   └── exclusions.json         # 禁区
├── context/                    # 设计上下文
│   └── requirements.md         # 用户需求
└── schemes/                    # 【顶层】策略数据（读写）
    ├── strategy.json           # 方案配置
    ├── zones.json              # 设计分区
    ├── finishes.json           # 完成面
    └── rz_*/modules.json       # 各分区布置
```

**三层架构**: baseline（只读）→ computed（自动）→ schemes（读写）

**多策略模式**: 通过 Git 分支实现，AI 只需操作当前 `schemes/` 目录，分支切换由 Server 管理。

---

## 3. 坐标系统

- **原点**: 左下角 | **X**: 向右 | **Y**: 向上 | **单位**: mm

---

## 4. 布置约束

```
对于每个要放置的模块:
1. bounds 必须完全在 zones[].rawBoundary 内
2. bounds 不能与任何 exclusions[].rawBoundary 重叠
3. bounds 不能与其他已放置 modules[] 重叠
```

---

## 5. 添加布置模块

在对应分区目录的 `modules.json` 中添加模块，示例（主卧 rz_3）：

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

## 6. 常见问题

| 问题 | 答案 |
|------|------|
| 模块放哪个分区？ | 查看 `schemes/zones.json` 找到房间对应的 `rz_*` ID |
| 如何避免与禁区冲突？ | 读取 `computed/exclusions.json`，确保 bounds 不重叠 |
| bounds 顶点顺序？ | 矩形连续顺序：左下→右下→右上→左上 |
| items 可以为空？ | 是，`items: []` 有效，后续由 Server 填充 |

---

*本文档由 BIMCanvas Server 自动生成*