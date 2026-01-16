import { ref, computed, onMounted, onUnmounted } from 'vue';
import { GitService, type GitStatus } from '../services/GitService';
import { useCanvasStore } from '../stores/canvasStore';

/**
 * 保存状态（单例模式，跨组件共享）
 */
const hasUncommittedChanges = ref(false);
const isSaving = ref(false);
const currentBranch = ref<string | null>(null);
const lastSaveTime = ref<Date | null>(null);

// SignalR 事件监听器是否已注册
let isListenerRegistered = false;

/**
 * 处理 SignalR 推送的 Git 状态变化事件
 */
function handleGitStatusChanged(event: CustomEvent<GitStatus>) {
    const status = event.detail;
    console.log('[useSave] Git status changed (SignalR):', status);

    hasUncommittedChanges.value = status.hasUncommittedChanges;
    currentBranch.value = status.currentBranch || null;
}

/**
 * 注册 SignalR 事件监听器
 */
function registerSignalRListener() {
    if (isListenerRegistered) return;

    window.addEventListener('bimcanvas:git-status-changed', handleGitStatusChanged as EventListener);
    isListenerRegistered = true;
    console.log('[useSave] SignalR listener registered');
}

/**
 * 注销 SignalR 事件监听器
 */
function unregisterSignalRListener() {
    window.removeEventListener('bimcanvas:git-status-changed', handleGitStatusChanged as EventListener);
    isListenerRegistered = false;
    console.log('[useSave] SignalR listener unregistered');
}

/**
 * 手动刷新 Git 状态（备用方法，用于 SignalR 失效时）
 */
async function refreshStatus() {
    try {
        console.log('[useSave] Manually refreshing git status...');
        const status = await GitService.getStatus();
        console.log('[useSave] Git status response:', status);
        hasUncommittedChanges.value = status.hasUncommittedChanges;
        currentBranch.value = status.currentBranch || null;
    } catch (error) {
        console.error('[useSave] Failed to refresh git status:', error);
    }
}

/**
 * 保存功能的 Composable
 * 
 * 核心概念：在 BIMCanvas v3 架构中，"保存" = "Git Commit"（存档）
 * 
 * 状态更新方式：
 * - 主要：通过 SignalR 推送接收 Git 状态变化
 * - 备用：refreshStatus() 方法可手动刷新
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
                    // 保存成功后，状态会通过 SignalR 推送更新
                } else {
                    console.log('[useSave] No changes to save');
                }

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
     * 组件挂载时注册 SignalR 监听器
     */
    onMounted(() => {
        registerSignalRListener();
    });

    onUnmounted(() => {
        // 注意：因为是单例状态，保持监听器注册
        // unregisterSignalRListener();
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
        registerSignalRListener,
        unregisterSignalRListener
    };
}
