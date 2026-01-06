import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { useCanvasStore } from './canvasStore';

const SERVER_API_BASE = 'http://localhost:5000';

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
}

export const useGitStore = defineStore('git', () => {
  // === State ===
  const branches = ref<GitBranch[]>([]);
  const isLoading = ref(false);
  const error = ref<string | null>(null);
  const hasUncommittedChanges = ref(false);

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
      const statusResponse = await fetch(`${SERVER_API_BASE}/api/project/status`);
      if (statusResponse.ok) {
        const status = await statusResponse.json();
        if (!status.isLoaded) {
          console.warn('[GitStore] 项目未加载，无法获取分支列表');
          error.value = '项目未加载';
          branches.value = [];
          return;
        }
      }

      const response = await fetch(`${SERVER_API_BASE}/api/git/branches`);
      if (response.ok) {
        branches.value = await response.json();
        if (branches.value.length === 0) {
          console.log('[GitStore] 分支列表为空（可能不是Git仓库）');
        }
      } else {
        throw new Error('Server API not available');
      }
    } catch (e) {
      console.warn('Failed to fetch branches:', e);
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
      const response = await fetch(`${SERVER_API_BASE}/api/git/status`);
      if (response.ok) {
        const status = await response.json();
        hasUncommittedChanges.value = status.hasUncommittedChanges;
        return status;
      }
      return null;
    } catch (e) {
      console.warn('[GitStore] 检查状态失败:', e);
      return null;
    }
  };

  /**
   * 提交当前更改（存档）
   */
  const commit = async (message?: string): Promise<{ success: boolean; message?: string }> => {
    try {
      isLoading.value = true;
      const response = await fetch(`${SERVER_API_BASE}/api/git/commit`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message })
      });

      if (response.ok) {
        const result = await response.json();
        hasUncommittedChanges.value = false;
        console.log('[GitStore] 提交成功:', result);
        return { success: true };
      } else {
        const err = await response.json();
        return { success: false, message: err.message };
      }
    } catch (e) {
      console.error('[GitStore] 提交失败:', e);
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
      const response = await fetch(`${SERVER_API_BASE}/api/git/discard`, {
        method: 'POST'
      });

      if (response.ok) {
        hasUncommittedChanges.value = false;
        // 重新加载项目以反映更改被丢弃后的状态
        const canvasStore = useCanvasStore();
        await canvasStore.loadProject(true);
        console.log('[GitStore] 已放弃所有更改');
        return { success: true };
      } else {
        const err = await response.json();
        return { success: false, message: err.message };
      }
    } catch (e) {
      console.error('[GitStore] 放弃更改请求失败:', e);
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
      console.warn('[GitStore] 检测到内存中有未保存的修改');
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

      const response = await fetch(`${SERVER_API_BASE}/api/git/checkout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          branchName,
          createIfNotExist: opts.createIfNotExist ?? false,
          commitBeforeCheckout: opts.commitBeforeCheckout ?? false,
          discardBeforeCheckout: opts.discardBeforeCheckout ?? false,
          commitMessage: opts.commitMessage
        })
      });

      if (response.ok) {
        // 切换成功后重新获取分支列表，确保状态与Server同步
        hasUncommittedChanges.value = false;
        await fetchBranches();

        // ✅ 重新加载项目数据，确保 Canvas 显示新分支的数据
        // preserveView=true: 分支切换时保持当前视图位置和缩放
        await canvasStore.loadProject(true);

        console.log('[GitStore] 分支切换成功:', branchName);
        return { success: true };
      } else {
        const err = await response.json();
        const errorMsg = err.message || '切换分支失败';

        // 409 冲突：有未提交的更改
        if (response.status === 409 && err.hasUncommittedChanges) {
          hasUncommittedChanges.value = true;
          console.warn('[GitStore] 分支切换冲突: 有未提交的更改');
          return {
            success: false,
            message: errorMsg,
            hasUncommittedChanges: true
          };
        }

        error.value = errorMsg;
        console.error('[GitStore] 分支切换失败:', {
          status: response.status,
          message: errorMsg,
          branchName
        });
        return { success: false, message: errorMsg };
      }
    } catch (e) {
      console.error('切换分支请求失败:', e);
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

  return {
    // State
    branches,
    isLoading,
    error,
    hasUncommittedChanges,
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
    clearError
  };
});
