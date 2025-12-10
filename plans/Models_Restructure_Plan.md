# BIMCanvas.Core.Models 目录结构重组方案

> **目标**：按数据来源/用途重新组织 Models 目录，提升代码可读性和维护性

---

## 1. 当前结构

```
Models/
├── Document/              (所有数据模型混在一起，共18个文件)
│   ├── CanvasDocument.cs, Metadata.cs, CoordinateTransform.cs
│   ├── Wall.cs, Column.cs, Opening.cs, Room.cs, FinishLocationBoundary.cs
│   ├── Zone.cs, WallFinish.cs, ExclusionArea.cs
│   ├── Module.cs, ModuleItem.cs, Facing.cs, FacingDirection.cs
│   └── RoomType.cs, ZoneTag.cs, FinishSource.cs (枚举)
└── Primitives/            (几何基元，保持不变)
    └── Point2D.cs, Vec2D.cs, Line2D.cs, Polygon2D.cs, AABB.cs
```

**问题**：Document 目录下文件过多，不同来源/用途的数据混在一起

---

## 2. 目标结构

```
Models/
├── Primitives/            【基础几何】保持不变
│   └── Point2D.cs, Vec2D.cs, Line2D.cs, Polygon2D.cs, AABB.cs
│
├── RevitSource/           【Revit提取的原始数据 + 文档根对象】
│   ├── CanvasDocument.cs  文档根对象
│   ├── Metadata.cs        导出元数据（含坐标变换参数，合并自CoordinateTransform）
│   ├── Wall.cs            墙体轮廓（含ElementId）
│   ├── Column.cs          柱子轮廓（含ElementId、IsStructural）
│   ├── Opening.cs         门窗数据（定位线、方向）
│   ├── Room.cs            房间边界
│   └── FinishLocationBoundary.cs  完成面定位边界
│
├── CanvasData/            【画布独有数据/Server计算】
│   ├── Zone.cs            设计区域（Server划分）
│   ├── WallFinish.cs      墙面完成面（Server生成）
│   └── ExclusionArea.cs   禁区（Server生成）
│
├── AIInput/               【AI输入数据】（预留，暂空）
│   └── (空目录，后续AI数据处理后填充)
│
├── AIOutput/              【AI输出数据】（预留，暂空）
│   └── (空目录，后续AI数据处理后填充)
│
├── RevitWriteback/        【Revit回写数据】
│   ├── Module.cs          布置模块（回写为FamilyInstance）
│   ├── ModuleItem.cs      模块内家具
│   ├── Facing.cs          朝向（联合类型）
│   └── FacingDirection.cs 朝向枚举
│
└── Shared/                【共享枚举】
    ├── RoomType.cs        房间类型枚举
    ├── ZoneTag.cs         区域标签枚举
    └── FinishSource.cs    完成面来源枚举
```

**关于 AI 数据预留**：
- AIInput/AIOutput 暂为空目录，保留命名空间
- 原因：给到 AI 的数据需要经过处理（非直接使用 RevitSource/CanvasData）
- 原因：AI 输出也需经过处理才能渲染或做 Revit 回写

---

## 3. 分组逻辑说明

| 分组 | 命名空间 | 数据来源 | 数据流向 |
|------|----------|----------|----------|
| **Primitives** | `.Models.Primitives` | 基础类型 | 被所有模型使用 |
| **RevitSource** | `.Models.RevitSource` | Revit插件导出 | 文档根对象 + 构件数据 |
| **CanvasData** | `.Models.CanvasData` | Server计算生成 | AI输入约束 |
| **AIInput** | `.Models.AIInput` | (预留) | 处理后给AI的数据 |
| **AIOutput** | `.Models.AIOutput` | (预留) | AI输出的原始结果 |
| **RevitWriteback** | `.Models.RevitWriteback` | AI输出经处理 | Revit插件回写 |
| **Shared** | `.Models.Shared` | 共享枚举 | 跨模块共享 |

---

## 4. 文件改动清单

### 4.1 移动文件

| 原路径 | 新路径 | 说明 |
|--------|--------|------|
| `Document/CanvasDocument.cs` | `RevitSource/CanvasDocument.cs` | 文档根对象 |
| `Document/Metadata.cs` | `RevitSource/Metadata.cs` | 合并 CoordinateTransform |
| `Document/Wall.cs` | `RevitSource/Wall.cs` | |
| `Document/Column.cs` | `RevitSource/Column.cs` | |
| `Document/Opening.cs` | `RevitSource/Opening.cs` | |
| `Document/Room.cs` | `RevitSource/Room.cs` | |
| `Document/FinishLocationBoundary.cs` | `RevitSource/FinishLocationBoundary.cs` | |
| `Document/Zone.cs` | `CanvasData/Zone.cs` | |
| `Document/WallFinish.cs` | `CanvasData/WallFinish.cs` | |
| `Document/ExclusionArea.cs` | `CanvasData/ExclusionArea.cs` | |
| `Document/Module.cs` | `RevitWriteback/Module.cs` | |
| `Document/ModuleItem.cs` | `RevitWriteback/ModuleItem.cs` | |
| `Document/Facing.cs` | `RevitWriteback/Facing.cs` | |
| `Document/FacingDirection.cs` | `RevitWriteback/FacingDirection.cs` | |
| `Document/RoomType.cs` | `Shared/RoomType.cs` | |
| `Document/ZoneTag.cs` | `Shared/ZoneTag.cs` | |
| `Document/FinishSource.cs` | `Shared/FinishSource.cs` | |

