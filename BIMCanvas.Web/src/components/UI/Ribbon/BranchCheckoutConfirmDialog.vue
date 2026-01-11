
<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';

interface Props {
  visible: boolean;
  targetBranch: string;
  currentBranch: string;
  isCreating?: boolean;  // true = 创建新分支, false = 切换分支
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'confirm', saveBeforeSwitch: boolean, commitMessage?: string): void;
  (e: 'cancel'): void;
}>();

const customMessage = ref('');

// 默认提交信息
const defaultMessage = computed(() => {
  const now = new Date();
  const timestamp = now.getFullYear().toString() +
    (now.getMonth() + 1).toString().padStart(2, '0') +
    now.getDate().toString().padStart(2, '0') + '_' +
    now.getHours().toString().padStart(2, '0') +
    now.getMinutes().toString().padStart(2, '0') +
    now.getSeconds().toString().padStart(2, '0');
  const prefix = props.isCreating ? '创建分支存档' : '切换分支存档';
  return `${prefix}_${timestamp}`;
});

// 最终提交信息
const finalMessage = computed(() =>
  customMessage.value.trim() || defaultMessage.value
);

// 根据场景动态文案
const actionText = computed(() => props.isCreating ? '创建分支' : '切换到');
const cancelText = computed(() => props.isCreating ? '取消创建' : '取消切换');

// 重置状态
watch(() => props.visible, (newVal) => {
  if (newVal) {
    customMessage.value = defaultMessage.value;
  }
});

const handleCommitAndSwitch = () => {
  emit('confirm', true, finalMessage.value);
};

const handleDiscardAndSwitch = () => {
  emit('confirm', false);
};

const handleCancel = () => {
  emit('cancel');
};
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay">
        <div class="dialog-card">
          <div class="dialog-header">
            <div class="header-icon warning">
              <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
                <line x1="12" y1="9" x2="12" y2="13"></line>
                <line x1="12" y1="17" x2="12.01" y2="17"></line>
              </svg>
            </div>
            <h3>存在未提交的更改</h3>
            <button class="close-btn" @click="handleCancel">
              <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <div class="dialog-body">
            <p class="message">
              当前分支 <span class="branch-name">{{ currentBranch }}</span> 有未提交的更改。
              {{ actionText }} <span class="branch-name">{{ targetBranch }}</span> 前需要处理这些更改。
            </p>

            <div class="input-section">
              <label class="input-label">提交信息 (可选)</label>
              <input
                v-model="customMessage"
                type="text"
                class="glass-input"
                :placeholder="defaultMessage"
                @keydown.enter="handleCommitAndSwitch"
              />
            </div>
          </div>

          <div class="dialog-footer">
            <GlassButton
              variant="ghost"
              @click="handleCancel"
            >
              {{ cancelText }}
            </GlassButton>
            <GlassButton 
              variant="ghost" 
              class="danger-btn"
              @click="handleDiscardAndSwitch"
            >
              放弃更改
            </GlassButton>
            <GlassButton
              variant="primary"
              @click="handleCommitAndSwitch"
            >
              提交更改
            </GlassButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.4); /* 仅变暗，无模糊 */
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.dialog-card {
  /* Professional Engineering Style (Tech/CAD) */
  background: #18181b; /* Matte Dark */
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px; /* Sharper corners */
  width: 420px; /* More compact width */
  
  /* Subtle Shadow & Inner Highlight */
  box-shadow: 
    0 8px 32px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(0, 0, 0, 0.2),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset; /* Inner highlight for depth */

  display: flex;
  flex-direction: column;
}

.dialog-header {
  padding: 16px 20px 12px; /* Compact padding */
  display: flex;
  align-items: center;
  gap: 10px;

  .header-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 28px; /* Smaller icon container */
    height: 28px;
    border-radius: 8px;
    flex-shrink: 0;

    &.warning {
      background: rgba(245, 158, 11, 0.15);
      color: #f59e0b;
    }
  }

  h3 {
    margin: 0;
    font-size: 1rem; /* Smaller title */
    font-weight: 600;
    color: var(--text-primary);
    flex: 1;
  }

  .close-btn {
    background: none;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 6px;
    border-radius: 50%;
    transition: all 0.2s;
    display: flex;
    align-items: center;
    justify-content: center;

    &:hover {
      background: rgba(255, 255, 255, 0.1);
      color: var(--text-primary);
    }
  }
}

.dialog-body {
  padding: 0 20px 20px; /* Compact padding */
  display: flex;
  flex-direction: column;
  gap: 16px; /* Smaller gap */
}

.message {
  margin: 0;
  font-size: 0.9rem; /* Smaller text */
  color: var(--text-secondary);
  line-height: 1.5;
}

.branch-name {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.9rem;
  padding: 2px 6px;
  background: rgba(59, 130, 246, 0.15);
  color: var(--accent-blue);
  border-radius: 4px;
  font-weight: 500;
}

.input-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.input-label {
  font-size: 0.75rem; /* Smaller label */
  color: var(--text-muted);
  font-weight: 500;
  margin-left: 2px;
}

.glass-input {
  width: 100%;
  background: rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 8px 12px; /* Compact padding */
  color: var(--text-primary);
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.85rem;
  outline: none;
  transition: all 0.2s;
  box-sizing: border-box;

  &::placeholder {
    color: var(--text-muted);
    opacity: 0.6;
  }

  &:focus {
    background: rgba(0, 0, 0, 0.3);
    border-color: var(--accent-blue);
    box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
  }
}

.dialog-footer {
  padding: 16px 20px; /* Compact padding */
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  justify-content: flex-end; /* Right align buttons */
  gap: 10px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 0 0 12px 12px; /* Match card radius */

  /* Ensure buttons are wide enough and match tech style */
  :deep(button) {
    min-width: 88px; /* Slightly smaller min-width */
    height: 32px; /* Compact height */
    border-radius: 6px; /* Tech style radius */
    justify-content: center;
    font-size: 0.85rem; /* Smaller font */
    padding: 0 12px;
    /* Subtle inner highlight for buttons */
    box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.05) inset;
  }
}

.danger-btn {
  --btn-text: #f97316;
  color: #f97316 !important;
  opacity: 0.8;
  
  &:hover {
    opacity: 1;
    background: rgba(249, 115, 22, 0.1) !important;
  }
}

/* 弹窗动画 */
.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.3s ease;

  .dialog-card {
    transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1); /* Precise Expo Ease */
  }
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;

  .dialog-card {
    transform: scale(0.9) translateY(20px);
  }
}
</style>
