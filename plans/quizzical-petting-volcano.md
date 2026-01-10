# 架构重构方案：让 Agent 修改支持撤回/重做

## 一、问题背景

### 用户痛点
用户在对话窗口要求 Agent 修改家具布置（如"把客厅沙发离电视近一点"），Agent 执行完成后，Web 端虽然正常渲染了变化，但**无法通过撤回/重做系统（Ctrl+Z）撤销 Agent 的修改**。

### 根本原因

**数据流链路**：
```
Agent 写文件 (modules.json)
    ↓
Server FileSystemWatcher 检测变化
    ↓
SignalR 推送 "ReceiveUpdate" 事件
    ↓
Web canvasStore 监听 'bimcanvas:server-update'
    ↓
调用 loadProject(true)
    ↓
timeline.clear()  ← 问题根源：无条件清空所有历史
    ↓
saveState()  // 仅保存新状态，旧状态丢失
```

**架构缺陷**：
1. **`loadProject()` 职责混乱**：既用于"加载新项目"（应清空历史），又用于"远程同步"（应保留历史）
2. **历史管理耦合**：TimelineManager 不知道变更来源，无法智能决策
3. **未来扩展受限**：缺乏操作溯源，无法支持协作、冲突解决等高级功能

---

## 二、重构方案：变更源追踪 + 事件驱动

### 核心理念
- **职责分离**：数据加载与历史管理解耦
- **来源追踪**：每个状态变更都记录来源和上下文
- **事件驱动**：通过事件系统实现松耦合的状态同步
- **面向未来**：架构支持协作、冲突解决、操作回放等高级功能

### 核心优势
1. ✅ **职责清晰**：每个方法/组件有单一明确的职责
2. ✅ **易于扩展**：新增场景只需添加 ChangeSource 枚举
3. ✅ **架构优雅**：策略集中配置，代码逻辑自明
4. ✅ **为未来铺路**：为多用户协作、操作回放等高级功能打基础

---

## 三、架构设计

### 3.1 核心类型系统

**文件**：`src/types/history.ts`（新建）

```typescript
// ========== 变更源枚举 ==========
export enum ChangeSource {
  // 用户主动操作
  UserEdit = 'user_edit',           // 用户手动编辑（拖动、旋转等）
  UserUpload = 'user_upload',       // 用户上传新项目
  UserUndo = 'user_undo',           // 用户撤销操作
  UserRedo = 'user_redo',           // 用户重做操作

  // Git 操作
  GitCheckout = 'git_checkout',     // Git 分支切换
  GitDiscard = 'git_discard',       // Git 放弃更改
  GitInit = 'git_init',             // Git 初始化项目

  // 远程同步
  AgentModify = 'agent_modify',     // AI Agent 修改
  ServerSync = 'server_sync',       // Server 端文件变化推送
  CollabSync = 'collab_sync',       // 协作者修改（未来）

  // 系统操作
  SystemInit = 'system_init',       // 系统初始化
  SystemRestore = 'system_restore'  // 系统恢复（异常处理）
}

// ========== 历史快照 ==========
export interface HistorySnapshot {
  // 基础信息
  id: string;                       // 快照唯一ID
  timestamp: number;                // 时间戳
  state: string;                    // 序列化的 ProjectData JSON

  // 来源追踪
  source: ChangeSource;             // 变更来源
  sourceId?: string;                // 来源标识（如 agentId、userId）

  // 变更描述
  description?: string;             // 人类可读描述
  changeType?: ChangeType;          // 变更类型（增、删、改）
  affectedIds?: string[];           // 受影响的对象ID列表

  // 元数据
  metadata?: {
    branchName?: string;            // Git分支名
    commitHash?: string;            // Git提交哈希
    agentConversationId?: string;   // Agent对话ID
    userSessionId?: string;         // 用户会话ID
    [key: string]: any;             // 扩展字段
  };
}

// ========== 变更类型 ==========
export enum ChangeType {
  Create = 'create',
  Update = 'update',
  Delete = 'delete',
  Move = 'move',
  Rotate = 'rotate',
  Batch = 'batch'
}

// ========== 加载选项 ==========
export interface LoadOptions {
  source: ChangeSource;             // 加载来源（必填）
  preserveView?: boolean;           // 保持视图（默认根据source决策）
  preserveHistory?: boolean;        // 保留历史（默认根据source决策）
  description?: string;             // 自定义描述
  metadata?: Record<string, any>;   // 元数据
}
```

