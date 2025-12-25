# BIMCanvas v3.0 Multi-Repo Collection 架构升级计划

> **目标**：从 v2.9 单一 JSON 升级为 v3.0 多文件夹结构 + .bcp 压缩包格式
> **范围**：全栈升级（Core / Revit / Server / Web / Agent）
> **向后兼容**：否，直接切换到新格式
> **创建日期**：2025-12-25
> **状态**：Phase 1-3 已完成，Phase 4 待实施

---

## 架构核心："文件播放器"模式

> **参考文档**：`docs/FileDrivenArchitecture.md`
> **核心理念**：文件系统是连接 AI、Web、Server 和用户的通用总线。Server 从"内存数据库"模式转型为"文件播放器"模式。

### 三层汉堡模型

| 层级 | 文件夹路径 | 内容 | 读写属性 | 流转逻辑 |
|------|-----------|------|----------|----------|
| **底层 (基准)** | `baseline/` | 墙、柱、门窗、房间 | **只读** (Revit 导出) | Server 启动加载 → 推送 Web 作为静态背景 |
| **中层 (计算)** | `schemes/{s}/zones.json` | 功能分区、完成面 | **混合** (AI/Server) | Server 读取 → 计算 `computedBoundary` → 推送 Web |
| **顶层 (布局)** | `schemes/{s}/modules.json` | 家具模块、位置 | **读写** (高频交互) | **双向同步**：文件变动 ↔ Web 渲染 |

### 双向同步场景

**场景 A：代码式设计** (Code-Based Design)
```
VS Code 编辑 modules.json 并保存
    ↓
FileSystemWatcher 检测到文件变化
    ↓
Server 重新解析 JSON
    ↓
SignalR 广播 LayoutUpdated 事件
    ↓
Web 端收到事件，平滑动画更新位置
```

**场景 B：可视化设计** (Visual Design)
```
用户在 Web 端拖拽沙发到新位置
    ↓
Web 发送移动指令给 Server
    ↓
Server 验证通过后，直接覆写 modules.json
    ↓
文件系统发生物理变更
    ↓
Server 再次广播更新（确认状态）
```

---

## 功能驱动的任务拆解

### 优先级 1：实现双向同步核心

#### T1.1 Server 层：工程文件夹加载与聚合
**目标**：Server 能读取工程文件夹，聚合为 DesignDocument

