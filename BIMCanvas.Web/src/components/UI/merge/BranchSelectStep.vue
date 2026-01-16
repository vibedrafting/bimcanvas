<script setup lang="ts">
import { computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import GlassSelect from '../base/GlassSelect.vue';

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
  props.branches
    .filter(b => b.value !== props.targetBranch)
    .map(b => ({
      ...b,
      label: b.isCurrent ? `${b.label} (当前)` : b.label
    }))
);

// 可选的目标分支
const availableTargetBranches = computed(() => 
  props.branches.map(b => ({
    ...b,
    label: b.isCurrent ? `${b.label} (当前)` : b.label
  }))
);

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
        <GlassSelect
          :model-value="targetBranch"
          @update:model-value="emit('update:targetBranch', $event as string)"
          :options="availableTargetBranches"
          placeholder="请选择目标分支"
          width="100%"
          variant="solid"
        />
      </div>

      <!-- 源分支 -->
      <div class="form-group">
        <label>源分支 <span class="hint">(数据来源)</span></label>
        <GlassSelect
          :model-value="sourceBranch"
          @update:model-value="emit('update:sourceBranch', $event as string)"
          :options="availableSourceBranches"
          placeholder="请选择源分支"
          width="100%"
          :disabled="!targetBranch"
          variant="solid"
        />
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
