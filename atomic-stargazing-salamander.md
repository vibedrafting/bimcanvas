# AICommandCenter.vue 代码拆分重构计划

## 一、重构目标

**现状问题**：
- AICommandCenter.vue 当前 **4716 行**，远超 Vue 官方推荐的单文件组件规模（300-500行）
- Script 1977 行 + Template 2200 行 + Style 540 行
- 多个独立功能模块耦合在单文件中，维护困难

**重构目标**：
- 提取 6 个独立 Composables，减少约 **1100 行代码**
- 提升可维护性和可测试性
- **保证现有功能 100% 兼容**，无破坏性修改

---

## 二、三阶段重构方案

### Phase 1: 提取独立 Composables（低风险 → 高收益）

#### 1.1 创建 `useModelConfig.ts`
**路径**: `src/composables/useModelConfig.ts`

**职责**:
- 管理模型列表（加载/保存/添加）
- 管理 Thinking Level 选择

**导出 API**:
```typescript
export function useModelConfig(agentApiBase: string) {
  const models = ref<{ id: string; label: string }[]>([]);
  const currentModel = ref<{ id: string; label: string } | null>(null);
  const thinkingLevels = [
    { id: 'none', label: '无' },
    { id: 'low', label: '低' },
    { id: 'medium', label: '中' },
    { id: 'high', label: '高' }
  ];
  const currentThinking = ref(thinkingLevels[0]);

  const fetchConfig = async () => { /* ... */ };
  const selectModel = (model: { id: string; label: string }) => { /* ... */ };
  const selectThinking = (level: { id: string; label: string }) => { /* ... */ };
  const addCustomModel = async (modelId: string) => { /* ... */ };

  return { models, currentModel, thinkingLevels, currentThinking, fetchConfig, selectModel, selectThinking, addCustomModel };
}
```

**迁移代码清单**:
- Lines 1754-1823: 模型/思考状态定义
- Lines 1203-1248: `fetchAgentConfig` 函数
- Lines 1776-1819: 保存/添加模型逻辑

**在 AICommandCenter.vue 中使用**:
```typescript
// 原代码
const models = ref<{ id: string; label: string }[]>([]);
const currentModel = ref<{ id: string; label: string } | null>(null);
// ... 以及相关函数

// 新代码
import { useModelConfig } from '@/composables/useModelConfig';
const modelConfig = useModelConfig(AGENT_API_BASE);
const { models, currentModel, thinkingLevels, currentThinking, fetchConfig, selectModel, selectThinking, addCustomModel } = modelConfig;
```

---

#### 1.2 创建 `useAutoScroll.ts`
**路径**: `src/composables/useAutoScroll.ts`

**职责**:
- 管理滚动容器 refs
- 自动滚动逻辑
- 滚动位置检测

**导出 API**:
```typescript
export function useAutoScroll() {
  const scrollRefs = ref<Record<string, HTMLElement | null>>({});
  const bottomRefs = ref<Record<string, HTMLElement | null>>({});

  const setScrollRef = (id: string, el: HTMLElement | null) => {
    scrollRefs.value[id] = el;
  };

  const setBottomRef = (id: string, el: HTMLElement | null) => {
    bottomRefs.value[id] = el;
  };

  const scrollToBottom = (options?: { windowId?: string; force?: boolean }) => { /* ... */ };
  const isNearBottom = (windowId?: string) => { /* ... */ };

  return { scrollRefs, bottomRefs, setScrollRef, setBottomRef, scrollToBottom, isNearBottom };
}
```

**迁移代码清单**:
- Lines 811-846: 滚动 refs 管理
- Lines 1660-1682: `scrollToBottom` 函数
- Lines 1168-1183: `isNearBottom` + `handleChatScroll`

**在 AICommandCenter.vue 中使用**:
```vue
<!-- 原模板 -->
<div :ref="(el) => { chatScrollRefs[window.id] = el as HTMLElement }">

<!-- 新模板 -->
<div :ref="(el) => setScrollRef(window.id, el as HTMLElement)">
```

