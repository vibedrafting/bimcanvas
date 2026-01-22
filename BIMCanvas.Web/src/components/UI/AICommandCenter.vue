<script setup lang="ts">
import { ref, onMounted, nextTick, computed, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { useGitStore } from '../../stores/gitStore';
import { ProjectService } from '../../services/ProjectService';
import { getScreenshotService } from '../../services/ScreenshotService';
import { storeToRefs } from 'pinia';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';
import BranchCreationDialog from './Ribbon/BranchCreationDialog.vue';
import type { SubAgent, ToolCall, ChatBubble, WaitingState } from '../../types/agent';
import { GitWorktreeService } from '../../services/GitWorktreeService';
import { SignalRService } from '../../services/SignalRService';
import {
  createTextBubble,
  createToolCallBubble,
  createSubAgentBubble,
  enterWaitingState,
  exitWaitingState,
  hasStreamingSubAgent,
  findBubbleByIdDeep,
  getLastStreamingTextBubble,
  completeBubble,
  failBubble,
  appendToolCallOutput,
  updateSubAgentResult,
  markAsBackground,
  findStreamingSubAgents
} from '../../utils/bubbleManager';
import ToolCallBubble from './ToolCallBubble.vue';
import SubAgentBubble from './SubAgentBubble.vue';
import WaitingIndicator from './WaitingIndicator.vue';
// ... imports
import GlassSelect from './base/GlassSelect.vue';

const props = defineProps<{
  panelReady?: boolean;
}>();

// ... existing code ...

// Icons
const branchIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="3" x2="6" y2="15"></line><circle cx="18" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M18 9a9 9 0 0 1-9 9"></path></svg>';
const createIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>';

// Computed: Options for Primary Window Branch Select (Switch Mode - includes Create New)
const primaryWindowBranchOptions = computed(() => [
  ...branches.value.map(b => ({
    label: b.name,
    value: b.name, // Use name as value for consistency with current logic
    icon: branchIcon,
    tags: b.commit ? [b.commit.message.substring(0, 25) + (b.commit.message.length > 25 ? '...' : '')] : [],
  })),
  {
    label: '新建分支...',
    value: '__create_new__',
    icon: createIcon
  }
]);

// Computed: Options for New Window Branch Select (Select Only - NO Create New)
const newWindowBranchOptions = computed(() =>
  branches.value.map(b => ({
    label: b.name,
    value: b.name,
    icon: branchIcon,
    tags: b.commit ? [b.commit.message.substring(0, 25) + (b.commit.message.length > 25 ? '...' : '')] : []
  }))
);

// Handle Primary Window Branch Change
const handlePrimaryBranchChange = (val: string | number) => {
  if (val === '__create_new__') {
    handleCreateNewBranchForPrimary();
    return;
  }
  
  const branchName = val as string;
  // Check if occupied by other window
  if (isBranchOccupiedByOther(branchName)) {
    // Optional: Show warning or prevent switch
    console.warn('Branch is occupied by another window');
    // For now we allow it but maybe we should show a toast
  }
  
  selectBranch(branchName);
};

// Handle New Window Branch Selection
const handleNewWindowBranchSelect = (val: string | number) => {
  if (val === '__create_new__') {
    handleCreateNewBranch();
    return;
  }
  
  const branchName = val as string;
  addWindow(branchName);
};

// ... existing code ...

// In Template: Replace Primary Window Dropdown
/*
<div class="window-tab primary" ...>
  ...
  <GlassSelect
    :model-value="win.branchId"
    @update:model-value="handlePrimaryBranchChange"
    :options="primaryWindowBranchOptions"
    width="140px"
    variant="glass" 
    class="branch-select-override"
  />
  ...
</div>
*/

// In Template: Replace New Window Button/Dropdown
/*
<div class="new-window-wrapper">
   <GlassSelect
      placeholder="New Window..."
      :options="newWindowBranchOptions"
      @update:model-value="handleNewWindowBranchSelect"
      width="40px" 
      variant="glass"
      :model-value="null" 
   >
     <template #trigger>
       <button class="add-window-btn" title="New Window">
         <svg ...>+</svg>
       </button>
     </template>
   </GlassSelect>
</div>
*/
// Wait, GlassSelect doesn't support custom trigger slot yet. 
// I should use GlassSelect normally but maybe style it to look like the add button? 
// Or better, keep the add button and use a hidden GlassSelect or just use GlassSelect as the "New Window" action itself.

// Actually, the user wants "New Virtual Window" to have the unified style. 
// The current UI has a "+" button that opens a dropdown.
// If I replace it with a GlassSelect, it will look like a select box.
// Maybe I should modify GlassSelect to support a custom trigger or just use it as is but small?

// Let's look at the screenshot. The "New Window" is a "+" tab.
// If I use GlassSelect there, it might look weird if it's a full select box.
// However, the user said "New Virtual Window" is one of the places to unify.
// Maybe they mean the dropdown *content* style, not necessarily the trigger.
// But GlassSelect bundles trigger and dropdown.

// Strategy:
// 1. For Primary Window: Replace the text/icon branch display with GlassSelect.
// 2. For New Window: The "+" button is iconic. 
//    I will try to use GlassSelect but maybe with a very minimal width or custom style to mimic the tab?
//    OR, I can add a `customTrigger` slot to `GlassSelect` to allow using the "+" button as the trigger.
//    This seems like the best approach to maintain the "+" tab aesthetic while getting the unified dropdown.

// Let's first add the slot to GlassSelect.vue.

// API Configuration
const AGENT_API_BASE = 'http://127.0.0.1:8765';

const panelWidth = ref(480);
const isResizing = ref(false);
const isBranchDropdownOpen = ref(false);
const showCheckoutConfirmDialog = ref(false);
const pendingCheckoutBranch = ref('');
const pendingWindowId = ref('');  // 窗口标签页切换时记录目标窗口ID
const pendingIsCreateBranch = ref(false);  // 是否是新建分支场景
const mode = ref('chat'); // 'chat' | 'tasks'
const isTaskSummaryExpanded = ref(false);

// Git Store - 使用共享Store管理分支状态
const gitStore = useGitStore();
const { branches, currentBranch, isLoading: isBranchLoading } = storeToRefs(gitStore);

// Store Integration
const store = useCanvasStore();
const { selectedIds } = storeToRefs(store);

// Computed Selection State - 使用 selectedIds 作为选中数量的数据源
const selectedModuleCount = computed(() => {
  return selectedIds.value.length;
});

// Debug watcher


// Sticky Scope State
const activeScope = ref('Global');

// Watch selection to update scope (Sticky Logic)
watch(selectedModuleCount, (count) => {
  if (count > 0) {
    // In a real app, we would find the room of the selected item here.
    // For now, we simulate it being 'Living Room'.
    activeScope.value = 'Living Room';
  }
  // If count === 0, we DO NOT reset activeScope, keeping it "sticky".
});

// Agent connection state
const agentStatus = ref<'connecting' | 'connected' | 'disconnected'>('disconnected');
const currentProjectPath = ref('');

// Chat state - 使用时间线气泡模型
interface ChatMessage {
  role: 'user' | 'ai';
  /** 是否正在流式传输 */
  isStreaming?: boolean;
  /** 开始时间戳 */
  startTime?: number;
  /** 结束时间戳 */
  endTime?: number;
  /** 思考内容 */
  thinking?: string;
  /** 思考持续时间 */
  thinkingDuration?: string;
  /** 时间线气泡列表（核心数据结构） */
  bubbles: ChatBubble[];
  /** 等待状态 */
  waitingState: WaitingState;
}

// === 多窗口聊天数据结构 (Phase 2: 窗口隔离架构) ===
interface ChatWindow {
  id: string;
  name: string;
  branchId: string;
  messages: ChatMessage[];
  isPrimary: boolean;
  // === 后端 Worktree 关联字段 ===
  worktreeName?: string;  // 后端 Worktree 名称（虚拟窗口必填）
  worktreePath?: string;  // 后端 Worktree 完整路径（虚拟窗口必填）
  isLoading?: boolean;    // 加载状态（创建/删除中）
  error?: string | null;  // 错误信息
  // === 窗口隔离状态（Phase 2 新增）===
  inputMessage: string;           // 输入框内容
  isStreaming: boolean;           // 正在接收SSE流
  pendingImages: string[];        // 待发送截图
  scrollPosition: number;         // 滚动位置
  expandedThinking: Record<number, boolean>; // 思考折叠状态
  shouldAutoScroll: boolean;      // 是否自动滚动
}

// 窗口状态
const windows = ref<ChatWindow[]>([]);
const activeWindowId = ref<string>('');

// 初始化默认窗口
const initDefaultWindow = () => {
  if (windows.value.length > 0) return; // 防止重复初始化
  const defaultId = 'window-main';
  windows.value = [{
    id: defaultId,
    name: 'Main',
    branchId: currentBranch.value || 'main',
    messages: [],
    isPrimary: true,
    // Phase 2: 窗口隔离状态
    inputMessage: '',
    isStreaming: false,
    pendingImages: [],
    scrollPosition: 0,
    expandedThinking: {},
    shouldAutoScroll: true
  }];
  activeWindowId.value = defaultId;
};

// Sync Primary Window branchId with currentBranch from gitStore
// Bug fix: 只在主窗口是当前活跃窗口时才同步 branchId
// 避免创建新窗口时主窗口的分支名跟着变化
watch(currentBranch, (newBranch) => {
  if (!newBranch) return;
  const primaryWindow = windows.value.find(w => w.isPrimary);
  // 只有当主窗口是当前活跃窗口时才同步
  if (primaryWindow &&
      primaryWindow.id === activeWindowId.value &&
      primaryWindow.branchId !== newBranch) {
    primaryWindow.branchId = newBranch;
  }
}, { immediate: true });

// === Phase 2: 核心 computed - 当前活跃窗口 ===
const activeWindow = computed(() =>
  windows.value.find(w => w.id === activeWindowId.value) || windows.value[0]
);

/**
 * 添加消息到当前窗口（Phase 2 简化版）
 * @param message 要添加的消息
 * @returns 消息在窗口消息列表中的索引
 */
const addMessage = (message: ChatMessage): number => {
  const win = activeWindow.value;
  if (!win) return -1;
  const index = win.messages.length;
  win.messages.push(message);
  return index;
};

/**
 * 向指定窗口添加消息（用于 SSE 流定位）
 * @param windowId 窗口ID
 * @param message 要添加的消息
 * @returns 消息索引
 */
const addMessageToWindow = (windowId: string, message: ChatMessage): number => {
  const win = windows.value.find(w => w.id === windowId);
  if (!win) return -1;
  const index = win.messages.length;
  win.messages.push(message);
  return index;
};

/**
 * 获取指定窗口的消息
 * @param windowId 窗口ID
 * @param msgIndex 消息索引
 */
const getWindowMessage = (windowId: string, msgIndex: number): ChatMessage | undefined => {
  const win = windows.value.find(w => w.id === windowId);
  return win?.messages[msgIndex];
};

// === 窗口管理函数 (Phase 4) ===

// 切换窗口（Phase 2: 无消息拷贝，v-show 瞬间切换）
// 核心：通知 Server 切换活跃窗口，然后重新加载项目数据
const switchWindow = async (id: string) => {
  if (activeWindowId.value === id) return;

  const win = windows.value.find(w => w.id === id);
  if (!win) return;

  // 0. 保存当前窗口滚动位置
  const currentWin = activeWindow.value;
  const currentScrollRef = chatScrollRefs.value[activeWindowId.value];
  if (currentScrollRef && currentWin) {
    currentWin.scrollPosition = currentScrollRef.scrollTop;
  }

  // 1. 切换（Phase 2: 不再需要消息拷贝！v-show 保留所有窗口 DOM）
  activeWindowId.value = id;
  branches.value.forEach(b => b.isCurrent = b.id === win.branchId);

  // 2. 通知 Server 激活窗口（让 GetProjectData 知道读取哪个 Worktree）
  try {
    await fetch('http://localhost:5000/api/windows/activate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ windowId: id })
    });
    console.log(`[Window] 激活窗口: ${win.name} (${id})`);
  } catch (e) {
    console.warn('[Window] 通知 Server 激活窗口失败:', e);
  }

  // 3. 重新加载项目数据（始终重新加载，因为 Server 根据 ActiveWindowId 返回不同数据）
  await store.loadProject({ source: 'git_checkout', preserveView: true });
  console.log(`[Window] 重新加载项目数据完成`);

  // 4. 恢复目标窗口滚动位置
  nextTick(() => {
    const targetScrollRef = chatScrollRefs.value[id];
    if (targetScrollRef && win.scrollPosition) {
      targetScrollRef.scrollTop = win.scrollPosition;
    }
  });
};

// State for new window dropdown
const showNewWindowDropdown = ref(false);
const showBranchCreationDialog = ref(false);

// Computed: Available branches (not currently opened in any window)
// Note: Using branch.name for comparison because currentBranch returns branch name, not id
const availableBranches = computed(() => {
    const occupiedBranchNames = windows.value.map(w => w.branchId);
    return branches.value.filter(b => !occupiedBranchNames.includes(b.name));
});

// Computed: Branch options for BranchCreationDialog
const branchOptionsForDialog = computed(() =>
  branches.value.map(b => ({ label: b.name, value: b.id }))
);

// 记录新建分支的来源：'newWindow' 或 'primarySwitch'
const branchCreationSource = ref<'newWindow' | 'primarySwitch'>('newWindow');

// 打开新建分支对话框（从新建窗口下拉框）
const handleCreateNewBranch = () => {
  showNewWindowDropdown.value = false;
  branchCreationSource.value = 'newWindow';
  showBranchCreationDialog.value = true;
};

// 打开新建分支对话框（从主窗口分支切换下拉框）
const handleCreateNewBranchForPrimary = () => {
  isBranchDropdownOpen.value = false;
  branchCreationSource.value = 'primarySwitch';
  showBranchCreationDialog.value = true;
};

// 处理分支创建完成
const handleBranchCreated = async (data: { name: string; baseBranch: string; reason: string }) => {
  showBranchCreationDialog.value = false;

  // 如果是主窗口切换，显示 loading
  const isPrimarySwitch = branchCreationSource.value === 'primarySwitch';
  if (isPrimarySwitch) {
    const primaryWindow = windows.value.find(w => w.isPrimary);
    if (primaryWindow) primaryWindow.isLoading = true;
  }

  try {
    // 创建分支
    const result = await gitStore.checkout(data.name, {
      createIfNotExist: true,
      commitMessage: data.reason,
      baseBranch: data.baseBranch
    });
    if (result.success) {
      // 刷新分支列表
      await gitStore.fetchBranches();

      // 根据来源决定后续操作
      if (branchCreationSource.value === 'newWindow') {
        // 从新建窗口下拉框来的，创建新窗口
        addWindow(data.name);
      }
      // 从主窗口下拉框来的，主窗口 branchId 会通过 watch(currentBranch) 自动更新
      return;
    }
    
    // 如果有未提交的更改，显示确认弹窗
    if (result.hasUncommittedChanges) {
      pendingCheckoutBranch.value = data.name;
      pendingWindowId.value = '';  // 清空窗口ID，这不是窗口切换场景
      pendingIsCreateBranch.value = true;  // 标记为新建分支场景
      showCheckoutConfirmDialog.value = true;
      // 关闭 loading（弹窗期间暂停）
      if (isPrimarySwitch) {
        const primaryWindow = windows.value.find(w => w.isPrimary);
        if (primaryWindow) primaryWindow.isLoading = false;
      }
      return;
    }
    
    console.error('创建/切换分支失败:', result.message);
  } finally {
    // 关闭 loading
    if (isPrimarySwitch) {
      const primaryWindow = windows.value.find(w => w.isPrimary);
      if (primaryWindow) primaryWindow.isLoading = false;
    }
  }
};

// Bug fix: 检查分支是否被其他窗口占用（用于主窗口分支切换下拉列表）
const isBranchOccupiedByOther = (branchName: string): boolean => {
    return windows.value.some(w =>
        w.branchId === branchName && w.id !== activeWindowId.value
    );
};

// 检查分支是否被任何窗口占用（用于新建窗口下拉列表）
const isBranchOccupied = (branchName: string): boolean => {
    return windows.value.some(w => w.branchId === branchName);
};

