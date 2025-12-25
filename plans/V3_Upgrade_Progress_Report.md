# BIMCanvas v3.0 架构升级进度报告

> **文档目的**：记录 v3.0 Multi-Repo Collection 架构升级的当前进度，供后续对话继续执行剩余阶段任务
> **创建日期**：2025-12-25
> **最后更新**：2025-12-26 (Phase 4 Web 层加载 v3.0 项目数据)

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

### Phase 3: Server 层项目加载 ✅

**完成时间**：2025-12-25

#### 新建文件 (4个)

| 文件 | 说明 |
|------|------|
| `Services/ManifestService.cs` | `.manifest` 键值对文件读写服务 |
| `Services/ComputedDataService.cs` | 计算数据管理（门扇禁区生成 + 验证） |
| `Services/StrategyService.cs` | 策略目录管理（创建 + 查询） |
| `Services/ProjectService.cs` | 项目加载完整流程（解压 + 初始化） |

#### 修改文件 (1个)

| 文件 | 修改内容 |
|------|----------|
| `Program.cs` | 集成项目加载流程，注册新服务，更新 Web URL 参数 |

#### 遗留文件处理 (3个)

| 文件 | 处理方式 |
|------|----------|
| `Services/CanvasStateManager.cs` | 重命名为 `.legacy`，待迁移 |
| `Services/ZoneCalculator.cs` | 重命名为 `.legacy`，待迁移 |
| `Controllers/CanvasController.cs` | 重命名为 `.legacy`，待迁移 |

#### 新增功能

**项目加载流程**：
1. 解压 `.bcp` 到 `用户文档/BIMCanvas/Projects/{名称}_{时间戳}/`
2. 计算 baseline 哈希 → 写入 `baseline/baseline.manifest`
3. 创建 `context/` 目录和 `requirements.md` 模板
4. 创建 `schemes/s1_Default/` 默认策略（含 strategy.json, zones.json, finishes.json, modules.json）
5. 更新 `project.json` 的 Schemes 引用
6. 验证 computed 数据有效性，无效时生成 `exclusions.json` + `computed.manifest`

**`.manifest` 文件格式**：
```
# Generated at 2025-12-25T14:30:25
version=1
generatedAt=2025-12-25T14:30:25+08:00
baselineHash=sha256:abc123def456...
```

**门扇禁区计算**：
- 读取 `openings.json` 中的门数据
- 对每扇门，根据 `Line` + `FacingDirection` 计算矩形禁区
- 禁区尺寸：`doorWidth × doorWidth`
- 写入 `computed/exclusions.json`

#### 目录结构（完整）

```
C:\Users\{username}\Documents\BIMCanvas\Projects\
└── demo_1_20251225_143025/
    ├── project.json                    # Revit 导出，Server 更新 Schemes 引用
    ├── baseline/
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
    │       ├── strategy.json           # 策略元数据
    │       ├── zones.json              # 功能分区（空）
    │       ├── finishes.json           # 完成面（空）
    │       └── modules.json            # 家具模块（空）
    └── computed/                       # 计算缓存
        ├── exclusions.json             # 门扇禁区数据
        └── computed.manifest           # 哈希验证文件
```

---

### Phase 4: Web 层加载 v3.0 项目数据 ✅

**完成时间**：2025-12-26
**Git Commit**：`0367373`

#### 新建文件 (2个)

| 文件 | 说明 |
|------|------|
| `Controllers/ProjectController.cs` | `/api/project?path=` 端点，聚合项目文件夹数据返回 `ProjectData` |
| `Dtos/ProjectData.cs` | v3.0 数据传输对象（ProjectData, BaselineData, SchemeData 等） |

#### 重写文件 (2个)

| 文件 | 说明 |
|------|------|
| `types/canvas.ts` | v3.0 类型定义，`ProjectData` 取代 `CanvasDocument` |
| `stores/canvasStore.ts` | 状态管理重构，`loadProject()` 取代 `loadFromJson()` |

#### 修改文件 (15个)

| 文件 | 修改内容 |
|------|----------|
| `App.vue` | 支持 `?project=` URL 参数加载项目 |
| `SceneBuilder.ts` | 数据路径：`doc.revit.*` → `data.baseline.*` |
| `OutlineBuilder.ts` | 数据路径：`doc.layout.modules` → `data.activeScheme.modules` |
| `LabelBuilder.ts` | 数据路径适配 |
| `ZoneBuilder.ts` | `doc.computed.zones` → `data.activeScheme.zones` |
| `ThreeSceneService.ts` | `store.document` → `store.projectData` |
| `TimelineManager.ts` | `CanvasDocument` → `ProjectData` 类型 |
| `DragManager.ts` | `store.document` → `store.projectData` |
| `InteractionService.ts` | `store.document` → `store.projectData` |
| `SnappingEngine.ts` | 所有数据路径适配 |
| `MoveTool.ts` | `store.document` → `store.projectData` |
| `RotateTool.ts` | `store.document` → `store.projectData` |
| `MirrorTool.ts` | `store.document` → `store.projectData` |
| `PropertyPanel.vue` | 项目属性显示适配 |
| `RibbonToolbar.vue` | 移除 `loadFromJson`，改用 v3.0 警告 |

