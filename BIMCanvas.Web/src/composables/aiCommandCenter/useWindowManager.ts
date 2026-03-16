import { computed, nextTick, ref, watch } from 'vue';
import type { Ref } from 'vue';
import type { GitBranch } from '../../stores/gitStore';
import type { ChatMessage, ChatWindow, DropdownPosition } from '../../types/aiCommandCenter';
import { GitWorktreeService } from '../../services/GitWorktreeService';
import { SignalRService } from '../../services/SignalRService';

interface WindowManagerOptions {
  branches: Ref<GitBranch[]>;
  currentBranch: Ref<string | null>;
  gitStore: {
    checkout: (branchId: string, options?: Record<string, unknown>) => Promise<{ success: boolean; message?: string; hasUncommittedChanges?: boolean }>;
    fetchBranches: () => Promise<void>;
  };
  store: {
    loadProject: (options: { source: string; preserveView: boolean }) => Promise<void>;
    saveToServer: () => Promise<boolean>;
  };
  agentApiBase: string;
}

export const useWindowManager = (options: WindowManagerOptions) => {
  const windows = ref<ChatWindow[]>([]);
  const activeWindowId = ref('');
  const showNewWindowDropdown = ref(false);
  const showBranchCreationDialog = ref(false);
  const showCheckoutConfirmDialog = ref(false);
  const pendingCheckoutBranch = ref('');
  const pendingWindowId = ref('');
  const pendingIsCreateBranch = ref(false);
  const isBranchDropdownOpen = ref(false);
  const branchCreationSource = ref<'newWindow' | 'primarySwitch'>('newWindow');
  const newWindowDropdownPosition = ref<DropdownPosition>({ top: 0 });

  let streamWelcomeMessage: (() => Promise<void> | void) | null = null;
  const setStreamWelcomeMessage = (handler: () => Promise<void> | void) => {
    streamWelcomeMessage = handler;
  };

  let chatScrollRefs = ref<Record<string, HTMLElement | null>>({});
  const setChatScrollRefs = (refs: Ref<Record<string, HTMLElement | null>>) => {
    chatScrollRefs = refs;
  };

  const activeWindow = computed(() =>
    windows.value.find(w => w.id === activeWindowId.value) || windows.value[0]
  );

  const initDefaultWindow = () => {
    if (windows.value.length > 0) return;
    const defaultId = 'window-main';
    windows.value = [{
      id: defaultId,
      name: 'Main',
      branchId: options.currentBranch.value || 'main',
      messages: [],
      isPrimary: true,
      inputMessage: '',
      isStreaming: false,
      pendingImages: [],
      scrollPosition: 0,
      expandedThinking: {},
      shouldAutoScroll: true
    }];
    activeWindowId.value = defaultId;
  };

  watch(options.currentBranch, (newBranch) => {
    if (!newBranch) return;
    const primaryWindow = windows.value.find(w => w.isPrimary);
    if (primaryWindow &&
        primaryWindow.id === activeWindowId.value &&
        primaryWindow.branchId !== newBranch) {
      primaryWindow.branchId = newBranch;
    }
  }, { immediate: true });

  const addMessage = (message: ChatMessage): number => {
    const win = activeWindow.value;
    if (!win) return -1;
    const index = win.messages.length;
    win.messages.push(message);
    return index;
  };

  const addMessageToWindow = (windowId: string, message: ChatMessage): number => {
    const win = windows.value.find(w => w.id === windowId);
    if (!win) return -1;
    const index = win.messages.length;
    win.messages.push(message);
    return index;
  };

  const getWindowMessage = (windowId: string, msgIndex: number): ChatMessage | undefined => {
    const win = windows.value.find(w => w.id === windowId);
    return win?.messages[msgIndex];
  };

  const switchWindow = async (id: string) => {
    if (activeWindowId.value === id) return;

    const win = windows.value.find(w => w.id === id);
    if (!win) return;

    const currentWin = activeWindow.value;
    const currentScrollRef = chatScrollRefs.value[activeWindowId.value];
    if (currentScrollRef && currentWin) {
      currentWin.scrollPosition = currentScrollRef.scrollTop;
    }

    activeWindowId.value = id;
    options.branches.value.forEach(b => b.isCurrent = b.id === win.branchId);

    try {
      await fetch('http://localhost:5000/api/windows/activate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ windowId: id })
      });
      console.log(`[Window] 激活窗口: ${win.name} (${id})`);
    } catch (error) {
      console.warn('[Window] 通知 Server 激活窗口失败:', error);
    }

    await options.store.loadProject({ source: 'git_checkout', preserveView: true });
    console.log('[Window] 重新加载项目数据完成');

    nextTick(() => {
      const targetScrollRef = chatScrollRefs.value[id];
      if (targetScrollRef && win.scrollPosition) {
        targetScrollRef.scrollTop = win.scrollPosition;
      }
    });
  };

  const branchOptionsForDialog = computed(() =>
    options.branches.value.map(b => ({ label: b.name, value: b.id }))
  );

  const handleCreateNewBranch = () => {
    showNewWindowDropdown.value = false;
    branchCreationSource.value = 'newWindow';
    showBranchCreationDialog.value = true;
  };

  const handleCreateNewBranchForPrimary = () => {
    isBranchDropdownOpen.value = false;
    branchCreationSource.value = 'primarySwitch';
    showBranchCreationDialog.value = true;
  };

  const setPrimaryWindowLoading = (loading: boolean) => {
    const primaryWindow = windows.value.find(w => w.isPrimary);
    if (primaryWindow) {
      primaryWindow.isLoading = loading;
    }
  };

  const selectBranch = async (branchId: string) => {
    isBranchDropdownOpen.value = false;
    setPrimaryWindowLoading(true);

    try {
      const result = await options.gitStore.checkout(branchId);

      if (result.success) {
        return;
      }

      if (result.hasUncommittedChanges) {
        pendingCheckoutBranch.value = branchId;
        showCheckoutConfirmDialog.value = true;
        setPrimaryWindowLoading(false);
        return;
      }

      console.error('切换分支失败:', result.message);
    } finally {
      setPrimaryWindowLoading(false);
    }
  };

  const handleCheckoutConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
    showCheckoutConfirmDialog.value = false;
    const branchName = pendingCheckoutBranch.value;
    const targetWindowId = pendingWindowId.value;
    const isCreateBranch = pendingIsCreateBranch.value;
    if (!branchName) return;

    setPrimaryWindowLoading(true);

    try {
      if (saveBeforeSwitch) {
        const saved = await options.store.saveToServer();
        if (!saved) {
          console.error('保存数据失败，无法切换分支');
          pendingCheckoutBranch.value = '';
          pendingWindowId.value = '';
          pendingIsCreateBranch.value = false;
          return;
        }

        await options.gitStore.checkout(branchName, {
          commitBeforeCheckout: true,
          commitMessage,
          createIfNotExist: isCreateBranch
        });
      } else {
        await options.gitStore.checkout(branchName, {
          discardBeforeCheckout: true,
          createIfNotExist: isCreateBranch
        });
      }

      pendingCheckoutBranch.value = '';
      pendingIsCreateBranch.value = false;

      if (targetWindowId) {
        switchWindow(targetWindowId);
        pendingWindowId.value = '';
      }
    } finally {
      setPrimaryWindowLoading(false);
    }
  };

  const handleCheckoutCancel = () => {
    showCheckoutConfirmDialog.value = false;
    pendingCheckoutBranch.value = '';
    pendingWindowId.value = '';
    pendingIsCreateBranch.value = false;
  };

  const handleBranchCreated = async (data: { name: string; baseBranch: string; reason: string; switchAfterCreate?: boolean }) => {
    showBranchCreationDialog.value = false;

    const isPrimarySwitch = branchCreationSource.value === 'primarySwitch';
    if (isPrimarySwitch) {
      const primaryWindow = windows.value.find(w => w.isPrimary);
      if (primaryWindow) primaryWindow.isLoading = true;
    }

    try {
      const result = await options.gitStore.checkout(data.name, {
        createIfNotExist: true,
        commitMessage: data.reason,
        baseBranch: data.baseBranch,
        switchAfterCreate: data.switchAfterCreate ?? true
      });
      if (result.success) {
        await options.gitStore.fetchBranches();

        if (branchCreationSource.value === 'newWindow') {
          addWindow(data.name);
        }
        return;
      }

      if (result.hasUncommittedChanges) {
        pendingCheckoutBranch.value = data.name;
        pendingWindowId.value = '';
        pendingIsCreateBranch.value = true;
        showCheckoutConfirmDialog.value = true;
        if (isPrimarySwitch) {
          const primaryWindow = windows.value.find(w => w.isPrimary);
          if (primaryWindow) primaryWindow.isLoading = false;
        }
        return;
      }

      console.error('创建/切换分支失败:', result.message);
    } finally {
      if (isPrimarySwitch) {
        const primaryWindow = windows.value.find(w => w.isPrimary);
        if (primaryWindow) primaryWindow.isLoading = false;
      }
    }
  };

  const isBranchOccupiedByOther = (branchName: string): boolean => {
    return windows.value.some(w =>
      w.branchId === branchName && w.id !== activeWindowId.value
    );
  };

  const isBranchOccupied = (branchName: string): boolean => {
    return windows.value.some(w => w.branchId === branchName);
  };

  const closeWindow = async (id: string) => {
    const index = windows.value.findIndex(w => w.id === id);
    if (index === -1) return;

    const win = windows.value[index];
    if (win.isPrimary) {
      console.warn('[Window] Cannot close primary window');
      return;
    }

    if (win.isLoading) {
      console.warn('[Window] Cannot close window while loading');
      return;
    }

    win.isLoading = true;
    console.log(`[Window] Closing window: ${win.name}...`);

    // ① 先关闭 Agent（释放 claude.exe 对 worktree 目录的 CWD 文件锁）
    try {
      await fetch(`${options.agentApiBase}/api/agent/close`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ windowId: id })
      });
      console.log(`[Window] Agent 实例已关闭: ${id}`);
    } catch (error: any) {
      console.warn(`[Window] 关闭 Agent 实例失败: ${error.message}`);
    }

    // ② 再删除 Worktree（Agent 已释放 CWD 锁，目录可被删除）
    try {
      if (win.worktreeName) {
        await GitWorktreeService.deleteWorktree(win.worktreeName, false);
        console.log(`[Window] Worktree deleted: ${win.worktreeName}`);
      }
    } catch (error: any) {
      console.error(`[Window] Delete worktree failed: ${error.message}`);
    }

    // ③ 注销映射
    try {
      await fetch(`http://localhost:5000/api/windows/worktree/${id}`, {
        method: 'DELETE'
      });
      console.log(`[Window] 注销 Worktree 映射: ${id}`);
    } catch (error: any) {
      console.warn(`[Window] 注销 Worktree 映射失败: ${error.message}`);
    }

    windows.value.splice(index, 1);
    console.log(`[Window] Closed window: ${win.name}`);

    if (activeWindowId.value === id) {
      const newActiveIndex = Math.min(index, windows.value.length - 1);
      const newActiveWin = windows.value[newActiveIndex];
      if (newActiveWin) {
        activeWindowId.value = newActiveWin.id;
        options.branches.value.forEach(b => b.isCurrent = b.id === newActiveWin.branchId);
        await options.store.loadProject({ source: 'git_checkout', preserveView: true });
      }
    }
  };

  const handleNewWindowClick = (event: MouseEvent) => {
    isBranchDropdownOpen.value = false;

    const btn = event.currentTarget as HTMLElement;
    if (btn) {
      const rect = btn.getBoundingClientRect();
      const parentRect = btn.closest('.header-tabs')?.getBoundingClientRect();
      if (parentRect) {
        const dropdownWidth = 280;
        const viewportWidth = window.innerWidth;
        const spaceOnRight = viewportWidth - rect.left;
        const top = parentRect.height + 4;

        if (spaceOnRight >= dropdownWidth + 8) {
          newWindowDropdownPosition.value = {
            top,
            left: rect.left - parentRect.left,
            right: undefined
          };
        } else {
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

  const toggleBranchDropdown = () => {
    showNewWindowDropdown.value = false;
    const opening = !isBranchDropdownOpen.value;
    isBranchDropdownOpen.value = opening;

    if (opening) {
      void options.gitStore.fetchBranches();
    }
  };

  const handleWindowTabClick = async (win: ChatWindow) => {
    if (activeWindowId.value !== win.id) {
      // 窗口切换 ≠ 分支切换，不需要 git checkout
      // switchWindow 会通知 Server 激活窗口并重新加载项目数据
      switchWindow(win.id);
    }
  };

  const addWindow = async (branchName: string) => {
    const branch = options.branches.value.find(b => b.name === branchName);
    if (!branch) return;

    const timestamp = Date.now();
    const worktreeName = `window-${timestamp}`;
    const newId = `window-${timestamp}`;
    const windowNumber = windows.value.length + 1;

    const newWindow: ChatWindow = {
      id: newId,
      name: `Chat ${windowNumber}`,
      branchId: branch.name,
      messages: [],
      isPrimary: false,
      worktreeName,
      isLoading: true,
      error: null,
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

    try {
      await GitWorktreeService.createWorktree({
        name: worktreeName,
        branch: branch.name,
        intent: 'parallel'
      });

      const idx = windows.value.findIndex(w => w.id === newId);
      if (idx !== -1) {
        windows.value[idx].isLoading = false;
        console.log(`[Window] Created successfully: ${newWindow.name}`);
      }

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

        const pathIdx = windows.value.findIndex(w => w.id === newId);
        if (pathIdx !== -1) {
          windows.value[pathIdx].worktreePath = createdWorktree.path;
        }

        await options.store.loadProject({ source: 'git_checkout', preserveView: true });
        console.log('[Window] 重新加载项目数据完成');
      }

      SignalRService.getInstance().registerWindow(newId, branch.name);

      if (streamWelcomeMessage) {
        await streamWelcomeMessage();
      }
    } catch (error: any) {
      const idx = windows.value.findIndex(w => w.id === newId);
      if (idx !== -1) {
        windows.value[idx].isLoading = false;
        windows.value[idx].error = error.message || '创建失败';
        console.error(`[Window] Create failed: ${error.message}`);
      }
      setTimeout(() => {
        const idx = windows.value.findIndex(w => w.id === newId);
        if (idx !== -1 && windows.value[idx].error) {
          windows.value.splice(idx, 1);
          const primary = windows.value.find(w => w.isPrimary);
          if (primary) switchWindow(primary.id);
        }
      }, 3000);
    }
  };

  return {
    windows,
    activeWindowId,
    activeWindow,
    showNewWindowDropdown,
    showBranchCreationDialog,
    showCheckoutConfirmDialog,
    pendingCheckoutBranch,
    pendingWindowId,
    pendingIsCreateBranch,
    isBranchDropdownOpen,
    branchCreationSource,
    newWindowDropdownPosition,
    branchOptionsForDialog,
    initDefaultWindow,
    addMessage,
    addMessageToWindow,
    getWindowMessage,
    switchWindow,
    handleCreateNewBranch,
    handleCreateNewBranchForPrimary,
    handleBranchCreated,
    selectBranch,
    handleCheckoutConfirm,
    handleCheckoutCancel,
    isBranchOccupiedByOther,
    isBranchOccupied,
    closeWindow,
    handleNewWindowClick,
    toggleBranchDropdown,
    handleWindowTabClick,
    addWindow,
    setChatScrollRefs,
    setStreamWelcomeMessage
  };
};