---

### 3.2 TimelineManager 重构

**文件**：`src/services/state/TimelineManager.ts`（完全重写）

#### 核心职责
1. 管理历史快照栈（支持 Undo/Redo）
2. 根据变更来源智能决策历史策略
3. 提供历史查询和过滤功能
4. 支持操作回放和协作冲突解决（未来）

#### 关键 API

```typescript
export class TimelineManager {
  // ========== 策略配置 ==========

  /**
   * 哪些来源会清空历史
   */
  private readonly CLEAR_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.UserUpload,
    ChangeSource.GitInit,
    ChangeSource.SystemInit
  ]);

  /**
   * 哪些来源会保留历史
   */
  private readonly PRESERVE_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.CollabSync,
    ChangeSource.UserEdit
  ]);

  /**
   * 哪些来源会保持视图
   */
  private readonly PRESERVE_VIEW_SOURCES = new Set<ChangeSource>([
    ChangeSource.GitCheckout,
    ChangeSource.GitDiscard,
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.UserUndo,
    ChangeSource.UserRedo
  ]);

  // ========== 核心 API ==========

  /**
   * 推入新快照
   */
  public push(
    state: ProjectData,
    source: ChangeSource,
    options?: {
      description?: string;
      changeType?: ChangeType;
      affectedIds?: string[];
      metadata?: Record<string, any>;
    }
  ): void;

  /**
   * 撤销（返回上一个快照）
   */
  public undo(): HistorySnapshot | null;

  /**
   * 重做（返回下一个快照）
   */
  public redo(): HistorySnapshot | null;

  /**
   * 清空历史
   */
  public clear(): void;

  // ========== 策略决策 API ==========

  /**
   * 判断指定来源是否应该清空历史
   */
  public shouldClearHistory(source: ChangeSource): boolean;

  /**
   * 判断指定来源是否应该保留历史
   */
  public shouldPreserveHistory(source: ChangeSource): boolean;

  /**
   * 判断指定来源是否应该保持视图
   */
  public shouldPreserveView(source: ChangeSource): boolean;

  // ========== 查询 API（用于调试/可视化）==========

  public getCurrentSnapshot(): HistorySnapshot | null;
  public getAllSnapshots(): ReadonlyArray<HistorySnapshot>;
  public getSnapshotsBySource(source: ChangeSource): HistorySnapshot[];
  public getSnapshotsByTimeRange(start: number, end: number): HistorySnapshot[];
}
```

**完整实现见附录 A**

---

### 3.3 CanvasStore 重构

**文件**：`src/stores/canvasStore.ts`（主要重构）

#### 核心变化

**1. 统一加载入口**

```typescript
/**
 * 加载项目 - 统一入口
 *
 * @param options 加载选项（支持 ChangeSource 简写）
 * @returns 加载是否成功
 */
const loadProject = async (options: LoadOptions | ChangeSource): Promise<boolean> => {
  // 兼容简写参数
  const opts: LoadOptions = typeof options === 'string'
    ? { source: options }
    : options;

  // 智能决策：是否保留历史/视图
  const preserveHistory = opts.preserveHistory ??
    timeline.shouldPreserveHistory(opts.source);
  const preserveView = opts.preserveView ??
    timeline.shouldPreserveView(opts.source);

  try {
    // 从 Server 获取数据
    const response = await axios.get<ProjectData>('http://localhost:5000/api/project');
    projectData.value = response.data;
    isDirty.value = false;

    // 历史管理策略
    if (timeline.shouldClearHistory(opts.source)) {
      timeline.clear();
    }

    // 保存快照
    timeline.push(response.data, opts.source, {
      description: opts.description || `Load from ${opts.source}`,
      metadata: opts.metadata
    });

    updateHistoryState();
    return true;

  } catch (err: any) {
    // 错误处理
    return false;
  }
};
```

**2. 远程同步专用方法**

