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

const saveBeforeSwitch = ref(true);
const customMessage = ref('');
const useCustomMessage = ref(false);

// 默认提交信息
const defaultMessage = computed(() =>
  `自动存档：切换到分支 ${props.targetBranch} 前保存`
);

// 最终提交信息
const finalMessage = computed(() =>
  useCustomMessage.value && customMessage.value
    ? customMessage.value
    : defaultMessage.value
);

// 重置状态
watch(() => props.visible, (newVal) => {
  if (newVal) {
    saveBeforeSwitch.value = true;
    customMessage.value = '';
    useCustomMessage.value = false;
  }
});

const handleConfirm = () => {
  emit('confirm', saveBeforeSwitch.value, saveBeforeSwitch.value ? finalMessage.value : undefined);
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
            <h3>存在未保存的更改</h3>
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

            <div class="options">
              <label class="option-item" :class="{ selected: saveBeforeSwitch }">
                <input
                  type="radio"
                  :value="true"
                  v-model="saveBeforeSwitch"
                  name="save-option"
                />
                <div class="option-content">
                  <span class="option-title">保存后切换</span>
                  <span class="option-desc">自动提交当前更改，然后切换分支</span>
                </div>
              </label>

              <label class="option-item" :class="{ selected: !saveBeforeSwitch }">
                <input
                  type="radio"
                  :value="false"
                  v-model="saveBeforeSwitch"
                  name="save-option"
                />
                <div class="option-content">
                  <span class="option-title">放弃更改并切换</span>
                  <span class="option-desc warning-text">丢弃所有未保存的更改（不可恢复）</span>
                </div>
              </label>
            </div>

            <!-- 自定义提交信息 -->
            <div v-if="saveBeforeSwitch" class="commit-message-section">
              <label class="checkbox-label">
                <input
                  type="checkbox"
                  v-model="useCustomMessage"
                />
                <span>自定义提交信息</span>
              </label>

              <div v-if="useCustomMessage" class="message-input-wrapper">
                <input
                  v-model="customMessage"
                  type="text"
                  class="glass-input"
                  :placeholder="defaultMessage"
                />
              </div>

              <div v-else class="default-message">
                <span class="label">提交信息：</span>
                <span class="value">{{ defaultMessage }}</span>
              </div>
            </div>
          </div>

          <div class="dialog-footer">
            <GlassButton variant="ghost" @click="handleCancel">取消</GlassButton>
            <GlassButton
              :variant="saveBeforeSwitch ? 'primary' : 'ghost'"
              :class="{ 'danger-btn': !saveBeforeSwitch }"
              @click="handleConfirm"
            >
              {{ saveBeforeSwitch ? '保存并切换' : '放弃更改' }}
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
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.dialog-card {
  background: var(--glass-bg-solid);
  border: var(--glass-border);
  border-radius: 12px;
  width: 420px;
  box-shadow: var(--shadow-modal);
  display: flex;
  flex-direction: column;

  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg-solid), var(--glass-bg-solid));
  background-origin: border-box;
  background-clip: padding-box, border-box;
}

.dialog-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-subtle);
  display: flex;
  align-items: center;
  gap: 12px;

  .header-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 32px;
    height: 32px;
    border-radius: 8px;

    &.warning {
      background: rgba(245, 158, 11, 0.15);
      color: #f59e0b;
    }
  }

  h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
    color: var(--text-primary);
    flex: 1;
  }

  .close-btn {
    background: none;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 4px;
    border-radius: 4px;

    &:hover {
      background: rgba(255, 255, 255, 0.1);
      color: var(--text-primary);
    }
  }
}

.dialog-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.message {
  margin: 0;
  font-size: 0.9rem;
  color: var(--text-secondary);
  line-height: 1.5;
}

.branch-name {
  font-family: 'JetBrains Mono', monospace;
  font-size: 0.85rem;
  padding: 2px 6px;
  background: rgba(59, 130, 246, 0.15);
  color: var(--accent-blue);
  border-radius: 4px;
}

.options {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.option-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;

  &:hover {
    background: rgba(255, 255, 255, 0.03);
  }

  &.selected {
    border-color: var(--accent-blue);
    background: rgba(59, 130, 246, 0.08);
  }

  input[type="radio"] {
    margin-top: 2px;
    accent-color: var(--accent-blue);
  }
}

.option-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.option-title {
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-primary);
}

.option-desc {
  font-size: 0.8rem;
  color: var(--text-secondary);

  &.warning-text {
    color: #ef4444;
  }
}

.commit-message-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 8px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: var(--text-secondary);
  cursor: pointer;

  input[type="checkbox"] {
    accent-color: var(--accent-blue);
  }
}

.message-input-wrapper {
  margin-top: 4px;
}

.glass-input {
  width: 100%;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--text-primary);
  font-family: inherit;
  font-size: 0.85rem;
  outline: none;
  transition: all 0.2s;
  box-sizing: border-box;

  &:focus {
    background: rgba(255, 255, 255, 0.08);
    border-color: var(--accent-blue);
  }
}

.default-message {
  font-size: 0.8rem;
  color: var(--text-muted);

  .label {
    color: var(--text-secondary);
  }

  .value {
    font-style: italic;
  }
}

.dialog-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border-subtle);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.danger-btn {
  color: #ef4444 !important;

  &:hover {
    background: rgba(239, 68, 68, 0.1) !important;
  }
}

/* Animation */
.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.2s ease;

  .dialog-card {
    transition: transform 0.2s var(--ease-spring);
  }
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;

  .dialog-card {
    transform: scale(0.95) translateY(10px);
  }
}
</style>
