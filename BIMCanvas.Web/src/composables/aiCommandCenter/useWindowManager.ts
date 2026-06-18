import { computed, nextTick, ref, watch } from 'vue';
import type { Ref } from 'vue';
import type { GitBranch } from '../../stores/gitStore';
import type { ChatMessage, ChatWindow, DropdownPosition } from '../../types/aiCommandCenter';
import { ChangeSource, type LoadOptions } from '../../types/history';
import { GitWorktreeService } from '../../services/GitWorktreeService';
import { SignalRService } from '../../services/SignalRService';
import { createDraftMessageId } from '../../services/ChatAttachmentService';
import { SERVER_API } from '../../config/api';
import { createLogger } from '../../utils/logger';

const log = createLogger('SYS');

const WINDOW_SESSION_STORAGE_KEY = 'bimcanvas.ai-command-center.window-session.v1';

interface PersistedChatWindow {
  id: string;
  name: string;
  branchId: string;
  isPrimary: boolean;
  worktreeName?: string;
  worktreePath?: string;
  scrollPosition?: number;
}

interface WindowSessionSnapshot {
  version: 1;
  activeWindowId: string;
  windows: PersistedChatWindow[];
}

interface WindowManagerOptions {
  branches: Ref<GitBranch[]>;
  currentBranch: Ref<string | null>;
  gitStore: {
    checkout: (branchId: string, options?: Record<string, unknown>) => Promise<{ success: boolean; message?: string; hasUncommittedChanges?: boolean }>;
    fetchBranches: () => Promise<void>;
  };
  store: {
    loadInitialProject: (options: LoadOptions | ChangeSource) => Promise<boolean>;
    saveModules: () => Promise<boolean>;
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

  const buildPersistedWindow = (window: ChatWindow): PersistedChatWindow => ({
    id: window.id,
    name: window.name,
    branchId: window.branchId,
    isPrimary: window.isPrimary,
    worktreeName: window.worktreeName,
    worktreePath: window.worktreePath,
    scrollPosition: window.scrollPosition
  });

  const persistWindowSession = () => {
    if (typeof window === 'undefined') {
      return;
    }

    if (windows.value.length === 0) {
      window.sessionStorage.removeItem(WINDOW_SESSION_STORAGE_KEY);
      return;
    }

    const snapshot: WindowSessionSnapshot = {
      version: 1,
      activeWindowId: activeWindowId.value,
      windows: windows.value.map(buildPersistedWindow)
    };

    window.sessionStorage.setItem(WINDOW_SESSION_STORAGE_KEY, JSON.stringify(snapshot));
  };

  const syncWindowActivation = async (windowId: string) => {
    const win = windows.value.find(w => w.id === windowId);
    if (!win) return;

    options.branches.value.forEach(b => b.isCurrent = b.id === win.branchId);

    try {
      await fetch(`${SERVER_API}/windows/activate`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ windowId })
      });
      log.debug('window activated', { name: win.name, windowId });
    } catch (error) {
      log.warn('notify server window activation failed', { err: error });
    }

    await options.store.loadInitialProject({ source: ChangeSource.GitCheckout, preserveView: true });
    log.debug('project data reloaded');
  };

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
      draftMessageId: createDraftMessageId(),
      isStreaming: false,
      todoProgress: null,
      pendingAttachments: [],
      pendingSpatialMarks: [],
      spatialMarkDraft: null,
      queuedMessage: null,
      scrollPosition: 0,
      expandedThinking: {},
      shouldAutoScroll: true
    }];
    activeWindowId.value = defaultId;
  };

  const restoreWindowSession = async (): Promise<boolean> => {
    if (typeof window === 'undefined') {
      return false;
    }

    const raw = window.sessionStorage.getItem(WINDOW_SESSION_STORAGE_KEY);
    if (!raw) {
      return false;
    }

    let snapshot: WindowSessionSnapshot | null = null;
    try {
      snapshot = JSON.parse(raw) as WindowSessionSnapshot;
    } catch (error) {
      log.warn('parse window snapshot failed, ignored', { err: error });
      window.sessionStorage.removeItem(WINDOW_SESSION_STORAGE_KEY);
      return false;
    }

    if (snapshot?.version !== 1 || !Array.isArray(snapshot.windows) || snapshot.windows.length === 0) {
      window.sessionStorage.removeItem(WINDOW_SESSION_STORAGE_KEY);
      return false;
    }

    windows.value = snapshot.windows.map(saved => ({
      id: saved.id,
      name: saved.name,
      branchId: saved.branchId,
      messages: [],
      isPrimary: saved.isPrimary,
      worktreeName: saved.worktreeName,
      worktreePath: saved.worktreePath,
      isLoading: false,
      error: null,
      inputMessage: '',
      draftMessageId: createDraftMessageId(),
      isStreaming: false,
      todoProgress: null,
      pendingAttachments: [],
      pendingSpatialMarks: [],
      spatialMarkDraft: null,
      queuedMessage: null,
      scrollPosition: saved.scrollPosition ?? 0,
      expandedThinking: {},
      shouldAutoScroll: true
    }));

    const fallbackWindow = windows.value[0];
    activeWindowId.value = windows.value.some(w => w.id === snapshot?.activeWindowId)
      ? snapshot!.activeWindowId
      : (fallbackWindow?.id || '');

    for (const restoredWindow of windows.value) {
      if (!restoredWindow.worktreePath) {
        continue;
      }

      try {
        await fetch(`${SERVER_API}/windows/register-worktree`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            windowId: restoredWindow.id,
            worktreePath: restoredWindow.worktreePath
          })
        });
      } catch (error) {
        log.warn('restore worktree mapping failed', { windowId: restoredWindow.id, err: error });
      }

      void SignalRService.getInstance().registerWindow(restoredWindow.id, restoredWindow.branchId);
    }

    log.debug('window snapshots restored', { count: windows.value.length });
    return true;
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
    await syncWindowActivation(id);

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

      log.error('branch switch failed', { message: result.message });
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
        const saved = await options.store.saveModules();
        if (!saved) {
          log.error('save data failed, cannot switch branch');
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

      log.error('create/switch branch failed', { message: result.message });
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
    if (!win) return;

    if (win.isPrimary) {
      log.warn('cannot close primary window');
      return;
    }

    if (win.isLoading) {
      log.warn('cannot close window while loading');
      return;
    }

    win.isLoading = true;
    log.debug('closing window', { name: win.name });

    // ① 先关闭 Agent（释放 claude.exe 对 worktree 目录的 CWD 文件锁）
    try {
      await fetch(`${options.agentApiBase}/api/agent/close`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ windowId: id })
      });
      log.debug('agent instance closed', { windowId: id });
    } catch (error: any) {
      log.warn('close agent instance failed', { err: error.message });
    }

    // ② 再删除 Worktree（Agent 已释放 CWD 锁，目录可被删除）
    try {
      if (win.worktreeName) {
        await GitWorktreeService.deleteWorktree(win.worktreeName, false);
        log.debug('worktree deleted', { worktreeName: win.worktreeName });
      }
    } catch (error: any) {
      log.error('delete worktree failed', { err: error.message });
    }

    // ③ 注销映射
    try {
      await fetch(`${SERVER_API}/windows/worktree/${id}`, {
        method: 'DELETE'
      });
      log.debug('worktree mapping unregistered', { windowId: id });
    } catch (error: any) {
      log.warn('unregister worktree mapping failed', { err: error.message });
    }

    windows.value.splice(index, 1);
    log.info('window closed', { name: win.name });

    if (activeWindowId.value === id) {
      const newActiveIndex = Math.min(index, windows.value.length - 1);
      const newActiveWin = windows.value[newActiveIndex];
      if (newActiveWin) {
        activeWindowId.value = newActiveWin.id;
        options.branches.value.forEach(b => b.isCurrent = b.id === newActiveWin.branchId);
        await options.store.loadInitialProject({ source: ChangeSource.GitCheckout, preserveView: true });
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
      draftMessageId: createDraftMessageId(),
      isStreaming: false,
      todoProgress: null,
      pendingAttachments: [],
      pendingSpatialMarks: [],
      spatialMarkDraft: null,
      queuedMessage: null,
      scrollPosition: 0,
      expandedThinking: {},
      shouldAutoScroll: true
    };
    windows.value.push(newWindow);
    switchWindow(newId);
    showNewWindowDropdown.value = false;
    log.debug('creating window', { name: newWindow.name, branch: branch.name });

    try {
      await GitWorktreeService.createWorktree({
        name: worktreeName,
        branch: branch.name,
        intent: 'parallel'
      });

      const idx = windows.value.findIndex(w => w.id === newId);
      const createdWindow = idx !== -1 ? windows.value[idx] : undefined;
      if (createdWindow) {
        createdWindow.isLoading = false;
        log.info('window created', { name: newWindow.name, branch: branch.name });
      }

      const worktreeInfo = await GitWorktreeService.getWorktrees();
      const createdWorktree = worktreeInfo.find(w => w.name === worktreeName);
      if (createdWorktree) {
        await fetch(`${SERVER_API}/windows/register-worktree`, {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({
            windowId: newId,
            worktreePath: createdWorktree.path
          })
        });
        log.debug('worktree mapping registered', { windowId: newId, path: createdWorktree.path });

        const pathIdx = windows.value.findIndex(w => w.id === newId);
        const pathWindow = pathIdx !== -1 ? windows.value[pathIdx] : undefined;
        if (pathWindow) {
          pathWindow.worktreePath = createdWorktree.path;
        }

        await options.store.loadInitialProject({ source: ChangeSource.GitCheckout, preserveView: true });
        log.debug('project data reloaded');
      }

      SignalRService.getInstance().registerWindow(newId, branch.name);

      if (streamWelcomeMessage) {
        await streamWelcomeMessage();
      }
    } catch (error: any) {
      const idx = windows.value.findIndex(w => w.id === newId);
      const errorWindow = idx !== -1 ? windows.value[idx] : undefined;
      if (errorWindow) {
        errorWindow.isLoading = false;
        errorWindow.error = error.message || '创建失败';
        log.error('window create failed', { err: error.message });
      }
      setTimeout(() => {
        const idx = windows.value.findIndex(w => w.id === newId);
        const pendingWindow = idx !== -1 ? windows.value[idx] : undefined;
        if (pendingWindow?.error) {
          windows.value.splice(idx, 1);
          const primary = windows.value.find(w => w.isPrimary);
          if (primary) switchWindow(primary.id);
        }
      }, 3000);
    }
  };

  watch(
    () => ({
      activeWindowId: activeWindowId.value,
      windows: windows.value.map(buildPersistedWindow)
    }),
    () => {
      persistWindowSession();
    },
    { deep: true }
  );

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
    restoreWindowSession,
    syncWindowActivation,
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
