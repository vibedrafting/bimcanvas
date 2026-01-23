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
            <pre>{{ notification?.message }}</pre>

            <!-- 如果有 worktreeNames，显示删除选项 -->
            <div v-if="worktreeNames && worktreeNames.length > 0" class="worktree-actions">
              <h4>Worktree 清理</h4>
              <ul>
                <li v-for="name in worktreeNames" :key="name">
                  <span class="worktree-name">{{ name }}</span>
                  <button @click="deleteWorktree(name)" class="delete-btn">删除</button>
                </li>
              </ul>
            </div>
          </div>
          <div class="modal-footer">
            <button class="confirm-btn" @click="close">确定</button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import type { AgentNotification } from '@/services/SignalRService';
import axios from 'axios';

const visible = ref(false);
const notification = ref<AgentNotification | null>(null);

const icon = computed(() => {
  switch (notification.value?.type) {
    case 'success': return '✅';
    case 'warning': return '⚠️';
    case 'error': return '❌';
    default: return 'ℹ️';
  }
});

const worktreeNames = computed(() => {
  return notification.value?.metadata?.worktreeNames || [];
});

function handleNotification(event: Event) {
  const customEvent = event as CustomEvent<AgentNotification>;
  notification.value = customEvent.detail;
  visible.value = true;
}

function close() {
  visible.value = false;
}

async function deleteWorktree(name: string) {
  if (!confirm(`确定删除 worktree "${name}" 及其分支？`)) {
    return;
  }

  try {
    await axios.delete(`http://localhost:5000/api/git/worktrees/${name}?deleteBranch=true`);
    alert(`已删除 worktree: ${name}`);

    // 从列表中移除
    if (notification.value?.metadata?.worktreeNames) {
      const index = notification.value.metadata.worktreeNames.indexOf(name);
      if (index > -1) {
        notification.value.metadata.worktreeNames.splice(index, 1);
      }
    }
  } catch (error: any) {
    const errorMsg = error.response?.data?.message || error.message || '未知错误';
    alert(`删除失败: ${errorMsg}`);
  }
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

.modal-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
  display: flex;
  justify-content: flex-end;
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

/* Worktree Actions */
.worktree-actions {
  margin-top: 1rem;
  padding-top: 1rem;
  border-top: 1px solid var(--glass-border, rgba(255, 255, 255, 0.1));
}

.worktree-actions h4 {
  margin: 0 0 0.75rem 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-secondary, #aaa);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.worktree-actions ul {
  list-style: none;
  padding: 0;
  margin: 0;
}

.worktree-actions li {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.75rem;
  margin-bottom: 0.5rem;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid var(--glass-border, rgba(255, 255, 255, 0.05));
  border-radius: 6px;
  transition: all 0.2s;
}

.worktree-actions li:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.1);
}

.worktree-actions li:last-child {
  margin-bottom: 0;
}

.worktree-name {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  color: var(--text-primary, #fff);
  flex: 1;
}

.delete-btn {
  background-color: #dc3545;
  color: white;
  border: none;
  padding: 0.375rem 0.75rem;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.delete-btn:hover {
  background-color: #c82333;
  transform: translateY(-1px);
  box-shadow: 0 2px 8px rgba(220, 53, 69, 0.3);
}

.delete-btn:active {
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
