import { ref, computed, onMounted, onUnmounted } from 'vue';
import { GitService } from '../services/GitService';
import { useCanvasStore } from '../stores/canvasStore';

/**
 * 保存状态（单例模式，跨组件共享）
 */
const hasUncommittedChanges = ref(false);
const isSaving = ref(false);
const currentBranch = ref<string | null>(null);
const lastSaveTime = ref<Date | null>(null);

// 定时刷新状态的间隔（毫秒）
const STATUS_POLL_INTERVAL = 5000;
let statusPollTimer: ReturnType<typeof setInterval> | null = null;
let isPollingActive = false;

/**
 * 刷新 Git 状态
 */
async function refreshStatus() {
    try {
        const status = await GitService.getStatus();
        hasUncommittedChanges.value = status.hasUncommittedChanges;
        currentBranch.value = status.currentBranch || null;
    } catch (error) {
        console.error('Failed to refresh git status:', error);
    }
}

/**
 * 启动状态轮询
 */
function startPolling() {
    if (isPollingActive) return;
    isPollingActive = true;

    // 立即刷新一次
    refreshStatus();

    // 定时刷新
    statusPollTimer = setInterval(refreshStatus, STATUS_POLL_INTERVAL);
}

/**
 * 停止状态轮询
 */
function stopPolling() {
    if (statusPollTimer) {
        clearInterval(statusPollTimer);
        statusPollTimer = null;
    }
    isPollingActive = false;
}

/**
 * 保存功能的 Composable
 * 
 * 核心概念：在 BIMCanvas v3 架构中，"保存" = "Git Commit"（存档）
 * 
 * 保存类型：
 * 1. 自动存档：在创建 Worktree 前静默执行（Server 端处理）
 * 2. 手动保存：用户点击 Save 按钮时触发
 * 
 * 保存按钮可用条件：
 * - 有已加载的项目
 * - 有未提交的更改
 */
export function useSave() {
    const store = useCanvasStore();

    /**
     * 保存按钮是否可用
     */
    const canSave = computed(() => {
        return store.projectData !== null && hasUncommittedChanges.value;
    });

    /**
     * 执行保存操作
     * @param message 可选的提交消息
     */
    async function handleSave(message?: string): Promise<boolean> {
        if (isSaving.value) {
            console.log('[useSave] Already saving, skip');
            return false;
        }

        if (!canSave.value) {
            console.log('[useSave] Nothing to save');
            return false;
        }

        isSaving.value = true;

        try {
            const result = await GitService.commit({
                message: message || undefined
            });

            if (result.success) {
                if (result.committed) {
                    console.log('[useSave] Saved successfully:', result.commit?.hash);
                    lastSaveTime.value = new Date();
                } else {
                    console.log('[useSave] No changes to save');
                }

                // 刷新状态
                await refreshStatus();
                return true;
            } else {
                console.error('[useSave] Save failed:', result.message);
                return false;
            }
        } catch (error) {
            console.error('[useSave] Save error:', error);
            return false;
        } finally {
            isSaving.value = false;
        }
    }

    /**
     * 注册键盘快捷键
     */
    function registerKeyboardShortcut() {
        const handler = (e: KeyboardEvent) => {
            if ((e.ctrlKey || e.metaKey) && e.key === 's') {
                e.preventDefault();
                handleSave();
            }
        };
        window.addEventListener('keydown', handler);
        return () => window.removeEventListener('keydown', handler);
    }

    /**
     * 组件挂载时启动轮询，卸载时停止
     */
    onMounted(() => {
        startPolling();
    });

    onUnmounted(() => {
        // 注意：因为是单例状态，只有当所有使用此 composable 的组件都卸载时才停止轮询
        // 这里暂时不停止，保持状态更新
        // stopPolling();
    });

    return {
        // 状态
        hasUncommittedChanges,
        isSaving,
        currentBranch,
        lastSaveTime,
        canSave,

        // 方法
        handleSave,
        refreshStatus,
        registerKeyboardShortcut,
        startPolling,
        stopPolling
    };
}
