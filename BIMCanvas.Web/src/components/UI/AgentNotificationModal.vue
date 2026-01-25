<template>
  <Teleport to="body">
    <Transition name="fade">
      <div v-if="visible" class="agent-notification-overlay">
        <div class="agent-notification-modal" :class="notification?.type">
          <div class="modal-header">
            <span class="icon">{{ icon }}</span>
            <h3>{{ notification?.title }}</h3>
            <button class="close-btn" @click="close">&times;</button>
          </div>
          <div class="modal-body">
            <!-- Worktree 列表模式 -->
            <div v-if="isWorktreeList" class="worktree-message">
              <p>Agent 已完成以下任务:</p>
              <ul>
                <li v-for="name in worktreeNames" :key="name"><code>{{ name }}</code></li>
              </ul>
            </div>
            <!-- 普通消息 -->
            <pre v-else>{{ notification?.message }}</pre>
          </div>
          <div class="modal-footer">
            <template v-if="isWorktreeList">
              <button class="secondary-btn" @click="close">稍后处理</button>
              <button class="confirm-btn" @click="openMergeWizard">打开合并向导</button>
            </template>
            <button v-else class="confirm-btn" @click="close">确定</button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { useMergeStore } from '../../stores/mergeStore';

// 简化的通知接口（移除 metadata）
interface AgentNotification {
  title: string;
  message: string;
  type: 'info' | 'success' | 'warning' | 'error';
  timestamp: string;
}

const visible = ref(false);
const notification = ref<AgentNotification | null>(null);
const mergeStore = useMergeStore();

const icon = computed(() => {
  switch (notification.value?.type) {
    case 'success': return '✅';
    case 'warning': return '⚠️';
    case 'error': return '❌';
    default: return 'ℹ️';
  }
});

// 判断 message 是否为 worktree 列表
const isWorktreeList = computed(() => {
  try {
    const parsed = JSON.parse(notification.value?.message || '');
    return Array.isArray(parsed) && parsed.length > 0;
  } catch {
    return false;
  }
});

// 解析 worktree 名称列表
const worktreeNames = computed(() => {
  if (!isWorktreeList.value) return [];
  return JSON.parse(notification.value!.message);
});

function handleNotification(event: Event) {
  const customEvent = event as CustomEvent<AgentNotification>;
  notification.value = customEvent.detail;
  visible.value = true;
}

function close() {
  visible.value = false;
}

function openMergeWizard() {
  mergeStore.openWizardWithWorktrees(worktreeNames.value);
  close();
}

onMounted(() => {
  window.addEventListener('bimcanvas:agent-notification', handleNotification);
});

onUnmounted(() => {
  window.removeEventListener('bimcanvas:agent-notification', handleNotification);
});
</script>

<style scoped>
.agent-notification-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
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

.agent-notification-modal.success {
  border-color: rgba(34, 197, 94, 0.5);
}

.agent-notification-modal.warning {
  border-color: rgba(234, 179, 8, 0.5);
}

.agent-notification-modal.error {
  border-color: rgba(239, 68, 68, 0.5);
}

.agent-notification-modal.info {
  border-color: rgba(59, 130, 246, 0.5);
}

.modal-header {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
}

.modal-header .icon {
  font-size: 24px;
}

.modal-header h3 {
  flex: 1;
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary, #fff);
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-secondary, #888);
  font-size: 24px;
  cursor: pointer;
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  transition: all 0.2s;
}

.close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--text-primary, #fff);
}

.modal-body {
  padding: 20px;
  overflow-y: auto;
  flex: 1;
}

.modal-body pre {
  margin: 0;
  font-family: inherit;
  font-size: 14px;
  line-height: 1.6;
  color: var(--text-primary, #fff);
  white-space: pre-wrap;
  word-wrap: break-word;
}

.worktree-message p {
  margin: 0 0 12px 0;
  color: var(--text-primary, #fff);
}

.worktree-message ul {
  list-style: disc;
  padding-left: 24px;
  margin: 0;
}

.worktree-message li {
  margin: 8px 0;
}

.worktree-message code {
  font-family: 'Consolas', monospace;
  background: rgba(255, 255, 255, 0.1);
  padding: 2px 8px;
  border-radius: 4px;
  color: #3b82f6;
}

.modal-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.secondary-btn {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: var(--text-primary, #fff);
  padding: 10px 24px;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.secondary-btn:hover {
  background: rgba(255, 255, 255, 0.15);
}

.confirm-btn {
  background: var(--accent-color, #3b82f6);
  color: white;
  border: none;
  padding: 10px 24px;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.confirm-btn:hover {
  background: var(--accent-hover, #2563eb);
  transform: translateY(-1px);
}

.confirm-btn:active {
  transform: translateY(0);
}

/* Transition */
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}

.fade-enter-active .agent-notification-modal,
.fade-leave-active .agent-notification-modal {
  transition: transform 0.2s ease;
}

.fade-enter-from .agent-notification-modal,
.fade-leave-to .agent-notification-modal {
  transform: scale(0.95);
}
</style>
