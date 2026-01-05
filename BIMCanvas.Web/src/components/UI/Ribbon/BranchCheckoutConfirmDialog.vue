
<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';

interface Props {
  visible: boolean;
  targetBranch: string;
  currentBranch: string;
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'confirm', saveBeforeSwitch: boolean, commitMessage?: string): void;
  (e: 'cancel'): void;
}>();

const customMessage = ref('');

// 默认提交信息
const defaultMessage = computed(() =>
  `自动存档：切换到分支 ${props.targetBranch} 前保存`
);

// 最终提交信息
const finalMessage = computed(() =>
  customMessage.value.trim() || defaultMessage.value
);

// 重置状态
watch(() => props.visible, (newVal) => {
  if (newVal) {
    customMessage.value = '';
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
      <div v-if="visible" class="dialog-overlay" @click.self="handleCancel">
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
              切换到 <span class="branch-name">{{ targetBranch }}</span> 前需要处理这些更改。
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
              class="danger-btn"
              @click="handleDiscardAndSwitch"
            >
              放弃更改并切换
            </GlassButton>
            <GlassButton
              variant="primary"
              @click="handleCommitAndSwitch"
            >
              提交并切换
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
  /* Deep Glass Style (More Opaque) */
  background: rgba(30, 32, 36, 0.75);
  backdrop-filter: blur(24px);
  -webkit-backdrop-filter: blur(24px);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 24px;
  width: 440px;
  
  /* Glare & Deep Shadow */
  background-image: linear-gradient(to bottom, rgba(255, 255, 255, 0.08), rgba(255, 255, 255, 0.02) 20%, transparent);
  box-shadow: 
    0 24px 60px rgba(0, 0, 0, 0.6), /* Deeper shadow */
    0 0 0 1px rgba(255, 255, 255, 0.05) inset; /* Subtle inner rim */

  background-origin: border-box;
  background-clip: padding-box, border-box;

  display: flex;
  flex-direction: column;
}

.dialog-header {
  padding: 24px 24px 16px;
  display: flex;
  align-items: center;
  gap: 12px;

  .header-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
    border-radius: 10px;
    flex-shrink: 0;

    &.warning {
      background: rgba(245, 158, 11, 0.15);
      color: #f59e0b;
    }
  }

  h3 {
    margin: 0;
    font-size: 1.1rem;
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
  padding: 0 24px 24px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.message {
  margin: 0;
  font-size: 0.95rem;
  color: var(--text-secondary);
  line-height: 1.6;
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
  gap: 8px;
}

.input-label {
  font-size: 0.8rem;
  color: var(--text-muted);
  font-weight: 500;
  margin-left: 2px;
}

.glass-input {
  width: 100%;
  background: rgba(0, 0, 0, 0.2);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  padding: 12px 16px;
  color: var(--text-primary);
  font-family: inherit;
  font-size: 0.9rem;
  outline: none;
  transition: all 0.2s;
  box-sizing: border-box;

  &::placeholder {
    color: var(--text-muted);
    font-style: italic;
    opacity: 0.6;
  }

  &:focus {
    background: rgba(0, 0, 0, 0.3);
    border-color: var(--accent-blue);
    box-shadow: 0 0 0 2px rgba(59, 130, 246, 0.15);
  }
}

.dialog-footer {
  padding: 20px 24px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  justify-content: flex-end; /* Right align buttons */
  gap: 12px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 0 0 24px 24px;
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
    transition: transform 0.4s cubic-bezier(0.34, 1.56, 0.64, 1);
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
