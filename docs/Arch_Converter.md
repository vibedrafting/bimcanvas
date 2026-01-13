# BIMCanvas 转换器架构

> **版本**: v1.0
> **创建日期**: 2026-01-13
> **关联文档**: [Architecture.md](./Architecture.md)

本文档详细描述 BIMCanvas 的几何数据转换器架构，包括设计原则、转换链路、坐标转换规范等核心内容。

---

## 1. 设计原则

### 1.1 Core 层"薄"设计哲学

> **核心定位**：「薄」数据契约 + 语义桥梁

BIMCanvas.Core 的职责边界：

| 类型 | 说明 |
|------|------|
| ✅ 定义通用数据模型 | CanvasDocument、Project 及其子结构 |
| ✅ 实现 AI 语义 → 几何转换 | GeometryNormalizer、FacingHelper |
| ✅ 提供单位转换 | feet ↔ mm, rad ↔ deg |
| ❌ 不做复杂几何运算 | 委托给 NetTopologySuite |
| ❌ 不做浮点精度处理 | 由调用方负责 |

**NuGet 依赖**：
- `Newtonsoft.Json` (13.0.3) - JSON 序列化
- `NetTopologySuite` (2.x) - 几何运算

### 1.2 NTS 中间层的作用

**为什么使用 NTS 作为中间层**：

1. **强大的几何运算**：布尔运算、包围盒计算、空间关系判断
2. **Revit API 能力有限**：原生 API 的几何操作功能不足
3. **解耦便于测试**：NTS 与 BIMCanvas.Core 解耦，便于独立单元测试

```
几何数据类型转换链
┌─────────────────────────────────────────────────────────────────┐
│  阶段           数据类型                    单位    坐标系      │
├─────────────────────────────────────────────────────────────────┤
│  Revit API      XYZ, Solid, CurveLoop       feet   项目坐标     │
│       ↓                                                         │
│  NTS 中间层     Polygon, LineSegment,       feet   项目坐标     │
│                 Coordinate, Vector2D                            │
│       ↓                                                         │
│  Core 层        Polygon2D, Line2D,          mm     归一化坐标   │
│                 Point2D, Facing                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. 转换器分层架构（必须严格遵守）

### 2.1 转换链路

```
转换链路：
Revit API ↔ NTS              (BIMCanvas.Revit/Converters/RevitNtsConverter)
     ↓
NTS (feet) → NTS (mm)        (BIMCanvas.Revit/Services/CoordinateTransformer)
     ↓
NTS ↔ Core.Models            (BIMCanvas.Core/Converters/NtsConverter)

⛔ 禁止：Revit 层直接输出 Core.Models 几何类型
```

### 2.2 转换器职责表

| 转换器 | 位置 | 职责 | 特点 |
|--------|------|------|------|
| `RevitNtsConverter` | Revit/Converters | Revit API ↔ NTS 类型转换 | 静态扩展方法，无状态 |
| `CoordinateTransformer` | Revit/Services | 坐标变换（原点偏移+旋转+单位） | 实例类，有状态 |
| `NtsConverter` | Core/Converters | NTS ↔ Core.Models 类型转换 | 静态类，无状态 |

### 2.3 禁止事项

| 禁止行为 | 原因 |
|----------|------|
| Revit 层直接输出 Core.Models 几何类型 | 破坏分层架构，难以维护 |
| 跳过 NTS 中间层直接转换 | 丢失几何运算能力 |
| 在 Core 层引用 Revit API | 会导致 .NET Standard 兼容性问题 |

### 2.4 自定义中间模型

| 模型 | 文件 | 核心字段 | 用途 |
|------|------|----------|------|
| `RevitBoundary` | Models/ | `Id`, `ElementIds`, `Boundary: Polygon` | 保留元素追溯信息 |
| `RevitOpening` | Models/ | `Id`, `ElementId`, `Type`, `LocationLine`, `FacingDirection`, `HandDirections` | 门窗几何 + 方向信息 |
| `RevitRoom` | Models/ | `Id`, `ElementId`, `Name`, `Boundary: Polygon` | 房间边界 + 名称 |

**设计原则**：保留 Revit 原生数据到最后一刻，便于追溯和调试。

---

## 3. 坐标转换规范

### 3.1 Revit → BIMCanvas 转换公式

```csharp
// Revit 坐标 → BIMCanvas 坐标
dx = revitX - origin.X;
dy = revitY - origin.Y;

