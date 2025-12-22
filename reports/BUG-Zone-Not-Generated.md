# BUG 报告：Zone 数据未生成

**报告日期**：2025-12-22
**严重程度**：Critical
**状态**：Open
**影响范围**：Zone 生成与渲染全流程

---

## 1. 问题现象

- Web 端 "Zones" 图层显示为空
- Export Data 导出的 JSON 中 `computed.zones = []`
- 即使 `revit.rooms` 包含 6 个房间数据，Zone 仍未生成

---

## 2. 根本原因

### **JSON 序列化库不兼容**

| 组件 | 使用的 JSON 库 | 配置位置 |
|------|---------------|----------|
| **BIMCanvas.Core** | `Newtonsoft.Json` | 各 Model 类的 `[JsonConverter]` 属性 |
| **BIMCanvas.Server** | `System.Text.Json` | `Program.cs:10` |

**核心问题**：`System.Text.Json` **不识别** `Newtonsoft.Json` 的 `[JsonConverter]` 属性。

### 受影响的类型

以下类型在 Core 层使用了 `Newtonsoft.Json` 的自定义 Converter：

| 类型 | 文件 | Converter |
|------|------|-----------|
| `Point2D` | `Models/Geometry/Point2D.cs:9` | `Point2DConverter` |
| `Vec2D` | `Models/Geometry/Vec2D.cs:10` | `Vec2DConverter` |
| `Line2D` | `Models/Geometry/Line2D.cs:10` | `Line2DConverter` |
| `Polygon2D` | `Models/Geometry/Polygon2D.cs:15` | `Polygon2DConverter` |
| `AABB` | `Models/Geometry/AABB.cs:9` | `AABBConverter` |
| `Facing` | `Models/Semantic/Facing.cs:11` | `FacingConverter` |

### 问题影响链

```
1. Web 加载 JSON 文件
   ↓
2. POST /api/canvas/load → Server 接收请求
   ↓
3. System.Text.Json 反序列化 DesignDocument
   ↓ ❌ Polygon2D 等类型无法正确反序列化（Newtonsoft Converter 被忽略）
   ↓ ❌ revit.rooms[].boundary 可能为 null 或格式错误
   ↓
4. ZoneCalculator.Process() 执行
   ↓ ❌ 从 rooms 创建 Zone 失败（boundary 数据异常）
   ↓
5. System.Text.Json 序列化返回结果
   ↓ ❌ Zone.RawBoundary (Polygon2D) 无法正确序列化
   ↓
6. Web 接收响应
   ↓ ❌ computed.zones 为空或格式错误
   ↓
7. ZoneBuilder.buildZones() 无数据可渲染
```

---

## 3. 证据

### 3.1 Server 使用 System.Text.Json

**文件**：`BIMCanvas.Server/Program.cs`

```csharp
// 第 7-12 行
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
    });
```

### 3.2 Core 使用 Newtonsoft.Json

**文件**：`BIMCanvas.Core/Models/Geometry/Polygon2D.cs`

```csharp
using Newtonsoft.Json;
using BIMCanvas.Core.Converters.Json;

namespace BIMCanvas.Core.Models.Geometry
{
    [JsonConverter(typeof(Polygon2DConverter))]  // Newtonsoft 属性
    public class Polygon2D
    {
        // ...
    }
}
```

### 3.3 Polygon2D 预期 JSON 格式

**Polygon2DConverter** 定义的格式：

```json
// 简单格式（无孔洞）
[[0,0], [100,0], [100,100], [0,100]]

// 完整格式（有孔洞）
{
  "shell": [[0,0], [100,0], [100,100], [0,100]],
  "holes": [[[20,20], [80,20], [80,80], [20,80]]]
}
```

**System.Text.Json 默认序列化**（错误格式）：

```json
{
  "vertices": [...],  // 属性名错误
  "holes": [...]
}
```

---

## 4. 解决方案

### 方案 A：Server 改用 Newtonsoft.Json（推荐）

**优点**：与 Core 层保持一致，改动最小

**步骤**：

1. 安装 NuGet 包：
   ```bash
   dotnet add BIMCanvas.Server package Microsoft.AspNetCore.Mvc.NewtonsoftJson
   ```

2. 修改 `Program.cs`：
   ```csharp
   builder.Services.AddControllers()
       .AddNewtonsoftJson(options =>
       {
           options.SerializerSettings.ContractResolver =
               new CamelCasePropertyNamesContractResolver();
           options.SerializerSettings.Formatting = Formatting.Indented;
       });
   ```

### 方案 B：为 System.Text.Json 编写 Converter

**优点**：使用更现代的 JSON 库

**缺点**：需要为每个类型编写两套 Converter，维护成本高

### 方案 C：使用 DTO 隔离层

**优点**：解耦 Core 和 Server

**缺点**：增加代码量和复杂度

---

## 5. 建议优先级

1. **立即**：实施方案 A（Server 改用 Newtonsoft.Json）
2. **验证**：重新测试 Zone 生成流程
3. **长期**：考虑统一 JSON 序列化策略

---

## 6. 相关文件

| 文件 | 描述 |
|------|------|
| `BIMCanvas.Server/Program.cs` | Server 入口，JSON 配置 |
| `BIMCanvas.Core/Converters/Json/*.cs` | Newtonsoft.Json Converters |
| `BIMCanvas.Core/Models/Geometry/*.cs` | 几何类型定义 |
| `BIMCanvas.Server/Services/ZoneCalculator.cs` | Zone 生成逻辑 |
| `BIMCanvas.Web/src/stores/canvasStore.ts` | Web 加载逻辑 |
| `BIMCanvas.Web/src/services/builders/ZoneBuilder.ts` | Zone 渲染逻辑 |

---

## 7. 测试用例

修复后需验证：

- [ ] POST `/api/canvas/load` 正确反序列化 `revit.rooms[].boundary`
- [ ] `ZoneCalculator.Process()` 生成 Room Zone 和 Exclusion Zone
- [ ] 响应中 `computed.zones` 包含正确格式的 Zone 数据
- [ ] Web 端 Zones 图层正确渲染
- [ ] Export Data 导出的 JSON 包含 `computed.zones` 数据

---

## 8. 附录：数据流对比

### 预期流程（修复后）

```
Web Load Data
    ↓
POST /api/canvas/load
    ↓ Newtonsoft.Json 反序列化
DesignDocument (revit.rooms 完整)
    ↓
ZoneCalculator.Process()
    ↓ 从 6 个 rooms 创建 6 个 Room Zone
    ↓ 从 7 个 doors 创建 7 个 Exclusion Zone
computed.zones = [13 个 Zone]
    ↓ Newtonsoft.Json 序列化
Response JSON
    ↓
Web ZoneBuilder.buildZones()
    ↓
Zones 图层渲染 13 个 Zone
```

### 当前流程（故障）

```
Web Load Data
    ↓
POST /api/canvas/load
    ↓ System.Text.Json 反序列化
DesignDocument (revit.rooms.boundary = null 或格式错误)
    ↓
ZoneCalculator.Process()
    ↓ rooms 数据异常，Zone 创建失败
computed.zones = []
    ↓ System.Text.Json 序列化
Response JSON (zones 为空)
    ↓
Web ZoneBuilder.buildZones()
    ↓
Zones 图层为空
```
