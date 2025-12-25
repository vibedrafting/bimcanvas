# BIMCanvas v3.0 Multi-Repo Collection 架构升级计划

> **目标**：从 v2.9 单一 JSON 升级为 v3.0 多文件夹结构 + .bcp 压缩包格式
> **范围**：全栈升级（Core / Revit / Server / Web / Agent）
> **向后兼容**：否，直接切换到新格式
> **创建日期**：2025-12-25
> **状态**：待实施

---

## 功能驱动的任务拆解（优先）

> **原则**：先确保现有功能在 v3.0 结构下正常工作，再考虑架构变更。

### 核心需求变化：数据源从内存转为文件系统

**当前**：Web 加载 JSON → 发送 Server 处理 → 返回存到内存 → 渲染
**期望**：Server 监听工程文件夹 → 文件变化时解析 → SignalR 推送 → Web 实时更新

**效果**：手动编辑工程文件夹中的 JSON（如修改 modules.json），Web 端实时更新显示。

### 现有功能 → 数据需求映射

| 功能 | 数据来源 (v2.9) | v3.0 文件位置 | 状态 |
|------|----------------|---------------|------|
| Web 渲染 - 墙柱 | `revit.walls[]`, `columns[]` | `baseline/architecture.json` | ✅ 直接对应 |
| Web 渲染 - 门窗 | `revit.openings[]` | `baseline/openings.json` | ✅ 直接对应 |
| Web 渲染 - 房间 | `revit.rooms[]` | `baseline/rooms.json` | ✅ 直接对应 |
| Web 渲染 - 家具 | `layout.modules[]` | `schemes/{s}/modules.json` | ✅ 路径变化但结构兼容 |
| 门扇禁区计算 | `openings[].line + facingDirection` | `baseline/openings.json` → zones | ✅ 输入不变 |
| 交互验证 | Zone 类型区分 | `schemes/{s}/zones.json` | ✅ 已有设计 |

**Zone 结构说明**（已确认正确）：
- `ZoneType Type`：区分 Exclusion（禁区）/ Room（房间）/ Designable（设计区）
- `RawBoundary`：原始轮廓
- `ComputedBoundary`：仅 Designable 类型需要计算（扣除完成面后的可用区域）

### 优先级 1：实现文件驱动的实时渲染

#### T1.1 Server 层：工程文件夹加载与聚合
**目标**：Server 能读取解压后的工程文件夹，聚合为 DesignDocument

```
输入：工程文件夹路径（解压后的 .bcp 或手动创建的文件夹）
输出：聚合后的 DesignDocument（兼容现有 Web 渲染）

聚合逻辑：
├── project.json           → Project 元数据
├── baseline/              → revit 子结构
│   ├── architecture.json  → walls[], columns[]
│   ├── openings.json      → openings[]
│   └── rooms.json         → rooms[]
└── schemes/{activeScheme}/
    ├── zones.json         → computed.zones[]
    ├── finishes.json      → computed.wallFinishes[]
    └── modules.json       → layout.modules[]
```

**关键文件**：
- `BIMCanvas.Server/Services/ProjectLoaderService.cs`（新建）

#### T1.2 Server 层：文件监听 + SignalR 推送
**目标**：监听工程文件夹变化，实时推送更新到 Web 端

```
工作流程：
1. FileSystemWatcher 监听工程文件夹
2. 检测到变化 → 重新解析受影响的 JSON 文件
3. 通过 SignalR Hub 推送更新到所有连接的 Web 客户端
4. Web 端接收更新，触发重新渲染
```

**关键文件**：
- `BIMCanvas.Server/Services/ProjectWatcherService.cs`（新建）
- `BIMCanvas.Server/Hubs/CanvasHub.cs`（扩展推送方法）
- `BIMCanvas.Web/src/services/SignalRService.ts`（现有，需扩展接收逻辑）
- `BIMCanvas.Web/src/stores/canvasStore.ts`（扩展接收更新逻辑）

#### T1.3 Web 端：接收实时更新
**目标**：Web 端接收 SignalR 推送，更新 document 并重新渲染

**变更点**：
- SignalRService 添加文件变化事件监听
- canvasStore 添加 `applyUpdate(partialDoc)` 方法
- 场景自动重建（或增量更新）

### 优先级 2：Revit 导出适配

#### T2.1 新增 BcpExporter
**目标**：Revit 导出 .bcp 格式（多文件夹 + ZIP 打包）

**关键文件**：
- `BIMCanvas.Revit/Services/BcpExporter.cs`（新建）
- `BIMCanvas.Revit/Services/CanvasExportService.cs`（修改输出流程）

**说明**：LocationLine 已有 `WallFinish.LocationLine`，无需单独导出文件。

### 优先级 3：架构增强

#### T3.1 Project/Strategy 模型
**目标**：支持多策略管理

