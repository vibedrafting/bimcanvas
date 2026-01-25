import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

const SERVER_API_BASE = 'http://localhost:5000';

/**
 * 覆盖合并结果
 */
export interface OverwriteMergeResult {
  success: boolean;
  message?: string;
  mergedZoneCount?: number;
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

  /** Worktree 名称数组（用于多选一模式） */
  const worktreeNames = ref<string[]>([]);

  /** 选中的 worktree 名称 */
  const selectedWorktree = ref<string>('');

  /** 合并后是否清理 worktree */
  const cleanupAfterMerge = ref<boolean>(false);

  /** Worktree 到分支的映射 */
  const worktreeBranchMapping = ref<Record<string, string>>({});

  // === Getters ===

  /** 是否为 Worktree 模式 */
  const isWorktreeMode = computed(() => worktreeNames.value.length > 0);

  /** Worktree 选项列表（用于单选列表） */
  const worktreeOptions = computed(() =>
    worktreeNames.value.map(name => ({
      value: name,
      label: name,
      branchName: worktreeBranchMapping.value[name] || '(未解析)'
    }))
  );

  /** 是否可以进行下一步 */
  const canProceed = computed(() => {
    if (currentStep.value === 1) {
      // Worktree 模式：必须选择一个 worktree
      if (isWorktreeMode.value) {
        return selectedWorktree.value !== '' && targetBranch.value !== '';
      }
      // 传统模式
      return targetBranch.value && sourceBranch.value && targetBranch.value !== sourceBranch.value;
    }
    return true;
  });

  // === Actions ===

  /**
   * 打开合并向导（传统模式）
   */
  const openWizard = (): void => {
    console.log('[MergeStore] openWizard() called, current isVisible:', isVisible.value);
    console.trace('[MergeStore] openWizard call stack');
    isVisible.value = true;
    currentStep.value = 1;
    targetBranch.value = '';
    sourceBranch.value = '';
    error.value = null;
    // 清空 worktree 相关字段
    worktreeNames.value = [];
    selectedWorktree.value = '';
    cleanupAfterMerge.value = false;
    worktreeBranchMapping.value = {};
    console.log('[MergeStore] openWizard() done, isVisible now:', isVisible.value);
  };

  /**
   * 打开合并向导（Worktree 模式）
   */
  const openWizardWithWorktrees = async (names: string[]): Promise<void> => {
    console.log('[MergeStore] openWizardWithWorktrees() called with:', names);
    isVisible.value = true;
    currentStep.value = 1;
    targetBranch.value = '';
    sourceBranch.value = '';
    error.value = null;
    worktreeNames.value = names;
    selectedWorktree.value = '';
    cleanupAfterMerge.value = false;
    worktreeBranchMapping.value = {};

    // 批量解析 worktree 到 branch 映射
    try {
      const response = await fetch(`${SERVER_API_BASE}/api/worktree/batch-resolve`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ names })
      });

      const result = await response.json();
      console.log('[MergeStore] batch-resolve 响应:', result);

      if (result.success) {
        worktreeBranchMapping.value = result.mapping;
      } else {
        error.value = `部分任务未找到: ${result.errors?.join(', ')}`;
      }
    } catch (e) {
      console.error('[MergeStore] 解析 worktree 映射失败:', e);
      error.value = '无法解析 worktree 元数据';
    }
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
    // 清空 worktree 相关字段
    worktreeNames.value = [];
    selectedWorktree.value = '';
    cleanupAfterMerge.value = false;
    worktreeBranchMapping.value = {};
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
          targetBranch: targetBranch.value,
          cleanupWorktree: cleanupAfterMerge.value,
          worktreeName: selectedWorktree.value
        })
      });

      const result = await response.json();
      console.log('[MergeStore] 合并响应:', result);

      if (response.ok && result.success) {
        // 检查是否实际执行了合并（mergedZoneCount > 0）
        if (result.mergedZoneCount === 0) {
          // 无差异，显示提示但不关闭向导
          console.log('[MergeStore] 两个分支内容相同，无需合并');
          const msg = result.message || '两个分支内容相同，无需合并';
          error.value = msg;
          return { success: true, message: msg, mergedZoneCount: 0 };
        }
        console.log('[MergeStore] 覆盖合并成功');
        closeWizard();
        return { success: true, mergedZoneCount: result.mergedZoneCount };
      } else {
        const errMsg = result.message || '合并失败';
        error.value = errMsg;
        return { success: false, message: errMsg };
      }
    } catch (e) {
      console.error('[MergeStore] 执行合并失败:', e);
      error.value = '网络错误';
      return { success: false, message: '网络错误' };
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
    worktreeNames,
    selectedWorktree,
    cleanupAfterMerge,
    worktreeBranchMapping,
    // Getters
    canProceed,
    isWorktreeMode,
    worktreeOptions,
    // Actions
    openWizard,
    openWizardWithWorktrees,
    closeWizard,
    nextStep,
    prevStep,
    executeOverwriteMerge,
    clearError
  };
});