#### 数据路径迁移规则

| 旧路径 (v2.9) | 新路径 (v3.0) |
|---------------|---------------|
| `document.revit.walls` | `projectData.baseline.walls` |
| `document.revit.columns` | `projectData.baseline.columns` |
| `document.revit.openings` | `projectData.baseline.openings` |
| `document.revit.rooms` | `projectData.baseline.rooms` |
| `document.layout.modules` | `projectData.activeScheme.modules` |
| `document.computed.zones` | `projectData.activeScheme.zones` |
| `store.document` | `store.projectData` |
| `CanvasDocument` 类型 | `ProjectData` 类型 |

#### 加载流程

```
1. Web 通过 URL 参数获取项目路径：?project=C:\Users\...\Projects\demo_1
2. canvasStore.loadProject(projectPath) 调用 Server API
3. Server /api/project 端点聚合以下文件：
   - project.json → project
   - baseline/*.json → baseline
   - schemes/{activeSchemeId}/*.json → activeScheme
   - computed/*.json → computed
4. 返回 ProjectData 对象，前端渲染场景
```

---

## 三、当前状态

### 构建状态

| 项目 | 状态 | 说明 |
|------|------|------|
| BIMCanvas.Core | ✅ 编译成功 | 81 nullable 警告（预存在），0 错误 |
| BIMCanvas.Revit | ✅ 编译成功 | 4 架构警告（预存在），0 错误 |
| BIMCanvas.Server | ✅ 编译成功 | 0 警告，0 错误（遗留服务已标记为 .legacy） |
| BIMCanvas.Web | ✅ 类型检查通过 | v3.0 类型定义已完成，数据路径已迁移 |

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

### Phase 3.1: Server 层遗留服务迁移 ⬜

**说明**：Phase 3 项目加载已完成，但以下遗留服务需要迁移到 v3.0 文件结构

#### 待迁移文件

| 文件 | 当前状态 | 迁移方向 |
|------|----------|----------|
| `CanvasStateManager.cs.legacy` | 使用 `DesignDocument` | 改为读取项目文件夹 |
| `ZoneCalculator.cs.legacy` | 使用 `DesignDocument` | 改为读取 baseline/ 和 schemes/ |
| `CanvasController.cs.legacy` | 使用 `DesignDocument` | 改为使用 ProjectService |

#### 新增 MCP 工具

- 更新 Canvas-MCP 工具以适配多文件结构
- 新增策略/变体管理工具

---

### Phase 4.1: Web 前端数据加载 ✅ (已完成)

见上方 Phase 4 详细记录。

---

### Phase 4.2: Web 前端 UI 增强 ⬜

- [ ] 新增策略切换 UI
- [ ] 新增变体管理 UI（Git 分支可视化）
- [ ] 新增 dirty 状态提示

---

### Phase 4.3: Agent 适配 ⬜

- [ ] 更新 MCP 工具调用以适配新结构
- [ ] 调整 zones/modules/finishes 的读写路径

---

## 五、验收标准

- [x] Core 层数据模型升级到 v3.0
- [x] Revit 可导出 `.bcp` 格式（仅 baseline + project.json）
- [x] 导出数据经评估符合 v3.0 规范
- [x] Server 可解压 `.bcp` 并创建 schemes/ 和 context/
- [x] Server 正确计算 BaselineHash 并写入 baseline.manifest 和 strategy.json
- [x] 默认策略创建正常工作
- [x] computed 数据验证和生成正常工作
- [x] Web 可通过 URL 参数加载 v3.0 项目文件夹
- [x] Web 类型定义和数据路径迁移到 v3.0
- [ ] 策略切换正常工作
- [ ] Git 分支（变体）创建/切换正常
- [ ] dirty 机制正确检测 baseline 变化
- [ ] 原有的 zones/modules/finishes 功能正常（需迁移遗留服务）

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
| Server 层说明 | `BIMCanvas.Server/README.md` | Server 层实现细节 |

---

## 七、继续任务的指令

在新对话中，使用以下提示继续任务：

```
请阅读以下文档了解当前进度：
- plans/V3_Upgrade_Progress_Report.md（本报告）
- plans/V3_Architecture_Upgrade_Plan.md（完整计划）

继续执行 Phase 3.1: Server 层遗留服务迁移
或
继续执行 Phase 4.2: Web 前端 UI 增强（策略切换、变体管理）
或
继续执行 Phase 4.3: Agent 适配
```

---

## 八、风险与注意事项

1. **Git 操作权限**：Windows 下 Git 操作可能需要特殊处理
2. **压缩包格式**：.bcp 本质是 ZIP，需确保中文路径兼容
3. **文件锁定**：多进程访问同一项目文件夹需要考虑锁机制
4. **命名空间冲突**：Revit 层需要使用别名解决与 Revit API 的类型冲突

---

*报告结束*