// Close window
// 对接后端 Git Worktree API 删除对应的 Worktree
const closeWindow = async (id: string) => {
    const index = windows.value.findIndex(w => w.id === id);
    if (index === -1) return;

    const win = windows.value[index];
    if (win.isPrimary) {
        console.warn('[Window] Cannot close primary window');
        return;
    }

    // 如果正在加载中，不允许关闭
    if (win.isLoading) {
        console.warn('[Window] Cannot close window while loading');
        return;
    }

    // 设置加载状态
    win.isLoading = true;
    console.log(`[Window] Closing window: ${win.name}...`);

    // 调用后端 API 删除 Worktree
    try {
        if (win.worktreeName) {
            await GitWorktreeService.deleteWorktree(win.worktreeName, false);
            console.log(`[Window] Worktree deleted: ${win.worktreeName}`);
        }
    } catch (error: any) {
        // 即使删除失败也继续关闭窗口（后端可能已不存在）
        console.error(`[Window] Delete worktree failed: ${error.message}`);
    }

    // 注销窗口 Worktree 映射
    try {
        await fetch(`http://localhost:5000/api/windows/worktree/${id}`, {
            method: 'DELETE'
        });
        console.log(`[Window] 注销 Worktree 映射: ${id}`);
    } catch (error: any) {
        console.warn(`[Window] 注销 Worktree 映射失败: ${error.message}`);
    }

    // 关闭 Agent 实例（释放资源）
    try {
        await fetch(`${AGENT_API_BASE}/api/agent/close`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ windowId: id })
        });
        console.log(`[Window] Agent 实例已关闭: ${id}`);
    } catch (error: any) {
        // 即使关闭失败也继续（Agent可能未创建）
        console.warn(`[Window] 关闭 Agent 实例失败: ${error.message}`);
    }

    // 从 UI 移除窗口（先移除，再切换焦点）
    windows.value.splice(index, 1);
    console.log(`[Window] Closed window: ${win.name}`);

    // 切换焦点（如果关闭的是当前活跃窗口）
    if (activeWindowId.value === id) {
        // 计算新的活跃窗口索引（移除后的数组）
        const newActiveIndex = Math.min(index, windows.value.length - 1);
        const newActiveWin = windows.value[newActiveIndex];
        if (newActiveWin) {
            // Phase 2: 只需切换 activeWindowId，computed 兼容层会自动映射消息
            activeWindowId.value = newActiveWin.id;
            branches.value.forEach(b => b.isCurrent = b.id === newActiveWin.branchId);
            // 重新加载项目数据
            await store.loadProject({ source: 'git_checkout', preserveView: true });
        }
    }
};

// Toggle New Window Dropdown (Exclusive)
// 移除 availableBranches 检查，因为现在有"新建分支"选项，下拉框应总是能打开
const handleNewWindowClick = (event: MouseEvent) => {
    // Close other dropdowns
    isBranchDropdownOpen.value = false;

    // 计算下拉框位置（相对于按钮）
    const btn = event.currentTarget as HTMLElement;
    if (btn) {
        const rect = btn.getBoundingClientRect();
        const parentRect = btn.closest('.header-tabs')?.getBoundingClientRect();
        if (parentRect) {
            const dropdownWidth = 280; // 下拉框宽度
            const viewportWidth = window.innerWidth;
            const spaceOnRight = viewportWidth - rect.left; // 按钮左边缘到视口右边的空间

            // 使用 header-tabs 的高度作为 top，确保与主窗口下拉框对齐
            const top = parentRect.height + 4; // 4px 间距

            if (spaceOnRight >= dropdownWidth + 8) {
                // 右侧空间足够，使用 left 定位（向右展开）
                newWindowDropdownPosition.value = {
                    top,
                    left: rect.left - parentRect.left,
                    right: undefined
                };
            } else {
                // 右侧空间不足，使用 right 定位（向左展开）
                newWindowDropdownPosition.value = {
                    top,
                    left: undefined,
                    right: parentRect.right - rect.right
                };
            }
        }
    }

    showNewWindowDropdown.value = !showNewWindowDropdown.value;
};

// Toggle Branch Switch Dropdown (Exclusive)
const toggleBranchDropdown = () => {
    // Close other dropdowns
    showNewWindowDropdown.value = false;
    
    isBranchDropdownOpen.value = !isBranchDropdownOpen.value;
};

// Handle Window Tab Click - Only for switching windows
// Branch dropdown is triggered by clicking on .tab-branch area (primary window only)
const handleWindowTabClick = async (win: ChatWindow) => {
    if (activeWindowId.value !== win.id) {
        // 如果目标窗口的分支与当前分支不同，需要检查未提交更改
        const currentWin = activeWindow.value;
        if (currentWin && win.branchId !== currentWin.branchId) {
            // 调用 gitStore.checkout 检查是否有未提交更改
            const result = await gitStore.checkout(win.branchId);
            if (result.success) {
                // 切换成功，继续切换窗口
                switchWindow(win.id);
                return;
            }
            if (result.hasUncommittedChanges) {
                // 有未提交更改，显示确认弹窗
                pendingCheckoutBranch.value = win.branchId;
                // 记录目标窗口ID，弹窗确认后需要切换到该窗口
                pendingWindowId.value = win.id;
                showCheckoutConfirmDialog.value = true;
                return;
            }
            console.error('切换分支失败:', result.message);
            return;
        }
        // 同一分支内的窗口切换，直接切换
        switchWindow(win.id);
    }
};

// Add window with selected branch (using branch name as identifier)
// 对接后端 Git Worktree API
const addWindow = async (branchName: string) => {
    const branch = branches.value.find(b => b.name === branchName);
    if (!branch) return;

    const timestamp = Date.now();
    const worktreeName = `window-${timestamp}`;
    const newId = `window-${timestamp}`;
    const windowNumber = windows.value.length + 1;

    // 1. 先在 UI 显示加载状态
    const newWindow: ChatWindow = {
        id: newId,
        name: `Chat ${windowNumber}`,
        branchId: branch.name,
        messages: [],
        isPrimary: false,
        worktreeName,
        isLoading: true,
        error: null,
        // Phase 2: 窗口隔离状态
        inputMessage: '',
        isStreaming: false,
        pendingImages: [],
        scrollPosition: 0,
        expandedThinking: {},
        shouldAutoScroll: true
    };
    windows.value.push(newWindow);
    switchWindow(newId);
    showNewWindowDropdown.value = false;
    console.log(`[Window] Creating window: ${newWindow.name} on branch ${branch.name}...`);

    // 2. 调用后端 API 创建 Worktree
    try {
        await GitWorktreeService.createWorktree({
            name: worktreeName,
            branch: branch.name
        });
        // 成功：更新状态
        const idx = windows.value.findIndex(w => w.id === newId);
        if (idx !== -1) {
            windows.value[idx].isLoading = false;
            console.log(`[Window] Created successfully: ${newWindow.name}`);
        }

        // 3. 注册窗口 Worktree 映射到 Server（用于 Canvas 数据隔离）
        const worktreeInfo = await GitWorktreeService.getWorktrees();
        const createdWorktree = worktreeInfo.find(w => w.name === worktreeName);
        if (createdWorktree) {
            await fetch('http://localhost:5000/api/windows/register-worktree', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    windowId: newId,
                    worktreePath: createdWorktree.path
                })
            });
            console.log(`[Window] 注册 Worktree 映射: ${newId} -> ${createdWorktree.path}`);

            // 保存 worktreePath 到窗口对象（用于 Chat 请求时传递给 Agent）
            const pathIdx = windows.value.findIndex(w => w.id === newId);
            if (pathIdx !== -1) {
                windows.value[pathIdx].worktreePath = createdWorktree.path;
            }

            // 重新加载项目数据（映射注册完成后才能正确读取 Worktree 数据）
            await store.loadProject({ source: 'git_checkout', preserveView: true });
            console.log(`[Window] 重新加载项目数据完成`);
        }

        // 4. 注册窗口到 SignalR 并获取分支锁（用于断开连接时清理资源）
        SignalRService.getInstance().registerWindow(newId, branch.name);

        // 5. 为新窗口初始化欢迎消息
        await streamWelcomeMessage();
    } catch (error: any) {
        // 失败：显示错误
        const idx = windows.value.findIndex(w => w.id === newId);
        if (idx !== -1) {
            windows.value[idx].isLoading = false;
            windows.value[idx].error = error.message || '创建失败';
            console.error(`[Window] Create failed: ${error.message}`);
        }
        // 3秒后自动移除失败的窗口
        setTimeout(() => {
            const idx = windows.value.findIndex(w => w.id === newId);
            if (idx !== -1 && windows.value[idx].error) {
                windows.value.splice(idx, 1);
                // 切换到主窗口
                const primary = windows.value.find(w => w.isPrimary);
                if (primary) switchWindow(primary.id);
            }
        }, 3000);
    }
};

// Claude Code 风格的拟人等待提示词 (169 个)
const WAITING_VERBS = [
  'Accomplishing', 'Actioning', 'Actualizing', 'Baking', 'Beaming', 'Beboppin',
  'Befuddling', 'Billowing', 'Blanching', 'Bloviating', 'Boogieing', 'Boondoggling',
  'Bootstrapping', 'Booping', 'Brewing', 'Burrowing', 'Calculating', 'Caramelizing',
  'Cascading', 'Capturing', 'Cerebrating', 'Channelling', 'Choreographing', 'Churning',
  'Clauding', 'Coalescing', 'Cogitating', 'Composing', 'Combobulating', 'Concocting',
  'Considering', 'Contemplating', 'Cooking', 'Crafting', 'Creating', 'Crunching',
  'Crystallizing', 'Cultivating', 'Deciphering', 'Deliberating', 'Determining',
  'Discombobulating', 'Distilling', 'Doing', 'Dilly-dallying', 'Doodling', 'Ebbing',
  'Effecting', 'Elucidating', 'Embellishing', 'Enchanting', 'Envisioning', 'Evaporating',
  'Fermenting', 'Fiddle-fadding', 'Finagling', 'Flambéing', 'Flibbertigibbeting',
  'Flowing', 'Flummoxing', 'Forging', 'Forming', 'Frosting', 'Frolicking', 'Gallivanting',
  'Generating', 'Germinating', 'Gitifying', 'Grooving', 'Gusting', 'Hatching', 'Herding',
  'Hibernating', 'Honking', 'Hullaballooing', 'Hyperspacing', 'Ideating', 'Imagining',
  'Incubating', 'Inferring', 'Infusing', 'Jitterbugging', 'Julienning', 'Kneading',
  'Leavening', 'Levitating', 'Lollygagging', 'Manifesting', 'Marinating', 'Meandering',
  'Misting', 'Moseying', 'Mulling', 'Mustering', 'Musing', 'Nebulizing', 'Noodling',
  'Nucleating', 'Orbiting', 'Perambulating', 'Percolating', 'Perusing', 'Philosophising',
  'Photosynthesizing', 'Pontificating', 'Pondering', 'Pollinating', 'Precipitating',
  'Processing', 'Proofing', 'Propagating', 'Puttering', 'Puzzling', 'Quantumizing',
  'Razzle-dazzling', 'Recombobulating', 'Reticulating', 'Ruminating', 'Scheming',
  'Schlepping', 'Scurrying', 'Scampering', 'Seasoning', 'Shenaniganing', 'Shimming',
  'Shimmying', 'Simmering', 'Skedaddling', 'Sketching', 'Slithering', 'Smooshing',
  'Spelunking', 'Spinning', 'Sprouting', 'Stewing', 'Sublimating', 'Sussing', 'Swooping',
  'Symbioting', 'Synthesizing', 'Tempering', 'Thinking', 'Thundering', 'Tinkering',
  'Topsy-turvying', 'Transfiguring', 'Transmuting', 'Trick-or-treating', 'Twisting',
  'Unfurling', 'Unravelling', 'Vibing', 'Waddling', 'Wandering', 'Warping',
  'Whatchamacalliting', 'Whirlpooling', 'Whirring', 'Whisking', 'Wibbling', 'Working',
  'Wrangling', 'Zesting', 'Zigzagging'
];

// 随机选择一个等待提示词
const getRandomWaitingVerb = (): string => {
  return WAITING_VERBS[Math.floor(Math.random() * WAITING_VERBS.length)];
};

// === Phase 2: 兼容层 computed（映射到当前活跃窗口）===
// 让大部分旧代码无需修改即可工作
const chatMessages = computed({
  get: () => activeWindow.value?.messages || [],
  set: (val) => { if (activeWindow.value) activeWindow.value.messages = val; }
});

const inputMessage = computed({
  get: () => activeWindow.value?.inputMessage || '',
  set: (val) => { if (activeWindow.value) activeWindow.value.inputMessage = val; }
});

const pendingImages = computed({
  get: () => activeWindow.value?.pendingImages || [],
  set: (val) => { if (activeWindow.value) activeWindow.value.pendingImages = val; }
});

const isLoading = computed({
  get: () => activeWindow.value?.isStreaming || false,
  set: (val) => { if (activeWindow.value) activeWindow.value.isStreaming = val; }
});

const expandedThinking = computed({
  get: () => activeWindow.value?.expandedThinking || {},
  set: (val) => { if (activeWindow.value) activeWindow.value.expandedThinking = val; }
});

const shouldAutoScroll = computed({
  get: () => activeWindow.value?.shouldAutoScroll ?? true,
  set: (val) => { if (activeWindow.value) activeWindow.value.shouldAutoScroll = val; }
});

// 全局状态（非窗口隔离）
const isPollingBackground = ref(false);  // 后台任务 polling 状态

// === Phase 2: 多窗口滚动容器 ref ===
const chatScrollRefs = ref<Record<string, HTMLElement | null>>({});
const chatBottomRefs = ref<Record<string, HTMLElement | null>>({});

// 兼容层：保留原有单窗口 ref（用于部分旧逻辑）
const chatScrollRef = computed(() => chatScrollRefs.value[activeWindowId.value] || null);
const chatBottomRef = computed(() => chatBottomRefs.value[activeWindowId.value] || null);

// 设置滚动容器 ref（用于模板 :ref）
const setChatScrollRef = (windowId: string, el: HTMLElement | null) => {
  if (el) {
    chatScrollRefs.value[windowId] = el;
  }
};

const setChatBottomRef = (windowId: string, el: HTMLElement | null) => {
  if (el) {
    chatBottomRefs.value[windowId] = el;
  }
};
const windowTabsRef = ref<HTMLElement | null>(null);
const newWindowBtnRef = ref<HTMLElement | null>(null);
const newWindowDropdownPosition = ref<{
    top: number;
    left?: number;
    right?: number;
}>({ top: 0 });

// 滚轮横向滚动窗口标签
const handleTabsWheel = (event: WheelEvent) => {
  if (!windowTabsRef.value) return;
  // 阻止默认垂直滚动
  event.preventDefault();
  // 将垂直滚动转为水平滚动
  windowTabsRef.value.scrollLeft += event.deltaY;
};

// Auto-resize Textarea
const textareaRef = ref<HTMLTextAreaElement | null>(null);

const adjustTextareaHeight = () => {
  const el = textareaRef.value;
  if (!el) return;
  el.style.height = 'auto';
  el.style.height = el.scrollHeight + 'px';
};

watch(inputMessage, (newVal) => {
    if (!newVal) {
        nextTick(() => {
            if (textareaRef.value) {
                textareaRef.value.style.height = 'auto';
            }
        });
    }
});

// Phase 2: 支持指定窗口的思考折叠切换
const toggleThinking = (windowId: string, index: number) => {
  const win = windows.value.find(w => w.id === windowId);
  if (win) {
    win.expandedThinking[index] = !win.expandedThinking[index];
  }
};

// 辅助函数：将 ChatBubble (subagent) 转换为 SubAgent 格式
const bubbleToSubAgent = (bubble: ChatBubble): SubAgent => {
  // 将 childBubbles (tool_call bubbles) 转换为 ToolCall[]
  const toolCalls: ToolCall[] = (bubble.childBubbles || [])
    .filter(child => child.type === 'tool_call')
    .map(child => ({
      id: child.id,
      toolName: child.toolName || '',
      description: child.toolDescription,
      params: child.toolParams || {},
      output: child.toolOutput,
      status: child.status === 'streaming' ? 'running' : child.status as ToolCall['status'],
      startTime: child.timestamp,
      error: child.toolError
    }));

  return {
    id: bubble.id,
    name: bubble.subAgentName || '',
    type: bubble.subAgentType || 'general-purpose',
    status: bubble.status === 'streaming' ? 'running' : bubble.status as SubAgent['status'],
    toolCalls,
    result: bubble.subAgentResult,
    startTime: bubble.timestamp
  };
};