```typescript
/**
 * 从 Server 同步数据（保留历史）
 * 专用于 Agent 修改、Server 推送等场景
 */
const syncFromServer = async (options?: {
  description?: string;
  metadata?: Record<string, any>;
}): Promise<boolean> => {
  return loadProject({
    source: ChangeSource.ServerSync,
    preserveView: true,
    preserveHistory: true,
    description: options?.description || 'Sync from server',
    metadata: options?.metadata
  });
};
```

**3. 增强的元素更新方法**

```typescript
const updateModule = (
  moduleId: string,
  updates: Partial<Module>,
  source: ChangeSource = ChangeSource.UserEdit
) => {
  // ... 更新逻辑

  if (!batchUpdateMode.value) {
    nextTick(() => {
      timeline.push(projectData.value!, source, {
        description: `Update module ${moduleId}`,
        changeType: ChangeType.Update,
        affectedIds: [moduleId]
      });
      updateHistoryState();
    });
  }
};
```

**4. 优化的事件监听**

```typescript
// 监听 Server 推送的文件变化
window.addEventListener('bimcanvas:server-update', async (e: any) => {
  const data = e.detail;

  if (data.action === 'reload') {
    await syncFromServer({
      description: 'Server file changed',
      metadata: { trigger: data.trigger }
    });
  }
});
```

---

### 3.4 调用点映射表

| 调用场景 | 原代码 | 重构后代码 | ChangeSource |
|---------|--------|-----------|--------------|
| **App 初始化** | `store.loadProject()` | `store.loadProject(ChangeSource.SystemInit)` | SystemInit（清空历史）|
| **上传项目** | `store.loadProject()` | `store.loadProject(ChangeSource.UserUpload)` | UserUpload（清空历史）|
| **Git 切换分支** | `store.loadProject(true)` | `store.loadProject({ source: ChangeSource.GitCheckout, preserveView: true })` | GitCheckout（清空历史）|
| **Git 放弃更改** | `store.loadProject(true)` | `store.loadProject(ChangeSource.GitDiscard)` | GitDiscard（清空历史）|
| **Server 推送** | `loadProject(true)` | `store.syncFromServer()` | ServerSync（保留历史）✅ |
| **冲突解决** | `store.loadProject()` | `store.loadProject(ChangeSource.SystemRestore)` | SystemRestore（清空历史）|

---

## 四、实施计划（分阶段）

### 阶段 1：基础架构（核心重构）

**目标**：建立新的类型系统和 TimelineManager

#### 任务清单
- [ ] 创建 `src/types/history.ts`
  - 定义 ChangeSource 枚举
  - 定义 HistorySnapshot 接口
  - 定义 ChangeType 枚举
  - 定义 LoadOptions 接口

- [ ] 完全重写 `src/services/state/TimelineManager.ts`
  - 实现增强的 push/undo/redo
  - 添加策略决策方法（shouldClearHistory 等）
  - 添加查询和过滤 API
  - 生成快照ID的工具方法

- [ ] 编写单元测试（可选，但推荐）
  - 测试历史栈基本操作
  - 测试策略决策逻辑
  - 测试边界条件

---

### 阶段 2：Store 重构

**目标**：重构 CanvasStore 的加载和历史逻辑

#### 任务清单
- [ ] 重构 `src/stores/canvasStore.ts` 的 loadProject 方法
  - 修改函数签名支持 LoadOptions
  - 实现智能决策保留历史/视图
  - 添加详细日志记录

- [ ] 新增 syncFromServer 方法
  - 专用于远程同步
  - 默认保留历史和视图

- [ ] 重构 undo/redo 方法
  - 使用增强的 Timeline API
  - 添加日志记录

- [ ] 重构元素更新方法
  - updateModule、addModule、removeModule 等
  - 添加 source 参数（默认 UserEdit）
  - 记录变更详情（affectedIds、changeType）

- [ ] 优化事件监听器
  - 'bimcanvas:server-update' 调用 syncFromServer

---

### 阶段 3：调用点迁移

**目标**：更新所有调用 loadProject 的地方

#### 文件修改清单

**1. App.vue**
- **行号**：约 34 行
- **原代码**：`const loadPromise = store.loadProject()`
- **修改为**：`const loadPromise = store.loadProject(ChangeSource.SystemInit)`

