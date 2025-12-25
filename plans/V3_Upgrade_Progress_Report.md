# BIMCanvas v3.0 架构升级进度报告

> **文档目的**：记录 v3.0 Multi-Repo Collection 架构升级的当前进度，供后续对话继续执行剩余阶段任务
> **创建日期**：2025-12-25
> **最后更新**：2025-12-25 (Phase 2.1 职责分离)

---

## 一、升级概述

**目标**：从 v2.9 单一 JSON (`DesignDocument`) 升级为 v3.0 多文件夹结构 + `.bcp` 压缩包格式

**核心理念**：
- 文件系统作为 Single Source of Truth
- "三层汉堡"模型：baseline（只读）→ schemes（混合）→ modules（读写）
- 每个策略是独立 Git 仓库，支持版本控制和变体管理

---

## 二、已完成阶段

### Phase 1: Core 层数据模型重构 ✅

**完成时间**：2025-12-25
**Git Commit**：`d892a5f`

#### 新建文件 (15个)

| 目录 | 文件 | 说明 |
|------|------|------|
| `Models/Shared/` | `StrategyStatus.cs` | 状态枚举：Valid / Dirty / Invalid |
| | `StrategyApproach.cs` | 设计方法枚举：CirculationFirst / FurnitureFirst / ... |
| `Models/Project/` | `Project.cs` | 项目入口（对应 project.json） |
| | `SchemeRef.cs` | 策略轻量引用 |
| | `Strategy.cs` | 策略元数据（对应 strategy.json） |
| | `StrategyOrigin.cs` | 衍生来源追踪 |
| | `BaselineManifest.cs` | Baseline 元数据（对应 metadata.json） |
| `Models/Revit/` | `LocationLine.cs` | 完成面定位线（从 WallFinish 分离） |
| | `Architecture.cs` | 建筑构造容器（walls + columns） |
| `Models/Computed/` | `ExclusionArea.cs` | 禁区模型 |
| | `FinishSegment.cs` | 完成面段（range 表示法） |
| `Converters/Json/` | `StrategyStatusConverter.cs` | snake_case JSON 序列化 |
| | `StrategyApproachConverter.cs` | snake_case JSON 序列化 |
| | `FinishSourceConverter.cs` | snake_case JSON 序列化 |
| `Services/` | `BaselineHashService.cs` | Baseline 哈希计算 + 一致性验证 |

#### 修改文件 (3个)

| 文件 | 修改内容 |
|------|----------|
| `Models/Revit/Wall.cs` | +IsStructural（Thickness 已移除，轮廓已含几何信息） |
| `Models/Computed/Zone.cs` | +RoomId, +ExclusionAreas, +Openings |
| `Models/Layout/Module.cs` | +PlacementReason |

#### 删除文件 (10个)

- `Models/Document/DesignDocument.cs`
- `Models/Revit/RevitData.cs`
- `Models/Revit/Metadata.cs`
- `Models/Revit/FinishLocationBoundary.cs`
- `Models/Computed/ComputedData.cs`
- `Models/Computed/WallFinish.cs`
- `Models/Layout/LayoutData.cs`
- `Models/Layout/Scheme.cs`
- `Converters/Revit/JsonToRevitConverter.cs`
- `Converters/Revit/RevitToJsonConverter.cs`

---

### Phase 2: Revit 导出层重构 ✅

**完成时间**：2025-12-25
**Git Commit**：`2846576`

#### 新建文件 (2个)

| 文件 | 说明 |
|------|------|
| `Adapters/LocationLineAdapter.cs` | 从墙面完成面边界提取定位线，关联墙体和房间 |
| `Services/BcpExporter.cs` | 导出 .bcp 多文件夹结构 + ZIP 压缩包 |

#### 修改文件 (2个)

| 文件 | 修改内容 |
|------|----------|
| `Services/CanvasExportService.cs` | 完全重构：输出从 `DesignDocument` → 多文件夹结构，新增 `ExportResult` 类 |
| `Commands/ExportCanvasCommand.cs` | 适配 v3.0，保存 `.bcp` 文件而非 `.json` |

#### 导出文件结构（Phase 2.1 更新后）

```
{项目名}.bcp (ZIP 压缩包)
├── project.json                    # 项目入口（Schemes 为空，由 Server 填充）
└── baseline/
    ├── metadata.json               # 坐标变换参数 + BaselineHash
    ├── architecture.json           # 墙体 + 柱子
    ├── openings.json               # 门窗
    ├── rooms.json                  # 房间
    └── location_lines.json         # 完成面定位线
```

> **职责分离**：`schemes/` 和 `context/` 由 Server 层在项目打开时创建，Revit 只负责导出原始建筑数据。

---

### Phase 2.1: Revit 导出职责分离 ✅

**完成时间**：2025-12-25
**Git Commits**：`6fef66f`, `f192166`

#### 数据评估结果

使用 `data/金凤127_标高 1.bcp` 进行验证：

| 数据项 | 数量 | 状态 |
|--------|------|------|
| 墙体 (walls) | 50 | ✅ polygon 完整 |
| 柱子 (columns) | 0 | ✅ 项目无柱子 |
| 门窗 (openings) | 15 | ✅ 门7扇 + 窗8扇 |
| 房间 (rooms) | 6 | ✅ 类型推断正确 |
| 定位线 (location_lines) | 66 | ✅ 约10条 roomId 为空（边界情况） |

#### 修改说明

| 变更 | 说明 |
|------|------|
| 移除 schemes/ 创建 | 策略由 Server 层创建，Revit 不参与 |
| 移除 context/ 创建 | 设计需求由 Server 层管理 |
| project.json 调整 | Schemes 为空列表，ActiveSchemeId 为 null |
| BaselineHash 移入 metadata.json | 供 Server 层验证 baseline 一致性 |
| 移除 Wall.Thickness | 冗余字段，Polygon 已含完整几何信息 |

