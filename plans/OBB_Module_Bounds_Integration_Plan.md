# OBB 模块轮廓表达与 Web 渲染方案

## 问题背景

### 当前架构冲突

| 层级 | 期望格式 | 实际情况 |
|------|---------|---------|
| **AI Agent 输出** | OBB 格式 `{center, size, rotation}` | ✅ layout-agent.md 示例使用此格式 |
| **Module.Bounds 定义** | `Polygon2D?` 类型 | ❌ 反序列化 OBB 失败 |
| **Web 端渲染** | `Polygon2D` 数组 `[[x,y], ...]` | ✅ 已实现完整渲染流程 |

### 用户核心疑问

1. **OBB 如何表达模块轮廓**：目前没有显式 OBB 类，如何存储和转换？
2. **Web 端如何正确渲染**：从 AI 输出到屏幕显示的完整流程是什么？

---

## 架构现状分析

### 发现 1：BIMCanvas 已有 GeometryNormalizer

**位置**：`BIMCanvas.Core/Algorithms/Spatial/GeometryNormalizer.cs`

```csharp
public static class GeometryNormalizer
{
    /// <summary>
    /// 从 OBB 参数创建矩形边界（4 顶点）
    /// </summary>
    public static Polygon2D CreateModuleBounds(
        double centerX,
        double centerY,
        double width,      // 沿朝向方向
        double depth,      // 垂直于朝向方向
        Facing facing)
    {
        var center = new Point2D(centerX, centerY);
        var size = new Vec2D(width, depth);

        // 内部调用 CreateRectangle
        return CreateRectangle(center, size, facing);
    }
}
```

**关键特性**：
- ✅ 已支持从 OBB 概念（中心点 + 尺寸 + 朝向）生成 Polygon2D
- ✅ 自动处理旋转变换（绕中心点旋转到目标朝向）
- ✅ 输出：逆时针 4 顶点数组 `[[左下], [右下], [右上], [左上]]`

### 发现 2：Web 端完整渲染流程

**位置**：`BIMCanvas.Web/src/services/builders/SceneBuilder.ts`

```typescript
// 步骤 1：从 JSON 读取 bounds
const module: Module = {
  bounds: [[2500,2200], [3500,2200], [3500,2800], [2500,2800]],  // Polygon2D
  facing: "north",
  // ...
};

// 步骤 2：创建 2D Shape
const shape = createShapeFromPolygon(module.bounds);

// 步骤 3：挤压成 3D 几何体
const geometry = new THREE.ExtrudeGeometry(shape, { depth: 750 });

// 步骤 4：创建网格并添加到场景
const mesh = new THREE.Mesh(geometry, materialModule);
mesh.rotation.x = -Math.PI / 2;  // XY → XZ 平面
scene.add(mesh);
```

**渲染特性**：
- ✅ 支持任意多边形（不限于矩形）
- ✅ 自动处理旋转（通过 Polygon2D 顶点已包含旋转信息）
- ✅ 朝向箭头独立渲染（读取 facing 字段）

### 发现 3：核心问题是数据模型不匹配

**症结**：
- AI 输出：`{center: [x,y], size: [w,h], rotation: θ}`（OBB 格式）
- Module.Bounds：期望 `Polygon2D?` 类型
- 反序列化器：Polygon2DConverter 无法解析 OBB 格式

---

## 解决方案设计

### 方案对比

| 方案 | 优点 | 缺点 | 推荐度 |
|------|------|------|--------|
| **A：创建 OBB 类 + 自定义转换器** | ✅ 符合架构原则"AI=OBB规划师"<br>✅ Agent 输出简洁<br>✅ 类型安全 | ⚠️ 需修改 Module.Bounds 类型<br>⚠️ 需实现 OBB→Polygon2D 转换 | ⭐⭐⭐⭐⭐ |
| **B：让 Agent 计算 Polygon2D** | ✅ 无需修改 Core 层<br>✅ 快速验证 | ❌ 违反架构原则<br>❌ AI 计算几何易出错<br>❌ Prompt 复杂 | ⭐⭐ |
| **C：Server 中间层转换** | ✅ 保持 Agent 简单<br>✅ 集中式验证 | ⚠️ 需要 DTO 分离<br>⚠️ 双重序列化 | ⭐⭐⭐ |

### 推荐方案：A（创建 OBB 类）