### 4.2 合并文件

将 `CoordinateTransform.cs` 的内容合并到 `Metadata.cs` 中：

```csharp
// 合并后 Metadata.cs（移除嵌套 CoordinateTransform，字段直接展开）
namespace BIMCanvas.Core.Models.RevitSource
{
    public class Metadata
    {
        /// <summary>布置高度（毫米），家具回写时使用</summary>
        public double PlacementElevation { get; set; } = 0;

        // === 坐标变换参数（原 CoordinateTransform 类） ===

        /// <summary>坐标原点在 Revit 项目坐标系中的位置（毫米）</summary>
        public double[] Origin { get; set; } = new double[3]; // [x, y, z]

        /// <summary>视图旋转角度（弧度）</summary>
        public double Rotation { get; set; }

        /// <summary>原点计算方法："boundingBox" 或 "cropBox"</summary>
        public string Method { get; set; } = "boundingBox";
    }
}
```

### 4.3 删除文件

| 文件 | 原因 |
|------|------|
| `Document/CoordinateTransform.cs` | 合并到 Metadata.cs |

### 4.4 创建空目录

| 目录 | 说明 |
|------|------|
| `Models/AIInput/` | 预留，暂空 |
| `Models/AIOutput/` | 预留，暂空 |

### 4.5 命名空间变更

| 原命名空间 | 新命名空间 |
|------------|------------|
| `BIMCanvas.Core.Models.Document` | `BIMCanvas.Core.Models.RevitSource` |
| `BIMCanvas.Core.Models.Document` | `BIMCanvas.Core.Models.CanvasData` |
| `BIMCanvas.Core.Models.Document` | `BIMCanvas.Core.Models.RevitWriteback` |
| `BIMCanvas.Core.Models.Document` | `BIMCanvas.Core.Models.Shared` |

### 4.6 删除目录

- `Models/Document/` （移动完成后删除空目录）

---

## 5. 引用更新

移动文件后需要更新以下位置的 using 语句：

### 5.1 BIMCanvas.Core 内部

| 文件 | 需要更新的 using |
|------|------------------|
| `CanvasDocument.cs` | 需要引用所有新命名空间 |
| `Zone.cs` | 引用 `RevitSource`（Opening引用）、`Shared`（枚举） |
| `WallFinish.cs` | 引用 `Shared`（FinishSource枚举） |
| `Module.cs` | 引用 `AIOutput`（Facing）、`Primitives` |

### 5.2 BIMCanvas.Revit 层

| 文件 | 需要更新的 using |
|------|------------------|
| `CanvasExportService.cs` | 引用 `RevitSource`、`CanvasData`、`RevitWriteback`、`Shared` |
| `ExportCanvasCommand.cs` | 引用 `RevitSource`（CanvasDocument） |

---

## 6. 实施步骤

### Step 1: 创建新目录结构
```
mkdir Models/RevitSource
mkdir Models/CanvasData
mkdir Models/AIInput
mkdir Models/AIOutput
mkdir Models/RevitWriteback
mkdir Models/Shared
```

### Step 2: 移动文件并更新命名空间
按分组移动文件，同时更新每个文件的 `namespace` 声明

### Step 3: 合并 CoordinateTransform 到 Metadata
将 CoordinateTransform.cs 的字段展开到 Metadata.cs，删除 CoordinateTransform.cs

### Step 4: 更新 CanvasDocument.cs 引用
添加对新命名空间的 using 语句

### Step 5: 更新 BIMCanvas.Revit 引用
更新 CanvasExportService.cs 等文件的 using 语句

### Step 6: 删除旧目录
```
rmdir Models/Document
```

### Step 7: 编译验证
```
dotnet build BIMCanvas.Core
MSBuild BIMCanvas.Revit
```

---

## 7. 用户已确认事项

| 问题 | 用户选择 |
|------|----------|
| AI数据分组方式 | 按数据来源分（Zone放CanvasData） |
| Revit回写数据 | 单独分组 RevitWriteback（Module/ModuleItem放此处） |
| 目录命名风格 | 英文 PascalCase |