// 反向旋转归一化（处理视图旋转）
localX = dx * cos(-rotation) - dy * sin(-rotation);
localY = dx * sin(-rotation) + dy * cos(-rotation);

// 单位转换：feet → mm
x_mm = localX × 304.8;
y_mm = localY × 304.8;
```

**导出流程示例**：

```
【Phase 1: Revit 原始数据】
Revit API (XYZ, Solid, CurveLoop) | feet | Revit项目坐标
    ↓
BoundaryAdapter.ExtractBoundaries() → List<RevitBoundary>
OpeningAdapter.ExtractOpenings()    → List<RevitOpening>
RoomAdapter.ExtractRooms()          → List<RevitRoom>
    ↓
NTS 格式 (Polygon, LineSegment) | feet | Revit项目坐标

【Phase 2: 计算包围盒原点】
所有 NTS Polygon → Envelope.Union() → origin = (MinX, MinY)

【Phase 3: 创建坐标转换器】
new CoordinateTransformer(origin, viewRotation)

【Phase 4: 统一坐标转换】
RevitBoundary (NTS Polygon)   → Boundary (Polygon2D, mm)
RevitOpening (NTS LineSegment) → Opening (Line2D, mm)
RevitRoom (NTS Polygon)        → Room (Polygon2D, mm)

【Phase 5: 用户确认房间类型】
RoomTypeInferrer.InferFromName() → ConfigWindow 确认

【Phase 6: 组装并导出 .bcp】（v3.0 重构）
Baseline { walls, columns, openings, rooms, locationLines } → .bcp (ZIP)
```

### 3.2 前端渲染坐标转换

BIMCanvas 采用 **CAD 标准坐标系**（笛卡尔坐标系），而非 Web 屏幕坐标系：

| 坐标系 | 原点 | Y轴正方向 | 使用场景 |
|--------|------|-----------|----------|
| CAD 标准 | 左下角 | 向上 | Revit、数据模型、几何计算 |
| Web 屏幕 | 左上角 | 向下 | Canvas 渲染、鼠标事件 |

```
┌─────────────────────────────────────────────────────────┐
│  Revit 层 (.NET FW 4.7.2)                               │
│  - 导出原始坐标（Y-up）                                  │
│  - 计算视图裁剪框偏移量                                  │
│  - 存入 metadata.revitMapping.projectBaseOffset         │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Core 层 (.NET Standard 2.0)                            │
│  - 纯笛卡尔坐标运算                                      │
│  - 几何验证、碰撞检测                                    │
│  - 不做任何坐标系转换                                    │
└─────────────────────────────────────────────────────────┘
                           │
                           ▼ JSON (Y-up, mm)
┌─────────────────────────────────────────────────────────┐
│  Web 层 (Vue 3 + TypeScript)                            │
│  - 渲染时进行坐标转换：y_screen = height - y_model      │
│  - 禁止使用 CSS scaleY(-1)                              │
│  - 事件处理时反向转换：y_model = height - y_screen      │
└─────────────────────────────────────────────────────────┘
```

**TypeScript 转换函数**：

```typescript
// 坐标转换函数
function modelToScreen(point: Point2D, canvasHeight: number): ScreenPoint {
  return {
    x: point.x,
    y: canvasHeight - point.y  // Y 轴翻转
  };
}