**设计理念**：
- **AI → OBB**：layout-agent 只输出 center, size, facing
- **Core → Polygon2D**：GeometryNormalizer 转换为精确几何
- **Web → 3D Mesh**：ExtrudeGeometry 渲染为 3D 模型

---

## 实施计划：方案 A

### 步骤 1：创建 OBB 类

**文件**：`BIMCanvas.Core/Models/Geometry/OBB.cs`（新建）

```csharp
using System;
using Newtonsoft.Json;

namespace BIMCanvas.Core.Models.Geometry
{
    /// <summary>
    /// Oriented Bounding Box（定向包围盒）
    /// AI 输出的布置意图表示：中心点 + 尺寸 + 旋转角
    /// </summary>
    public class OBB
    {
        /// <summary>
        /// 中心点坐标 [x, y]（毫米）
        /// </summary>
        [JsonProperty("center")]
        public Point2D Center { get; set; }

        /// <summary>
        /// 尺寸 [width, depth]（毫米）
        /// width: 沿朝向方向的长度
        /// depth: 垂直于朝向方向的长度
        /// </summary>
        [JsonProperty("size")]
        public Vec2D Size { get; set; }

        /// <summary>
        /// 旋转角（度）
        /// 0° = 北方（Y+）, 90° = 东方（X+）
        /// 逆时针为正
        /// </summary>
        [JsonProperty("rotation")]
        public double Rotation { get; set; }

        /// <summary>
        /// 构造函数
        /// </summary>
        public OBB(Point2D center, Vec2D size, double rotation = 0)
        {
            Center = center;
            Size = size;
            Rotation = rotation;
        }

        /// <summary>
        /// 转换为精确边界（4 顶点多边形）
        /// </summary>
        public Polygon2D ToPolygon2D()
        {
            // 1. 创建朝北矩形（rotation=0）
            var halfW = Size.X / 2;
            var halfD = Size.Y / 2;
            var vertices = new Point2D[]
            {
                new Point2D(Center.X - halfW, Center.Y - halfD),  // 左下
                new Point2D(Center.X + halfW, Center.Y - halfD),  // 右下
                new Point2D(Center.X + halfW, Center.Y + halfD),  // 右上
                new Point2D(Center.X - halfW, Center.Y + halfD)   // 左上
            };

            if (Math.Abs(Rotation) < 1e-6)
            {
                return new Polygon2D(vertices);  // 无旋转，直接返回
            }

            // 2. 旋转到目标角度
            var angleRad = Rotation * Math.PI / 180.0;
            var cos = Math.Cos(angleRad);
            var sin = Math.Sin(angleRad);

            var rotatedVertices = new Point2D[4];
            for (int i = 0; i < 4; i++)
            {
                var dx = vertices[i].X - Center.X;
                var dy = vertices[i].Y - Center.Y;
                rotatedVertices[i] = new Point2D(
                    Center.X + dx * cos - dy * sin,
                    Center.Y + dx * sin + dy * cos
                );
            }

            return new Polygon2D(rotatedVertices);
        }

        /// <summary>
        /// 从 Facing 创建 OBB（使用语义朝向）
        /// </summary>
        public static OBB FromFacing(Point2D center, Vec2D size, Facing facing)
        {
            var angle = FacingHelper.FacingToAngle(facing);  // 假设有此辅助方法
            return new OBB(center, size, angle);
        }
    }
}
```

### 步骤 2：修改 Module.Bounds 类型

**文件**：`BIMCanvas.Core/Models/Layout/Module.cs`

```csharp
public class Module
{
    // ... 其他字段

    /// <summary>
    /// 模块边界
    /// - OBB：AI 输出的简化表示（center, size, rotation）
    /// - Polygon2D：精确边界（转换后，用于验证和渲染）
    /// </summary>
    [JsonProperty("bounds")]
    [JsonConverter(typeof(ModuleBoundsConverter))]  // 自定义转换器
    public object Bounds { get; set; }  // 可以是 OBB 或 Polygon2D

    /// <summary>
    /// 获取精确边界（用于碰撞检测）
    /// </summary>
    [JsonIgnore]
    public Polygon2D PreciseBounds
    {
        get
        {
            if (Bounds is Polygon2D polygon)
                return polygon;
            if (Bounds is OBB obb)
                return obb.ToPolygon2D();
            throw new InvalidOperationException("Bounds must be OBB or Polygon2D");
        }
    }

    // ... 其他字段
}
```

