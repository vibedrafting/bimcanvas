import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

const SERVER_API_BASE = 'http://localhost:5000';

/**
 * 覆盖合并结果
 */
export interface OverwriteMergeResult {
  success: boolean;
  message?: string;
}

/**
 * 合并向导 Store (MVP v0.1)
 *
 * 简化版：只支持全量覆盖合并
 */
export const useMergeStore = defineStore('merge', () => {
  // === State ===

  /** 向导是否可见 */
  const isVisible = ref(false);

  /** 当前步骤：1=选择分支, 2=确认合并 */
  const currentStep = ref(1);

  /** 目标分支 */
  const targetBranch = ref('');

  /** 源分支 */
  const sourceBranch = ref('');

  /** 是否正在合并 */
  const isMerging = ref(false);

  /** 错误信息 */
  const error = ref<string | null>(null);

  // === Getters ===

  /** 是否可以进行下一步 */
  const canProceed = computed(() => {
    if (currentStep.value === 1) {
      return targetBranch.value && sourceBranch.value && targetBranch.value !== sourceBranch.value;
    }
    return true;
  });

  // === Actions ===

  /**
   * 打开合并向导
   */
  const openWizard = (): void => {
    console.log('[MergeStore] openWizard() called, current isVisible:', isVisible.value);
    console.trace('[MergeStore] openWizard call stack');
    isVisible.value = true;
    currentStep.value = 1;
    targetBranch.value = '';
    sourceBranch.value = '';
    error.value = null;
    console.log('[MergeStore] openWizard() done, isVisible now:', isVisible.value);
  };

  /**
   * 关闭合并向导
   */
  const closeWizard = (): void => {
    console.log('[MergeStore] closeWizard() called, current isVisible:', isVisible.value);
    console.trace('[MergeStore] closeWizard call stack');
    isVisible.value = false;
    currentStep.value = 1;
    targetBranch.value = '';
    sourceBranch.value = '';
    error.value = null;
    console.log('[MergeStore] closeWizard() done, isVisible now:', isVisible.value);
  };

  /**
   * 下一步
   */
  const nextStep = (): void => {
    if (canProceed.value && currentStep.value < 2) {
      currentStep.value++;
    }
  };

  /**
   * 上一步
   */
  const prevStep = (): void => {
    if (currentStep.value > 1) {
      currentStep.value--;
    }
  };

  /**
   * 执行覆盖合并
   */
  const executeOverwriteMerge = async (): Promise<OverwriteMergeResult> => {
    if (!sourceBranch.value || !targetBranch.value) {
      error.value = '请选择源分支和目标分支';
      return { success: false, message: error.value };
    }

    if (sourceBranch.value === targetBranch.value) {
      error.value = '源分支和目标分支不能相同';
      return { success: false, message: error.value };
    }

    try {
      isMerging.value = true;
      error.value = null;

      const response = await fetch(`${SERVER_API_BASE}/api/merge/overwrite`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sourceBranch: sourceBranch.value,
          targetBranch: targetBranch.value
        })
      });

      const result = await response.json();

      if (response.ok && result.success) {
        console.log('[MergeStore] 覆盖合并成功');
        closeWizard();
        return { success: true };
      } else {
        error.value = result.message || '合并失败';
        return { success: false, message: error.value };
      }
    } catch (e) {
      console.error('[MergeStore] 执行合并失败:', e);
      error.value = '网络错误';
      return { success: false, message: error.value };
    } finally {
      isMerging.value = false;
    }
  };

  /**
   * 清除错误
   */
  const clearError = (): void => {
    error.value = null;
  };

  return {
    // State
    isVisible,
    currentStep,
    targetBranch,
    sourceBranch,
    isMerging,
    error,
    // Getters
    canProceed,
    // Actions
    openWizard,
    closeWizard,
    nextStep,
    prevStep,
    executeOverwriteMerge,
    clearError
  };
});