// Computed: Active or Recent SubAgents for the Task Monitor
// Logic:
// 1. If any agents are RUNNING, show them (Priority: High)
// 2. If no running agents, show agents from the LAST message (Priority: Low, represents "Completed" state)
// 3. Otherwise empty (Idle)
const activeSubAgents = computed(() => {
  // 1. Find all running agents globally (from bubbles)
  const runningAgents: SubAgent[] = [];
  chatMessages.value.forEach(msg => {
    if (msg.bubbles) {
      const runningBubbles = msg.bubbles.filter(
        b => b.type === 'subagent' && b.status === 'streaming'
      );
      runningAgents.push(...runningBubbles.map(bubbleToSubAgent));
    }
  });

  if (runningAgents.length > 0) {
    return runningAgents;
  }

  // 2. Fallback: Find the last message with subagent bubbles
  for (let i = chatMessages.value.length - 1; i >= 0; i--) {
    const msg = chatMessages.value[i];
    if (msg.role === 'ai' && msg.bubbles) {
      const subAgentBubbles = msg.bubbles.filter(b => b.type === 'subagent');
      if (subAgentBubbles.length > 0) {
        return subAgentBubbles.map(bubbleToSubAgent);
      }
    }
  }

  return [];
});

const handleStopAgent = (agentId: string) => {
    console.log('Request to stop agent:', agentId);
    // TODO: Implement backend interrupt call
};

// === Task Widget State Management ===
const taskWidgetExpanded = ref(false);

// Auto-expand logic for Task Widget
watch(activeSubAgents, (newAgents, oldAgents) => {
  const newRunning = newAgents.some(a => a.status === 'running');
  const oldRunning = oldAgents?.some(a => a.status === 'running') ?? false;
  
  // 1. From No Tasks -> Has Tasks: Auto expand
  if (newAgents.length > 0 && (!oldAgents || oldAgents.length === 0)) {
    taskWidgetExpanded.value = true;
  }
  // 2. From Completed/Idle -> Running: Auto expand
  if (newRunning && !oldRunning) {
    taskWidgetExpanded.value = true;
  }
}, { deep: true });

// Mock Data for Tasks - REMOVED (TaskSummaryWidget now uses subAgents)
// Proposals mock data is kept below for the Proposals carousel

const proposals = ref([
  {
    id: 'A',
    name: 'Ultimate Storage',
    tags: ['Storage++', 'Flow-'],
    metrics: { storage: '12.5m³', flow: 'Compact' },
    insight: 'Sacrificed 10% open space for max storage.',
    color: '#4facfe',
    thumbnailPattern: 'radial-gradient(circle at 30% 30%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)'
  },
  {
    id: 'B',
    name: 'Flow Priority',
    tags: ['Flow++', 'Open'],
    metrics: { storage: '8.0m³', flow: 'Excellent' },
    insight: 'Optimized for 1200mm main walkways.',
    color: '#00f2fe',
    thumbnailPattern: 'radial-gradient(circle at 70% 70%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)'
  },
  {
    id: 'C',
    name: 'Minimalist',
    tags: ['Light++', 'Cost-'],
    metrics: { storage: '6.5m³', flow: 'Good' },
    insight: 'Removed non-essential partitions.',
    color: '#a18cd1',
    thumbnailPattern: 'radial-gradient(circle at 50% 50%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #fbc2eb 0%, #a6c1ee 100%)'
  },
]);

// 设置主窗口的 loading 状态
const setPrimaryWindowLoading = (loading: boolean) => {
  const primaryWindow = windows.value.find(w => w.isPrimary);
  if (primaryWindow) {
    primaryWindow.isLoading = loading;
  }
};

// Select branch - 使用Store方法
const selectBranch = async (branchId: string) => {
  isBranchDropdownOpen.value = false;

  // 开始切换，显示 loading
  setPrimaryWindowLoading(true);

  try {
    const result = await gitStore.checkout(branchId);

    if (result.success) {
      return;
    }

    // 如果有未提交的更改，显示确认弹窗（loading 保持，等待用户选择）
    if (result.hasUncommittedChanges) {
      pendingCheckoutBranch.value = branchId;
      showCheckoutConfirmDialog.value = true;
      // 弹窗期间暂停 loading
      setPrimaryWindowLoading(false);
      return;
    }

    console.error('切换分支失败:', result.message);
  } finally {
    // 切换完成或失败，关闭 loading
    setPrimaryWindowLoading(false);
  }
};

// 确认弹窗回调
const handleCheckoutConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
  showCheckoutConfirmDialog.value = false;
  const branchName = pendingCheckoutBranch.value;
  const targetWindowId = pendingWindowId.value;  // 窗口标签页切换时的目标窗口
  const isCreateBranch = pendingIsCreateBranch.value;  // 是否是新建分支场景
  if (!branchName) return;

  // 开始切换，显示 loading
  setPrimaryWindowLoading(true);

  try {
    if (saveBeforeSwitch) {
      // 1. 先保存内存数据到文件系统
      const saved = await store.saveToServer();
      if (!saved) {
        console.error('保存数据失败，无法切换分支');
        pendingCheckoutBranch.value = '';
        pendingWindowId.value = '';
        pendingIsCreateBranch.value = false;
        return;
      }

      // 2. 再用 commitBeforeCheckout 提交并切换
      await gitStore.checkout(branchName, {
        commitBeforeCheckout: true,
        commitMessage,
        createIfNotExist: isCreateBranch  // 新建分支场景需要此参数
      });
    } else {
      // 放弃更改并切换：Server端原子操作
      await gitStore.checkout(branchName, { 
        discardBeforeCheckout: true,
        createIfNotExist: isCreateBranch  // 新建分支场景需要此参数
      });
    }

    pendingCheckoutBranch.value = '';
    pendingIsCreateBranch.value = false;
    
    // 如果是窗口标签页切换触发的，切换到目标窗口
    if (targetWindowId) {
      switchWindow(targetWindowId);
      pendingWindowId.value = '';
    }
  } finally {
    // 切换完成，关闭 loading
    setPrimaryWindowLoading(false);
  }
};

const handleCheckoutCancel = () => {
  showCheckoutConfirmDialog.value = false;
  pendingCheckoutBranch.value = '';
  pendingWindowId.value = '';
  pendingIsCreateBranch.value = false;
};

// Clear selection
const clearSelection = () => {
  store.clearSelection();
};

// Check Agent health on mount
onMounted(async () => {
  initDefaultWindow();  // 初始化默认窗口
  await checkAgentHealth();
  await gitStore.fetchBranches();  // 使用Store获取分支列表
  await fetchProjectPath();  // 获取当前项目路径
  // 启动截图服务 SSE 监听（响应 Agent 截图请求）
  const screenshotService = getScreenshotService(AGENT_API_BASE);
  screenshotService.startListening();
  // Note: Welcome message is now triggered by panelReady prop
});

// Watch for panelReady to trigger welcome message streaming
watch(() => props.panelReady, (newVal) => {
  if (newVal) {
    streamWelcomeMessage();
  }
});

const streamWelcomeMessage = async () => {
    const win = activeWindow.value;
    if (!win) return;

    // Prevent duplicate welcome messages if one already exists
    if (win.messages.length > 0) return;

    const welcomeText = '你好！我是 BIMCanvas 的布置助手。我可以帮助你分析房间功能、提供布置建议。有什么我能帮你的吗？';
    const targetWindowId = win.id;

    // 创建欢迎消息，使用气泡模型
    const welcomeBubble = createTextBubble('');
    const msgIndex = addMessage({
        role: 'ai',
        bubbles: [welcomeBubble],
        waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
        isStreaming: true
    });

    // Simulate typing effect
    let i = 0;
    const interval = setInterval(() => {
        // Phase 2: 使用 getWindowMessage 定位消息
        const msg = getWindowMessage(targetWindowId, msgIndex);
        if (!msg) {
            clearInterval(interval);
            return;
        }

        if (i < welcomeText.length) {
            // 更新气泡内容
            msg.bubbles[0].content += welcomeText[i];
            i++;
            scrollToBottom({ windowId: targetWindowId });
        } else {
            clearInterval(interval);
            // 标记完成
            msg.bubbles[0].status = 'completed';
            msg.isStreaming = false;
        }
    }, 30);
};

// Watch for chat messages to auto-scroll (watch already imported at top)
watch(() => chatMessages.value, () => {
    // Only auto-scroll if user is already near bottom
    if (shouldAutoScroll.value) {
        nextTick(() => {
            scrollToBottom();
        });
    }
}, { deep: true });

// Phase 2: 支持指定窗口的 isNearBottom
const isNearBottom = (windowId?: string) => {
    const targetWindowId = windowId || activeWindowId.value;
    const el = chatScrollRefs.value[targetWindowId];
    if (!el) return true;
    const threshold = 100;
    return el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
};

// Phase 2: 支持指定窗口的滚动事件处理
const handleChatScroll = (windowId: string) => {
    if (mode.value !== 'chat') return;
    const win = windows.value.find(w => w.id === windowId);
    if (win) {
        win.shouldAutoScroll = isNearBottom(windowId);
    }
};

// Agent API functions
const checkAgentHealth = async () => {
  agentStatus.value = 'connecting';
  try {
    const response = await fetch(`${AGENT_API_BASE}/health`);
    if (response.ok) {
      agentStatus.value = 'connected';
      // 连接成功后获取服务端配置
      await fetchAgentConfig();
    } else {
      agentStatus.value = 'disconnected';
    }
  } catch {
    agentStatus.value = 'disconnected';
  }
};

// 获取 Agent 服务端配置并初始化模型/思考强度选择
const fetchAgentConfig = async () => {
  try {
    // 并行获取默认配置和 Web 配置
    const [configRes, webConfigRes] = await Promise.all([
      fetch(`${AGENT_API_BASE}/api/config`),
      fetch(`${AGENT_API_BASE}/api/web_config`)
    ]);

    // 加载模型列表（完全由配置文件控制）
    if (webConfigRes.ok) {
      const webConfig = await webConfigRes.json();
      models.value = webConfig.customModels || [];
    }

    // 加载默认配置
    if (configRes.ok) {
      const config = await configRes.json();
      const { model: defaultModel, thinkingLevel: defaultThinking } = config;

      // 初始化模型选择
      if (defaultModel) {
        let found = models.value.find(m => m.id === defaultModel);
        if (!found) {
          // 默认模型不在列表中，添加到列表
          found = { id: defaultModel, label: defaultModel };
          models.value.push(found);
        }
        currentModel.value = found;
      } else if (models.value.length > 0) {
        // 没有默认模型配置，选择列表中的第一个
        currentModel.value = models.value[0];
      }

      // 初始化思考强度选择
      if (defaultThinking) {
        const foundThinking = thinkingLevels.find(t => t.id === defaultThinking);
        if (foundThinking) {
          currentThinking.value = foundThinking;
        }
      }
    }

    console.log('Agent 配置已加载:', { model: currentModel.value?.id, thinking: currentThinking.value.id });
  } catch (error) {
    console.warn('获取 Agent 配置失败:', error);
  }
};

// 获取当前项目路径
const fetchProjectPath = async () => {
  try {
    const status = await ProjectService.getStatus();
    if (status.isLoaded && status.projectPath) {
      currentProjectPath.value = status.projectPath;
      console.log('项目路径已设置:', status.projectPath);
    } else {
      console.warn('项目未加载或路径为空');
    }
  } catch (error) {
    console.error('获取项目路径失败:', error);
  }
};