**2. useProjectFile.ts**
- **行号 1**：约 63 行（上传成功）
  - **原代码**：`await store.loadProject();`
  - **修改为**：`await store.loadProject(ChangeSource.UserUpload);`

- **行号 2**：约 87 行（冲突解决）
  - **原代码**：`await store.loadProject();`
  - **修改为**：`await store.loadProject(ChangeSource.SystemRestore);`

**3. gitStore.ts**
- **行号 1**：约 164 行（Git 放弃更改）
  - **原代码**：`await canvasStore.loadProject(true);`
  - **修改为**：`await canvasStore.loadProject(ChangeSource.GitDiscard);`

- **行号 2**：约 229 行（Git 切换分支）
  - **原代码**：`await canvasStore.loadProject(true);`
  - **修改为**：
    ```typescript
    await canvasStore.loadProject({
      source: ChangeSource.GitCheckout,
      preserveView: true
    });
    ```

- **行号 3**：（如果有其他 Git 相关调用）
  - 根据实际场景选择合适的 ChangeSource

---

### 阶段 4：测试验证

**目标**：确保所有场景正常工作

#### 核心功能测试

| 测试场景 | 操作步骤 | 预期结果 |
|---------|----------|----------|
| **A1. Agent 修改可撤回** | 1. Web 加载项目<br>2. Agent 添加模块<br>3. 按 Ctrl+Z | Agent 添加的模块被撤回 ✅ |
| **A2. 连续修改多次撤回** | 1. Agent 添加模块 A<br>2. Agent 移动模块 B<br>3. 按两次 Ctrl+Z | 先撤回移动，再撤回添加 ✅ |
| **A3. Web 操作不受影响** | 1. Web 移动模块<br>2. Agent 添加模块<br>3. 按 Ctrl+Z | 撤回 Agent 添加，保留 Web 移动 ✅ |
| **B1. 上传项目清空历史** | 1. 添加模块<br>2. 上传新项目<br>3. 按 Ctrl+Z | 无法撤回（历史已清空）✅ |
| **B2. Git 切换分支清空历史** | 1. 在分支 A 添加模块<br>2. 切换到分支 B<br>3. 按 Ctrl+Z | 无法撤回（历史已清空）✅ |
| **B3. Git 放弃更改清空历史** | 1. 添加模块<br>2. Git Discard<br>3. 按 Ctrl+Z | 无法撤回（历史已清空）✅ |

#### 日志验证

**预期日志序列（Agent 修改场景）**：
```
[Store] Server update received {...}
[Store] Loading project... { source: 'server_sync', preserveHistory: true, preserveView: true }
[Timeline] Pushed snapshot: server_sync { index: 5, total: 6, description: 'Server file changed' }
[Store] Project loaded: MyProject
```

**预期日志序列（Git 切换分支场景）**：
```
[Store] Loading project... { source: 'git_checkout', preserveHistory: false, preserveView: true }
[Timeline] Clearing history due to source type
[Timeline] Pushed snapshot: git_checkout { index: 0, total: 1, description: 'Load from git_checkout' }
[Store] Project loaded: MyProject
```

#### 架构质量验证

- [ ] 每个方法职责单一（单一职责原则）
- [ ] 历史管理与数据加载解耦（关注点分离）
- [ ] 策略配置集中管理（可维护性）
- [ ] 新增场景只需修改枚举和配置（扩展性）

---

### 阶段 5：调试工具（可选）

**目标**：帮助开发者理解历史栈

#### 历史可视化面板

**文件**：`src/components/Debug/HistoryVisualizer.vue`（新建）

**功能**：
- 显示所有快照的列表
- 按来源分类/过滤（UserEdit、AgentModify 等）
- 显示每个快照的时间戳、描述、受影响的对象
- 支持点击跳转到任意快照（未来）

**集成位置**：DebugConsole 或独立面板

---

## 五、关键文件清单

### 需要新建的文件（1个）

| 文件 | 职责 | 预计行数 |
|------|------|---------|
| **`src/types/history.ts`** | 定义核心类型系统 | 80 行 |

### 需要完全重写的文件（1个）