```
输入：工程文件夹路径
输出：聚合后的 DesignDocument

聚合逻辑：
├── project.json           → Project 元数据
├── baseline/              → revit 子结构（只读）
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

#### T1.2 Server 层：文件监听 + SignalR 推送（场景 A）
**目标**：FileWatcher 检测变化 → 解析 → SignalR 广播

**关键文件**：
- `BIMCanvas.Server/Services/ProjectWatcherService.cs`（新建）
- `BIMCanvas.Server/Hubs/CanvasHub.cs`（扩展 `LayoutUpdated` 事件）

**注意**：
- 仅监听 `schemes/{s}/` 下的文件（modules.json, zones.json 等）
- baseline/ 为只读，不需要监听

#### T1.3 Server 层：Web 操作 → 文件写入（场景 B）
**目标**：Web 端操作 → Server 验证 → 覆写 JSON 文件 → 广播确认

**关键文件**：
- `BIMCanvas.Server/Services/ProjectWriterService.cs`（新建）
- `BIMCanvas.Server/Controllers/CanvasController.cs`（扩展写入端点）

**持久化策略**：
- 磁盘即时同步：`MouseUp` 时立即写入文件
- 禁用去抖动：阻塞式立即写入，确保文件系统与内存状态毫秒级一致

#### T1.4 Web 端：接收实时更新 + 发送操作
**目标**：双向通信

**接收更新**（场景 A）：
- SignalRService 监听 `LayoutUpdated` 事件
- canvasStore 的 `applyUpdate(partialDoc)` 方法
- 场景增量更新（或全量重建）

**发送操作**（场景 B）：
- MoveTool/RotateTool 在 `MouseUp` 时发送操作到 Server
- 等待 Server 确认后更新本地状态

**关键文件**：
- `BIMCanvas.Web/src/services/SignalRService.ts`（扩展）
- `BIMCanvas.Web/src/stores/canvasStore.ts`（扩展）

### 优先级 2：持久化与版本控制

#### T2.1 Undo/Redo 重构
**目标**：Undo 本质是逆向写入文件

**机制**：
- 用户移动 A → B，Server 记录逆向操作 `{ cmd: "Move", from: B, to: A }` 入栈
- 用户点击 Undo，Server 执行逆向操作，写入文件
- **外部干扰规则**：检测到非 Web 端发起的文件变更时，清空 Undo 栈

**关键文件**：
- `BIMCanvas.Server/Services/UndoStackService.cs`（新建）

#### T2.2 Git 周期存档
**目标**：生成版本历史节点

**触发条件**：
- 显式保存：用户点击"保存"按钮 → `git add . && git commit -m "Manual Save"`
- 自动保存：每隔 1 分钟（有操作时）→ `git add . && git commit -m "Auto Save"`

**关键文件**：
- `BIMCanvas.Server/Services/GitArchiveService.cs`（新建）

### 优先级 3：AI 协作工作流

#### T3.1 Git 分支策略
**目标**：Web 在 main，AI 在临时分支

**工作流**：
1. 用户请求 AI 帮助 → Server 基于 `main` 创建 `ai-feat-{timestamp}` 分支
2. AI 在临时分支上修改 `modules.json`，提交代码
3. AI 完成后，Web 进入"评审模式"

#### T3.2 Visual Merge UI
**目标**：可视化冲突解决 / 方案融合

**界面**：分屏显示，左侧"我的方案"，右侧"AI 提案"
**颗粒度**：按 Zone（可设计区）进行差异对比
**交互**：用户选择性合并 → Server 执行精确 JSON 合并 → 新 Commit 到 main

### 优先级 4：Revit 导出适配

#### T4.1 新增 BcpExporter
**目标**：Revit 导出 .bcp 格式（多文件夹 + ZIP 打包）

**关键文件**：
- `BIMCanvas.Revit/Services/BcpExporter.cs`（新建）
- `BIMCanvas.Revit/Services/CanvasExportService.cs`（修改输出流程）

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

> **职责分离**：Revit 只导出 `baseline/` + `project.json`，`schemes/` 和 `context/` 由 Server 层在打开项目时创建。

```csharp
public class BcpExporter
{
    // 创建临时文件夹结构（仅 baseline/）
    // 序列化各层数据到对应 JSON 文件
    // 打包为 .bcp (ZIP 格式)
    public void ExportToBcp(
        string outputPath,
        string projectName,
        BaselineManifest manifest,      // 包含 BaselineHash
        Architecture architecture,
        List<Opening> openings,
        List<Room> rooms,
        List<LocationLine> locationLines);
}
```

### 2.4 Revit 导出文件夹结构

```
{项目名}.bcp (ZIP 压缩包，Revit 导出)
├── project.json                    # 项目入口（Schemes 为空，由 Server 填充）
└── baseline/
    ├── metadata.json               # 包含坐标变换参数
    ├── architecture.json           # walls[] + columns[]
    ├── openings.json
    ├── rooms.json
    └── location_lines.json
```

> **注意**：`baseline.manifest`（哈希文件）由 Server 在打开项目时生成，不由 Revit 导出。

### 2.5 Server 打开项目后的完整结构

```
{用户文档}/BIMCanvas/Projects/{项目名}_{时间戳}/
├── project.json                    # 更新 Schemes 引用
├── baseline/                       # 只读，来自 Revit
│   ├── metadata.json
│   ├── architecture.json
│   ├── openings.json
│   ├── rooms.json
│   ├── location_lines.json
│   └── baseline.manifest           # Server 生成的哈希文件
├── context/                        # Server 创建
│   └── requirements.md             # 设计需求模板
├── schemes/                        # Server 创建
│   └── s1_Default/                 # 默认策略
│       ├── strategy.json           # 包含 lastValidatedBaselineHash
│       ├── zones.json              # 空
│       ├── finishes.json           # 空
│       └── modules.json            # 空
└── computed/                       # Server 生成的计算缓存
    ├── exclusions.json             # 门扇禁区 ExclusionArea[]
    └── computed.manifest           # 哈希验证文件
```

> **固定工作目录**：所有项目解压到 `用户文档/BIMCanvas/Projects/`，项目文件夹命名为 `{bcp文件名}_{时间戳}`（如 `demo_1_20251225_143025`）。

---

## Phase 3: Server 层适配

> **重要职责**：Server 在打开 `.bcp` 时需要创建 `schemes/`、`context/` 和 `computed/` 目录，补充 Revit 导出时未包含的部分。

### 3.1 新增 ManifestService

**文件**：`BIMCanvas.Server/Services/ManifestService.cs`

```csharp
public class ManifestService
{
    // 读取 .manifest 文件
    public Dictionary<string, string> ReadManifest(string manifestPath);

