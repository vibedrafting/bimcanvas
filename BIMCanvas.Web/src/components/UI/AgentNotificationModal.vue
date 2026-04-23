<template>
  <Teleport to="body">
    <!-- Worktree 列表：全屏 Modal，需要用户主动操作 -->
    <Transition name="fade">
      <div v-if="modalVisible" class="agent-notification-overlay">
        <div class="agent-notification-modal" :class="modalNotification?.type">
          <div class="modal-header">
            <span class="icon">{{ modalIcon }}</span>
            <h3>{{ modalNotification?.title }}</h3>
            <button class="close-btn" @click="closeModal">&times;</button>
          </div>
          <div class="modal-body">
            <div class="worktree-message">
              <p>Agent 已完成以下任务:</p>
              <ul>
                <li v-for="name in worktreeNames" :key="name"><code>{{ name }}</code></li>
              </ul>
            </div>
          </div>
          <div class="modal-footer">
            <button class="secondary-btn" @click="closeModal">稍后处理</button>
            <button class="confirm-btn" @click="openMergeWizard">打开合并向导</button>
          </div>
        </div>
      </div>
    </Transition>

    <!-- Toast 堆叠容器：左下角，从下往上堆叠 -->
    <div class="toast-stack">
      <!-- 溢出提示：超出最大显示数时，在顶部显示 "+N 更多" -->
      <Transition name="toast-slide">
        <div v-if="hiddenCount > 0" class="toast-overflow-badge">
          +{{ hiddenCount }} 条更多
        </div>
      </Transition>

      <!-- 可见 Toast 列表 -->
      <TransitionGroup name="toast-slide" tag="div" class="toast-list">
        <div
          v-for="toast in visibleToasts"
          :key="toast.id"
          class="agent-toast"
          :class="toast.type"
        >
          <span class="toast-icon">{{ toastIcon(toast.type) }}</span>
          <div class="toast-content">
            <div class="toast-title">{{ toast.title }}</div>
            <div class="toast-message">{{ toast.message }}</div>
          </div>
          <button class="toast-close" @click="removeToast(toast.id)">&times;</button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useMergeStore } from '../../stores/mergeStore';

interface AgentNotification {
  title: string;
  message: string;
  type: 'info' | 'success' | 'warning' | 'error';
  timestamp: string;
}

interface ToastItem {
  id: number;
  title: string;
  message: string;
  type: 'info' | 'success' | 'warning' | 'error';
}

const MAX_VISIBLE = 3;
let nextId = 0;

// Toast 队列（全部，包含隐藏的）
const toasts = ref<ToastItem[]>([]);

// 全屏 Modal（worktree 列表）
const modalVisible = ref(false);
const modalNotification = ref<AgentNotification | null>(null);
const mergeStore = useMergeStore();

// 最多显示 MAX_VISIBLE 条，从队尾取（最新的）
const visibleToasts = computed(() => toasts.value.slice(-MAX_VISIBLE));
const hiddenCount = computed(() => Math.max(0, toasts.value.length - MAX_VISIBLE));

const modalIcon = computed(() => {
  switch (modalNotification.value?.type) {
    case 'success': return '✅';
    case 'warning': return '⚠️';
    case 'error': return '❌';
    default: return 'ℹ️';
  }
});

function toastIcon(type: string) {
  switch (type) {
    case 'success': return '✅';
    case 'warning': return '⚠️';
    case 'error': return '❌';
    default: return 'ℹ️';
  }
}