#### T3.2 dirty 机制
**目标**：检测 baseline 变化

#### T3.3 Git 分支（变体）管理
**目标**：支持策略内的版本回溯

---

## 核心变更总结

| 维度 | v2.9 (当前) | v3.0 (目标) |
|------|-------------|-------------|
| **存储格式** | 单一 `DesignDocument.json` | 多文件夹结构 → `.bcp` 压缩包 |
| **策略管理** | `layout.schemes[]` 数组 | `schemes/{策略}/` 独立文件夹 |
| **变体管理** | 无 | Git 分支 |
| **版本控制** | 外部 Git | 每个策略是独立 Git 仓库 |
| **一致性检查** | 无 | dirty 机制 (`lastValidatedBaselineHash`) |

---

## Phase 1: Core 层数据模型重构

### 1.1 新建项目结构类

**文件位置**：`BIMCanvas.Core/Models/Project/`

```
新增文件：
├── Project.cs              # 项目入口（对应 project.json）
├── SchemeRef.cs            # 策略引用
├── Strategy.cs             # 策略元数据（对应 strategy.json）
├── StrategyOrigin.cs       # 衍生来源
├── StrategyStatus.cs       # 状态枚举 (valid/dirty/invalid)
├── StrategyApproach.cs     # 设计方法枚举
└── BaselineManifest.cs     # baseline 元数据（对应 metadata.json）
```

### 1.2 重构数据分层

**baseline 层**（只读，从 Revit 导出）:

| 原类 | 新位置 | 变更 |
|------|--------|------|
| `RevitData.Metadata` | `baseline/metadata.json` → `BaselineManifest` | 独立文件 |
| `RevitData.Walls/Columns` | `baseline/architecture.json` → `Architecture` | 独立文件 |
| `RevitData.Openings` | `baseline/openings.json` → 保持 `Opening[]` | 独立文件 |
| `RevitData.Rooms` | `baseline/rooms.json` → 保持 `Room[]` | 独立文件 |
| *(新增)* | `baseline/location_lines.json` → `LocationLine[]` | 完成面定位线 |

**schemes 层**（每个策略独立）:

| 原类 | 新位置 | 变更 |
|------|--------|------|
| `ComputedData.Zones` | `schemes/{s}/zones.json` | 移入策略层 |
| `WallFinish` | `schemes/{s}/finishes.json` → `FinishSegment[]` | 重构为 segment 模型 |
| `LayoutData.Modules` | `schemes/{s}/modules.json` | 移入策略层 |

### 1.3 新增 LocationLine 类

**文件**：`BIMCanvas.Core/Models/Revit/LocationLine.cs`

```csharp
public class LocationLine
{
    public string Id { get; set; }           // ll_{序号}
    public string WallId { get; set; }       // 所属墙体
    public string RoomId { get; set; }       // 所属房间
    public string Side { get; set; }         // "interior" | "exterior"
    public Line2D Line { get; set; }         // 定位线坐标
    public double Length { get; set; }       // 冗余存储便于计算
}
```

### 1.4 重构 FinishSegment（完成面段）

**文件**：`BIMCanvas.Core/Models/Computed/FinishSegment.cs`

```csharp
public class FinishSegment
{
    public string Id { get; set; }           // fs_{序号}
    public string SourceLineId { get; set; } // 引用 LocationLine.Id
    public double[] Range { get; set; }      // [起点mm, 终点mm] 绝对值
    public string FinishModuleId { get; set; }
    public double Thickness { get; set; }
    public FinishSource Source { get; set; } // room_default/zone_override/user_override
    public string? ZoneId { get; set; }      // 仅 zone_override 时有值
    public string? Reason { get; set; }
}
```

### 1.5 Hash 计算服务

**文件**：`BIMCanvas.Core/Services/BaselineHashService.cs`

```csharp
public class BaselineHashService
{
    // 计算关键文件的联合 hash
    // architecture.json + rooms.json + openings.json
    public string ComputeBaselineHash(string baselinePath);

    // 验证策略与 baseline 的一致性
    public StrategyStatus ValidateStrategy(Strategy strategy, string baselinePath);
}
```

---

## Phase 2: Revit 导出层重构

### 2.1 修改 CanvasExportService

**文件**：`BIMCanvas.Revit/Services/CanvasExportService.cs`

变更：
1. 输出从单一 JSON → 多文件夹结构
2. 新增 LocationLine 提取逻辑
3. 最终打包为 `.bcp` 压缩包

### 2.2 新增 LocationLineAdapter

**文件**：`BIMCanvas.Revit/Adapters/LocationLineAdapter.cs`

```csharp
public class LocationLineAdapter
{
    // 从 WallFinishAdapter 的边界提取定位线
    // 关联 Wall 和 Room
    public List<LocationLine> ExtractLocationLines(
        List<RevitWallFinish> wallFinishes,
        List<RevitRoom> rooms);
}
```