| 文件 | 原行数 | 重构后行数 | 复杂度 |
|------|--------|-----------|--------|
| **`src/services/state/TimelineManager.ts`** | 58 行 | 200+ 行 | ⭐⭐⭐⭐ |

### 需要重构的文件（4个）

| 文件 | 修改点数量 | 修改复杂度 | 核心变化 |
|------|----------|------------|---------|
| **`src/stores/canvasStore.ts`** | 5 处 | ⭐⭐⭐⭐ | loadProject 重构、syncFromServer 新增、元素更新方法增强 |
| **`src/stores/gitStore.ts`** | 2-3 处 | ⭐⭐ | 更新 loadProject 调用 |
| **`src/composables/useProjectFile.ts`** | 2 处 | ⭐⭐ | 更新 loadProject 调用 |
| **`src/App.vue`** | 1 处 | ⭐ | 更新初始化调用 |

### 可选新建的文件（调试工具）

| 文件 | 职责 | 预计行数 |
|------|------|---------|
| `src/components/Debug/HistoryVisualizer.vue` | 历史可视化面板 | 150 行 |

---

## 六、风险评估与缓解

### 6.1 重构风险

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| **改动较大** | 可能引入新 Bug | 分阶段实施，每阶段充分测试 |
| **测试成本高** | 需要测试所有场景 | 编写自动化测试，覆盖核心场景 |
| **学习成本** | 团队需要理解新类型系统 | 编写详细文档，代码审查讲解 |
| **调用点遗漏** | 某些场景未迁移 | 全局搜索 `loadProject(`，逐个检查 |

### 6.2 性能影响

**问题**：HistorySnapshot 增加了字段，内存占用增加
**评估**：
- 新增字段主要是字符串和小对象
- 单个快照增加约 100-200 字节
- 50 条历史约增加 5-10KB 内存

**结论**：影响可忽略

### 6.3 协作场景（未来）

**问题**：多个 Web 客户端同时连接
**现状**：当前为单用户模式
**未来扩展**：
- 使用 `HistorySnapshot.metadata` 记录用户会话ID
- 实现冲突检测和解决机制（基于 affectedIds）
- 支持 Operational Transformation 或 CRDT

---

## 七、未来扩展路径

### 7.1 协作冲突解决

**架构支持**：
```typescript
interface ConflictSnapshot extends HistorySnapshot {
  conflictWith: string;           // 冲突的快照ID
  resolution?: 'local' | 'remote' | 'merged';
}

class TimelineManager {
  public detectConflict(
    localSnapshot: HistorySnapshot,
    remoteSnapshot: HistorySnapshot
  ): ConflictSnapshot | null {
    // 基于 affectedIds 判断冲突
  }

  public resolveConflict(
    conflict: ConflictSnapshot,
    strategy: 'local' | 'remote' | 'merge'
  ): HistorySnapshot {
    // 解决冲突，生成新快照
  }
}
```

### 7.2 操作回放

**架构支持**：
```typescript
class TimelineManager {
  public replayFromSnapshot(snapshotId: string): void {
    const index = this.snapshots.findIndex(s => s.id === snapshotId);
    if (index !== -1) {
      this.currentIndex = index;
      // 触发状态恢复
    }
  }

  public getOperationSequence(
    startId: string,
    endId: string
  ): HistorySnapshot[] {
    // 获取两个快照之间的所有操作序列
  }
}
```

### 7.3 分支式历史（Git-like）

**架构支持**：
```typescript
interface HistoryBranch {
  name: string;
  snapshots: HistorySnapshot[];
  parent?: HistoryBranch;
}

class TimelineManager {
  private branches: Map<string, HistoryBranch> = new Map();
  private currentBranch: string = 'main';

  public createBranch(name: string): void;
  public switchBranch(name: string): void;
  public mergeBranch(source: string, target: string): void;
}
```

---

## 八、实施建议

### 预计时间
- **阶段 1**（基础架构）：2-3 小时
- **阶段 2**（Store 重构）：3-4 小时
- **阶段 3**（调用点迁移）：1-2 小时
- **阶段 4**（测试验证）：2-3 小时
- **总计**：8-12 小时