### 步骤 3：实现 ModuleBoundsConverter

**文件**：`BIMCanvas.Core/Converters/Json/ModuleBoundsConverter.cs`（新建）

```csharp
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using BIMCanvas.Core.Models.Geometry;

namespace BIMCanvas.Core.Converters.Json
{
    /// <summary>
    /// Module.Bounds 双格式转换器
    /// 读取：支持 OBB 或 Polygon2D
    /// 写入：保持原始格式
    /// </summary>
    public class ModuleBoundsConverter : JsonConverter<object>
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            // 1. 数组格式 → Polygon2D
            if (token.Type == JTokenType.Array)
            {
                var points = token.ToObject<Point2D[]>(serializer);
                return new Polygon2D(points!);
            }

            // 2. 对象格式
            if (token.Type == JTokenType.Object)
            {
                var obj = (JObject)token;

                // 2.1 检查是否为 Polygon2D 完整格式 {"shell": [...], "holes": [...]}
                if (obj.ContainsKey("shell"))
                {
                    return obj.ToObject<Polygon2D>(serializer)!;
                }

                // 2.2 检查是否为 OBB 格式 {"center": [...], "size": [...], "rotation": ...}
                if (obj.ContainsKey("center") && obj.ContainsKey("size"))
                {
                    return obj.ToObject<OBB>(serializer)!;
                }

                throw new JsonException("Bounds object must have 'shell' (Polygon2D) or 'center+size' (OBB) properties");
            }

            throw new JsonException($"Unexpected token type for Bounds: {token.Type}");
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is Polygon2D polygon)
            {
                // Polygon2D → 简单数组或完整格式
                if (!polygon.HasHoles)
                {
                    serializer.Serialize(writer, polygon.Vertices);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WritePropertyName("shell");
                    serializer.Serialize(writer, polygon.Vertices);
                    writer.WritePropertyName("holes");
                    serializer.Serialize(writer, polygon.Holes);
                    writer.WriteEndObject();
                }
            }
            else if (value is OBB obb)
            {
                // OBB → {"center", "size", "rotation"}
                writer.WriteStartObject();
                writer.WritePropertyName("center");
                serializer.Serialize(writer, obb.Center);
                writer.WritePropertyName("size");
                serializer.Serialize(writer, obb.Size);
                writer.WritePropertyName("rotation");
                writer.WriteValue(obb.Rotation);
                writer.WriteEndObject();
            }
            else
            {
                throw new JsonSerializationException($"Unsupported Bounds type: {value?.GetType()}");
            }
        }
    }
}
```

### 步骤 4：更新 layout-agent.md 示例

**文件**：`C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md`

```markdown
## 输出格式（modules.json）
**重要**：modules.json 是直接的数组，不需要 `{"modules": [...]}` 包装。

**Bounds 格式**：支持 OBB（推荐）或 Polygon2D。

### OBB 格式（推荐）

```json
[{
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
}]
```

**字段说明**：
- `bounds.center`: 模块中心点 [x, y]（毫米）
- `bounds.size`: 尺寸 [width, depth]（毫米）
  - width: 沿朝向方向的长度
  - depth: 垂直于朝向方向的长度
- `bounds.rotation`: 旋转角（度），0° = 北方，逆时针为正

### Polygon2D 格式（高级）

```json
[{
  "id": "m_1",
  "moduleId": "mod_bed_001",
  "zoneId": "rz_3",
  "bounds": [[2500,2200], [3500,2200], [3500,2800], [2500,2800]],
  "facing": "north",
  "items": []
}]
```

**使用场景**：
- OBB：常规矩形家具（推荐）
- Polygon2D：不规则形状或预计算轮廓
```

### 步骤 5：Server 层验证更新

**文件**：`BIMCanvas.Server/Services/PlacementService.cs`（修改）