### 2.3 新增 BcpExporter

**文件**：`BIMCanvas.Revit/Services/BcpExporter.cs`

```csharp
public class BcpExporter
{
    // 创建临时文件夹结构
    // 序列化各层数据到对应 JSON 文件
    // 打包为 .bcp (ZIP 格式)
    public void ExportToBcp(
        string outputPath,
        BaselineManifest manifest,
        Architecture architecture,
        List<Opening> openings,
        List<Room> rooms,
        List<LocationLine> locationLines);
}
```

### 2.4 导出文件夹结构

```
{项目名}.bcp (ZIP 压缩包)
├── project.json                    # 项目入口
├── baseline/
│   ├── metadata.json
│   ├── architecture.json
│   ├── openings.json
│   ├── rooms.json
│   └── location_lines.json
├── context/
│   └── requirements.md             # 空模板
└── schemes/
    └── s1_Default/                 # 默认策略
        ├── strategy.json
        ├── zones.json              # 空
        ├── finishes.json           # 空
        └── modules.json            # 空
```

---

## Phase 3: Server 层适配

### 3.1 新增 ProjectService

**文件**：`BIMCanvas.Server/Services/ProjectService.cs`

```csharp
public class ProjectService
{
    // 解压 .bcp 到工作目录
    public Project OpenProject(string bcpPath);

    // 保存项目到 .bcp
    public void SaveProject(Project project, string bcpPath);

    // 创建新策略
    public Strategy CreateStrategy(Project project, string name, StrategyApproach approach);

    // 从变体升级为策略（复制文件夹）
    public Strategy PromoteVariantToStrategy(Project project, string sourceStrategyId, string branch);
}
```

### 3.2 新增 StrategyService

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

### 3.3 修改 MCP 工具

更新 Canvas-MCP 工具以适配新的数据结构：
- 读取/写入从单一 JSON → 多文件
- 新增策略/变体管理工具

---

## Phase 4: Web/Agent 适配

### 4.1 Web 前端适配
- 更新数据模型以匹配新的 JSON 结构
- 新增策略切换 UI
- 新增变体管理 UI（Git 分支可视化）
- 新增 dirty 状态提示

### 4.2 Agent 适配
- 更新 MCP 工具调用以适配新结构
- 调整 zones/modules/finishes 的读写路径

---

## 实施顺序（建议分阶段）

### Stage A: Core 层重构（优先）
1. 新建 `Models/Project/` 目录及类
2. 新增 `LocationLine` 类
3. 重构 `FinishSegment` 类
4. 实现 `BaselineHashService`
5. 编写单元测试

**关键文件**：
- `BIMCanvas.Core/Models/Project/*.cs` (新建)
- `BIMCanvas.Core/Models/Revit/LocationLine.cs` (新建)
- `BIMCanvas.Core/Models/Computed/FinishSegment.cs` (重构)
- `BIMCanvas.Core/Services/BaselineHashService.cs` (新建)

### Stage B: Revit 导出重构
1. 新增 `LocationLineAdapter`
2. 新增 `BcpExporter`
3. 修改 `CanvasExportService` 输出流程
4. 测试导出功能

**关键文件**：
- `BIMCanvas.Revit/Adapters/LocationLineAdapter.cs` (新建)
- `BIMCanvas.Revit/Services/BcpExporter.cs` (新建)
- `BIMCanvas.Revit/Services/CanvasExportService.cs` (修改)

### Stage C: Server 层实现
1. 实现 `ProjectService`
2. 实现 `StrategyService`（Git 操作）
3. 更新 MCP 工具

**关键文件**：
- `BIMCanvas.Server/Services/ProjectService.cs` (新建)
- `BIMCanvas.Server/Services/StrategyService.cs` (新建)

### Stage D: 前端/Agent 适配
1. 更新 Web 数据模型
2. 更新 Agent MCP 调用

---

## 风险与注意事项

1. **Git 操作权限**：Windows 下 Git 操作可能需要特殊处理
2. **压缩包格式**：.bcp 本质是 ZIP，需确保中文路径兼容
3. **文件锁定**：多进程访问同一项目文件夹需要考虑锁机制
4. **迁移工具**：虽然不做向后兼容，但建议提供 v2.9 → v3.0 迁移脚本

---

## 验收标准

- [ ] Revit 可导出 `.bcp` 格式
- [ ] Server 可解压并读取 `.bcp`
- [ ] 策略创建/切换正常工作
- [ ] Git 分支（变体）创建/切换正常
- [ ] dirty 机制正确检测 baseline 变化
- [ ] 原有的 zones/modules/finishes 功能正常

---

## 相关文档

- 架构评审：`reviews/DataStructureRefactoring_Review.md`
- JSON Schema：`docs/Schema-JSON-v3.md`
- 示例项目：`ProjectStructure_Demo/`