const worktreeNames = computed(() => {
  try {
    const parsed = JSON.parse(modalNotification.value?.message || '');
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
});

function handleNotification(event: Event) {
  const customEvent = event as CustomEvent<AgentNotification>;
  const detail = customEvent.detail;

  // 判断是否为 worktree 列表 → 全屏 Modal
  try {
    const parsed = JSON.parse(detail?.message || '');
    if (Array.isArray(parsed) && parsed.length > 0) {
      modalNotification.value = detail;
      modalVisible.value = true;
      return;
    }
  } catch { /* 普通文本 */ }

  // 普通通知 → 加入 Toast 队列
  toasts.value.push({
    id: nextId++,
    title: detail.title,
    message: detail.message,
    type: detail.type ?? 'info',
  });
}

function removeToast(id: number) {
  const idx = toasts.value.findIndex(t => t.id === id);
  if (idx !== -1) toasts.value.splice(idx, 1);
}

function closeModal() {
  modalVisible.value = false;
}

function openMergeWizard() {
  mergeStore.openWizardWithWorktrees(worktreeNames.value);
  closeModal();
}

onMounted(() => {
  window.addEventListener('bimcanvas:agent-notification', handleNotification);
});

onUnmounted(() => {
  window.removeEventListener('bimcanvas:agent-notification', handleNotification);
});
</script>

<style scoped>
/* ========== 全屏 Modal（worktree 列表模式）========== */
.agent-notification-overlay {
  position: fixed;
  top: 0; left: 0; right: 0; bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
  backdrop-filter: blur(4px);
}

.agent-notification-modal {
  background: #1e1e1e;
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  border-radius: 12px;
  min-width: 400px;
  max-width: 600px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
}

.agent-notification-modal.warning { border-color: rgba(234, 179, 8, 0.5); }
.agent-notification-modal.success { border-color: rgba(34, 197, 94, 0.5); }
.agent-notification-modal.error   { border-color: rgba(239, 68, 68, 0.5); }
.agent-notification-modal.info    { border-color: rgba(59, 130, 246, 0.5); }

.modal-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
}
.modal-header .icon { font-size: 24px; }
.modal-header h3 { flex: 1; margin: 0; font-size: 18px; font-weight: 600; color: var(--text-primary, #fff); }

.close-btn {
  background: none; border: none;
  color: var(--text-secondary, #888); font-size: 24px; cursor: pointer;
  padding: 0; width: 32px; height: 32px;
  display: flex; align-items: center; justify-content: center;
  border-radius: 6px; transition: all 0.2s;
}
.close-btn:hover { background: rgba(255, 255, 255, 0.1); color: var(--text-primary, #fff); }

.modal-body { padding: 20px; overflow-y: auto; flex: 1; }
.worktree-message p { margin: 0 0 12px 0; color: var(--text-primary, #fff); }
.worktree-message ul { list-style: disc; padding-left: 24px; margin: 0; }
.worktree-message li { margin: 8px 0; }
.worktree-message code {
  font-family: 'Consolas', monospace;
  background: rgba(255, 255, 255, 0.1);
  padding: 2px 8px; border-radius: 4px; color: #3b82f6;
}

.modal-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  display: flex; gap: 12px; justify-content: flex-end;
}
.secondary-btn {
  background: rgba(255, 255, 255, 0.1); border: 1px solid rgba(255, 255, 255, 0.2);
  color: var(--text-primary, #fff); padding: 10px 24px; border-radius: 8px;
  font-size: 14px; font-weight: 500; cursor: pointer; transition: all 0.2s;
}
.secondary-btn:hover { background: rgba(255, 255, 255, 0.15); }
.confirm-btn {
  background: var(--accent-color, #3b82f6); color: white; border: none;
  padding: 10px 24px; border-radius: 8px; font-size: 14px; font-weight: 500;
  cursor: pointer; transition: all 0.2s;
}
.confirm-btn:hover { background: var(--accent-hover, #2563eb); transform: translateY(-1px); }
.confirm-btn:active { transform: translateY(0); }

/* ========== Toast 堆叠容器 ========== */
.toast-stack {
  position: fixed;
  bottom: 24px;
  left: 84px; /* 图层按钮占 left:24px + width:48px + 间距12px，Toast 紧接其右 */
  width: 320px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  z-index: 10000;
}

/* 溢出提示标签 */
.toast-overflow-badge {
  align-self: flex-start;
  background: var(--surface-highlight, rgba(255, 255, 255, 0.08));
  border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
  border-radius: 20px;
  padding: 3px 10px;
  font-size: 11px;
  color: var(--text-secondary, rgba(255, 255, 255, 0.5));
  cursor: default;
}

/* TransitionGroup 容器 */
.toast-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

/* ========== 单条 Toast ========== */
.agent-toast {
  width: 100%;
  background: var(--glass-bg, rgba(20, 20, 30, 0.65));
  backdrop-filter: var(--glass-blur, blur(24px) saturate(180%));
  -webkit-backdrop-filter: var(--glass-blur, blur(24px) saturate(180%));
  border: var(--glass-border, 1px solid rgba(255, 255, 255, 0.12));
  border-radius: var(--radius-md, 8px);
  box-shadow: var(--shadow-panel, 0 4px 30px rgba(0, 0, 0, 0.2));
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 12px 14px;
  box-sizing: border-box;
}

.agent-toast.warning { border-left: 2px solid var(--accent-yellow, #ffcc00); }
.agent-toast.success { border-left: 2px solid var(--accent-green, #34c759); }
.agent-toast.error   { border-left: 2px solid var(--accent-danger, #ff6b6b); }
.agent-toast.info    { border-left: 2px solid var(--accent-blue, #3b82f6); }

.toast-icon { font-size: 14px; flex-shrink: 0; margin-top: 2px; opacity: 0.85; }

.toast-content { flex: 1; min-width: 0; }

.toast-title {
  font-size: 13px; font-weight: 600;
  color: var(--text-primary, #e0e0e0);
  margin-bottom: 3px; letter-spacing: 0.01em;
}

.toast-message {
  font-size: 12px;
  color: var(--text-secondary, rgba(255, 255, 255, 0.5));
  line-height: 1.5; word-break: break-word;
}

.toast-close {
  background: none; border: none;
  color: var(--text-tertiary, rgba(255, 255, 255, 0.3));
  font-size: 16px; cursor: pointer;
  padding: 0; width: 20px; height: 20px;
  display: flex; align-items: center; justify-content: center;
  border-radius: 4px; flex-shrink: 0;
  transition: all 0.15s; line-height: 1;
}
.toast-close:hover { background: rgba(255, 255, 255, 0.08); color: rgba(255, 255, 255, 0.7); }

/* ========== Transition 动画 ========== */
.fade-enter-active, .fade-leave-active { transition: opacity 0.2s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }
.fade-enter-active .agent-notification-modal,
.fade-leave-active .agent-notification-modal { transition: transform 0.2s ease; }
.fade-enter-from .agent-notification-modal,
.fade-leave-to .agent-notification-modal { transform: scale(0.95); }

.toast-slide-enter-active { transition: transform 0.25s ease, opacity 0.25s ease; }
.toast-slide-leave-active { transition: transform 0.2s ease, opacity 0.2s ease; }
.toast-slide-enter-from   { transform: translateY(12px); opacity: 0; }
.toast-slide-leave-to     { transform: translateY(6px); opacity: 0; }

/* TransitionGroup move 动画 */
.toast-slide-move { transition: transform 0.25s ease; }
</style>