### 实施顺序
1. 先实现 `src/types/history.ts`（类型定义）
2. 重构 `TimelineManager.ts`（核心逻辑）
3. 重构 `canvasStore.ts` 的 loadProject 和 syncFromServer
4. 逐个迁移调用点（按文件顺序）
5. 全面测试验证
6. 可选：实现历史可视化面板

### 注意事项
- **类型安全**：确保所有 loadProject 调用都传入 ChangeSource
- **日志完整**：每个关键操作都记录日志，便于调试
- **分支开发**：在独立分支开发，充分测试后合并
- **代码审查**：重构完成后进行 Code Review

---

## 九、验证检查清单

### 代码修改
- [ ] 创建 `src/types/history.ts`
- [ ] 重写 `src/services/state/TimelineManager.ts`
- [ ] 重构 `src/stores/canvasStore.ts`
  - [ ] loadProject 方法
  - [ ] syncFromServer 方法
  - [ ] undo/redo 方法
  - [ ] updateModule 等元素更新方法
  - [ ] 事件监听器
- [ ] 更新 `src/App.vue` 调用点
- [ ] 更新 `src/composables/useProjectFile.ts` 调用点
- [ ] 更新 `src/stores/gitStore.ts` 调用点

### 测试验证
- [ ] A1：Agent 修改可撤回
- [ ] A2：连续修改多次撤回
- [ ] A3：Web 操作不受影响
- [ ] B1：上传项目清空历史
- [ ] B2：Git 切换分支清空历史
- [ ] B3：Git 放弃更改清空历史

### 日志验证
- [ ] 远程同步日志显示 `source: 'server_sync'`
- [ ] Git 操作日志显示 `source: 'git_checkout'`
- [ ] 历史保留/清空逻辑正确执行

### 架构质量
- [ ] 代码职责清晰，易于理解
- [ ] 新增场景只需修改配置
- [ ] 类型系统完整，无 any 类型滥用

---

## 十、预期效果

### 功能效果
1. ✅ **Agent 修改可撤回**：用户按 Ctrl+Z 可以撤销 Agent 的修改
2. ✅ **历史完整保留**：Web 手动操作和 Agent 修改都进入历史
3. ✅ **Git 操作正常**：切换分支、上传项目仍清空历史（符合预期）
4. ✅ **来源可追溯**：每个快照都记录来源和上下文

### 架构效果
1. ✅ **职责分离**：数据加载归加载，历史管理归历史
2. ✅ **易于扩展**：新增场景只需添加枚举和配置
3. ✅ **面向未来**：支持协作、回放、分支等高级功能
4. ✅ **代码自明**：方法名、参数名清楚表达意图

---

## 附录 A：TimelineManager 完整实现

