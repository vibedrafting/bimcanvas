<script setup lang="ts">
import { computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';

interface BranchOption {
  value: string;
  label: string;
  isCurrent: boolean;
}

const props = defineProps<{
  targetBranch: string;
  sourceBranch: string;
  branches: BranchOption[];
  canProceed: boolean;
}>();

const emit = defineEmits<{
  (e: 'update:targetBranch', value: string): void;
  (e: 'update:sourceBranch', value: string): void;
  (e: 'next'): void;
}>();

// 可选的源分支（排除目标分支）
const availableSourceBranches = computed(() =>
  props.branches.filter(b => b.value !== props.targetBranch)
);

// 可选的目标分支
const availableTargetBranches = computed(() => props.branches);

// 错误提示
const validationError = computed(() => {
  if (props.targetBranch && props.sourceBranch && props.targetBranch === props.sourceBranch) {
    return '源分支和目标分支不能相同';
  }
  return null;
});
</script>

<template>
  <div class="step-content">
    <div class="form-section">
      <!-- 目标分支 -->
      <div class="form-group">
        <label>目标分支 <span class="hint">(将被覆盖)</span></label>
        <select
          :value="targetBranch"
          @change="emit('update:targetBranch', ($event.target as HTMLSelectElement).value)"
          class="branch-select"
        >
          <option value="" disabled>请选择目标分支</option>
          <option
            v-for="branch in availableTargetBranches"
            :key="branch.value"
            :value="branch.value"
          >
            {{ branch.label }}{{ branch.isCurrent ? ' (当前)' : '' }}
          </option>
        </select>
      </div>

      <!-- 源分支 -->
      <div class="form-group">
        <label>源分支 <span class="hint">(数据来源)</span></label>
        <select
          :value="sourceBranch"
          @change="emit('update:sourceBranch', ($event.target as HTMLSelectElement).value)"
          class="branch-select"
          :disabled="!targetBranch"
        >
          <option value="" disabled>请选择源分支</option>
          <option
            v-for="branch in availableSourceBranches"
            :key="branch.value"
            :value="branch.value"
          >
            {{ branch.label }}{{ branch.isCurrent ? ' (当前)' : '' }}
          </option>
        </select>
      </div>

      <!-- 验证错误 -->
      <div v-if="validationError" class="validation-error">
        {{ validationError }}
      </div>
    </div>

    <!-- 操作按钮 -->
    <div class="step-actions">
      <GlassButton
        variant="primary"
        :disabled="!canProceed"
        @click="emit('next')"
      >
        下一步
      </GlassButton>
    </div>
  </div>
</template>

<style scoped lang="scss">
.step-content {
  padding: 20px;
  background: #1a1d24;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-bottom: 24px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 8px;

  label {
    font-size: 0.85rem;
    font-weight: 500;
    color: #e0e0e0;

    .hint {
      font-weight: 400;
      color: #a0a0a0;
      font-size: 0.75rem;
    }
  }
}

.branch-select {
  width: 100%;
  padding: 10px 12px;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  background: #22262e;
  color: #e0e0e0;
  font-size: 0.9rem;
  cursor: pointer;
  transition: border-color 0.15s ease;

  &:focus {
    outline: none;
    border-color: #3b82f6;
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }

  option {
    background: #22262e;
    color: #e0e0e0;
  }
}

.validation-error {
  padding: 8px 12px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: 6px;
  color: #ef4444;
  font-size: 0.85rem;
}

.step-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
}
</style>