const sendMessage = async () => {
  const win = activeWindow.value;
  if (!win) return;

  const message = win.inputMessage.trim();
  if (!message || win.isStreaming) return;

  // Phase 2: 记住发送时的窗口ID，SSE循环中使用此ID定位消息
  const targetWindowId = win.id;

  // Add user message to chat - 使用气泡模型
  const userTextBubble = createTextBubble(message);
  userTextBubble.status = 'completed';
  addMessageToWindow(targetWindowId, {
    role: 'user',
    bubbles: [userTextBubble],
    waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 }
  });
  win.inputMessage = '';
  win.isStreaming = true;  // 仅当前窗口进入加载状态

  // Force scroll to bottom when user sends message
  win.shouldAutoScroll = true;
  await nextTick();
  scrollToBottom({ force: true, windowId: targetWindowId });
  requestAnimationFrame(() => scrollToBottom({ force: true, windowId: targetWindowId }));
  setTimeout(() => scrollToBottom({ force: true, windowId: targetWindowId }), 50);
  setTimeout(() => scrollToBottom({ force: true, windowId: targetWindowId }), 150);

  // Add placeholder AI message for streaming - 使用气泡模型
  const initialWaitingState: WaitingState = {
    isWaiting: true,
    waitingVerb: getRandomWaitingVerb(),
    waitingSince: Date.now()
  };
  const aiMessageIndex = addMessageToWindow(targetWindowId, {
    role: 'ai',
    bubbles: [],
    waitingState: initialWaitingState,
    isStreaming: true,
    startTime: Date.now(),
    thinking: '',
    thinkingDuration: undefined
  });

  // Start thinking timer - updates every second while streaming
  // Phase 2: 使用 getWindowMessage 定位到目标窗口的消息
  const timerInterval = setInterval(() => {
    const msg = getWindowMessage(targetWindowId, aiMessageIndex);
    // Only update if still streaming and no bubbles yet (still in thinking phase)
    if (msg && msg.isStreaming && msg.bubbles.length === 0 && msg.thinking) {
      const duration = Math.round((Date.now() - (msg.startTime || Date.now())) / 1000);
      msg.thinkingDuration = duration + 's';
    } else {
      clearInterval(timerInterval);
    }
  }, 1000);

  try {
    // 获取并清空待发送图片
    const imagesToSend = [...pendingImages.value];
    pendingImages.value = [];

    // 确保 windowId 有效（空值保护）
    const effectiveWindowId = activeWindowId.value || 'window-main';

    // 调试日志：排查请求发送问题
    console.log('[sendMessage] Request:', {
      projectPath: currentProjectPath.value,
      windowId: effectiveWindowId,
      message: message.substring(0, 50) + (message.length > 50 ? '...' : ''),
      imagesCount: imagesToSend.length,
      model: currentModel.value?.id,
      thinkingLevel: currentThinking.value.id
    });

    const response = await fetch(`${AGENT_API_BASE}/api/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        projectPath: currentProjectPath.value,
        windowId: effectiveWindowId,  // 支持多窗口并行（已空值保护）
        worktreePath: activeWindow.value?.worktreePath,  // 虚拟窗口工作路径
        message: message,
        images: imagesToSend,  // 新增：图片附件
        model: currentModel.value?.id,
        thinkingLevel: currentThinking.value.id
      })
    });

    if (!response.ok) {
      throw new Error(`HTTP error: ${response.status}`);
    }

    const reader = response.body?.getReader();
    const decoder = new TextDecoder();

    if (!reader) {
      throw new Error('No response body');
    }

    let buffer = '';
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() || '';

      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const data = line.slice(6);
          if (data === '[DONE]') {
            break;
          }
          try {
            const parsed = JSON.parse(data);

            // 调试日志：记录所有接收到的 SSE 事件（用于验证 SubAgent 完成后最终结论是否正常输出）
            if (import.meta.env.DEV) {
              console.log('[SSE Event]', {
                type: parsed.type,
                content: parsed.content?.slice(0, 100),
                subAgentId: parsed.subAgentId,
                success: parsed.success
              });
            }

            // Phase 2: 使用 getWindowMessage 定位到目标窗口的消息
            const currentMsg = getWindowMessage(targetWindowId, aiMessageIndex);
            if (!currentMsg) continue;  // 窗口可能已关闭

            // Phase 2: 获取目标窗口的 expandedThinking 状态
            const targetWin = windows.value.find(w => w.id === targetWindowId);
            if (!targetWin) continue;

            // ===== Thinking Events =====
            if (parsed.type === 'thinking' || parsed.type === 'thinking_complete') {
              if (parsed.type === 'thinking_complete') {
                currentMsg.thinking = parsed.content;
              } else {
                currentMsg.thinking = (currentMsg.thinking || '') + parsed.content;
              }
              // Auto-expand thinking on first chunk
              if (!targetWin.expandedThinking[aiMessageIndex]) {
                targetWin.expandedThinking[aiMessageIndex] = true;
              }
            }

            // ===== Text Events (使用气泡模型) =====
            else if (parsed.type === 'text') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // Auto-collapse thinking when text starts
              if (currentMsg.thinking && targetWin.expandedThinking[aiMessageIndex] === true) {
                currentMsg.endTime = Date.now();
                const duration = Math.round((currentMsg.endTime - (currentMsg.startTime || currentMsg.endTime)) / 1000);
                currentMsg.thinkingDuration = duration + 's';
                targetWin.expandedThinking = { ...targetWin.expandedThinking, [aiMessageIndex]: false };
                nextTick(() => scrollToBottom({ windowId: targetWindowId }));
              }

              // ✅ 如果是 recoverable 错误，跳过显示
              if (parsed.errorType === 'recoverable') {
                if (import.meta.env.DEV) {
                  console.log('[Recoverable error (hidden)]', parsed.errorContent || parsed.content);
                }
                continue;  // 跳过这个事件，不添加到气泡
              }

              // ✅ 如果是 blocking 错误，也不显示在对话面板
              if (parsed.errorType === 'blocking') {
                if (import.meta.env.DEV) {
                  console.warn('[Blocking error (hidden from chat)]', parsed.errorContent || parsed.content);
                }
                continue;  // 跳过显示（Server 控制台已经打印了）
              }

              // ✅ 处理权限错误（打印日志但不添加到气泡）
              if (parsed.errorType === 'permission_required') {
                console.warn('[Permission error]', parsed.errorContent || parsed.content);
                continue;  // 跳过显示
              }

              // 找到最后一个正在流式传输的文本气泡
              let lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

              if (lastTextBubble) {
                // 追加到现有文本气泡
                lastTextBubble.content = (lastTextBubble.content || '') + (parsed.content || '');
              } else {
                // 创建新的文本气泡
                const newTextBubble = createTextBubble(parsed.content || '');
                currentMsg.bubbles.push(newTextBubble);
              }

              // 调试模式：记录被隐藏的 recoverable 错误
              if (parsed.hiddenContent && import.meta.env.DEV) {
                console.debug('[Hidden recoverable error]', parsed.hiddenContent);
              }
            }

            else if (parsed.type === 'text_complete') {
              // 标记最后一个文本气泡为完成
              let lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

              if (lastTextBubble) {
                // 情况1：有正在流式传输的气泡，标记为完成
                completeBubble(lastTextBubble);
              } else if (parsed.content) {
                // 情况2：没有流式气泡但有内容（SubAgent完成后的最终文本）
                // 创建新气泡并直接标记为完成
                const newTextBubble = createTextBubble(parsed.content);
                newTextBubble.status = 'completed';
                currentMsg.bubbles.push(newTextBubble);
              }

              // 只有当没有 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== Error Event =====
            else if (parsed.error) {
              // ✅ 只打印到控制台，不创建气泡（错误已在 Server 控制台显示）
              console.error('[SSE Error]', parsed.error);
              // 不添加到气泡列表
            }

            // ===== SubAgent Events (使用气泡模型) =====
            else if (parsed.type === 'subagent_start') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // 如果有正在流式传输的文本气泡，先标记为完成
              const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
              if (lastTextBubble) {
                completeBubble(lastTextBubble);
              }

              // 创建 SubAgent 气泡
              const subAgentBubble = createSubAgentBubble(
                parsed.subAgentId,
                parsed.subAgentName,
                parsed.subAgentType
              );
              currentMsg.bubbles.push(subAgentBubble);
            }

            else if (parsed.type === 'subagent_complete') {
              const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.subAgentId);
              if (subAgentBubble) {
                if (parsed.success === false) {
                  failBubble(subAgentBubble, parsed.error);
                } else {
                  completeBubble(subAgentBubble);
                }
                if (parsed.content) {
                  updateSubAgentResult(subAgentBubble, parsed.content);
                }
              }
              // 只有当没有其他 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== Tool Call Events (使用气泡模型) =====
            else if (parsed.type === 'tool_call_start') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // 如果有正在流式传输的文本气泡，先标记为完成
              const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
              if (lastTextBubble) {
                completeBubble(lastTextBubble);
              }

              // 创建工具调用气泡
              const toolBubble = createToolCallBubble(
                parsed.toolCallId,
                parsed.toolName,
                parsed.toolDescription,
                parsed.toolParams
              );

              if (parsed.subAgentId) {
                // SubAgent 内的工具调用 - 添加到 childBubbles
                const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.subAgentId);
                if (subAgentBubble && subAgentBubble.type === 'subagent') {
                  if (!subAgentBubble.childBubbles) {
                    subAgentBubble.childBubbles = [];
                  }
                  subAgentBubble.childBubbles.push(toolBubble);
                }
              } else {
                // 主 Agent 的工具调用 - 添加到主时间线
                currentMsg.bubbles.push(toolBubble);
              }
            }

            else if (parsed.type === 'tool_call_output') {
              // 在所有气泡中查找工具调用气泡（包括 childBubbles）
              const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
              if (toolBubble && toolBubble.type === 'tool_call') {
                appendToolCallOutput(toolBubble, parsed.toolOutput);
              }
            }

            else if (parsed.type === 'tool_call_complete') {
              const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
              if (toolBubble && toolBubble.type === 'tool_call') {
                if (parsed.success) {
                  completeBubble(toolBubble);
                } else {
                  failBubble(toolBubble, parsed.error);
                }
              }
              // 只有当没有 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== TaskOutput Polling Event (后台任务轮询) =====
            else if (parsed.type === 'task_output_polling') {
              // 设置全局 polling 状态（用于 UI 提示）
              isPollingBackground.value = true;

              // 将所有 streaming 状态的 SubAgent 标记为后台执行
              const streamingSubAgents = findStreamingSubAgents(currentMsg.bubbles);
              for (const bubble of streamingSubAgents) {
                markAsBackground(bubble);
                // 更新结果显示轮询状态
                bubble.subAgentResult = `正在获取结果... (timeout: ${parsed.timeout / 1000}s)`;
              }
            }

            await nextTick();
            scrollToBottom({ windowId: targetWindowId });
          } catch (e) {
            console.error('Parse error:', e, data);
          }
        }
      }
    }

    // Mark streaming as complete - Phase 2: 使用目标窗口
    const finalMsg = getWindowMessage(targetWindowId, aiMessageIndex);
    if (finalMsg) {
      finalMsg.isStreaming = false;
      finalMsg.waitingState.isWaiting = false;

      // 将最后一个 streaming 状态的气泡标记为 completed
      const lastStreamingBubble = getLastStreamingTextBubble(finalMsg.bubbles);
      if (lastStreamingBubble) {
        completeBubble(lastStreamingBubble);
      }
    }

    agentStatus.value = 'connected';

  } catch (error) {
    console.error('Chat error:', error);
    // Phase 2: 使用目标窗口
    const currentMsg = getWindowMessage(targetWindowId, aiMessageIndex);
    if (currentMsg) {
      // 如果没有任何气泡，创建错误文本气泡
      if (currentMsg.bubbles.length === 0) {
        const errorBubble = createTextBubble('Sorry, I encountered an error. Please check if the Agent server is running.');
        errorBubble.status = 'failed';
        currentMsg.bubbles.push(errorBubble);
      }
      currentMsg.isStreaming = false;
      currentMsg.waitingState.isWaiting = false;
    }
    agentStatus.value = 'disconnected';
  } finally {
    // Phase 2: 使用目标窗口的 isStreaming
    const targetWin = windows.value.find(w => w.id === targetWindowId);
    if (targetWin) {
      targetWin.isStreaming = false;
    }
    isPollingBackground.value = false;  // 重置 polling 状态
    await nextTick();
    scrollToBottom({ windowId: targetWindowId });
  }
};



// Phase 2: scrollToBottom 支持指定窗口
const scrollToBottom = (options?: { force?: boolean; windowId?: string }) => {
  if (!options?.force && mode.value !== 'chat') return;

  // 确定目标窗口
  const targetWindowId = options?.windowId || activeWindowId.value;
  const targetWin = windows.value.find(w => w.id === targetWindowId);

  // 检查目标窗口是否应该自动滚动
  if (!options?.force && targetWin && !targetWin.shouldAutoScroll) return;

  // 获取目标窗口的滚动容器
  const bottomRef = chatBottomRefs.value[targetWindowId];
  if (bottomRef) {
    bottomRef.scrollIntoView({ block: 'end' });
    return;
  }

  const el = chatScrollRefs.value[targetWindowId];
  if (el) {
    el.scrollTop = el.scrollHeight;
  }
};

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    sendMessage();
  }
};

const startResize = () => {
  isResizing.value = true;
  window.addEventListener('mousemove', handleResize);
  window.addEventListener('mouseup', stopResize);
  document.body.style.cursor = 'ew-resize';
  document.body.style.userSelect = 'none';
};

const handleResize = (e: MouseEvent) => {
  const newWidth = window.innerWidth - e.clientX;
  if (newWidth >= 300 && newWidth <= 600) {
    panelWidth.value = newWidth;
  }
};

const stopResize = () => {
  isResizing.value = false;
  window.removeEventListener('mousemove', handleResize);
  window.removeEventListener('mouseup', stopResize);
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
};

const carouselTrackRef = ref<HTMLElement | null>(null);

const handleWheel = (e: WheelEvent) => {
  if (carouselTrackRef.value && e.deltaY !== 0) {
    e.preventDefault();
    carouselTrackRef.value.scrollLeft += e.deltaY;
  }
};

const removeContext = (type: 'scope' | 'selection', item?: string) => {
    if (type === 'scope') {
        contextScope.value = '';
    } else if (item) {
        contextSelection.value = contextSelection.value.filter(i => i !== item);
    }
}

// Context Menu State
const isContextMenuOpen = ref(false);
const activeSubmenu = ref<string | null>(null);
const submenuDirection = ref<'left' | 'right'>('left');
const isAttachmentMenuOpen = ref(false);

// Toggle functions
const toggleContextMenu = () => {
  isContextMenuOpen.value = !isContextMenuOpen.value;
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
  isAttachmentMenuOpen.value = false;
  if (!isContextMenuOpen.value) activeSubmenu.value = null;
};

const toggleAttachmentMenu = () => {
  isAttachmentMenuOpen.value = !isAttachmentMenuOpen.value;
  isContextMenuOpen.value = false;
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
};

// Model & Thinking State
// 存储完整对象 { id, label }，发送时使用 id，显示时使用 label
// 模型列表完全由配置文件控制，启动时从 web_config.json 加载
const models = ref<{ id: string; label: string }[]>([]);

const thinkingLevels = [
  { id: 'off', label: 'Off' },
  { id: 'low', label: 'Low' },
  { id: 'medium', label: 'Medium' },
  { id: 'high', label: 'High' }
];

const currentModel = ref<{ id: string; label: string } | null>(null);
const currentThinking = ref(thinkingLevels[0]);  // 默认 off，后续由配置文件覆盖
const isModelMenuOpen = ref(false);
const isThinkingMenuOpen = ref(false);

// 添加模型输入状态
const isAddingModel = ref(false);
const newModelId = ref('');
const newModelInputRef = ref<HTMLInputElement | null>(null);

// 保存模型列表到服务端
const saveCustomModels = async () => {
  try {
    await fetch(`${AGENT_API_BASE}/api/web_config`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ customModels: models.value })
    });
  } catch (error) {
    console.warn('保存模型列表失败:', error);
  }
};

const selectModel = (model: { id: string; label: string }) => {
  currentModel.value = model;
  isModelMenuOpen.value = false;
  isAddingModel.value = false;
};

// 开始添加模型
const startAddModel = () => {
  isAddingModel.value = true;
  newModelId.value = '';
  nextTick(() => {
    newModelInputRef.value?.focus();
  });
};

// 确认添加模型
const confirmAddModel = async () => {
  const id = newModelId.value.trim();
  if (id && !models.value.some(m => m.id === id)) {
    const newModel = { id, label: id };  // label 使用 id
    models.value.push(newModel);
    selectModel(newModel);
    await saveCustomModels();
  }
  cancelAddModel();
};

// 取消添加模型
const cancelAddModel = () => {
  isAddingModel.value = false;
  newModelId.value = '';
};

const selectThinking = (level: { id: string; label: string }) => {
  currentThinking.value = level;
  isThinkingMenuOpen.value = false;
};

const contextOptions = {
  zones: [
    { id: 'living-room', label: 'Living Room' },
    { id: 'kitchen', label: 'Kitchen' },
    { id: 'master-bedroom', label: 'Master Bedroom' },
    { id: 'bathroom', label: 'Bathroom' },
    { id: 'balcony', label: 'Balcony' }
  ],
  regulations: [
    { id: 'wheelchair', label: 'Wheelchair Access (ADA)' },
    { id: 'feng-shui', label: 'Feng Shui Principles' },
    { id: 'fire-code', label: 'Fire Safety Code' }
  ],
  attachments: [
    { id: 'upload', label: 'Upload Image...' },
    { id: 'docs', label: 'Project Requirements.pdf' }
  ]
};

const openSubmenu = (id: string, event: MouseEvent) => {
    activeSubmenu.value = id;
    
    // Smart positioning logic
    const target = event.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const submenuWidth = 220; // Matches CSS width
    const windowWidth = window.innerWidth;
    
    // Check if there is space on the right
    if (rect.right + submenuWidth + 20 < windowWidth) {
        submenuDirection.value = 'right';
    } else {
        submenuDirection.value = 'left';
    }
};

const handleContextSelect = async (type: string, item: any) => {
  console.log('Selected context:', type, item);



  // Logic to add context
  if (type === 'zones') {
    activeScope.value = item.label; // Update scope for demo
  } else {
    // For other types, maybe add a chip to the input or a temporary toast
    // For now, let's just simulate adding it to the input for visibility
    inputMessage.value += ` [Context: ${item.label}] `;
  }

  isContextMenuOpen.value = false;
  activeSubmenu.value = null;
};

// Close menus when clicking outside
const handleGlobalClick = (e: MouseEvent) => {
  const target = e.target as HTMLElement;

  // Close Context Menu
  if (!target.closest('.add-context-wrapper')) {
    isContextMenuOpen.value = false;
    isAttachmentMenuOpen.value = false; // Also close attachment menu
    activeSubmenu.value = null;
  }

  // Close Branch Dropdown (Primary Window)
  // 检查整个主窗口标签区域，而不仅仅是小三角图标
  if (!target.closest('.window-tab.primary-clickable') && !target.closest('.branch-dropdown-overlay')) {
    isBranchDropdownOpen.value = false;
  }

  // Close New Window Dropdown
  // 检查按钮或下拉框本身（下拉框已移到 header-tabs 层级）
  if (!target.closest('.new-window-wrapper') && !target.closest('.new-window-dropdown')) {
    showNewWindowDropdown.value = false;
  }

  // Close Model Menu
  if (!target.closest('.control-pill-wrapper.model')) {
    isModelMenuOpen.value = false;
  }

  // Close Thinking Menu
  if (!target.closest('.control-pill-wrapper.thinking')) {
    isThinkingMenuOpen.value = false;
  }
};

// 处理表格区域的滚轮事件：禁止垂直滚轮触发表格水平滚动
const handleTableWheel = (e: WheelEvent) => {
  const target = e.target as HTMLElement;
  const tableWrapper = target.closest('.table-node-wrapper');

  if (tableWrapper) {
    // 如果主要是垂直滚动，阻止表格的水平滚动
    // 让事件冒泡到父容器进行垂直滚动
    if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
      e.preventDefault();
      // 手动触发父容器的垂直滚动
      const scrollContainer = chatScrollRef.value;
      if (scrollContainer) {
        scrollContainer.scrollTop += e.deltaY;
      }
    }
  }
};

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
  // 在聊天区域监听 wheel 事件，需要 passive: false 才能 preventDefault
  chatScrollRef.value?.addEventListener('wheel', handleTableWheel, { passive: false });
});

import { onUnmounted } from 'vue';
onUnmounted(() => {
  window.removeEventListener('click', handleGlobalClick);
  chatScrollRef.value?.removeEventListener('wheel', handleTableWheel);
  // 停止截图服务 SSE 监听
  const screenshotService = getScreenshotService(AGENT_API_BASE);
  screenshotService.stopListening();
});

import TaskSummaryWidget from './TaskSummaryWidget.vue';
import MarkdownText from './base/MarkdownText.vue';
import AdvancedScreenshotOverlay from './AdvancedScreenshotOverlay.vue';

// 框选截图状态
const showScreenshotOverlay = ref(false);

const handleScreenshotCapture = async (imageData: string) => {
  showScreenshotOverlay.value = false;
  try {
    const screenshotService = getScreenshotService(AGENT_API_BASE);
    // 保存到本地
    const filePath = await screenshotService.saveToLocal(imageData);
    console.log(`[Screenshot] Saved to: ${filePath}`);
    // 添加到待发送附件
    pendingImages.value.push(imageData);
    console.log(`[Screenshot] Added to pending, total: ${pendingImages.value.length}`);
  } catch (e) {
    console.error('[Screenshot] Save failed:', e);
  }
};

const handleScreenshotCancel = () => {
  showScreenshotOverlay.value = false;
};

const removePendingImage = (index: number) => {
  pendingImages.value.splice(index, 1);
};
</script>

<template>
  <Teleport to="body">
  <transition name="panel-slide" appear>
  <aside 
    class="ai-command-center" 
    :style="{ 
      width: panelWidth + 'px',
      position: 'fixed',
      top: '72px',
      right: '0',
      height: 'calc(100% - 72px)',
      zIndex: 190
    }"
    v-show="props.panelReady && !showScreenshotOverlay"
  >
    <!-- Resize Handle -->
    <div class="resize-handle" @mousedown="startResize">
        <div class="handle-bar"></div>
    </div>
    
    <div class="main-content">
      
      <!-- Layer 1: Context Header (Design J: Inline Branch & Strict New Window) -->
      <div class="layer-context">
        
        <!-- Row 1: Global Mode Switch (Left-Aligned Toolbar) -->
        <div class="header-toolbar">
            <div class="mode-switch">
                <button :class="{ active: mode === 'chat' }" @click="mode = 'chat'">Chat</button>
                <button :class="{ active: mode === 'tasks' }" @click="mode = 'tasks'">Task</button>
            </div>
            
            <!-- Right Side Actions (Placeholder for Balance) -->
            <div class="toolbar-actions">
                <button class="icon-btn" title="History">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"></circle>
                        <polyline points="12 6 12 12 16 14"></polyline>
                    </svg>
                </button>
            </div>
        </div>

        <!-- Row 2: Window Context (Tabs with Inline Branch) -->
        <div class="header-tabs" v-if="mode === 'chat'">
          
          <!-- Wrapper for Tabs + Fixed New Window Button -->
          <div class="tabs-wrapper">
              <!-- Window Tabs -->
              <div 
                class="window-tabs" 
                ref="windowTabsRef"
                @wheel="handleTabsWheel"
              >
                <div 
                  v-for="win in windows" 
                  :key="win.id"
                  class="window-tab"
                  :class="{ 
                    active: activeWindowId === win.id,
                    primary: win.isPrimary,
                    loading: win.isLoading,
                    error: win.error
                  }"
                  @click.stop="handleWindowTabClick(win)"
                >
                  <!-- Branch Info (Main Content) -->
                  <div class="tab-branch">
                      <!-- Main Content Wrapper for Centering -->
                      <div class="branch-main">
                          <span class="branch-icon">🌿</span>
                          <span class="branch-name">{{ win.branchId }}</span>
                      </div>

                      <!-- Primary Window Switch Indicator - Only clickable area -->
                      <button 
                          v-if="win.isPrimary" 
                          class="branch-switch-btn" 
                          :class="{ 'is-open': isBranchDropdownOpen }"
                          title="Switch Branch"
                          @click.stop="toggleBranchDropdown()"
                      >
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                              <polyline points="6 9 12 15 18 9"></polyline>
                          </svg>
                      </button>
                  </div>

                  <!-- Status Indicators & Controls (Absolute Positioned) -->
                  <!-- 加载状态指示器 -->
                  <span v-if="win.isLoading" class="tab-status loading" title="加载中...">⏳</span>
                  <!-- 错误状态指示器 -->
                  <span v-else-if="win.error" class="tab-status error" :title="win.error">⚠️</span>
                  <!-- 关闭按钮：非主窗口且非加载中时显示 -->
                  <button v-if="!win.isPrimary && !win.isLoading" class="tab-close" @click.stop="closeWindow(win.id)">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <line x1="18" y1="6" x2="6" y2="18"></line>
                          <line x1="6" y1="6" x2="18" y2="18"></line>
                      </svg>
                  </button>
                </div>

                <!-- New Window Button -->
                <div class="new-window-wrapper">
                  <button
                      class="new-window-btn"
                      title="New Window"
                      @click.stop="handleNewWindowClick"
                  >
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"></line>
                      <line x1="5" y1="12" x2="19" y2="12"></line>
                      </svg>
                  </button>
                </div>
              </div>
          </div>

          <!-- New Window Dropdown (Compact Style - Select Only) -->
          <div
              class="unified-dropdown new-window-dropdown"
              v-if="showNewWindowDropdown"
              :style="{
                top: newWindowDropdownPosition.top + 'px',
                left: newWindowDropdownPosition.left != null ? newWindowDropdownPosition.left + 'px' : 'auto',
                right: newWindowDropdownPosition.right != null ? newWindowDropdownPosition.right + 'px' : 'auto'
            }"
              @click.stop
          >
              <div
                  v-for="branch in branches"
                  :key="branch.name"
                  class="dropdown-option"
                  :class="{ disabled: isBranchOccupied(branch.name) }"
                  @click="!isBranchOccupied(branch.name) && addWindow(branch.name)"
              >
                  <div class="option-main">
                      <span class="option-icon" v-html="branchIcon"></span>
                      <span class="option-label">{{ branch.name }}</span>
                  </div>
                  <div v-if="branch.commit" class="option-tags">
                      <span class="tag-badge">{{ branch.commit.message.substring(0, 20) }}{{ branch.commit.message.length > 20 ? '...' : '' }}</span>
                  </div>
              </div>
          </div>

          <!-- Primary Window Branch Dropdown (Compact Style - Switch Mode with Create New) -->
          <div class="unified-dropdown branch-dropdown-overlay" v-if="isBranchDropdownOpen" @click.stop>
              <div
                  v-for="branch in branches"
                  :key="branch.id"
                  class="dropdown-option"
                  :class="{
                      selected: branch.name === currentBranch,
                      disabled: isBranchOccupiedByOther(branch.name)
                  }"
                  @click="!isBranchOccupiedByOther(branch.name) && selectBranch(branch.id)"
              >
                  <div class="option-main">
                      <span class="option-icon" v-html="branchIcon"></span>
                      <span class="option-label">{{ branch.name }}</span>
                  </div>
                  <div v-if="branch.commit" class="option-tags">
                      <span class="tag-badge">{{ branch.commit.message.substring(0, 20) }}{{ branch.commit.message.length > 20 ? '...' : '' }}</span>
                  </div>
              </div>
              <!-- 新建分支选项 -->
              <div class="dropdown-option create-new" @click="handleCreateNewBranchForPrimary">
                  <div class="option-main">
                      <span class="option-icon" v-html="createIcon"></span>
                      <span class="option-label">新建分支...</span>
                  </div>
              </div>
          </div>
        </div>

      </div>

      <!-- Layer 2: Intelligence Stream -->
      <div class="layer-stream">

         <!-- View: Chat - Phase 2: 多窗口 v-show 架构 -->
        <div v-show="mode === 'chat'" class="view-chat-container">
          <!-- 每个窗口独立的聊天容器 -->
          <div
            v-for="win in windows"
            :key="win.id"
            v-show="activeWindowId === win.id"
            class="view-chat window-chat-container"
            :ref="el => setChatScrollRef(win.id, el as HTMLElement)"
            @scroll="handleChatScroll(win.id)"
          >
            <!-- Actual Chat History -->
            <template v-for="(msg, msgIndex) in win.messages" :key="`${win.id}-${msgIndex}`">
                <div class="chat-message" :class="[msg.role === 'user' ? 'user' : 'ai', { streaming: msg.isStreaming }]">
                    <!-- Avatar Removed -->
                    <div class="message-wrapper">
                        <!-- Thinking Section (for AI messages only) -->
                        <div v-if="msg.role === 'ai' && msg.thinking" class="thinking-section">
                            <div class="thinking-header" @click="toggleThinking(win.id, msgIndex)">
                                <svg
                                    class="thinking-chevron"
                                    :class="{ expanded: win.expandedThinking[msgIndex] }"
                                    viewBox="0 0 24 24"
                                    fill="none"
                                    stroke="currentColor"
                                    stroke-width="2"
                                >
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                                <span class="thinking-label">
                                    <template v-if="msg.isStreaming && msg.bubbles.length === 0">
                                        Thinking for {{ msg.thinkingDuration || '0s' }}<span class="dot">.</span><span class="dot">.</span><span class="dot">.</span>
                                    </template>
                                    <template v-else>
                                        Thought for {{ msg.thinkingDuration || '0s' }}
                                    </template>
                                </span>
                            </div>
                            <transition name="thinking-expand">
                                <div v-show="win.expandedThinking[msgIndex]" class="thinking-content">
                                    <MarkdownText :content="msg.thinking" />
                                </div>
                            </transition>
                        </div>
                        <!-- 时间线气泡列表渲染 -->
                        <template v-for="bubble in msg.bubbles" :key="bubble.id">
                            <!-- 文本气泡 - 用户消息用纯文本，AI 消息用 Markdown -->
                            <div class="bubble" v-if="bubble.type === 'text' && bubble.content">
                                <template v-if="msg.role === 'user'">{{ bubble.content }}</template>
                                <MarkdownText v-else :content="bubble.content" />
                            </div>

                            <!-- 工具调用气泡 -->
                            <ToolCallBubble
                                v-else-if="bubble.type === 'tool_call'"
                                :bubble="bubble"
                            />

                            <!-- SubAgent 气泡 -->
                            <SubAgentBubble
                                v-else-if="bubble.type === 'subagent'"
                                :bubble="bubble"
                            />
                        </template>

                        <!-- 等待提示词（在气泡列表末尾） -->
                        <WaitingIndicator
                            v-if="msg.waitingState?.isWaiting"
                            :state="msg.waitingState"
                        />
                    </div>
                </div>
            </template>

            <!-- Note: Loading state now handled within streaming messages -->
            <div :ref="el => setChatBottomRef(win.id, el as HTMLElement)" class="chat-bottom-anchor"></div>
          </div>
        </div>

        <!-- View: Tasks (formerly Review) -->
        <div v-show="mode === 'tasks'" class="view-tasks">
            <!-- Agent Activity Monitor (SubAgent tracking) -->
            <TaskSummaryWidget 
                :sub-agents="activeSubAgents"
                v-model:expanded="taskWidgetExpanded"
            />

            <!-- Proposal Carousel -->
            <div class="carousel-section">
                <div class="section-title">Proposals</div>
                <div class="carousel-track" ref="carouselTrackRef" @wheel="handleWheel">
                    <div class="proposal-card" v-for="p in proposals" :key="p.id">
                        <!-- 1. Visual Thumbnail -->
                        <div class="card-thumbnail" :style="{ background: p.thumbnailPattern }">
                            <div class="thumbnail-overlay">
                                <span class="preview-badge">Preview {{ p.id }}</span>
                            </div>
                        </div>
                        
                        <!-- 2. Identity & Strategy -->
                        <div class="card-content">
                            <div class="card-header-row">
                                <div class="title">{{ p.name }}</div>
                                <div class="tags">
                                    <span v-for="tag in p.tags" :key="tag" class="tag">{{ tag }}</span>
                                </div>
                            </div>
                            
                            <!-- 3. Key Metrics -->
                            <div class="metrics-row">
                                <div class="metric">
                                    <span class="label">Storage</span>
                                    <span class="value">{{ p.metrics.storage }}</span>
                                </div>
                                <div class="metric">
                                    <span class="label">Flow</span>
                                    <span class="value">{{ p.metrics.flow }}</span>
                                </div>
                            </div>
                            
                            <!-- 4. AI Insight -->
                            <div class="insight-row">
                                <span class="icon">✨</span>
                                <span class="text">{{ p.insight }}</span>
                            </div>
                        </div>

                        <!-- Hover Actions -->
                        <div class="hover-actions">
                            <button class="action-btn">Refine</button>
                            <button class="action-btn primary">Apply</button>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Alert Card (Mock) -->
            <div class="card alert-card">
                <div class="alert-header">
                    <span class="icon">⚠️</span>
                    <span>Conflict Detected</span>
                </div>
                <div class="alert-body">
                    Wall move caused collision in Master Bedroom.
                </div>
                <div class="alert-actions">
                    <button class="action-btn small">Auto-Fix</button>
                    <button class="action-btn small outline">Undo</button>
                </div>
            </div>
        </div>

      </div>

      <!-- Layer 3: Command Footer -->
      <div class="layer-footer" v-if="mode === 'chat'">
        
        <!-- Context Bar (Replaces Selection Status Bar) -->
        <div class="context-bar">
            <!-- 1. Scope Chip (Always visible, defaults to Global/Room) -->
            <div class="context-chip scope">
                <span class="chip-icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
                        <polyline points="9 22 9 12 15 12 15 22"></polyline>
                    </svg>
                </span>
                <span class="chip-text">{{ activeScope }}</span>
            </div>

            <!-- 2. Selection Chip (Visible only when items selected) -->
            <transition name="chip-fade">
                <div class="context-chip selection" v-if="selectedModuleCount > 0">
                    <span class="chip-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                        </svg>
                    </span>
                    <span class="chip-text">Selected ({{ selectedModuleCount }})</span>
                    <button class="chip-close" @click.stop="clearSelection">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="18" y1="6" x2="6" y2="18"></line>
                            <line x1="6" y1="6" x2="18" y2="18"></line>
                        </svg>
                    </button>
                </div>
            </transition>

            <!-- 3. Add Context Button -->
            <div class="add-context-wrapper">
                <button 
                    class="add-context-btn" 
                    title="Add Context"
                    @click.stop="toggleContextMenu"
                    :class="{ active: isContextMenuOpen }"
                >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="12" y1="5" x2="12" y2="19"></line>
                        <line x1="5" y1="12" x2="19" y2="12"></line>
                    </svg>
                </button>

                <!-- Context Menu -->
                <transition name="scale-up">
                    <div class="context-menu" v-if="isContextMenuOpen">
                        
                        <!-- Main Menu -->
                        <div class="menu-section">
                            <div class="menu-header">Add Context</div>
                            
                            <!-- Zones Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('zones', $event)"
                                :class="{ active: activeSubmenu === 'zones' }"
                            >
                                <span class="item-text">Reference Zone</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>

                            <!-- Regulations Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('regulations', $event)"
                                :class="{ active: activeSubmenu === 'regulations' }"
                            >
                                <span class="item-text">Regulations</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>

                            <!-- Attachments Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('attachments', $event)"
                                :class="{ active: activeSubmenu === 'attachments' }"
                            >
                                <span class="item-text">Attachments</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>
                        </div>

                        <!-- Submenus (Flyout) -->
                        <div 
                            class="submenu-container" 
                            v-if="activeSubmenu"
                            :class="submenuDirection"
                        >
                            
                            <!-- Zones Submenu -->
                            <div class="submenu" v-if="activeSubmenu === 'zones'">
                                <div class="menu-header">Select Zone</div>
                                <div 
                                    class="menu-item" 
                                    v-for="zone in contextOptions.zones" 
                                    :key="zone.id"
                                    @click="handleContextSelect('zones', zone)"
                                >
                                    <span class="item-text">{{ zone.label }}</span>
                                </div>
                            </div>

                            <!-- Regulations Submenu -->
                            <div class="submenu" v-if="activeSubmenu === 'regulations'">
                                <div class="menu-header">Apply Regulation</div>
                                <div 
                                    class="menu-item" 
                                    v-for="reg in contextOptions.regulations" 
                                    :key="reg.id"
                                    @click="handleContextSelect('regulations', reg)"
                                >
                                    <span class="item-text">{{ reg.label }}</span>
                                </div>
                            </div>

                             <!-- Attachments Submenu -->
                             <div class="submenu" v-if="activeSubmenu === 'attachments'">
                                <div class="menu-header">Attach File</div>
                                <div 
                                    class="menu-item" 
                                    v-for="att in contextOptions.attachments" 
                                    :key="att.id"
                                    @click="handleContextSelect('attachments', att)"
                                >
                                    <span class="item-text">{{ att.label }}</span>
                                </div>
                            </div>

                        </div>

                    </div>
                </transition>
            </div>
        </div>

        <!-- Polling Background Status Indicator -->
        <transition name="slide-down">
          <div v-if="isPollingBackground" class="polling-indicator">
            <span class="polling-dot"></span>
            <span class="polling-text">正在等待后台任务...</span>
          </div>
        </transition>

        <!-- Antigravity Input Box -->
        <div class="antigravity-input-box">
            <!-- Pending Attachments Preview -->
            <div class="pending-attachments" v-if="pendingImages.length > 0">
              <div class="attachment-item" v-for="(img, idx) in pendingImages" :key="idx">
                <img :src="img" class="attachment-thumbnail" alt="attachment" />
                <button class="remove-attachment" @click="removePendingImage(idx)">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="18" y1="6" x2="6" y2="18"></line>
                    <line x1="6" y1="6" x2="18" y2="18"></line>
                  </svg>
                </button>
              </div>
            </div>
            <textarea
              ref="textareaRef"
              v-model="inputMessage"
              placeholder="你好"
              @keydown="handleKeydown"
              @input="adjustTextareaHeight"
              :disabled="isLoading || agentStatus !== 'connected'"
              rows="1"
            ></textarea>
            
            <div class="input-footer">
                <div class="left-controls">
                    <!-- Attachment Button (Paperclip) -->
                    <!-- Screenshot Button -->
                    <button 
                        class="icon-btn" 
                        title="Screenshot"
                        @click="showScreenshotOverlay = true"
                    >
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path>
                            <circle cx="12" cy="13" r="4"></circle>
                        </svg>
                    </button>

                    <!-- Attachment Button (Paperclip) -->
                     <div class="add-context-wrapper">
                        <button 
                            class="icon-btn" 
                            title="Add Attachment"
                            @click.stop="toggleAttachmentMenu"
                            :class="{ active: isAttachmentMenuOpen }"
                        >
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
                            </svg>
                        </button>

                        <!-- Attachment Menu -->
                        <transition name="scale-up">
                            <div class="context-menu" v-if="isAttachmentMenuOpen">
                                <div class="menu-header">Attachments</div>
                                <!-- Screenshot Options REMOVED -->

                                <div class="menu-divider"></div>
                                <!-- Upload Options -->
                                <div class="menu-item">
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                                            <circle cx="8.5" cy="8.5" r="1.5"></circle>
                                            <polyline points="21 15 16 10 5 21"></polyline>
                                        </svg>
                                    </span>
                                    <span class="item-text">上传图片</span>
                                </div>
                                <div class="menu-item">
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                                            <polyline points="14 2 14 8 20 8"></polyline>
                                            <line x1="16" y1="13" x2="8" y2="13"></line>
                                            <line x1="16" y1="17" x2="8" y2="17"></line>
                                            <polyline points="10 9 9 9 8 9"></polyline>
                                        </svg>
                                    </span>
                                    <span class="item-text">上传文件</span>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Model Pill -->
                    <div class="control-pill-wrapper model" :class="{ open: isModelMenuOpen }">
                        <button class="control-pill" @click="isModelMenuOpen = !isModelMenuOpen">
                            <span class="prefix-icon">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/>
                                </svg>
                            </span>
                            <span class="text">{{ currentModel?.label || 'Select Model' }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu model-menu" v-if="isModelMenuOpen">
                                <div class="menu-header">Model</div>
                                <div
                                    v-for="m in models"
                                    :key="m.id"
                                    class="menu-item"
                                    :class="{ active: currentModel?.id === m.id }"
                                    @click="selectModel(m)"
                                >
                                    <span class="item-text">{{ m.label }}</span>
                                    <svg v-if="currentModel?.id === m.id" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                                <!-- 分隔线 -->
                                <div class="menu-divider"></div>
                                <!-- 添加模型按钮或输入框 -->
                                <div v-if="!isAddingModel" class="menu-item add-model" @click.stop="startAddModel">
                                    <svg class="add-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <line x1="12" y1="5" x2="12" y2="19"></line>
                                        <line x1="5" y1="12" x2="19" y2="12"></line>
                                    </svg>
                                    <span class="item-text">Add Model...</span>
                                </div>
                                <div v-else class="add-model-input" @click.stop>
                                    <input
                                        ref="newModelInputRef"
                                        v-model="newModelId"
                                        type="text"
                                        placeholder="Model ID"
                                        @keyup.enter="confirmAddModel"
                                        @keyup.escape="cancelAddModel"
                                    />
                                    <div class="input-actions">
                                        <button class="confirm-btn" @click="confirmAddModel" :disabled="!newModelId.trim()">
                                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                                <polyline points="20 6 9 17 4 12"></polyline>
                                            </svg>
                                        </button>
                                        <button class="cancel-btn" @click="cancelAddModel">
                                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                                <line x1="18" y1="6" x2="6" y2="18"></line>
                                                <line x1="6" y1="6" x2="18" y2="18"></line>
                                            </svg>
                                        </button>
                                    </div>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Thinking Pill -->
                     <div class="control-pill-wrapper thinking" :class="{ open: isThinkingMenuOpen }">
                        <button class="control-pill" @click="isThinkingMenuOpen = !isThinkingMenuOpen">
                            <span class="text">{{ currentThinking.label }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isThinkingMenuOpen">
                                <div class="menu-header">Thinking Intensity</div>
                                <div
                                    v-for="t in thinkingLevels"
                                    :key="t.id"
                                    class="menu-item"
                                    :class="{ active: currentThinking.id === t.id }"
                                    @click="selectThinking(t)"
                                >
                                    <span class="item-text">{{ t.label }}</span>
                                    <svg v-if="currentThinking.id === t.id" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                            </div>
                        </transition>
                    </div>
                </div>

                <div class="right-controls">
                    <button
                      class="send-btn-round"
                      @click="sendMessage"
                      :disabled="isLoading || !inputMessage.trim() || agentStatus !== 'connected'"
                    >
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                            <line x1="12" y1="19" x2="12" y2="5"></line>
                            <polyline points="5 12 12 5 19 12"></polyline>
                        </svg>
                    </button>
                </div>
            </div>
        </div>
      </div>

    </div>

    <!-- Branch Checkout Confirm Dialog -->
    <BranchCheckoutConfirmDialog
      :visible="showCheckoutConfirmDialog"
      :target-branch="pendingCheckoutBranch"
      :current-branch="currentBranch"
      @confirm="handleCheckoutConfirm"
      @cancel="handleCheckoutCancel"
    />

    <!-- Branch Creation Dialog (新建分支对话框) -->
    <BranchCreationDialog
      :visible="showBranchCreationDialog"
      :base-branch="currentBranch"
      :base-tags="[]"
      :all-branches="branchOptionsForDialog"
      @create="handleBranchCreated"
      @cancel="showBranchCreationDialog = false"
    />

    <!-- Screenshot Overlay for select area screenshot -->
    <Teleport to="body">
      <AdvancedScreenshotOverlay
        v-if="showScreenshotOverlay"
        @capture="handleScreenshotCapture"
        @cancel="handleScreenshotCancel"
      />
    </Teleport>
  </aside>
  </transition>
  </Teleport>
</template>

<style scoped lang="scss">
/* Panel Slide Animation */
.panel-slide-enter-active,
.panel-slide-leave-active {
  transition: transform 0.6s cubic-bezier(0.25, 1, 0.5, 1);
}

.panel-slide-enter-from,
.panel-slide-leave-to {
  transform: translateX(120%); /* Move completely off-screen to the right */
}

.ai-command-center {
  /* Local Variables */
  --chrome-bg: #0A0A0A; /* Pure Dark Grey for seamless integration */

  /* Layout & Positioning - Fixed to float over Canvas */
  position: fixed;
  z-index: 190; /* Ensure it's above canvas but below header (200) */
  top: 72px; /* Below header */
  right: 0;
  height: calc(100% - 72px);
  margin-left: auto;
  margin-right: 0;
  
  /* Aurora Glass Effect */
  /* Aurora Glass Effect */
  background: #050505; /* Darken header to Black for Chrome contrast */
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  border-right: none;
  border-radius: 24px 0 0 24px;
  
  /* Glare & Shadow */
  background-image: var(--glass-glare); /* Remove gradient to keep it dark */
  box-shadow: 
    -12px 0 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow to the left */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */
  
  /* Animation */
  /* transition: width 0.1s;  Removed for smoother dragging */
  /* overflow: hidden;  Removed to allow context menu flyout */
  z-index: 90;
  display: flex;
  flex-direction: row; /* Changed to row to include resize handle */
  position: relative;
}

/* --- Resize Handle --- */
.resize-handle {
    width: 12px;
    height: 100%;
    cursor: ew-resize;
    display: flex;
    align-items: center;
    justify-content: center;
    position: absolute;
    left: 0;
    top: 0;
    z-index: 100;
    
    &:hover .handle-bar {
        background: var(--accent-primary);
        opacity: 0.8;
    }
}

.handle-bar {
    width: 4px;
    height: 48px;
    background: var(--text-tertiary);
    border-radius: 2px;
    opacity: 0.2;
    transition: all 0.2s;
}

/* --- Main Content --- */
.main-content {
    flex: 1;
    display: flex;
    flex-direction: column;
    height: 100%;
    padding-left: 12px; /* Space for resize handle */
    min-width: 0; /* Allow flex shrinking */
}

/* --- Layer 1: Context Header (Design J Refined: Premium & Balanced) --- */
.layer-context {
    padding: 0;
    height: auto;
    border-bottom: none; /* Remove border to allow tabs to overlap content border */
    display: flex;
    flex-direction: column;
    flex-shrink: 0;

    /* Row 1: Global Mode Switch (Compact Toolbar) */
    .header-toolbar {
        height: 40px; /* Increased height for larger toggle */
        padding: 0 12px; /* Align with tabs padding */
        display: flex;
        align-items: center;
        justify-content: center; /* 居中显示 */
        position: relative; /* 用于绝对定位子元素 */
        background: var(--surface-dim);
        border-bottom: 1px solid var(--border-subtle);
    }

    /* Mode Switch (Refined Segmented Control) */
    .mode-switch {
        display: flex;
        align-items: center;
        align-items: center;
        background: rgba(0, 0, 0, 0.2);
        padding: 3px; /* Increased padding */
        border-radius: 6px;
        gap: 4px; /* 增加按钮间距 */

        button {
            border: none;
            background: transparent;
            padding: 4px 20px; /* Increased padding */
            color: var(--text-tertiary);
            font-size: 13px; /* Slightly larger font */
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s ease;
            border-radius: 4px;
            min-width: 72px; /* Increased min-width */
            text-align: center;

            &:hover {
                color: var(--text-secondary);
            }

            &.active {
                background: var(--surface-elevated);
                color: var(--text-primary);
                font-weight: 600;
                box-shadow: 0 1px 2px rgba(0,0,0,0.15);
            }
        }
    }

    /* Toolbar Actions (Right Side - 绝对定位不影响居中) */
    .toolbar-actions {
        position: absolute;
        right: 12px;
        display: flex;
        align-items: center;
        gap: 8px;

        .icon-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 24px;
            height: 24px;
            border-radius: 4px;
            border: none;
            background: transparent;
            color: var(--text-tertiary);
            cursor: pointer;
            transition: all 0.2s ease;

            &:hover {
                background: rgba(255, 255, 255, 0.1);
                color: var(--text-primary);
            }

            svg { width: 14px; height: 14px; }
        }
    }

    /* Row 2: Window Context (Tabs + Branch) */
    .header-tabs {
        height: 40px; /* Compact height */
        padding: 0; /* Remove padding for flush left alignment */
        display: flex;
        align-items: flex-end; /* Align tabs to bottom for connected look */
        background: transparent;
        position: relative;
        /* border-bottom is on layer-context, we will overlap it */
    }

    /* Wrapper for Tabs + Fixed Button */
    .tabs-wrapper {
        display: flex;
        align-items: center;
        width: 100%;
        height: 100%;
        gap: 4px;
    }

    /* Window Tabs (Scrollable Area) */
    .window-tabs {
        display: flex;
        align-items: flex-end; /* Align items to bottom */
        gap: 2px; /* Small gap between tabs */
        overflow-x: auto;
        overflow-y: visible; /* 确保下拉框不被截断 */
        scrollbar-width: none;
        flex: 1;
        min-width: 0;
        height: 100%;
        /* Remove segmented control style */
        background: transparent;
        padding: 0;
        border-radius: 0;
        align-self: stretch; /* Stretch to fill height */

        &::-webkit-scrollbar { display: none; }
    }

    .window-tab {
        display: flex;
        flex-direction: column;
        justify-content: center;
        gap: 1px; /* Tighter gap */
        padding: 0; /* Remove padding from container */
        background: transparent;
        color: rgba(255, 255, 255, 0.5); /* Dim Grey for inactive (50%) */
        font-weight: 500; /* Medium weight for inactive */
        /* Chrome Style: Physical Surface */
        border-radius: 8px 8px 0 0; /* Chrome-like rounded top */
        margin-right: -1px; /* Connect tabs visually */
        border: 1px solid transparent; /* Prepare for border */
        border-right: 1px solid rgba(255,255,255,0.05); /* Separator */
        border-bottom: none;
        position: relative; /* For absolute positioning of controls */
        margin-bottom: 0;
        
        /* Layout Fixes */
        height: 34px; /* Standard height for inactive tabs */
        margin-top: 1px; /* Push down to align text with active tab */
        min-width: 120px;
        max-width: 200px;
        padding: 0 20px; /* Space for content */

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            color: rgba(255, 255, 255, 0.8); /* 80% White on hover */
        }

        &.active {
            /* Chrome Style: Seamless Active State */
            background: var(--chrome-bg); /* Match content area background */
            color: #ffffff; /* Pure White */
            font-weight: 600; /* Semi-Bold for active */
            border: 1px solid var(--border-dim);
            border-bottom: none; /* Open to bottom */
            
            /* Overlap the content border */
            margin-bottom: -1px;
            margin-top: 0; /* Rise up */
            z-index: 100; /* Ensure it sits on top of content border */
            height: 35px; /* Increase height to ensure overlap */

            /* Branch name becomes white */
            .tab-branch { color: inherit; }
        }

        /* Controls visibility on hover */
        &:hover {
            .tab-close { opacity: 1; }
            /* .branch-switch-btn is always visible for primary, no hover needed */
        }

        /* 加载状态 */
        &.loading {
            opacity: 0.7;
            cursor: wait;
            .tab-name { color: var(--text-muted); }
        }

        /* 错误状态 */
        &.error {
            border-color: var(--error, #ef4444);
            background: rgba(239, 68, 68, 0.1);
            .tab-name { color: var(--error, #ef4444); }
        }

        /* Status Indicators (Absolute) */
        .tab-status {
            position: absolute;
            right: 8px;
            top: 50%;
            transform: translateY(-50%);
            font-size: 12px;
            flex-shrink: 0;
            z-index: 10;

            &.loading {
                animation: pulse 1.5s ease-in-out infinite;
            }

            &.error {
                cursor: help;
            }
        }

        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.4; }
        }

        /* Branch Info (Main Content) */
        .tab-branch {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 100%;
            height: 100%; /* Fill the tab */
            font-size: 12px; /* Compact font */
            font-weight: 500;
            color: inherit; /* Inherit from parent to match active/inactive state */
            padding: 0; /* Remove padding to allow true centering */
            transition: all 0.2s ease;

            /* Wrapper for Name + Icon */
            .branch-main {
                position: relative; /* For absolute positioning of icon */
                display: flex;
                align-items: center;
                justify-content: center;
                width: auto; /* Let text define width */
                max-width: 100%;
                /* Ensure this is centered */
                margin: 0 auto;
            }

            .branch-icon { 
                position: absolute;
                right: 100%; /* Hang on the left side of the text */
                margin-right: 6px;
                top: 50%;
                transform: translateY(-50%);
                font-size: 12px; /* Compact icon */
                opacity: 0.8; 
                display: flex;
                align-items: center;
                white-space: nowrap; /* Prevent wrapping */
            }

            .branch-name {
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                flex: 1;
                text-align: center;
                font-family: 'JetBrains Mono', monospace;
            }

            .branch-switch-btn {
                position: absolute;
                right: 4px; /* Align with close button position */
                top: 50%;
                transform: translateY(-50%);
                padding: 0;
                width: 20px;
                height: 20px;
                display: flex;
                align-items: center;
                justify-content: center;
                background: transparent;
                border: none;
                border-radius: 4px;
                color: var(--text-tertiary);
                cursor: pointer;
                transition: background 0.2s, color 0.2s;
                z-index: 20; /* Higher than others */
                
                svg { 
                    width: 14px; 
                    height: 14px; 
                    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
                }

                &.is-open svg {
                    transform: rotate(180deg);
                }

                &:hover {
                    background: rgba(255, 255, 255, 0.1);
                    color: var(--text-primary);
                }
            }
        }

        /* Legacy: primary-clickable class (keep for compatibility) */
        &.primary-clickable {
            cursor: pointer;
        }
    }

    .tab-close {
        position: absolute;
        right: 8px;
        top: 50%;
        transform: translateY(-50%);
        display: flex;
        z-index: 10;
        align-items: center;
        justify-content: center;
        width: 14px;
        height: 14px;
        border-radius: 50%;
        border: none;
        background: transparent;
        color: var(--text-tertiary);
        cursor: pointer;
        opacity: 0;
        transition: all 0.2s ease;
        padding: 0;

        svg { width: 10px; height: 10px; }
    }

    .window-tab:hover .tab-close {
        opacity: 1;
    }

    .tab-close:hover {
        background: rgba(255, 255, 255, 0.2);
        color: var(--text-primary);
    }

    /* New Window Button Wrapper (紧跟窗口标签) */
    .new-window-wrapper {
        position: relative;
        flex-shrink: 0;
        height: 32px; /* Match tab height */
        display: flex;
        align-items: center;
        margin-left: 4px; /* Add spacing from the tabs */
        margin-bottom: 0; /* Sit on the line */
        align-self: flex-end; /* Align to bottom like tabs */
    }

    .new-window-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 6px;
        background: transparent;
        border: none;
        color: var(--text-tertiary);
        cursor: pointer;
        transition: all 0.2s ease;

        &:hover:not(:disabled) {
            background: var(--surface-dim);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.3;
            cursor: not-allowed;
        }

        svg { width: 16px; height: 16px; }
    }

    /* Dropdowns (Premium Glassmorphism) */
    .branch-dropdown-menu,
    .new-window-dropdown,
    .branch-dropdown-overlay {
        position: absolute;
        background: rgba(30, 30, 35, 0.95); /* Dark semi-transparent */
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        border: 1px solid rgba(255, 255, 255, 0.1);
        border-radius: 8px;
        box-shadow: 
            0 4px 20px rgba(0, 0, 0, 0.4),
            0 0 0 1px rgba(255, 255, 255, 0.05) inset;
        z-index: 200;
        overflow: hidden;
        margin-top: 4px;
    }
    
    .new-window-dropdown {
        /* 位置通过 JavaScript 动态设置 */
        /* left 和 right 由 JS 动态控制 */
        width: 200px;
        
        .dropdown-header {
            padding: 6px 10px;
            font-size: 10px;
            font-weight: 600;
            color: var(--text-tertiary);
            text-transform: uppercase;
            letter-spacing: 0.5px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.05);
            background: rgba(255, 255, 255, 0.02);
        }
    }

    .branch-dropdown-overlay {
        top: 100%;
        left: 12px; /* Align with padding */
        width: 240px;
        
        .branch-tree {
            max-height: 240px;
            overflow-y: auto;
            padding: 4px;
        }
    }

    .branch-list, .branch-tree {
        max-height: 200px;
        overflow-y: auto;
        padding: 4px;
    }

    .branch-item {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 6px 8px;
        border-radius: 6px;
        cursor: pointer;
        font-size: 12px;
        color: var(--text-secondary);
        transition: all 0.15s;
        border: 1px solid transparent;

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            color: var(--text-primary);
        }

        &.current {
            background: rgba(var(--accent-primary-rgb), 0.1);
            color: var(--accent-primary);
            border-color: rgba(var(--accent-primary-rgb), 0.2);
            font-weight: 500;
        }

        &.occupied {
            opacity: 0.5;
            cursor: not-allowed;

            &:hover {
                background: transparent;
                color: var(--text-secondary);
            }
        }

        .branch-main {
            display: flex;
            align-items: center;
            gap: 8px;
            width: 100%;
        }

        .branch-icon { font-size: 12px; opacity: 0.7; }
        .branch-name { font-family: 'JetBrains Mono', monospace; flex: 1; }
        .current-indicator { margin-left: auto; color: var(--accent-primary); display: flex; }
        .occupied-hint {
            margin-left: auto;
            font-size: 10px;
            color: var(--text-tertiary);
            font-style: italic;
        }

        /* 新建分支选项样式 */
        &.create-new {
            color: var(--accent-primary);
            &:hover {
                background: rgba(var(--accent-primary-rgb), 0.1);
            }
        }
    }

    /* 下拉框分隔线 */
    .dropdown-divider {
        height: 1px;
        background: rgba(255, 255, 255, 0.1);
        margin: 4px 0;
    }
}

/* --- Layer 2: Intelligence Stream --- */
.layer-stream {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    position: relative;
    min-height: 0;
    background: var(--chrome-bg); /* Unified opaque background */
    border-top: 1px solid var(--border-dim); /* Add border here for tabs to overlap */
    border-left: 1px solid var(--border-dim); /* Match tab border for alignment */
    border-right: 1px solid var(--border-dim); /* Match tab border for alignment */
    margin-top: -1px; /* Pull up to meet tabs */
}

/* 共享滚动容器样式 */
.view-chat, .view-tasks {
    flex: 1;
    overflow-y: auto;
    overflow-x: hidden;
    
    /* Scrollbar styling */
    &::-webkit-scrollbar {
        width: 6px;
    }
    &::-webkit-scrollbar-track {
        background: transparent;
    }
    &::-webkit-scrollbar-thumb {
        background: rgba(255, 255, 255, 0.1);
        border-radius: 3px;
    }
    &:hover::-webkit-scrollbar-thumb {
        background: rgba(255, 255, 255, 0.2);
    }
}

/* --- Context Menu Styles --- */
.add-context-wrapper {
    position: relative;
}

/* --- Unified Menu Styles (Antigravity) --- */
.context-menu, .pill-menu, .submenu-container {
    position: absolute;
    bottom: 100%;
    left: 0;
    right: auto;
    margin-bottom: 6px;
    background: rgba(20, 20, 20, 0.95); /* Deep dark background */
    backdrop-filter: blur(0); /* No blur needed for opaque dark */
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: 8px; /* Sharper radius */
    padding: 4px;
    box-shadow: 0 12px 40px rgba(0,0,0,0.6);
    z-index: 1000;
    min-width: 160px;
    display: flex;
    flex-direction: column;
    gap: 1px;
    transform-origin: bottom left;
    animation: scale-up 0.1s cubic-bezier(0.2, 0, 0.13, 1.5);
}

.submenu-container {
    bottom: 0;
    left: 100%; /* Flyout to the right */
    margin-bottom: 0;
    margin-left: 8px;
}

.submenu-container.left {
    left: auto;
    right: 100%;
    margin-left: 0;
    margin-right: 8px;
}

.menu-header {
    padding: 6px 8px 4px;
    font-size: 10px; /* Micro label */
    color: rgba(255, 255, 255, 0.3);
    font-weight: 600;
    letter-spacing: 0.5px;
    text-transform: uppercase;
}

.menu-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px;
    border-radius: 4px;
    font-size: 11px; /* Micro font */
    color: rgba(255, 255, 255, 0.7);
    cursor: pointer;
    transition: all 0.1s;
    min-height: 24px;
    position: relative;

    &:hover, &.active {
        background: rgba(255, 255, 255, 0.08);
        color: white;
    }
    
    .icon, .chevron {
        color: rgba(255, 255, 255, 0.4);
        width: 14px;
        height: 14px;
        display: flex;
        align-items: center;
        justify-content: center;
        svg { width: 100%; height: 100%; }
    }

    .check-icon {
        margin-left: auto;
        width: 12px;
        height: 12px;
        color: #007AFF;
    }

    .item-text {
        flex: 1;
    }
}

/* --- Add Model Styles --- */
.menu-divider {
    height: 1px;
    background: rgba(255, 255, 255, 0.1);
    margin: 4px 0;
}

.menu-item.add-model {
    color: rgba(255, 255, 255, 0.5);

    .add-icon {
        width: 14px;
        height: 14px;
        color: rgba(255, 255, 255, 0.4);
    }

    &:hover {
        color: #007AFF;
        .add-icon {
            color: #007AFF;
        }
    }
}

.add-model-input {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px;

    input {
        flex: 1;
        background: rgba(255, 255, 255, 0.1);
        border: 1px solid rgba(255, 255, 255, 0.2);
        border-radius: 4px;
        padding: 4px 8px;
        font-size: 11px;
        color: white;
        outline: none;

        &::placeholder {
            color: rgba(255, 255, 255, 0.4);
        }

        &:focus {
            border-color: #007AFF;
            background: rgba(0, 122, 255, 0.1);
        }
    }

    .input-actions {
        display: flex;
        gap: 4px;

        button {
            width: 22px;
            height: 22px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.1s;

            svg {
                width: 12px;
                height: 12px;
            }
        }

        .confirm-btn {
            background: #007AFF;
            color: white;

            &:hover:not(:disabled) {
                background: #0066DD;
            }

            &:disabled {
                opacity: 0.4;
                cursor: not-allowed;
            }
        }

        .cancel-btn {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.6);

            &:hover {
                background: rgba(255, 255, 255, 0.15);
                color: white;
            }
        }
    }
}

/* 扩展模型菜单宽度以适应输入框 */
.pill-menu.model-menu {
    min-width: 200px;
}

.submenu-container.left {
    right: 100%; /* Fly out to the left */
    left: auto;
    margin-right: 8px;
    animation: slideLeft 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.submenu-container.right {
    left: 100%; /* Fly out to the right */
    right: auto;
    margin-left: 8px;
    animation: slideRight 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.submenu {
    display: flex;
    flex-direction: column;
    gap: 2px;
}

@keyframes slideLeft {
    from { opacity: 0; transform: translateX(10px); }
    to { opacity: 1; transform: translateX(0); }
}

@keyframes slideRight {
    from { opacity: 0; transform: translateX(-10px); }
    to { opacity: 1; transform: translateX(0); }
}

/* Transition for main menu */
.scale-up-enter-active,
.scale-up-leave-active {
    transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.scale-up-enter-from,
.scale-up-leave-to {
    opacity: 0;
    transform: scale(0.95) translateY(10px);
}
.layer-stream {
    flex: 1;
    overflow-y: auto;
    scrollbar-gutter: stable; /* 预留滚动条空间，避免布局抖动 */
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;

    /* Scrollbar styling */
    &::-webkit-scrollbar { width: 4px; }
    &::-webkit-scrollbar-thumb { background: var(--border-dim); border-radius: 2px; }
}

.view-tasks {
    display: flex;
    flex-direction: column;
    gap: 16px;
}

/* Phase 2: 多窗口聊天容器 */
.view-chat-container {
    position: relative;
    width: 100%;
    height: 100%;
    overflow: hidden;
}

.window-chat-container {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    overflow-y: auto;
    overflow-x: hidden;
}

.view-chat {
    display: flex;
    flex-direction: column;
    gap: 16px;

    .chat-bottom-anchor {
        height: 1px;
        width: 100%;
        flex-shrink: 0;
    }

    .chat-message {
        display: flex;
        gap: 8px;
        align-items: flex-start;

        &.user {
            flex-direction: row-reverse;
            .bubble {
                background: var(--accent-primary);
                color: white;
                border: none;
                white-space: pre-wrap; // 用户消息保留换行
            }
        }

        &.ai {
            .bubble {
                background: var(--surface-card);
                border: 1px solid var(--border-dim);
                color: var(--text-primary);
            }
        }



        .message-wrapper {
            display: flex;
            flex-direction: column;
            gap: 8px;
            max-width: 92%;
        }

        .bubble {
            padding: 6px 10px; /* Reduced padding */
            border-radius: 12px;
            font-size: 0.8rem; /* Reduced from 0.85rem */
            line-height: 1.35; /* Tighter line height */
            word-wrap: break-word; /* Enable word wrapping */
            word-break: break-word; /* Break long words */
            overflow-wrap: break-word; /* Fallback for older browsers */
            /* Note: white-space: pre-wrap removed - causes bottom padding from trailing newlines */

            &.empty {
                min-height: 20px;
            }

            // === 覆盖 markstream-vue 库的默认样式（极致紧凑） ===

            // 顶层容器：清除所有默认间距
            :deep(.markdown-renderer) {
                margin: 0;
                padding: 0;
            }
            :deep(.node-slot),
            :deep(.node-content),
            :deep(.node-space) {
                margin: 0;
                padding: 0;
            }

            // 段落：适度间距
            :deep(p) {
                margin: 0;
            }
            :deep(.paragraph-node) {
                margin: 0.3em 0;
            }

            // 标题：舒适层次感
            :deep(.heading-1) {
                font-size: 1.05rem;
                margin: 0.7em 0 0.35em 0;
            }
            :deep(.heading-2) {
                font-size: 0.95rem;
                margin: 0.6em 0 0.3em 0;
            }
            :deep(.heading-3) {
                font-size: 0.88rem;
                margin: 0.5em 0 0.25em 0;
            }
            :deep(.heading-4),
            :deep(.heading-5),
            :deep(.heading-6) {
                font-size: 0.82rem;
                margin: 0.45em 0 0.2em 0;
            }

            // 列表：适度间距
            :deep(.list-node) {
                margin: 0.25em 0;
                padding-left: 1.1em;
            }
            :deep(.list-item) {
                margin: 0;
                padding: 0;
            }

            // 表格容器：舒适间距
            :deep(.table-node-wrapper) {
                margin: 0.5em 0;
            }

            // 表格：边框可见 + 舒适单元格
            :deep(.table-node) {
                --table-border: rgba(255, 255, 255, 0.25);
                margin: 0;
            }
            :deep(.table-node th),
            :deep(.table-node td) {
                border: 1px solid rgba(255, 255, 255, 0.2);
                padding: 0.3em 0.5em;
            }
            :deep(.table-node th) {
                background: rgba(255, 255, 255, 0.05);
            }

            // 引用块：适度间距
            :deep(.blockquote) {
                margin: 0.3em 0;
                padding-left: 0.6em;
            }

            // 代码块：适度间距
            :deep(.code-block-container) {
                margin: 0.3em 0;
            }

            // 分割线：适度间距
            :deep(.hr-node) {
                margin: 0.5em 0;
            }
        }

        @keyframes dot-fade {
            0% { opacity: 0; }
            50% { opacity: 1; }
            100% { opacity: 0; }
        }

        @keyframes spin {
            to { transform: rotate(360deg); }
        }

        @keyframes text-breathe {
            0%, 100% { opacity: 0.4; }
            50% { opacity: 1; }
        }

        /* Thinking Section Styles */
        .thinking-section {
            margin-bottom: 4px;

            .thinking-header {
                display: flex;
                align-items: center;
                gap: 6px;
                padding: 4px 0;
                cursor: pointer;
                user-select: none;
                opacity: 0.7;
                transition: opacity 0.2s;

                &:hover {
                    opacity: 1;
                }

                .thinking-label {
                    font-size: 0.8rem;
                    color: var(--text-tertiary);
                    font-style: italic;
                    /* font-family: 'SF Mono', 'Roboto Mono', monospace; Removed to match Generating... */
                    
                    .dot {
                        animation: dot-fade 1.5s infinite;
                        opacity: 0;
                    }
                    .dot:nth-child(1) { animation-delay: 0.0s; }
                    .dot:nth-child(2) { animation-delay: 0.5s; }
                    .dot:nth-child(3) { animation-delay: 1.0s; }
                }

                .thinking-chevron {
                    width: 14px;
                    height: 14px;
                    color: var(--text-tertiary);
                    transition: transform 0.2s;

                    &.expanded {
                        transform: rotate(90deg);
                    }
                }
            }

            .thinking-content {
                padding: 6px 10px; /* Reduced padding */
                color: var(--text-secondary);
                line-height: 1.4; /* Tighter line height */
                font-size: 0.8rem;
                border-left: 2px solid var(--border-dim);
                margin-left: 6px; /* Align with chevron center approx */
                margin-top: 2px;
                margin-bottom: 6px;
                /* white-space: pre-wrap;  Removed because MarkdownText handles it */
                background: rgba(0, 0, 0, 0.2); /* Very subtle background */
                border-radius: 0 8px 8px 0;
            }
        }

        /* SubAgents Section */
        .subagents-section {
            margin: 8px 0;
        }

        /* Universal vacuum period waiting indicator */
        .vacuum-generating {
            font-size: 0.8rem;
            color: var(--text-tertiary);
            font-style: italic;
            padding: 4px 0;
            margin-top: 4px;
            
            .generating-text .char {
                animation: text-breathe 1.5s ease-in-out infinite;
            }

            .dot {
                animation: dot-fade 1.5s infinite;
                opacity: 0;
            }
            .dot:nth-child(1) { animation-delay: 0.0s; }
            .dot:nth-child(2) { animation-delay: 0.5s; }
            .dot:nth-child(3) { animation-delay: 1.0s; }
        }
    }
}

/* Thinking expand transition */
.thinking-expand-enter-active,
.thinking-expand-leave-active {
    transition: all 0.2s ease;
    max-height: 200px;
    overflow: hidden;
}

.thinking-expand-enter-from,
.thinking-expand-leave-to {
    opacity: 0;
    max-height: 0;
}


.card {
    background: var(--surface-card);
    border: 1px solid var(--border-dim);
    border-radius: 12px;
    padding: 12px;
    
    &.task-card {
        .card-header {
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 0.85rem;
            color: var(--text-primary);
            margin-bottom: 8px;
            
            .spinner {
                width: 12px;
                height: 12px;
                border: 2px solid var(--accent-primary);
                border-top-color: transparent;
                border-radius: 50%;
                animation: spin 1s linear infinite;
            }
        }
        .progress-track {
            height: 4px;
            background: var(--border-dim);
            border-radius: 2px;
            overflow: hidden;
            margin-bottom: 8px;
            .progress-fill {
                height: 100%;
                background: var(--accent-primary);
                border-radius: 2px;
                transition: width 0.3s ease;
            }
        }
        .status {
            font-size: 0.75rem;
            color: var(--text-tertiary);
        }
        .card-actions {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            .text-btn {
                background: none;
                border: none;
                color: var(--text-secondary);
                font-size: 0.7rem;
                cursor: pointer;
                &:hover { color: var(--text-primary); }
            }
        }
    }

    &.alert-card {
        border-color: rgba(255, 165, 0, 0.3);
        background: rgba(255, 165, 0, 0.05);
        
        .alert-header {
            color: #ffb74d;
            font-weight: 600;
            font-size: 0.85rem;
            margin-bottom: 6px;
            display: flex;
            gap: 6px;
        }
        .alert-body {
            font-size: 0.8rem;
            color: var(--text-secondary);
            margin-bottom: 10px;
            line-height: 1.4;
        }
        .alert-actions {
            display: flex;
            gap: 8px;
            .action-btn {
                padding: 4px 8px;
                font-size: 0.75rem;
            }
        }
    }
}

.carousel-section {
    .section-title {
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 1px;
        color: var(--text-tertiary);
        margin-bottom: 8px;
    }
    .carousel-track {
        display: flex;
        gap: 10px;
        overflow-x: auto;
        padding-top: 6px;  /* Space for hover translateY(-4px) effect */
        padding-bottom: 4px;
        
        &::-webkit-scrollbar { height: 6px; }
        &::-webkit-scrollbar-thumb { background: var(--border-dim); border-radius: 3px; }
        &:hover::-webkit-scrollbar-thumb { background: var(--text-tertiary); }
    }
}

.proposal-card {
    min-width: 200px; /* Wider card */
    width: 200px;
    background: var(--surface-card);
    border-radius: 12px;
    overflow: hidden;
    border: 1px solid var(--border-dim);
    cursor: pointer;
    transition: all 0.2s;
    position: relative;
    display: flex;
    flex-direction: column;

    &:hover {
        transform: translateY(-4px);
        box-shadow: 0 8px 20px rgba(0,0,0,0.15);
        .hover-actions { opacity: 1; }
    }

    /* 1. Visual Thumbnail */
    .card-thumbnail {
        height: 110px; /* Dominant visual area */
        position: relative;
        
        .thumbnail-overlay {
            position: absolute;
            top: 8px;
            left: 8px;
            
            .preview-badge {
                background: rgba(0,0,0,0.4);
                backdrop-filter: blur(4px);
                color: white;
                font-size: 0.6rem;
                padding: 2px 6px;
                border-radius: 4px;
                font-weight: 500;
            }
        }
    }

    /* 2. Content Area */
    .card-content {
        padding: 10px;
        display: flex;
        flex-direction: column;
        gap: 8px;
        background: var(--surface-elevated);

        .card-header-row {
            .title {
                font-size: 0.85rem;
                font-weight: 600;
                color: var(--text-primary);
                margin-bottom: 4px;
            }
            .tags {
                display: flex;
                gap: 4px;
                flex-wrap: wrap;
                
                .tag {
                    font-size: 0.65rem;
                    padding: 1px 5px;
                    border-radius: 3px;
                    background: var(--surface-highlight);
                    color: var(--text-secondary);
                    border: 1px solid var(--border-subtle);
                }
            }
        }

        /* 3. Metrics */
        .metrics-row {
            display: flex;
            justify-content: space-between;
            padding: 6px 0;
            border-top: 1px solid var(--border-subtle);
            border-bottom: 1px solid var(--border-subtle);
            
            .metric {
                display: flex;
                flex-direction: column;
                gap: 1px;
                
                .label { font-size: 0.6rem; color: var(--text-tertiary); text-transform: uppercase; }
                .value { font-size: 0.75rem; font-weight: 500; color: var(--text-primary); }
            }
        }

        /* 4. AI Insight */
        .insight-row {
            display: flex;
            gap: 4px;
            align-items: flex-start;
            
            .icon { font-size: 0.7rem; margin-top: 1px; }
            .text {
                font-size: 0.65rem;
                color: var(--text-secondary);
                line-height: 1.3;
                display: -webkit-box;
                -webkit-line-clamp: 2;
                -webkit-box-orient: vertical;
                overflow: hidden;
            }
        }
    }

    .hover-actions {
        position: absolute;
        inset: 0;
        background: rgba(0,0,0,0.3); /* Lighter overlay to see content */
        backdrop-filter: blur(2px);
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 8px;
        opacity: 0;
        transition: opacity 0.2s;
        
        .action-btn {
            font-size: 0.75rem;
            padding: 6px 16px;
            border-radius: 16px;
            border: 1px solid rgba(255,255,255,0.3);
            background: rgba(0,0,0,0.6);
            color: white;
            cursor: pointer;
            font-weight: 500;
            width: 80%;
            
            &.primary {
                background: var(--accent-primary);
                border-color: var(--accent-primary);
                box-shadow: 0 4px 12px rgba(0,0,0,0.2);
            }
            
            &:hover {
                transform: scale(1.05);
            }
        }
    }
}

/* --- Layer 3: Command Footer --- */
.layer-footer {
    padding: 16px 20px;
    background: transparent; /* Force transparent to let glass bg show through */
    border-top: 1px solid var(--border-dim);
}

.context-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 12px;
    flex-wrap: wrap;

    .context-chip {
        display: flex;
        align-items: center;
        gap: 6px;
        padding: 4px 8px;
        background: var(--surface-dim);
        border: 1px solid var(--border-dim);
        border-radius: 6px;
        font-size: 0.75rem;
        color: var(--text-secondary);
        transition: all 0.2s;
        cursor: default;
        user-select: none;

        .chip-icon {
            display: flex;
            align-items: center;
            color: var(--text-tertiary);
            svg { width: 14px; height: 14px; }
        }

        .chip-text {
            font-weight: 500;
        }

        &.scope {
            background: rgba(10, 132, 255, 0.1); /* Fallback blue tint */
            border-color: transparent;
            .chip-icon { color: var(--accent-blue); }
            .chip-text { color: var(--accent-blue); }

            &:hover {
                border-color: var(--accent-blue);
                background: rgba(10, 132, 255, 0.15);
            }
        }

        &.selection {
            background: var(--surface-highlight);
            border-color: var(--border-subtle);
            .chip-text { color: var(--text-primary); }
            
            .chip-close {
                display: flex;
                align-items: center;
                justify-content: center;
                background: none;
                border: none;
                padding: 2px;
                margin-left: 2px;
                cursor: pointer;
                color: var(--text-tertiary);
                border-radius: 4px;
                
                svg { width: 12px; height: 12px; }

                &:hover {
                    background: var(--surface-dim);
                    color: var(--text-primary);
                }
            }
        }

        &:hover {
            border-color: var(--border-subtle);
            box-shadow: 0 2px 4px rgba(0,0,0,0.05);
        }
    }

    .add-context-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 6px;
        border: 1px dashed var(--border-dim);
        background: transparent;
        color: var(--text-tertiary);
        cursor: pointer;
        transition: all 0.2s;

        svg { width: 14px; height: 14px; }

        &:hover {
            border-color: var(--text-secondary);
            color: var(--text-secondary);
            background: var(--surface-dim);
        }
    }
}

/* Chip Animation */
.chip-fade-enter-active,
.chip-fade-leave-active {
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    max-width: 200px;
    opacity: 1;
    overflow: hidden;
    white-space: nowrap;
}

.chip-fade-enter-from,
.chip-fade-leave-to {
    opacity: 0;
    max-width: 0;
    padding-left: 0;
    padding-right: 0;
    margin-left: 0;
    margin-right: 0;
    border-width: 0;
    transform: scale(0.95);
}

/* --- Polling Indicator --- */
.polling-indicator {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 16px;
    margin: 0 4px 8px;
    background: rgba(251, 191, 36, 0.1);  /* amber/warning color */
    border-radius: 8px;
    border: 1px solid rgba(251, 191, 36, 0.3);
    color: rgba(251, 191, 36, 0.9);
    font-size: 0.8rem;

    .polling-dot {
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background: rgba(251, 191, 36, 0.9);
        animation: pulse-polling 1.5s ease-in-out infinite;
    }

    .polling-text {
        font-weight: 500;
    }
}

@keyframes pulse-polling {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.4; transform: scale(1.2); }
}

.slide-down-enter-active,
.slide-down-leave-active {
    transition: all 0.3s ease;
}

.slide-down-enter-from,
.slide-down-leave-to {
    opacity: 0;
    transform: translateY(-10px);
}

/* --- Antigravity Input Box --- */
/* --- Antigravity Input Box --- */
.antigravity-input-box {
    margin: 0 4px 16px; /* Widen the box */
    background: rgba(255, 255, 255, 0.03); /* Lighter, more distinct background */
    backdrop-filter: blur(20px);
    -webkit-backdrop-filter: blur(20px);
    border-radius: 16px;
    border: none; /* Remove solid border */
    /* Optical border + Top highlight + Deep shadow */
    box-shadow: 
        inset 0 0 0 0.5px rgba(255, 255, 255, 0.1),
        inset 0 1px 0 rgba(255, 255, 255, 0.05),
        0 4px 24px rgba(0, 0, 0, 0.2);
    display: flex;
    flex-direction: column;
    position: relative;
    transition: all 0.2s ease;

    &:focus-within {
        background: rgba(255, 255, 255, 0.05); /* Keep it bright/glassy, slightly more opaque */
        box-shadow:
            inset 0 0 0 0.5px rgba(255, 255, 255, 0.2), /* Brighter border */
            inset 0 1px 0 rgba(255, 255, 255, 0.1),
            0 8px 32px rgba(0, 0, 0, 0.4);
    }

    .pending-attachments {
        display: flex;
        gap: 8px;
        padding: 12px 12px 8px;
        flex-wrap: wrap;

        .attachment-item {
            position: relative;
            width: 64px;
            height: 64px;
            flex-shrink: 0;
        }

        .attachment-thumbnail {
            width: 100%;
            height: 100%;
            object-fit: cover;
            border-radius: 8px;
            border: 1px solid rgba(255, 255, 255, 0.15);
        }

        .remove-attachment {
            position: absolute;
            top: -6px;
            right: -6px;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            background: #ff4444;
            border: none;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            transition: transform 0.15s ease, background 0.15s ease;

            &:hover {
                background: #ff2222;
                transform: scale(1.1);
            }

            svg {
                width: 12px;
                height: 12px;
                stroke: white;
            }
        }
    }

    textarea {
        display: block; /* Ensure block layout */
        width: 100%;
        box-sizing: border-box; /* Critical for correct width with padding */
        background: transparent;
        border: none;
        color: #E0E0E0;
        font-size: 0.9rem;
        line-height: 1.5;
        padding: 12px 16px 4px; /* Top padding */
        resize: none;
        outline: none;
        font-family: inherit;
        max-height: 200px;
        overflow-y: auto !important; /* Force scroll capability */
        
        /* Custom Scrollbar */
        &::-webkit-scrollbar { 
            width: 6px; 
            background: transparent;
        }
        &::-webkit-scrollbar-track { 
            background: rgba(255, 255, 255, 0.02); /* Faint track */
            border-radius: 3px;
            margin: 8px 0;
        }
        &::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.2); /* Subtle visibility */
            border-radius: 3px; 
        }
        &:hover::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.4); /* Visible on hover */
        }

        &::placeholder {
            color: rgba(255, 255, 255, 0.3);
        }
    }

    .input-footer {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 4px 8px 4px; /* Extremely compact padding */
    }

    .left-controls {
        display: flex;
        align-items: center;
        gap: 4px; /* Compact gap */
    }

    .right-controls {
        display: flex;
        align-items: center;
    }

    /* Buttons & Pills */
    .icon-btn {
        width: 28px;
        height: 28px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: transparent;
        border: none;
        border-radius: 6px;
        color: rgba(255, 255, 255, 0.4);
        cursor: pointer;
        transition: all 0.2s;

        &:hover, &.active {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.8);
        }
        
        svg { width: 16px; height: 16px; }
    }

    .control-pill-wrapper {
        position: relative;
    }

    .control-pill {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 0 6px;
        background: transparent;
        border: 1px solid transparent;
        border-radius: 4px;
        color: rgba(255, 255, 255, 0.5);
        font-size: 11px; /* Micro typography */
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        height: 24px; /* Precision scale height */
        letter-spacing: 0.3px;

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            border-color: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.9);
        }
        
        /* Active state when menu is open */
        .open & {
            background: rgba(255, 255, 255, 0.1);
            border-color: rgba(255, 255, 255, 0.15);
            color: rgba(255, 255, 255, 1);
        }

        .prefix-icon {
            display: flex;
            align-items: center;
            opacity: 0.8;
            svg { width: 12px; height: 12px; }
        }
    }

    .send-btn-round {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        background: #007AFF; /* Antigravity Blue */
        border: none;
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        cursor: pointer;
        transition: transform 0.1s, background 0.2s;
        
        svg { width: 16px; height: 16px; transform: rotate(90deg); /* Point up */ }

        &:hover {
            background: #0062cc;
        }
        &:active {
            transform: scale(0.95);
        }
        &:disabled {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.3);
            cursor: not-allowed;
        }
    }

    /* Menus */
}

/* Branch Select Override for Compact Tab View */
.branch-select-compact {
  display: inline-flex;
  
  :deep(.select-trigger) {
    padding: 0;
    border: none;
    background: transparent !important;
    height: auto;
    min-height: unset;
    
    &:hover {
      background: transparent !important;
      
      .branch-trigger {
        background: rgba(255, 255, 255, 0.1);
      }
    }
    
    &.active .branch-trigger {
      background: rgba(255, 255, 255, 0.15);
      color: var(--text-primary);
    }
  }
}

.branch-trigger {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 6px;
  border-radius: 4px;
  transition: all 0.2s;
  cursor: pointer;
  color: var(--text-secondary);

  &:hover {
    background: rgba(255, 255, 255, 0.1);
    color: var(--text-primary);
  }

  .branch-name {
    max-width: 100px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  
  .chevron {
    opacity: 0.5;
  }
}

/* New Window Button Override */
.new-window-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
  margin-left: 4px;

  &:hover {
    background: rgba(255, 255, 255, 0.1);
    color: var(--text-primary);
  }
  
  /* When GlassSelect is open, it adds 'active' class to trigger */
  /* But here the trigger IS the button inside the slot */
}

/* ============================================== */
/* Unified Dropdown Styles (GlassSelect-like)    */
/* ============================================== */
.unified-dropdown {
  position: absolute;
  background: #14141e !important; /* Solid opaque background - force override */
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 4px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
  z-index: 200;
  width: max-content; /* Grow to fit content */
  min-width: 260px; /* Reasonable minimum to fit branch name + tags */
  max-width: 320px; /* Prevent excessive width */
  max-height: 320px;
  overflow-y: auto;
  overflow-x: hidden;

  .dropdown-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 6px 10px; /* More compact like GlassSelect */
    color: var(--text-secondary);
    font-size: 0.8rem; /* Slightly smaller */
    cursor: pointer;
    border-radius: 4px;
    transition: all 0.2s;
    position: relative;

    &:hover {
      background: rgba(255, 255, 255, 0.05);
      color: var(--text-primary);
    }

    &.selected {
      background: rgba(59, 130, 246, 0.15);
      color: var(--accent-blue);
    }

    &.disabled {
      opacity: 0.5;
      cursor: not-allowed;

      &:hover {
        background: transparent;
        color: var(--text-secondary);
      }
    }

    &.create-new {
      /* Inherit default color */

      &:hover {
        background: rgba(59, 130, 246, 0.1);
      }
    }
  }

  .option-main {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: 1;
    min-width: 0; /* Enable truncation in flex child */
  }

  .option-icon {
    font-size: 1rem;
    line-height: 1;
    display: flex;
    align-items: center;
    flex-shrink: 0;

    svg {
      width: 16px;
      height: 16px;
    }
  }

  .option-label {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .option-tags {
    display: flex;
    gap: 4px;
    flex-shrink: 0;
  }

  .tag-badge {
    font-size: 0.65rem;
    padding: 2px 6px;
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid rgba(255, 255, 255, 0.05);
    border-radius: 4px;
    color: var(--text-secondary);
    white-space: nowrap;
    max-width: 120px;
    overflow: hidden;
    text-overflow: ellipsis;
    font-family: var(--font-mono, monospace);
  }

  .option-hint {
    font-size: 0.7rem;
    color: var(--text-muted);
    font-style: italic;
    white-space: nowrap;
  }

  .check-icon {
    flex-shrink: 0;
    color: var(--accent-blue);
    margin-left: 4px;
  }

  .dropdown-divider {
    height: 1px;
    background: rgba(255, 255, 255, 0.1);
    margin: 4px 0;
  }
}

/* Position override for specific dropdowns */
.branch-dropdown-overlay {
  top: 100%;
  left: 0;
  margin-top: 4px;
}

.new-window-dropdown {
  /* Position already set via inline styles */
}
</style>