function screenToModel(screenPoint: ScreenPoint, canvasHeight: number): Point2D {
  return {
    x: screenPoint.x,
    y: canvasHeight - screenPoint.y  // Y 轴反向翻转
  };
}
```

### 3.3 注意事项

> ⚠️ **重要**：禁止使用 CSS `scaleY(-1)` 进行坐标翻转，会导致文字倒置等副作用。
> 必须使用上述显式转换函数。

### 3.4 角度语义规范

BIMCanvas 使用三套角度系统，需注意区分：

| 系统 | 正方向 | 来源 | 使用场景 |
|------|--------|------|----------|
| **数据模型角** | CCW+ | 2D 数学（Y-up） | `rotatePoint2D()`, JSON 存储 |
| **交互角** | CW+ | `atan2(z, x)` | 鼠标拖动计算 |
| **Three.js 角** | CCW+ | `rotation.y` | 3D 渲染预览 |

---

## 4. 几何归一化与布置验证

### 4.1 职责分层

- `GeometryNormalizer`：纯几何转换（AI 意图 → Polygon2D）
- `PlacementValidator`：布置验证（只验证，不修正）

### 4.2 GeometryNormalizer

```csharp
// GeometryNormalizer - AI 布置意图 → Polygon2D
public static class GeometryNormalizer
{
    /// <summary>
    /// 根据 center + size + facing 创建矩形 Polygon2D
    /// </summary>
    public static Polygon2D CreateRectangle(Point2D center, Vec2D size, Facing facing)
    {
        var halfW = size.X / 2;
        var halfH = size.Y / 2;

        // 本地坐标（未旋转）
        var localCorners = new[]
        {
            new Point2D(-halfW, -halfH),
            new Point2D(halfW, -halfH),
            new Point2D(halfW, halfH),
            new Point2D(-halfW, halfH)
        };

        // 根据 facing 计算旋转角度
        var angle = facing.ToAngleRadians();

        // 旋转并平移到世界坐标
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);

        var worldCorners = localCorners.Select(p => new Point2D(
            center.X + p.X * cos - p.Y * sin,
            center.Y + p.X * sin + p.Y * cos
        )).ToArray();

        return new Polygon2D(worldCorners);
    }
}
```

### 4.3 PlacementValidator

```csharp
// PlacementValidator - 布置验证（只验证，不修正）
public static class PlacementValidator
{
    /// <summary>
    /// 验证模块布置是否合法
    /// </summary>
    /// <returns>Result&lt;bool, List&lt;Violation&gt;&gt;</returns>
    public static ValidationResult Validate(
        Polygon2D moduleBounds,
        Zone zone,
        IEnumerable<Module> existingModules)
    {
        var violations = new List<Violation>();

        // 约束1: 必须在 innerBoundary 内
        if (!CollisionDetector.IsWithin(moduleBounds, zone.InnerBoundary))
            violations.Add(new Violation("超出设计区域边界"));

        // 约束2: 不能与禁区重叠
        foreach (var exclusion in zone.ExclusionAreas ?? Enumerable.Empty<ExclusionArea>())
        {
            if (CollisionDetector.Intersects(moduleBounds, exclusion.Boundary))
                violations.Add(new Violation($"与禁区 {exclusion.Id} 重叠"));
        }

        // 约束3: 不能与其他模块重叠
        foreach (var existing in existingModules)
        {
            if (CollisionDetector.Intersects(moduleBounds, existing.Bounds))
                violations.Add(new Violation($"与模块 {existing.Id} 重叠"));
        }

        return violations.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(violations);
    }
}
```

### 4.4 关键设计原则

| 原则 | 说明 |
|------|------|
| `PlacementValidator` **只做 Validation** | 返回验证结果，不修改输入数据 |
| **不做 Correction** | 「床头靠墙」是 AI 的规划职责，不是 Core 的修正职责 |
| 未来吸附功能 | 单独创建 `SnapHelper` 或 `ConstraintSolver` |

---

## 5. 关键工具类

| 工具类 | 文件 | 职责 |
|--------|------|------|
| `RevitNtsConverter` | Converters/ | Revit API ↔ NTS 类型转换扩展 |
| `NtsConverter` | Core/Converters/ | NTS ↔ Core.Models 类型转换 |
| `OutlineExtractor` | Utilities/ | 在指定高度切割几何体，提取轮廓 |
| `OpeningDirectionAnalyzer` | Utilities/ | 分析门窗开启方向（通过 IFC 导出工具） |
| `PrefixId` | Utilities/ | 生成带前缀的顺序 ID |
| `TransactionHelper` | Utilities/ | 统一事务失败处理 |

---

## 6. 设计决策总结

| 决策项 | 选择 | 理由 |
|--------|------|------|
| **MathHelper/Epsilon** | 不需要 | Core 层保持简单，精度问题由 NTS 处理 |
| **PolygonOperations** | 不实现 | 复杂几何运算委托给 NTS |
| **NtsAdapter 可见性** | `internal` | 不污染公共 API |
| **PlacementValidator** | 只验证，不修正 | 「修正」是 AI 的规划职责 |