---

#### 1.3 创建 `useUIState.ts`
**路径**: `src/composables/useUIState.ts`

**职责**:
- 管理所有 dropdown/dialog 开关状态
- 全局点击事件处理

**导出 API**:
```typescript
export function useUIState() {
  // Dropdown 状态
  const isBranchDropdownOpen = ref(false);
  const showNewWindowDropdown = ref(false);
  const isContextMenuOpen = ref(false);
  const isModelMenuOpen = ref(false);
  const isThinkingMenuOpen = ref(false);
  const isAttachmentMenuOpen = ref(false);

  // Dialog 状态
  const showCheckoutConfirmDialog = ref(false);
  const showBranchCreationDialog = ref(false);
  const showScreenshotOverlay = ref(false);

  // 其他状态
  const mode = ref<'chat' | 'tasks'>('chat');
  const taskWidgetExpanded = ref(false);

  const toggleBranchDropdown = () => { isBranchDropdownOpen.value = !isBranchDropdownOpen.value; };
  const toggleContextMenu = () => { isContextMenuOpen.value = !isContextMenuOpen.value; };
  const closeAllDropdowns = () => { /* ... */ };
  const handleGlobalClick = (e: MouseEvent) => { /* ... */ };

  onMounted(() => {
    document.addEventListener('click', handleGlobalClick);
  });

  onUnmounted(() => {
    document.removeEventListener('click', handleGlobalClick);
  });

  return { /* 所有状态和方法 */ };
}
```

**迁移代码清单**:
- Lines 165-170: Dropdown 状态
- Lines 1732-1751: Context/Attachment Menu
- Lines 1767-1768: Model/Thinking Menu
- Lines 1881-1912: 全局点击处理

---

### Phase 2: 提取核心逻辑 Composables（中等风险）

#### 2.1 创建 `useWindowManager.ts`
**路径**: `src/composables/useWindowManager.ts`

**职责**:
- 管理多窗口状态（ChatWindow[]）
- 窗口切换/添加/关闭
- Worktree 映射管理

**导出 API**:
```typescript
export function useWindowManager(deps: {
  gitStore: ReturnType<typeof useGitStore>;
  canvasStore: ReturnType<typeof useCanvasStore>;
  currentBranch: Ref<string | null>;
}) {
  const windows = ref<ChatWindow[]>([]);
  const activeWindowId = ref<string>('');

  const activeWindow = computed(() => windows.value.find(w => w.id === activeWindowId.value));

  const initDefaultWindow = () => { /* ... */ };
  const switchWindow = async (id: string) => { /* ... */ };
  const addWindow = async (branchName: string) => { /* ... */ };
  const closeWindow = async (id: string) => { /* ... */ };

  // 消息操作
  const addMessage = (message: ChatMessage) => { /* ... */ };
  const addMessageToWindow = (windowId: string, message: ChatMessage) => { /* ... */ };
  const getWindowMessage = (windowId: string, msgIndex: number) => { /* ... */ };

  return { windows, activeWindowId, activeWindow, initDefaultWindow, switchWindow, addWindow, closeWindow, addMessage, addMessageToWindow, getWindowMessage };
}
```

**迁移代码清单**:
- Lines 225-283: ChatWindow 接口 + 状态
- Lines 251-269: `initDefaultWindow`
- Lines 330-371: `switchWindow`
- Lines 642-737: `addWindow`
- Lines 475-546: `closeWindow`
- Lines 295-325: 消息操作函数

**依赖关系**:
- 依赖: gitStore, canvasStore, currentBranch
- 被依赖: Agent Stream, Branch Manager

---

#### 2.2 创建 `useBranchManager.ts`
**路径**: `src/composables/useBranchManager.ts`

**职责**:
- 分支切换逻辑
- 未提交更改处理
- 分支创建对话框协调