```csharp
public ValidationResult ValidateModule(Module module, ProjectData project)
{
    // 1. 获取精确边界（自动转换 OBB → Polygon2D）
    Polygon2D preciseBounds;
    try
    {
        preciseBounds = module.PreciseBounds;
    }
    catch (Exception ex)
    {
        return ValidationResult.Failure($"Invalid bounds format: {ex.Message}");
    }

    // 2. 验证边界（使用 preciseBounds）
    var zone = project.Computed.RoomZones.FirstOrDefault(z => z.Id == module.ZoneId);
    if (zone == null)
    {
        return ValidationResult.Failure($"Zone '{module.ZoneId}' not found");
    }

    // 3. 碰撞检测
    if (!CollisionDetector.IsWithin(preciseBounds, zone.ComputedBoundary))
    {
        return ValidationResult.Failure("Module out of zone bounds");
    }

    // 4. 禁区检查
    foreach (var exclusion in project.Computed.ExclusionAreas)
    {
        if (CollisionDetector.Overlaps(preciseBounds, exclusion.Boundary))
        {
            return ValidationResult.Failure($"Overlaps with exclusion area '{exclusion.Id}'");
        }
    }

    return ValidationResult.Success();
}
```

---

## Web 端渲染流程（完整）

### 数据流向

```
1. AI Agent 输出
   ↓
   JSON: { bounds: { center: [x,y], size: [w,h], rotation: θ } }

2. Server 反序列化
   ↓
   ModuleBoundsConverter 识别为 OBB
   ↓
   Module.Bounds = new OBB(...)

3. Server 验证
   ↓
   module.PreciseBounds 触发 OBB.ToPolygon2D()
   ↓
   CollisionDetector.IsWithin(preciseBounds, zoneBoundary)

4. 持久化到文件
   ↓
   ModuleBoundsConverter.WriteJson() → 保持 OBB 格式
   ↓
   schemes/modules.json

5. Web 端加载
   ↓
   fetch('/api/project/data')
   ↓
   canvasStore.projectData.activeScheme.modules[]

6. ThreeSceneService 监听
   ↓
   watch(projectData) → SceneBuilder.buildFromDocument()

7. 渲染模块
   ↓
   createModuleMesh(module) {
     // 7.1 如果是 OBB，先转换
     let bounds = module.bounds;
     if (bounds.center && bounds.size) {
       bounds = convertOBBToPolygon(bounds);  // 前端转换
     }

     // 7.2 创建 Shape
     const shape = createShapeFromPolygon(bounds);

     // 7.3 挤压成 3D
     const geometry = new THREE.ExtrudeGeometry(shape, { depth: 750 });

     // 7.4 创建 Mesh
     const mesh = new THREE.Mesh(geometry, materialModule);
     mesh.rotation.x = -Math.PI / 2;
     scene.add(mesh);
   }

8. 朝向箭头
   ↓
   createFacingArrow(module) {
     const angle = facingToAngle(module.facing);
     arrow.position.set(centerX, centerY, 0);
     arrow.rotation.z = -angle;
   }

9. 最终渲染
   ↓
   renderer.render(scene, camera)
   ↓
   屏幕显示：3D 模型 + 朝向箭头
```

### 关键转换函数

#### 前端 OBB → Polygon2D

**文件**：`BIMCanvas.Web/src/utils/geometry.ts`（新建）

```typescript
export interface OBB {
  center: Point2D;
  size: Point2D;     // [width, depth]
  rotation: number;  // 度数
}

/**
 * OBB → Polygon2D（4 顶点矩形）
 */
export function convertOBBToPolygon(obb: OBB): Polygon2D {
  const [cx, cy] = obb.center;
  const [w, h] = obb.size;
  const angleRad = obb.rotation * Math.PI / 180;

  // 1. 创建朝北矩形
  const halfW = w / 2;
  const halfH = h / 2;
  const vertices: Point2D[] = [
    [cx - halfW, cy - halfH],  // 左下
    [cx + halfW, cy - halfH],  // 右下
    [cx + halfW, cy + halfH],  // 右上
    [cx - halfW, cy + halfH]   // 左上
  ];

  // 2. 旋转到目标角度
  if (Math.abs(obb.rotation) < 0.001) {
    return vertices;  // 无旋转
  }

  const cos = Math.cos(angleRad);
  const sin = Math.sin(angleRad);

  return vertices.map(([x, y]) => {
    const dx = x - cx;
    const dy = y - cy;
    return [
      cx + dx * cos - dy * sin,
      cy + dx * sin + dy * cos
    ];
  });
}
```

#### SceneBuilder 适配

