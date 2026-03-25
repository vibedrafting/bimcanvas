import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import { SERVER_API } from '../config/api';

/**
 * 虚拟窗口状态
 */
export interface VirtualWindow {
  /** 窗口唯一 ID */
  id: string;
  /** 显示标题 */
  title: string;
  /** 关联的分支名（每个窗口绑定一个分支） */
  branchName: string;
  /** 创建时间 */
  createdAt: number;
  /** 是否有未保存的更改 */
  isDirty: boolean;
}

/**
 * 分支锁信息（从 Server 获取）
 */
export interface BranchLock {
  branch: string;
  windowId: string;
}

/**
 * 窗口管理 Store
 *
 * 核心规则：一个分支只能被一个窗口打开
 * 窗口 = 分支的隔离视图
 */
export const useWindowStore = defineStore('window', () => {
  // === State ===

  /** 所有虚拟窗口 */
  const windows = ref<VirtualWindow[]>([]);

  /** 当前激活的窗口 ID */
  const activeWindowId = ref<string | null>(null);

  /** 分支锁状态（来自 Server） */
  const branchLocks = ref<BranchLock[]>([]);

  /** 是否正在加载 */
  const isLoading = ref(false);

  /** 错误信息 */
  const error = ref<string | null>(null);

  // === Getters ===

  /** 当前激活的窗口 */
  const activeWindow = computed(() =>
    windows.value.find(w => w.id === activeWindowId.value) ?? null
  );

  /** 当前窗口的分支名 */
  const currentBranch = computed(() =>
    activeWindow.value?.branchName ?? null
  );

  /** 已被占用的分支列表 */
  const occupiedBranches = computed(() =>
    windows.value.map(w => w.branchName)
  );

  /** 检查分支是否可用（未被其他窗口占用） */
  const isBranchAvailable = computed(() => (branchName: string) => {
    // 检查本地窗口
    const localOccupied = windows.value.some(
      w => w.branchName === branchName && w.id !== activeWindowId.value
    );
    if (localOccupied) return false;

    // 检查 Server 端锁（其他实例）
    const serverLocked = branchLocks.value.some(
      lock => lock.branch === branchName && lock.windowId !== activeWindowId.value
    );
    return !serverLocked;
  });

  /** 窗口数量 */
  const windowCount = computed(() => windows.value.length);

  // === Actions ===

  /**
   * 生成窗口 ID
   */
  const generateWindowId = (): string => {
    return `win_${Date.now()}_${Math.random().toString(36).substring(2, 7)}`;
  };

  /**
   * 创建虚拟窗口
   * @param branchName 关联的分支名
   * @param title 窗口标题（可选，默认使用分支名）
   * @returns 新窗口或 null（如果分支已被占用）
   */
  const createVirtualWindow = async (
    branchName: string,
    title?: string
  ): Promise<VirtualWindow | null> => {
    // 检查分支是否可用
    if (!isBranchAvailable.value(branchName)) {
      error.value = `分支 "${branchName}" 已被其他窗口占用`;
      console.warn('[WindowStore] 分支已被占用:', branchName);
      return null;
    }

    const windowId = generateWindowId();

    try {
      isLoading.value = true;
      error.value = null;

      // 向 Server 申请分支锁
      const lockResult = await acquireBranchLock(branchName, windowId);
      if (!lockResult) {
        error.value = `无法获取分支 "${branchName}" 的锁`;
        return null;
      }

      const newWindow: VirtualWindow = {
        id: windowId,
        title: title ?? branchName,
        branchName,
        createdAt: Date.now(),
        isDirty: false
      };

      windows.value.push(newWindow);

      // 如果是第一个窗口，自动激活
      if (windows.value.length === 1) {
        activeWindowId.value = windowId;
      }

      console.log('[WindowStore] 创建窗口:', newWindow);
      return newWindow;
    } catch (e) {
      console.error('[WindowStore] 创建窗口失败:', e);
      error.value = '创建窗口失败';
      return null;
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 关闭虚拟窗口
   * @param windowId 窗口 ID
   * @param force 是否强制关闭（忽略脏数据）
   * @returns 是否成功关闭
   */
  const closeVirtualWindow = async (
    windowId: string,
    force = false
  ): Promise<boolean> => {
    const window = windows.value.find(w => w.id === windowId);
    if (!window) {
      console.warn('[WindowStore] 窗口不存在:', windowId);
      return false;
    }

    // 检查脏数据
    if (!force && window.isDirty) {
      error.value = '窗口有未保存的更改';
      return false;
    }

    try {
      isLoading.value = true;
      error.value = null;

      // 释放分支锁
      await releaseBranchLock(window.branchName, windowId);

      // 移除窗口
      windows.value = windows.value.filter(w => w.id !== windowId);

      // 如果关闭的是当前窗口，切换到其他窗口
      if (activeWindowId.value === windowId) {
        const lastWindow = windows.value[windows.value.length - 1];
        activeWindowId.value = windows.value.length > 0
          ? (lastWindow?.id ?? null)
          : null;
      }

      console.log('[WindowStore] 关闭窗口:', windowId);
      return true;
    } catch (e) {
      console.error('[WindowStore] 关闭窗口失败:', e);
      error.value = '关闭窗口失败';
      return false;
    } finally {
      isLoading.value = false;
    }
  };

  /**
   * 激活窗口
   */
  const activateWindow = (windowId: string): boolean => {
    const window = windows.value.find(w => w.id === windowId);
    if (!window) {
      console.warn('[WindowStore] 窗口不存在:', windowId);
      return false;
    }

    activeWindowId.value = windowId;
    console.log('[WindowStore] 激活窗口:', windowId);
    return true;
  };

  /**
   * 标记窗口为脏（有未保存更改）
   */
  const markWindowDirty = (windowId: string, isDirty = true): void => {
    const window = windows.value.find(w => w.id === windowId);
    if (window) {
      window.isDirty = isDirty;
    }
  };

  /**
   * 向 Server 申请分支锁
   */
  const acquireBranchLock = async (
    branchName: string,
    windowId: string
  ): Promise<boolean> => {
    try {
      const response = await fetch(`${SERVER_API}/windows/lock`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ branchName, windowId })
      });

      if (response.ok) {
        const result = await response.json();
        return result.success ?? true;
      }

      // 404 或其他错误时，本地模式下允许继续
      console.warn('[WindowStore] Server 分支锁 API 不可用，使用本地模式');
      return true;
    } catch (e) {
      // 网络错误时，本地模式下允许继续
      console.warn('[WindowStore] 无法连接 Server，使用本地模式');
      return true;
    }
  };

  /**
   * 释放分支锁
   */
  const releaseBranchLock = async (
    branchName: string,
    windowId: string
  ): Promise<boolean> => {
    try {
      const response = await fetch(`${SERVER_API}/windows/lock`, {
        method: 'DELETE',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ branchName, windowId })
      });

      return response.ok;
    } catch (e) {
      console.warn('[WindowStore] 释放锁失败:', e);
      return false;
    }
  };

  /**
   * 从 Server 刷新分支锁状态
   */
  const refreshBranchLocks = async (): Promise<void> => {
    try {
      const response = await fetch(`${SERVER_API}/windows/locks`);
      if (response.ok) {
        branchLocks.value = await response.json();
      }
    } catch (e) {
      console.warn('[WindowStore] 刷新分支锁失败:', e);
    }
  };

  /**
   * 清除错误
   */
  const clearError = (): void => {
    error.value = null;
  };

  /**
   * 初始化默认窗口（如果没有窗口）
   */
  const initDefaultWindow = async (branchName: string): Promise<void> => {
    if (windows.value.length === 0) {
      await createVirtualWindow(branchName, 'Main');
    }
  };

  return {
    // State
    windows,
    activeWindowId,
    branchLocks,
    isLoading,
    error,
    // Getters
    activeWindow,
    currentBranch,
    occupiedBranches,
    isBranchAvailable,
    windowCount,
    // Actions
    createVirtualWindow,
    closeVirtualWindow,
    activateWindow,
    markWindowDirty,
    refreshBranchLocks,
    clearError,
    initDefaultWindow
  };
});