---

## 三、当前状态

### 构建状态

| 项目 | 状态 | 说明 |
|------|------|------|
| BIMCanvas.Core | ✅ 编译成功 | 81 nullable 警告（预存在），0 错误 |
| BIMCanvas.Revit | ✅ 编译成功 | 4 架构警告（预存在），0 错误 |

### 关键类型变化

| 旧类型 (v2.9) | 新类型 (v3.0) | 说明 |
|---------------|---------------|------|
| `DesignDocument` | 已删除 | 使用多文件结构替代 |
| `RevitData` | 已删除 | 拆分为独立 JSON 文件 |
| `Metadata` | `BaselineManifest` | 移至 `Models/Project/` |
| `WallFinish` | `FinishSegment` | 使用 range 表示法 |
| `FinishLocationBoundary` | `LocationLine` | 分离为独立实体 |

### 命名空间冲突处理

在 `BIMCanvas.Revit` 中使用别名解决与 `Autodesk.Revit.DB` 的冲突：

```csharp
using CoreWall = BIMCanvas.Core.Models.Revit.Wall;
using CoreColumn = BIMCanvas.Core.Models.Revit.Column;
using CoreOpening = BIMCanvas.Core.Models.Revit.Opening;
using CoreRoom = BIMCanvas.Core.Models.Revit.Room;
using CoreArchitecture = BIMCanvas.Core.Models.Revit.Architecture;
using CoreLocationLine = BIMCanvas.Core.Models.Revit.LocationLine;
```

---

## 四、待完成阶段

### Phase 3: Server 层适配 ⬜

**参考文档**：`plans/V3_Architecture_Upgrade_Plan.md` §Phase 3

> **重要**：由于 Phase 2.1 职责分离，Server 层现在需要在项目打开时创建 `schemes/` 和 `context/` 目录。

#### 3.1 新增 ProjectService

**文件**：`BIMCanvas.Server/Services/ProjectService.cs`

```csharp
public class ProjectService
{
    // 解压 .bcp 到工作目录，创建 schemes/ 和 context/ 目录
    public Project OpenProject(string bcpPath);

    // 保存项目到 .bcp
    public void SaveProject(Project project, string bcpPath);

    // 创建新策略（在 schemes/ 下创建子目录）
    public Strategy CreateStrategy(Project project, string name, StrategyApproach approach);

    // 从变体升级为策略（复制文件夹）
    public Strategy PromoteVariantToStrategy(Project project, string sourceStrategyId, string branch);
}
```

#### 3.2 新增 StrategyService

**文件**：`BIMCanvas.Server/Services/StrategyService.cs`

```csharp
public class StrategyService
{
    // 初始化策略 Git 仓库
    public void InitializeGit(string strategyPath);

    // 创建变体（Git 分支）
    public void CreateVariant(string strategyPath, string branchName);

    // 切换变体
    public void SwitchVariant(string strategyPath, string branchName);

    // 获取提交历史
    public List<CommitInfo> GetCommitHistory(string strategyPath);
}
```

#### 3.3 修改 MCP 工具

- 更新 Canvas-MCP 工具以适配多文件结构
- 新增策略/变体管理工具

---

### Phase 4: Web/Agent 适配 ⬜

**参考文档**：`plans/V3_Architecture_Upgrade_Plan.md` §Phase 4

#### 4.1 Web 前端适配

- 更新数据模型以匹配新的 JSON 结构
- 新增策略切换 UI
- 新增变体管理 UI（Git 分支可视化）
- 新增 dirty 状态提示

#### 4.2 Agent 适配

- 更新 MCP 工具调用以适配新结构
- 调整 zones/modules/finishes 的读写路径

---

## 五、验收标准

- [x] Core 层数据模型升级到 v3.0
- [x] Revit 可导出 `.bcp` 格式
- [x] 导出数据经评估符合 v3.0 规范
- [ ] Server 可解压并读取 `.bcp`
- [ ] Server 在打开项目时创建 schemes/ 和 context/
- [ ] 策略创建/切换正常工作
- [ ] Git 分支（变体）创建/切换正常
- [ ] dirty 机制正确检测 baseline 变化
- [ ] 原有的 zones/modules/finishes 功能正常

---

## 六、相关文档索引

| 文档 | 路径 | 说明 |
|------|------|------|
| 升级计划 | `plans/V3_Architecture_Upgrade_Plan.md` | 完整升级计划和设计 |
| 文件驱动架构 | `docs/FileDrivenArchitecture.md` | "文件播放器"模式说明 |
| JSON Schema v3 | `docs/Schema-JSON-v3.md` | v3.0 数据模型定义 |
| 项目说明 | `CLAUDE.md` | 项目指令和约束 |
| Core 层说明 | `BIMCanvas.Core/README.md` | Core 层实现细节 |
| Revit 层说明 | `BIMCanvas.Revit/README.md` | Revit 层实现细节 |

---

## 七、继续任务的指令

在新对话中，使用以下提示继续任务：

```
请阅读以下文档了解当前进度：
- plans/V3_Upgrade_Progress_Report.md（本报告）
- plans/V3_Architecture_Upgrade_Plan.md（完整计划）

继续执行 Phase 3: Server 层适配
```

---

## 八、风险与注意事项

1. **Git 操作权限**：Windows 下 Git 操作可能需要特殊处理
2. **压缩包格式**：.bcp 本质是 ZIP，需确保中文路径兼容
3. **文件锁定**：多进程访问同一项目文件夹需要考虑锁机制
4. **命名空间冲突**：Revit 层需要使用别名解决与 Revit API 的类型冲突

---

*报告结束*