```typescript
private createModuleMesh(mod: Module) {
    // 1. 获取 bounds（支持双格式）
    let boundsPolygon: Polygon2D;

    if (Array.isArray(mod.bounds)) {
        // Polygon2D 格式
        boundsPolygon = mod.bounds;
    } else if (mod.bounds.center && mod.bounds.size) {
        // OBB 格式 → 转换
        boundsPolygon = convertOBBToPolygon(mod.bounds as OBB);
    } else {
        console.error('Invalid bounds format:', mod.bounds);
        return;
    }

    // 2. 创建 Shape（后续流程不变）
    const shape = this.createShapeFromPolygon(boundsPolygon);
    const geometry = new THREE.ExtrudeGeometry(shape, { depth: 750 });
    const mesh = new THREE.Mesh(geometry, this.materials.get('module'));
    // ...
}
```

---

## 关键文件清单

### 需要新建的文件

| 文件 | 路径 | 内容 |
|------|------|------|
| OBB 类 | `BIMCanvas.Core/Models/Geometry/OBB.cs` | OBB 数据模型 + ToPolygon2D() |
| Bounds 转换器 | `BIMCanvas.Core/Converters/Json/ModuleBoundsConverter.cs` | 双格式序列化/反序列化 |
| 几何工具 | `BIMCanvas.Web/src/utils/geometry.ts` | convertOBBToPolygon() 前端转换 |

### 需要修改的文件

| 文件 | 路径 | 修改内容 |
|------|------|---------|
| Module 模型 | `BIMCanvas.Core/Models/Layout/Module.cs` | Bounds 类型改为 object + PreciseBounds 属性 |
| layout-agent 配置 | `C:\Users\huhaonan\Documents\BIMCanvas\agents\layout-agent.md` | 更新输出格式示例（OBB） |
| PlacementService | `BIMCanvas.Server/Services/PlacementService.cs` | 使用 PreciseBounds 验证 |
| SceneBuilder | `BIMCanvas.Web/src/services/builders/SceneBuilder.ts` | 支持 OBB/Polygon2D 双格式 |

### 测试验证文件

| 文件 | 路径 | 用途 |
|------|------|------|
| 测试项目 | `C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1\schemes\modules.json` | 放置测试数据 |

---

## 验证测试计划

### 测试 1：OBB → Polygon2D 转换

```csharp
// Core 层单元测试
[Test]
public void TestOBBToPolygon2D()
{
    var obb = new OBB(
        center: new Point2D(1000, 2000),
        size: new Vec2D(800, 600),
        rotation: 45
    );

    var polygon = obb.ToPolygon2D();

    Assert.AreEqual(4, polygon.Vertices.Length);
    // 验证中心点
    var center = polygon.ComputeCenter();
    Assert.AreEqual(1000, center.X, 0.1);
    Assert.AreEqual(2000, center.Y, 0.1);
}
```

### 测试 2：JSON 序列化/反序列化

```csharp
[Test]
public void TestModuleBoundsSerialization()
{
    var module = new Module
    {
        Id = "m_1",
        Bounds = new OBB(
            new Point2D(3000, 4000),
            new Vec2D(1800, 2000),
            rotation: 0
        )
    };

    // 序列化
    var json = JsonConvert.SerializeObject(module);
    Console.WriteLine(json);

    // 反序列化
    var deserialized = JsonConvert.DeserializeObject<Module>(json);
    Assert.IsInstanceOf<OBB>(deserialized.Bounds);

    var obb = (OBB)deserialized.Bounds;
    Assert.AreEqual(3000, obb.Center.X);
    Assert.AreEqual(1800, obb.Size.X);
}
```

### 测试 3：Agent 输出验证

```json
// 创建测试文件：schemes/modules.json
[{
  "id": "m_test",
  "moduleId": "mod_sofa_001",
  "zoneId": "rz_6",
  "bounds": {
    "center": [5000, 3000],
    "size": [2000, 900],
    "rotation": 0
  },
  "facing": "north",
  "items": []
}]
```

**验证步骤**：
1. 重启 Server（加载新 JSON）
2. 打开 Web 界面（http://localhost:5173）
3. 检查是否正确渲染沙发（位置、尺寸、朝向）
4. 使用 F12 检查 console 是否有错误

### 测试 4：Web 端渲染

