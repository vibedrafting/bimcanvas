import { defineStore } from 'pinia';
import { ref, computed } from 'vue';

const SERVER_API_BASE = 'http://localhost:5000';

/**
 * 模块变化
 */
export interface ModuleChange {
  oldModule?: any;
  newModule?: any;
}

/**
 * 分区差异
 */
export interface ZoneDiff {
  /** 分区 ID */
  zoneId: string;
  /** 源分支 */
  sourceBranch: string;
  /** 目标分支 */
  targetBranch: string;
  /** 新增的模块 */
  addedModules: any[];
  /** 删除的模块 */
  removedModules: any[];
  /** 修改的模块 */
  modifiedModules: ModuleChange[];
  /** 是否有变化 */
  hasChanges: boolean;
  /** 变化统计 */
  summary: string;
}

/**
 * 可合并分支信息
 */
export interface MergeBranchInfo {
  /** 分支名 */
  name: string;
  /** 最后提交信息 */
  lastCommit: string;
  /** 最后提交时间 */
  lastCommitTime: string;
  /** 是否是 AI 生成的分支 */
  isAiBranch: boolean;
}

/**
 * 选择性合并结果
 */
export interface SelectiveMergeResult {
  success: boolean;
  mergedZones: string[];
  failedZones: string[];
  errorMessage?: string;
}

/**
 * 合并状态 Store
 *
 * 用于管理分区级差异对比和选择性合并
 */
export const useMergeStore = defineStore('merge', () => {
  // === State ===

  /** 可合并的分支列表 */
  const mergeableBranches = ref<MergeBranchInfo[]>([]);

  /** 当前选择的源分支 */
  const selectedSourceBranch = ref<string | null>(null);

  /** 分区差异列表 */
  const zoneDiffs = ref<ZoneDiff[]>([]);

  /** 选中的分区（用于选择性合并） */
  const selectedZones = ref<Set<string>>(new Set());

  /** 是否正在加载 */
  const isLoading = ref(false);

  /** 是否正在合并 */
  const isMerging = ref(false);

  /** 错误信息 */
  const error = ref<string | null>(null);

  /** 是否显示合并面板 */
  const showMergePanel = ref(false);

  // === Getters ===

  /** 是否有差异 */
  const hasDiffs = computed(() => zoneDiffs.value.length > 0);

  /** 选中的分区数量 */
  const selectedCount = computed(() => selectedZones.value.size);

  /** 全部选中 */
  const isAllSelected = computed(() =>
    zoneDiffs.value.length > 0 &&
    selectedZones.value.size === zoneDiffs.value.length
  );

  /** AI 生成的分支 */
  const aiBranches = computed(() =>
    mergeableBranches.value.filter(b => b.isAiBranch)
  );

  // === Actions ===

  /**
   * 获取可合并的分支列表
   */
  const fetchMergeableBranches = async (): Promise<void> => {
    try {
      isLoading.value = true;
      error.value = null;

      const response = await fetch(`${SERVER_API_BASE}/api/merge/branches`);
      if (response.ok) {
        mergeableBranches.value = await response.json();
      } else {
        const err = await response.json();
        error.value = err.message || '获取分支列表失败';
      }
    } catch (e) {
      console.error('[MergeStore] 获取分支列表失败:', e);
      error.value = '网络错误';
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 获取分区差异
   */
  const fetchZoneDiffs = async (sourceBranch: string): Promise<void> => {
    try {
      isLoading.value = true;
      error.value = null;
      selectedSourceBranch.value = sourceBranch;
      selectedZones.value.clear();

      const response = await fetch(
        `${SERVER_API_BASE}/api/merge/diff?sourceBranch=${encodeURIComponent(sourceBranch)}`
      );

      if (response.ok) {
        zoneDiffs.value = await response.json();
        console.log(`[MergeStore] 获取到 ${zoneDiffs.value.length} 个分区差异`);
      } else {
        const err = await response.json();
        error.value = err.message || '获取差异失败';
        zoneDiffs.value = [];
      }
    } catch (e) {
      console.error('[MergeStore] 获取差异失败:', e);
      error.value = '网络错误';
      zoneDiffs.value = [];
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 切换分区选中状态
   */
  const toggleZoneSelection = (zoneId: string): void => {
    if (selectedZones.value.has(zoneId)) {
      selectedZones.value.delete(zoneId);
    } else {
      selectedZones.value.add(zoneId);
    }
    // 触发响应式更新
    selectedZones.value = new Set(selectedZones.value);
  };

  /**
   * 全选/取消全选
   */
  const toggleSelectAll = (): void => {
    if (isAllSelected.value) {
      selectedZones.value.clear();
    } else {
      selectedZones.value = new Set(zoneDiffs.value.map(d => d.zoneId));
    }
    selectedZones.value = new Set(selectedZones.value);
  };

  /**
   * 执行选择性合并
   */
  const executeSelectiveMerge = async (): Promise<SelectiveMergeResult | null> => {
    if (!selectedSourceBranch.value || selectedZones.value.size === 0) {
      error.value = '请选择分支和分区';
      return null;
    }

    try {
      isMerging.value = true;
      error.value = null;

      const response = await fetch(`${SERVER_API_BASE}/api/merge/selective`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          sourceBranch: selectedSourceBranch.value,
          selectedZones: Array.from(selectedZones.value)
        })
      });

      const result = await response.json();

      if (response.ok) {
        console.log('[MergeStore] 选择性合并完成:', result);

        // 合并成功后清理状态
        if (result.success) {
          zoneDiffs.value = [];
          selectedZones.value.clear();
          selectedSourceBranch.value = null;
        }

        return result;
      } else {
        error.value = result.message || '合并失败';
        return null;
      }
    } catch (e) {
      console.error('[MergeStore] 执行合并失败:', e);
      error.value = '网络错误';
      return null;
    } finally {
      isMerging.value = false;
    }
  };

  /**
   * 打开合并面板
   */
  const openMergePanel = async (): Promise<void> => {
    showMergePanel.value = true;
    await fetchMergeableBranches();
  };

  /**
   * 关闭合并面板
   */
  const closeMergePanel = (): void => {
    showMergePanel.value = false;
    zoneDiffs.value = [];
    selectedZones.value.clear();
    selectedSourceBranch.value = null;
    error.value = null;
  };

  /**
   * 清除错误
   */
  const clearError = (): void => {
    error.value = null;
  };

  return {
    // State
    mergeableBranches,
    selectedSourceBranch,
    zoneDiffs,
    selectedZones,
    isLoading,
    isMerging,
    error,
    showMergePanel,
    // Getters
    hasDiffs,
    selectedCount,
    isAllSelected,
    aiBranches,
    // Actions
    fetchMergeableBranches,
    fetchZoneDiffs,
    toggleZoneSelection,
    toggleSelectAll,
    executeSelectiveMerge,
    openMergePanel,
    closeMergePanel,
    clearError
  };
});