**导出 API**:
```typescript
export function useBranchManager(deps: {
  gitStore: ReturnType<typeof useGitStore>;
  canvasStore: ReturnType<typeof useCanvasStore>;
  windowManager: ReturnType<typeof useWindowManager>;
}) {
  const pendingCheckoutBranch = ref('');
  const pendingWindowId = ref('');
  const pendingIsCreateBranch = ref(false);

  const selectBranch = async (branchId: string) => { /* ... */ };
  const handleCheckoutConfirm = async (save: boolean, msg?: string) => { /* ... */ };
  const handleCheckoutCancel = () => { /* ... */ };
  const handleBranchCreated = async (data: { name: string; baseBranch: string; reason: string }) => { /* ... */ };

  const isBranchOccupied = (branchName: string) => { /* ... */ };
  const isBranchOccupiedByOther = (branchName: string) => { /* ... */ };

  return { /* 所有状态和方法 */ };
}
```

**迁移代码清单**:
- Lines 167-169: Checkout 状态
- Lines 1002-1030: `selectBranch`
- Lines 1033-1088: Checkout Confirm/Cancel
- Lines 407-459: `handleBranchCreated`
- Lines 462-471: 分支占用检查

**依赖关系**:
- 依赖: gitStore, canvasStore, windowManager
- 与 windowManager 相互协作

---

#### 2.3 创建 `useAgentStream.ts`
**路径**: `src/composables/useAgentStream.ts`

**职责**:
- SSE 流处理核心逻辑
- 消息发送
- 气泡模型管理

**导出 API**:
```typescript
export function useAgentStream(deps: {
  agentApiBase: string;
  windowManager: ReturnType<typeof useWindowManager>;
  modelConfig: ReturnType<typeof useModelConfig>;
  projectPath: Ref<string>;
}) {
  const sendMessage = async (windowId: string, message: string, images: string[]) => { /* ... */ };
  const streamWelcomeMessage = async () => { /* ... */ };

  return { sendMessage, streamWelcomeMessage };
}
```

**迁移代码清单**:
- Lines 1266-1656: `sendMessage` 核心逻辑
- Lines 1114-1155: `streamWelcomeMessage`

**依赖关系**:
- 依赖: windowManager, modelConfig, projectPath
- 使用: bubbleManager 工具函数

---

## 三、实施步骤

### Step 1: Phase 1 重构（优先执行，风险低）

1. **创建文件骨架**
   ```bash
   touch src/composables/useModelConfig.ts
   touch src/composables/useAutoScroll.ts
   touch src/composables/useUIState.ts
   ```

2. **按顺序迁移**（每次一个 Composable）
   - ✅ 迁移 `useModelConfig.ts`
   - ✅ 迁移 `useAutoScroll.ts`
   - ✅ 迁移 `useUIState.ts`

3. **每次迁移后测试**
   - 模型选择功能正常
   - 自动滚动逻辑正常
   - 下拉菜单开关正常

### Step 2: Phase 2 重构（可选，复杂度较高）

1. **创建文件骨架**
   ```bash
   touch src/composables/useWindowManager.ts
   touch src/composables/useBranchManager.ts
   touch src/composables/useAgentStream.ts
   ```

2. **按依赖顺序迁移**
   - ⭕ 迁移 `useWindowManager.ts`（核心模块）
   - ⭕ 迁移 `useBranchManager.ts`（依赖 WindowManager）
   - ⭕ 迁移 `useAgentStream.ts`（依赖 WindowManager + ModelConfig）

3. **全面测试**
   - 多窗口切换功能
   - 分支切换流程
   - SSE 消息接收

---

## 四、代码量对比

| 模块 | 当前行数 | 重构后行数 | 减少 |
|------|---------|-----------|------|
| AICommandCenter.vue | 4716 | ~3600 | -1116 |
| Composables (新增) | 0 | ~800 | +800 |
| **净减少** | - | - | **-316** |

