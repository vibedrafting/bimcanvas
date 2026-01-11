<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import GlassSelect from '../base/GlassSelect.vue';

interface Props {
  visible: boolean;
  baseBranch: string;
  baseTags: string[];
  allBranches: { label: string; value: string }[];
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'create', data: { name: string; baseBranch: string; tags: string[]; reason: string }): void;
  (e: 'cancel'): void;
}>();

const newBranchName = ref('');
const selectedBaseBranch = ref(props.baseBranch);
const commitMessage = ref('');

// 默认提交信息
const defaultMessage = computed(() => {
  const now = new Date();
  const timestamp = now.getFullYear().toString() +
    (now.getMonth() + 1).toString().padStart(2, '0') +
    now.getDate().toString().padStart(2, '0') + '_' +
    now.getHours().toString().padStart(2, '0') +
    now.getMinutes().toString().padStart(2, '0') +
    now.getSeconds().toString().padStart(2, '0');
  return 'branch_' + timestamp;
});

// Watch for visibility changes to reset form
watch(() => props.visible, (newVal) => {
  if (newVal) {
    newBranchName.value = '';
    selectedBaseBranch.value = props.baseBranch;
    commitMessage.value = defaultMessage.value;
  }
});

const handleCreate = () => {
  console.log('[BranchCreationDialog] handleCreate called, branchName:', newBranchName.value);

  if (!newBranchName.value.trim()) {
    console.log('[BranchCreationDialog] Branch name is empty, returning');
    return;
  }

  const data = {
    name: newBranchName.value.trim(),
    baseBranch: selectedBaseBranch.value,
    tags: props.baseTags,
    reason: commitMessage.value.trim() || defaultMessage.value
  };

  console.log('[BranchCreationDialog] Emitting create event with data:', data);
  emit('create', data);
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
            <div class="header-icon">
              <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="6" y1="3" x2="6" y2="15"></line>
                <circle cx="18" cy="6" r="3"></circle>
                <circle cx="6" cy="18" r="3"></circle>
                <path d="M18 9a9 9 0 0 1-9 9"></path>
              </svg>
            </div>
            <h3>创建新分支</h3>
            <button class="close-btn" @click="handleCancel">
              <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <div class="dialog-body">
            <!-- Base Branch -->
            <div class="input-section">
              <label class="input-label">基于分支</label>
              <GlassSelect
                v-model="selectedBaseBranch"
                :options="allBranches"
                width="100%"
              />
            </div>

            <!-- New Branch Name -->
            <div class="input-section">
              <label class="input-label">新分支名称</label>
              <input
                v-model="newBranchName"
                type="text"
                class="glass-input"
                placeholder="例如: feature/new-layout"
                @keydown.enter="handleCreate"
              />
            </div>

            <!-- Commit Message -->
            <div class="input-section">
              <label class="input-label">提交信息 (可选)</label>
              <input
                v-model="commitMessage"
                type="text"
                class="glass-input"
                :placeholder="defaultMessage"
                @keydown.enter="handleCreate"
              />
            </div>
          </div>

          <div class="dialog-footer">
            <GlassButton variant="ghost" @click="handleCancel">
              取消
            </GlassButton>
            <GlassButton
              variant="primary"
              :disabled="!newBranchName.trim()"
              @click="handleCreate"
            >
              创建分支
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
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.dialog-card {
  background: #18181b;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  width: 420px;
  box-shadow:
    0 8px 32px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(0, 0, 0, 0.2),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
  display: flex;
  flex-direction: column;
}

.dialog-header {
  padding: 16px 20px 12px;
  display: flex;
  align-items: center;
  gap: 10px;

  .header-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    width: 28px;
    height: 28px;
    border-radius: 8px;
    flex-shrink: 0;
    background: rgba(59, 130, 246, 0.15);
    color: var(--accent-blue);
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
  padding: 0 20px 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.input-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.input-label {
  font-size: 0.75rem;
  color: var(--text-muted);
  font-weight: 500;
  margin-left: 2px;
}

.glass-input {
  width: 100%;
  background: rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 8px 12px;
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
  padding: 16px 20px;
  border-top: 1px solid rgba(255, 255, 255, 0.08);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  background: rgba(0, 0, 0, 0.1);
  border-radius: 0 0 12px 12px;

  :deep(button) {
    min-width: 88px;
    height: 32px;
    border-radius: 6px;
    justify-content: center;
    font-size: 0.85rem;
    padding: 0 12px;
    box-shadow: 0 0 0 1px rgba(255, 255, 255, 0.05) inset;
  }
}

/* Animation */
.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.3s ease;

  .dialog-card {
    transition: transform 0.3s cubic-bezier(0.16, 1, 0.3, 1);
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
