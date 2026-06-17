import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { ChangeSource } from '../types/history';
import { useCanvasStore } from './canvasStore';
import { SERVER_API } from '../config/api';
import { createLogger } from '../utils/logger';

const log = createLogger('SYS');

// Git分支信息接口
export interface GitBranch {
  id: string;
  name: string;
  isCurrent: boolean;
  commit: {
    message: string;
    time: string;
    hash: string;
    author: string;
  };
  /** 是否被锁定（多窗口场景） */
  isLocked?: boolean;
  /** 锁定者窗口 ID */
  lockedBy?: string;
}

// 切换分支结果
interface CheckoutResult {
  success: boolean;
  message?: string;
  hasUncommittedChanges?: boolean;
}

// Git 状态
interface GitStatus {
  isLoaded: boolean;
  isGitRepo: boolean;
  hasUncommittedChanges: boolean;
  currentBranch?: string;
}

// 切换选项
interface CheckoutOptions {
  createIfNotExist?: boolean;
  commitBeforeCheckout?: boolean;
  commitMessage?: string;
  discardBeforeCheckout?: boolean;  // 切换前放弃更改（Server端原子操作）
  baseBranch?: string;  // 创建新分支时的基准分支（仅 createIfNotExist=true 时使用）
  switchAfterCreate?: boolean;  // 创建新分支后是否切换（默认 true）
}

// 分支锁信息
interface BranchLock {
  branch: string;
  windowId: string;
}