```typescript
// BIMCanvas.Web/src/services/builders/SceneBuilder.ts
private createModuleMesh(mod: Module) {
    console.log('Rendering module:', mod.id);
    console.log('Bounds format:', mod.bounds);

    let boundsPolygon: Polygon2D;
    if (Array.isArray(mod.bounds)) {
        console.log('Using Polygon2D format');
        boundsPolygon = mod.bounds;
    } else if (mod.bounds.center) {
        console.log('Converting OBB to Polygon2D');
        boundsPolygon = convertOBBToPolygon(mod.bounds);
        console.log('Converted:', boundsPolygon);
    }

    // ... 后续渲染
}
```

---

## 回答用户问题

### Q1：OBB 类如何表达布置模块的轮廓？

**答**：通过 `OBB.ToPolygon2D()` 方法转换为精确轮廓：

```
OBB { center: [3000, 4000], size: [1800, 2000], rotation: 0 }
         ↓
    ToPolygon2D()
         ↓
Polygon2D: [[2100,3000], [3900,3000], [3900,5000], [2100,5000]]
         ↓
    4 个顶点描述矩形轮廓（逆时针）
```

**工作原理**：
1. 以 center 为中心创建轴对齐矩形（rotation=0 时朝北）
2. 如果 rotation ≠ 0，绕中心点旋转所有顶点
3. 输出：4 个 Point2D 构成的闭合多边形

### Q2：Web 端如何正确渲染 AI 布置的家具位置和轮廓？

**答**：完整渲染流程（8 个步骤）：

```
1. Agent 输出 OBB
   ↓
2. Server 反序列化（ModuleBoundsConverter）
   ↓
3. Server 验证（OBB → Polygon2D）
   ↓
4. 持久化到 modules.json（保持 OBB 格式）
   ↓
5. Web 端加载 JSON
   ↓
6. SceneBuilder 转换（OBB → Polygon2D，如果需要）
   ↓
7. Three.js 渲染
   - Polygon2D → THREE.Shape → ExtrudeGeometry → Mesh
   - rotation.x = -π/2（XY → XZ 平面）
   - 添加朝向箭头（facingToAngle）
   ↓
8. 屏幕显示：3D 模型 + 包围盒 + 箭头
```

**关键技术**：
- **坐标系统**：数据模型 [x,y] → 渲染坐标 Vector3(x, 0, -y)
- **挤压**：ExtrudeGeometry 在 Z 轴方向挤压 750mm
- **旋转**：通过 Polygon2D 顶点包含旋转信息，无需额外处理
- **朝向**：独立渲染箭头，rotation.z = -facingAngle

---

## 实施优先级

### 阶段 1：Core 层基础（高优先级）

- [ ] 创建 OBB.cs
- [ ] 创建 ModuleBoundsConverter.cs
- [ ] 修改 Module.cs（Bounds 类型 + PreciseBounds）
- [ ] 单元测试（OBB 转换 + JSON 序列化）

### 阶段 2：layout-agent 适配（中优先级）

- [ ] 更新 layout-agent.md 输出格式示例
- [ ] 测试 Agent 输出（手动创建测试 JSON）

### 阶段 3：Web 端适配（中优先级）

- [ ] 创建 geometry.ts（convertOBBToPolygon）
- [ ] 修改 SceneBuilder.createModuleMesh()
- [ ] 浏览器验证渲染

### 阶段 4：Server 层验证（低优先级）

- [ ] 修改 PlacementService（使用 PreciseBounds）
- [ ] 端到端测试

---

## 架构优势总结

| 层级 | 表达方式 | 优势 |
|------|---------|------|
| **AI Agent** | OBB {center, size, rotation} | ✅ 简洁、符合人类直觉<br>✅ 减少计算错误 |
| **Core 层** | OBB + Polygon2D 双格式 | ✅ 类型安全<br>✅ 自动转换<br>✅ 向后兼容 |
| **Server 层** | 统一使用 PreciseBounds | ✅ 精确碰撞检测<br>✅ 验证可靠 |
| **Web 层** | Polygon2D → ExtrudeGeometry | ✅ 支持任意形状<br>✅ 渲染灵活 |

**核心设计原则**：
- **AI = 高层决策**：只关心"在哪里、多大、朝哪"
- **Core = 几何引擎**：负责精确计算和转换
- **Web = 可视化**：呈现最终结果

---

**计划状态**：✅ 设计完成，等待执行实施