    // 写入 .manifest 文件
    public void WriteManifest(string manifestPath, Dictionary<string, string> values);

    // 从 baseline/ 读取 baselineHash
    public string? GetBaselineHash(string baselinePath);

    // 写入 baseline.manifest
    public void WriteBaselineManifest(string baselinePath, string baselineHash);
}
```

**`.manifest` 文件格式**（键值对，非 JSON）：

```
# Generated at 2025-12-25T14:30:25
version=1
generatedAt=2025-12-25T14:30:25+08:00
baselineHash=sha256:abc123def456...
```

### 3.2 新增 ComputedDataService

**文件**：`BIMCanvas.Server/Services/ComputedDataService.cs`

```csharp
public class ComputedDataService
{
    // 验证 computed 数据是否有效
    // 比较 computed.manifest 中的 baselineHash 与 baseline.manifest
    public bool ValidateComputedData(string projectPath);

    // 生成 computed 数据（门扇禁区等）
    public void GenerateComputedData(string projectPath);
}
```

**门扇禁区计算逻辑**（ExclusionArea）：

对每扇门，根据 `Line` + `FacingDirection` 计算矩形禁区：
- 禁区尺寸：`doorWidth × doorWidth`
- 偏移向量：`FacingDirection × doorWidth`
- 4 个顶点：门线起点、门线终点、终点+偏移、起点+偏移

```csharp
var offset = facingDirection * doorWidth;
var polygon = new Polygon2D(new[] {
    lineStart,
    lineEnd,
    lineEnd + offset,
    lineStart + offset
});
```

### 3.3 新增 ProjectService

**文件**：`BIMCanvas.Server/Services/ProjectService.cs`

```csharp
public class ProjectService
{
    // 加载项目（完整流程）
    // 1. 解压 .bcp 到 用户文档/BIMCanvas/Projects/{名称}_{时间戳}/
    // 2. 计算 baseline 哈希 → 写入 baseline/baseline.manifest
    // 3. 创建 context/ 目录和 requirements.md 模板
    // 4. 创建 schemes/s1_Default/ 默认策略
    // 5. 更新 project.json 的 Schemes 引用
    // 6. 验证 computed 数据有效性，无效时生成 exclusions.json + computed.manifest
    public string LoadProject(string bcpPath);

    // 保存项目到 .bcp
    public void SaveProject(string projectPath, string bcpPath);
}
```

### 3.4 新增 StrategyService

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

### 3.5 修改 MCP 工具

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
1. 实现 `ManifestService`（.manifest 文件读写）
2. 实现 `ComputedDataService`（门扇禁区生成）
3. 实现 `StrategyService`（策略目录管理）
4. 实现 `ProjectService`（项目加载完整流程）
5. 更新 MCP 工具

**关键文件**：
- `BIMCanvas.Server/Services/ManifestService.cs` (新建)
- `BIMCanvas.Server/Services/ComputedDataService.cs` (新建)
- `BIMCanvas.Server/Services/StrategyService.cs` (新建)
- `BIMCanvas.Server/Services/ProjectService.cs` (新建)

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

### Phase 1-2: Core + Revit 层 ✅
- [x] Core 层数据模型升级到 v3.0
- [x] Revit 可导出 `.bcp` 格式（仅 baseline + project.json）
- [x] 导出数据经评估符合 v3.0 规范

### Phase 3: Server 层 ✅
- [x] Server 可解压 `.bcp` 到固定工作目录
- [x] Server 创建 schemes/ 和 context/ 目录
- [x] Server 正确计算 BaselineHash 并写入 baseline.manifest
- [x] baseline.manifest 正确生成
- [x] 默认策略创建正常工作
- [x] computed 数据验证和生成正常工作
- [x] 门扇禁区计算正确

### Phase 3.1+4: 遗留服务迁移 + Web/Agent 适配 ⬜
- [ ] 策略切换正常工作
- [ ] Git 分支（变体）创建/切换正常
- [ ] dirty 机制正确检测 baseline 变化
- [ ] 原有的 zones/modules/finishes 功能正常（需迁移遗留服务）

---

## 相关文档

- 架构评审：`reviews/DataStructureRefactoring_Review.md`
- JSON Schema：`docs/Schema-JSON-v3.md`
- 示例项目：`ProjectStructure_Demo/`