```typescript
import type { ProjectData } from '@/types';
import { ChangeSource, ChangeType, type HistorySnapshot } from '@/types/history';

/**
 * 时间线管理器 - 增强版
 *
 * 职责：
 * 1. 管理历史快照栈（支持 Undo/Redo）
 * 2. 根据变更来源智能决策历史策略
 * 3. 提供历史查询和过滤功能
 * 4. 支持操作回放和协作冲突解决（未来）
 */
export class TimelineManager {
  private snapshots: HistorySnapshot[] = [];
  private currentIndex: number = -1;
  private maxHistory: number = 50;

  // ========== 策略配置 ==========

  /**
   * 哪些来源会清空历史
   */
  private readonly CLEAR_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.UserUpload,
    ChangeSource.GitInit,
    ChangeSource.SystemInit
  ]);

  /**
   * 哪些来源会保留历史
   */
  private readonly PRESERVE_HISTORY_SOURCES = new Set<ChangeSource>([
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.CollabSync,
    ChangeSource.UserEdit
  ]);

  /**
   * 哪些来源会保持视图
   */
  private readonly PRESERVE_VIEW_SOURCES = new Set<ChangeSource>([
    ChangeSource.GitCheckout,
    ChangeSource.GitDiscard,
    ChangeSource.AgentModify,
    ChangeSource.ServerSync,
    ChangeSource.UserUndo,
    ChangeSource.UserRedo
  ]);

  // ========== 核心 API ==========

  /**
   * 推入新快照
   */
  public push(
    state: ProjectData,
    source: ChangeSource,
    options?: {
      description?: string;
      changeType?: ChangeType;
      affectedIds?: string[];
      metadata?: Record<string, any>;
    }
  ): void {
    // 如果不在历史末尾，丢弃未来状态
    if (this.currentIndex < this.snapshots.length - 1) {
      this.snapshots = this.snapshots.slice(0, this.currentIndex + 1);
    }

    // 创建快照
    const snapshot: HistorySnapshot = {
      id: this.generateSnapshotId(),
      timestamp: Date.now(),
      state: JSON.stringify(state),
      source,
      description: options?.description,
      changeType: options?.changeType,
      affectedIds: options?.affectedIds,
      metadata: options?.metadata
    };

    this.snapshots.push(snapshot);
    this.currentIndex++;

    // 限制历史大小
    if (this.snapshots.length > this.maxHistory) {
      this.snapshots.shift();
      this.currentIndex--;
    }

    console.log(`[Timeline] Pushed snapshot: ${source}`, {
      index: this.currentIndex,
      total: this.snapshots.length,
      description: options?.description
    });
  }

  /**
   * 撤销（返回上一个快照）
   */
  public undo(): HistorySnapshot | null {
    if (!this.canUndo) return null;

    this.currentIndex--;
    const snapshot = this.snapshots[this.currentIndex];

    console.log(`[Timeline] Undo to snapshot: ${snapshot.source}`, {
      index: this.currentIndex,
      timestamp: new Date(snapshot.timestamp).toLocaleString()
    });

    return snapshot;
  }

  /**
   * 重做（返回下一个快照）
   */
  public redo(): HistorySnapshot | null {
    if (!this.canRedo) return null;

    this.currentIndex++;
    const snapshot = this.snapshots[this.currentIndex];

    console.log(`[Timeline] Redo to snapshot: ${snapshot.source}`, {
      index: this.currentIndex,
      timestamp: new Date(snapshot.timestamp).toLocaleString()
    });

    return snapshot;
  }

  /**
   * 清空历史
   */
  public clear(): void {
    this.snapshots = [];
    this.currentIndex = -1;
    console.log('[Timeline] History cleared');
  }

  // ========== 策略决策 API ==========

  public shouldClearHistory(source: ChangeSource): boolean {
    return this.CLEAR_HISTORY_SOURCES.has(source);
  }

  public shouldPreserveHistory(source: ChangeSource): boolean {
    return this.PRESERVE_HISTORY_SOURCES.has(source);
  }

  public shouldPreserveView(source: ChangeSource): boolean {
    return this.PRESERVE_VIEW_SOURCES.has(source);
  }

  // ========== 查询 API ==========

  public getCurrentSnapshot(): HistorySnapshot | null {
    return this.snapshots[this.currentIndex] || null;
  }

  public getAllSnapshots(): ReadonlyArray<HistorySnapshot> {
    return [...this.snapshots];
  }

  public getSnapshotsBySource(source: ChangeSource): HistorySnapshot[] {
    return this.snapshots.filter(s => s.source === source);
  }

  public getSnapshotsByTimeRange(start: number, end: number): HistorySnapshot[] {
    return this.snapshots.filter(s => s.timestamp >= start && s.timestamp <= end);
  }

  // ========== 状态 Getters ==========

  public get canUndo(): boolean {
    return this.currentIndex > 0;
  }

  public get canRedo(): boolean {
    return this.currentIndex < this.snapshots.length - 1;
  }

  public get historyLength(): number {
    return this.snapshots.length;
  }

  // ========== 私有方法 ==========

  private generateSnapshotId(): string {
    return `snapshot_${Date.now()}_${Math.random().toString(36).substring(7)}`;
  }
}
```

---

## 结论

这个重构方案通过引入**变更源追踪**和**策略驱动**的设计，彻底解决了 Agent 修改无法撤回的问题，同时为未来的协作、冲突解决、操作回放等高级功能打下了坚实的架构基础。

**核心原则**：
- 职责清晰 → 易于理解和维护
- 策略集中 → 易于扩展和配置
- 类型安全 → 减少运行时错误
- 面向未来 → 支持高级功能扩展
