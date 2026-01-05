import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

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
}

export const useGitStore = defineStore('git', () => {
  // === State ===
  const branches = ref<GitBranch[]>([]);
  const isLoading = ref(false);
  const error = ref<string | null>(null);

  // === Getters ===
  const currentBranch = computed(() =>
    branches.value.find(b => b.isCurrent)?.name ?? '(no branch)'
  );

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

      const response = await fetch(`${SERVER_API_BASE}/api/git/branches`);
      if (response.ok) {
        branches.value = await response.json();
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
   * 切换到指定分支
   * @param branchName 分支名称
   * @param createIfNotExist 如果分支不存在是否创建
   */
  const checkout = async (branchName: string, createIfNotExist = false): Promise<CheckoutResult> => {
    try {
      isLoading.value = true;
      error.value = null;

      const response = await fetch(`${SERVER_API_BASE}/api/git/checkout`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ branchName, createIfNotExist })
      });

      if (response.ok) {
        // 更新本地状态
        branches.value.forEach(b => b.isCurrent = b.id === branchName);

        // 如果是新创建的分支，重新获取列表以包含新分支
        if (createIfNotExist) {
          await fetchBranches();
        }

        return { success: true };
      } else {
        const err = await response.json();
        error.value = err.message || '切换分支失败';
        return { success: false, message: err.message };
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
    // Getters
    currentBranch,
    currentBranchId,
    isOffline,
    // Actions
    fetchBranches,
    checkout,
    clearError
  };
});
