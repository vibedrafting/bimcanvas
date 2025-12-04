# BIMCanvas.Core 项目实现方案评审

> [!IMPORTANT]
> **协作规则**
> 1. **追加式讨论**：所有新意见请以 `### [时间戳] [专家名]: [观点]` 格式追加在 "深入讨论" 章节。
> 2. **严禁修改**：禁止修改其他专家的已存档观点。
> 3. **优先级标注**：明确区分 `[Blocker]` (阻碍性) 与 `[Suggestion]` (建议性)。

> [!TIP]
> **讨论原则**
> - **建设性**：反对时请提供替代方案。
> - **聚焦核心**：优先解决架构风险与数据一致性。
> - **拥抱共识**：寻找折中方案或最优解，避免无休止的争论。

## 1. 议题概述

- **主题**：BIMCanvas.Core 项目结构设计与实现策略
- **发起时间**：2025-12-04
- **参与者**：Claude (AI Architect)，待补充其他专家
- **背景信息**：

  BIMCanvas 项目已完成文档设计阶段（v2.3 数据模型），现进入代码实现阶段。根据 Architecture.md 的架构设计，BIMCanvas.Core 是整个系统的基础类库，被所有其他 .NET 项目引用：

  ```
  BIMCanvas.Core (.NET Standard 2.0)
        ▲
        │ 引用
        ├── BIMCanvas.Revit (.NET FW 4.7.2)
        ├── BIMCanvas.MCP.Canvas (.NET 6+)
        ├── BIMCanvas.MCP.Library (.NET 6+)
        └── BIMCanvas.Web.Server (.NET 6+)
  ```

  **核心职责**：
  1. 数据模型定义（CanvasDocument, Zone, Module 等）
  2. 单位/格式转换（Revit ↔ JSON）
  3. 空间算法（碰撞检测、布置验证）

  **相关文档**：
  - `docs/Architecture.md` - 系统架构（§3.1 项目结构，§6.1 Core 详细设计）
  - `docs/Schema-JSON.md` - v2.3 数据模型规范
  - `docs/PRD.md` - 产品需求文档

  **待讨论问题**：
  1. 项目结构如何组织（目录划分、命名规范）
  2. 几何基元的实现策略（纯手写 vs 引入 NTS）
  3. JSON 序列化库选择（System.Text.Json vs Newtonsoft.Json）
  4. 是否同步创建单元测试项目
  5. 开发优先级排序

---

## 2. 初始观点

### 专家：Claude (AI Architect)

- **核心观点**：采用分层渐进策略，优先实现 Models 层，确保数据模型与 Schema-JSON.md 完全对齐。

- **详细分析**：

  #### [论点 1] 项目结构建议

  ```
  BIMCanvas.Core/
  ├── Models/
  │   ├── Primitives/       几何基元 (Point2D, Vec2D, Polygon2D, AABB)
  │   ├── Document/         文档结构 (CanvasDocument, Outline, Wall, Opening)
  │   ├── Zones/            区域 (Zone, ExclusionArea, ZoneFunction)
  │   ├── Modules/          模块 (Module, ModuleItem, Facing)
  │   └── Shared/           通用 (Result<T>)
  ├── Converters/
  │   ├── UnitConverter.cs
  │   ├── RevitToJsonConverter.cs
  │   └── JsonToRevitConverter.cs
  └── Algorithms/
      ├── CollisionDetector.cs
      ├── FacingHelper.cs
      ├── PlacementNormalizer.cs
      └── GeometryHelper.cs
  ```

  **理由**：与 Architecture.md §3.1 保持一致，目录结构清晰反映职责边界。

  #### [论点 2] 几何基元实现策略

  **建议**：MVP 阶段纯手写，暂不引入 NetTopologySuite (NTS)。

  | 方案 | 优点 | 缺点 |
  |------|------|------|
  | 纯手写 | 零依赖、完全可控、符合 KISS | 需自己实现多边形运算 |
  | 引入 NTS | 功能强大、算法成熟 | 增加依赖、学习成本 |

  **MVP 所需几何操作**（可手写实现）：
  - `IsPointInPolygon` - 点在多边形内判断
  - `PolygonIntersects` - 两多边形相交判断（可用 AABB 快速剔除）
  - `ComputeAABB` - 计算包围盒
  - `RotatePolygon` - 多边形旋转

  **预留 NTS 兼容**：将几何操作封装在 `GeometryHelper` 中，未来可替换实现。

  #### [论点 3] JSON 序列化选择

  **建议**：使用 `System.Text.Json`

  | 库 | .NET Standard 2.0 支持 | 性能 | 依赖体积 |
  |-----|------------------------|------|----------|
  | System.Text.Json | 需 NuGet 包 | 更快 | 更小 |
  | Newtonsoft.Json | 原生支持 | 稍慢 | 更大 |

  **注意**：Core 层不应强制序列化配置，仅提供模型定义。各消费方（MCP、Web.Server）自行配置序列化行为。

  #### [论点 4] 单元测试策略

  **建议**：同步创建 `BIMCanvas.Core.Tests` 项目

  - 使用 xUnit + FluentAssertions
  - 重点覆盖：几何运算、碰撞检测、单位转换
  - 测试数据可直接使用 Schema-JSON.md §9.1 的示例

  #### [论点 5] 开发优先级

  ```
  第一批（阻塞其他开发）：
  ├── Models/Primitives/*     几何基元
  ├── Models/Document/*       文档结构
  ├── Models/Zones/*          区域定义
  ├── Models/Modules/*        模块定义
  └── Converters/UnitConverter.cs

  第二批（MCP 开发依赖）：
  ├── Algorithms/CollisionDetector.cs
  ├── Algorithms/FacingHelper.cs
  └── Algorithms/GeometryHelper.cs

  第三批（Revit 集成依赖）：
  ├── Converters/RevitToJsonConverter.cs
  └── Converters/JsonToRevitConverter.cs
  ```

- **待讨论问题**：

  1. `[Suggestion]` Polygon2D 是用 `double[][]` 还是自定义 `Point2D[]`？前者 JSON 友好，后者类型安全。
  2. `[Suggestion]` `Facing` 联合类型（string | Vec2D）在 C# 中如何优雅表示？
  3. `[Blocker]` 是否需要定义 `ICanvasDocument` 接口以支持未来扩展？

---

## 3. 深入讨论

> **追加讨论格式示例**：
>
> `### [YYYY-MM-DD HH:mm] [专家名]: [观点标题]`
>
> 内容详情...

<!-- 请在此分隔线下方追加新的讨论内容 -->

---

## 4. 共识总结

<!-- 讨论结束后填写，汇总达成的共识和结论 -->

### 核心共识

- [ ] **共识 1**: 项目目录结构方案
- [ ] **共识 2**: 几何库选择（手写 / NTS）
- [ ] **共识 3**: JSON 序列化库选择
- [ ] **共识 4**: 是否创建单元测试项目
- [ ] **共识 5**: Polygon2D 表示方式
- [ ] **共识 6**: Facing 联合类型实现方案

### 结论摘要

[待讨论完成后填写]