**可维护性提升**:
- ✅ 单一职责：每个 Composable 只负责一个功能域
- ✅ 独立测试：可为每个 Composable 编写单元测试
- ✅ 复用性：Model Config 等可在其他组件复用

---

## 五、关键文件清单

### 需要修改的文件
1. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\components\UI\AICommandCenter.vue`
   - 主重构目标文件

### 需要创建的文件（Phase 1）
1. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useModelConfig.ts`
2. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useAutoScroll.ts`
3. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useUIState.ts`

### 需要创建的文件（Phase 2，可选）
1. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useWindowManager.ts`
2. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useBranchManager.ts`
3. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useAgentStream.ts`

### 参考文件
1. `E:\工作文档\开发类\MyCode\BIMCanvas\BIMCanvas.Web\src\composables\useSave.ts`
   - 参考模板：Composable 代码风格

---

## 六、验证方案

### 功能测试清单（Phase 1）
- [ ] 模型选择正常工作
- [ ] Thinking Level 切换正常
- [ ] 自定义模型添加功能正常
- [ ] 自动滚动逻辑正常
- [ ] 滚动到底部功能正常
- [ ] 所有下拉菜单开关正常
- [ ] 全局点击关闭菜单正常
- [ ] 对话框显示/关闭正常

### 功能测试清单（Phase 2）
- [ ] 多窗口切换无错
- [ ] 窗口关闭正常
- [ ] 新建窗口功能正常
- [ ] 分支切换流程正常
- [ ] 未提交更改警告正常
- [ ] SSE 消息接收正常
- [ ] 气泡模型渲染正常

### 性能测试
- [ ] 组件初始化时间 < 500ms
- [ ] SSE 流处理无卡顿
- [ ] 窗口切换流畅度

---

## 七、风险评估

### 低风险（Phase 1）
- ✅ **useModelConfig**: 完全独立，无外部副作用
- ✅ **useAutoScroll**: 纯 DOM 操作，易于测试
- ✅ **useUIState**: 简单开关逻辑，易于验证

### 中等风险（Phase 2）
- ⚠️ **useWindowManager**: 窗口状态管理复杂，需仔细测试
  - **缓解**: 保留原有测试用例，逐步迁移
- ⚠️ **useBranchManager**: 与 Git Store 深度耦合
  - **缓解**: 通过依赖注入传递 Store，保持可测试性
- ⚠️ **useAgentStream**: SSE 流处理关键
  - **缓解**: 先提取工具函数，保留核心逻辑在主组件

---

## 八、依赖关系图

```
┌─────────────────────────────────────────────────┐
│         AICommandCenter.vue (主组件)              │
└───────────┬─────────────────────────────────────┘
            │
            ├─→ [Phase 1: 独立模块]
            │   ├─→ useModelConfig (无依赖)
            │   ├─→ useAutoScroll (无依赖)
            │   └─→ useUIState (无依赖)
            │
            ├─→ [Phase 2: 核心模块]
            │   ├─→ useWindowManager ──┐
            │   ├─→ useBranchManager ──┼─→ 相互依赖
            │   └─→ useAgentStream ────┘
            │
            └─→ [External Dependencies]
                ├─→ canvasStore (Pinia)
                ├─→ gitStore (Pinia)
                └─→ Services (ProjectService, GitWorktreeService, etc.)
```

---

## 九、后续优化建议

1. **TypeScript 类型强化**: 为 Composables 添加完整类型注解
2. **单元测试覆盖**: 为每个 Composable 编写 Vitest 测试
3. **文档完善**: 为每个 Composable 编写 JSDoc 注释
4. **性能优化**: 使用 `shallowRef` 优化大对象响应性

---

**总结**: 本重构方案采用渐进式策略，从低风险高收益的独立模块开始（Phase 1），逐步提取核心逻辑（Phase 2）。**建议优先执行 Phase 1**，即可减少约 300 行代码，显著提升可维护性。Phase 2 可根据后续需求选择性执行。