export const useGitStore = defineStore('git', () => {
  // === State ===
  const branches = ref<GitBranch[]>([]);
  const isLoading = ref(false);
  const error = ref<string | null>(null);
  const hasUncommittedChanges = ref(false);
  /** 当前窗口 ID（用于检查锁） */
  const currentWindowId = ref<string | null>(null);

  // === Getters ===
  const currentBranch = computed(() => {
    if (error.value === '项目未加载') return '(未加载项目)';
    if (error.value === 'offline') return '(离线)';
    return branches.value.find(b => b.isCurrent)?.name ?? '(no branch)';
  });

  const currentBranchId = computed(() =>
    branches.value.find(b => b.isCurrent)?.id ?? ''
  );

  const isOffline = computed(() => error.value === 'offline');

  // === Actions ===

  /**
   * 从Server获取分支列表
   */
  const fetchBranches = async (): Promise<void> => {
    try {
      isLoading.value = true;
      error.value = null;

      // 先检查项目状态
      const statusResponse = await fetch(`${SERVER_API}/project/status`);
      if (statusResponse.ok) {
        const status = await statusResponse.json();
        if (!status.isLoaded) {
          log.warn('project not loaded, cannot fetch branches');
          error.value = '项目未加载';
          branches.value = [];
          return;
        }
      }

      const response = await fetch(`${SERVER_API}/git/branches`);
      if (response.ok) {
        const branchList = await response.json();

        // 获取分支锁状态
        const locks = await fetchBranchLocks();

        // 合并锁状态到分支列表
        branches.value = branchList.map((branch: GitBranch) => {
          const lock = locks.find(l => l.branch === branch.name);
          return {
            ...branch,
            isLocked: !!lock && lock.windowId !== currentWindowId.value,
            lockedBy: lock?.windowId
          };
        });

        if (branches.value.length === 0) {
          log.debug('branch list empty (maybe not a git repo)');
        }
      } else {
        throw new Error('Server API not available');
      }
    } catch (e) {
      log.warn('fetch branches failed', { err: e });
      error.value = 'offline';
      branches.value = [];
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 检查 Git 工作区状态
   */
  const checkStatus = async (): Promise<GitStatus | null> => {
    try {
      const response = await fetch(`${SERVER_API}/git/status`);
      if (response.ok) {
        const status = await response.json();
        hasUncommittedChanges.value = status.hasUncommittedChanges;
        return status;
      }
      return null;
    } catch (e) {
      log.warn('check status failed', { err: e });
      return null;
    }
  };

  /**
   * 提交当前更改（存档）
   */
  const commit = async (message?: string): Promise<{ success: boolean; message?: string }> => {
    try {
      isLoading.value = true;
      const response = await fetch(`${SERVER_API}/git/commit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message })
      });

      if (response.ok) {
        const result = await response.json();
        hasUncommittedChanges.value = false;
        log.info('commit succeeded', { result });
        return { success: true };
      } else {
        const err = await response.json();
        return { success: false, message: err.message };
      }
    } catch (e) {
      log.error('commit failed', { err: e });
      return { success: false, message: '请求失败' };
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 放弃所有更改
   */
  const discardChanges = async (): Promise<{ success: boolean; message?: string }> => {
    try {
      isLoading.value = true;
      const response = await fetch(`${SERVER_API}/git/discard`, {
        method: 'POST'
      });

      if (response.ok) {
        hasUncommittedChanges.value = false;
        // 重新加载项目以反映更改被丢弃后的状态
        const canvasStore = useCanvasStore();
        await canvasStore.loadInitialProject(ChangeSource.GitDiscard);
        log.info('all changes discarded');
        return { success: true };
      } else {
        const err = await response.json();
        return { success: false, message: err.message };
      }
    } catch (e) {
      log.error('discard changes failed', { err: e });
      return { success: false, message: '请求失败' };
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 切换到指定分支
   * @param branchName 分支名称
   * @param options 切换选项
   */
  const checkout = async (branchName: string, options: CheckoutOptions | boolean = {}): Promise<CheckoutResult> => {
    // 兼容旧的 createIfNotExist 参数
    const opts: CheckoutOptions = typeof options === 'boolean'
      ? { createIfNotExist: options }
      : options;

    const canvasStore = useCanvasStore();

    // 检查内存中是否有未保存的修改（脏数据检测）
    // 跳过检查的情况：
    // - commitBeforeCheckout 模式：用户已确认要保存
    // - discardBeforeCheckout 模式：Server端会放弃更改后再切换
    if (!opts.commitBeforeCheckout && !opts.discardBeforeCheckout && canvasStore.isDirty) {
      log.warn('unsaved in-memory changes detected');
      hasUncommittedChanges.value = true;
      return {
        success: false,
        message: '存在未保存的更改',
        hasUncommittedChanges: true
      };
    }

    try {
      isLoading.value = true;
      error.value = null;

      const requestBody = {
        branchName,
        createIfNotExist: opts.createIfNotExist ?? false,
        commitBeforeCheckout: opts.commitBeforeCheckout ?? false,
        discardBeforeCheckout: opts.discardBeforeCheckout ?? false,
        commitMessage: opts.commitMessage,
        baseBranch: opts.baseBranch,
        switchAfterCreate: opts.switchAfterCreate ?? true
      };
      log.debug('checkout request', { requestBody });

      const response = await fetch(`${SERVER_API}/git/checkout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(requestBody)
      });

      if (response.ok) {
        const result = await response.json();
        hasUncommittedChanges.value = false;
        await fetchBranches();

        // 只创建不切换时，跳过项目重载和窗口激活
        if (result.switched === false) {
          log.info('branch created but not switched', { branchName });
          return { success: true };
        }

        // 切换成功后的正常流程
        // 通知 Server 激活主窗口
        try {
          await fetch(`${SERVER_API}/windows/activate`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
              windowId: 'window-main'
            })
          });
        } catch (e) {
          log.warn('activate main window failed (non-fatal)', { err: e });
        }

        // 重新加载项目数据，确保 Canvas 显示新分支的数据
        await canvasStore.loadInitialProject({ source: ChangeSource.GitCheckout, preserveView: true });

        log.info('branch switched', { branchName });
        return { success: true };
      } else {
        const err = await response.json();
        const errorMsg = err.message || '切换分支失败';

        // 409 冲突：有未提交的更改
        if (response.status === 409 && err.hasUncommittedChanges) {
          hasUncommittedChanges.value = true;
          log.warn('checkout conflict: uncommitted changes');
          return {
            success: false,
            message: errorMsg,
            hasUncommittedChanges: true
          };
        }

        error.value = errorMsg;
        log.error('branch switch failed', {
          status: response.status,
          message: errorMsg,
          branchName
        });
        return { success: false, message: errorMsg };
      }
    } catch (e) {
      log.error('checkout request failed', { err: e });
      error.value = '请求失败';
      return { success: false, message: '请求失败' };
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 清除错误状态
   */
  const clearError = () => {
    error.value = null;
  };

  /**
   * 获取分支锁列表（内部使用）
   */
  const fetchBranchLocks = async (): Promise<BranchLock[]> => {
    try {
      const response = await fetch(`${SERVER_API}/windows/locks`);
      if (response.ok) {
        return await response.json();
      }
    } catch (e) {
      log.warn('fetch branch locks failed', { err: e });
    }
    return [];
  };

  /**
   * 设置当前窗口 ID（用于分支锁检查）
   */
  const setCurrentWindowId = (windowId: string | null) => {
    currentWindowId.value = windowId;
  };

  /**
   * 检查分支是否可切换（未被锁定或是自己锁定的）
   */
  const canSwitchToBranch = (branchName: string): boolean => {
    const branch = branches.value.find(b => b.name === branchName);
    if (!branch) return true; // 分支不在列表中，允许切换
    return !branch.isLocked; // 未锁定或自己锁定的
  };

  return {
    // State
    branches,
    isLoading,
    error,
    hasUncommittedChanges,
    currentWindowId,
    // Getters
    currentBranch,
    currentBranchId,
    isOffline,
    // Actions
    fetchBranches,
    checkStatus,
    commit,
    discardChanges,
    checkout,
    clearError,
    setCurrentWindowId,
    canSwitchToBranch
  };
});
