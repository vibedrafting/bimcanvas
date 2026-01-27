<script setup lang="ts">
import { computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import GlassSelect from '../base/GlassSelect.vue';
import type { WorktreeMetadataEntry } from '../../../types/worktree';

interface BranchOption {
  value: string;
  label: string;
  isCurrent: boolean;
}

interface WorktreeOption {
  value: string;
  label: string;
  branchName: string;
}

const props = defineProps<{
  targetBranch: string;
  sourceBranch: string;
  branches: BranchOption[];
  canProceed: boolean;
  worktreeMode?: boolean;
  worktreeOptions?: WorktreeOption[];
  selectedWorktree?: string;
  worktreeMetadata?: WorktreeMetadataEntry[];
  branchesToCleanup?: string[];
}>();

const emit = defineEmits<{
  (e: 'update:targetBranch', value: string): void;
  (e: 'update:sourceBranch', value: string): void;
  (e: 'update:selectedWorktree', value: string): void;
  (e: 'update:branchesToCleanup', value: string[]): void;
  (e: 'next'): void;
}>();

// 分支图标
const branchIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="3" x2="6" y2="15"></line><circle cx="18" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M18 9a9 9 0 0 1-9 9"></path></svg>';

// 切换分支清理选择
const toggleBranchCleanup = (branchName: string) => {
  const current = props.branchesToCleanup || [];
  const index = current.indexOf(branchName);
  if (index >= 0) {
    emit('update:branchesToCleanup', current.filter(n => n !== branchName));
  } else {
    emit('update:branchesToCleanup', [...current, branchName]);
  }
};

// 可选的源分支（排除目标分支）
const availableSourceBranches = computed(() =>
  props.branches
    .filter(b => b.value !== props.targetBranch)
    .map(b => ({
      ...b,
      label: b.isCurrent ? `${b.label} (当前)` : b.label,
      icon: branchIcon,
      tags: b.commit ? [b.commit.message.substring(0, 25) + (b.commit.message.length > 25 ? '...' : '')] : []
    }))
);

// 可选的目标分支
const availableTargetBranches = computed(() =>
  props.branches.map(b => ({
    ...b,
    label: b.isCurrent ? `${b.label} (当前)` : b.label,
    icon: branchIcon,
    tags: b.commit ? [b.commit.message.substring(0, 25) + (b.commit.message.length > 25 ? '...' : '')] : []
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

      <!-- Worktree 模式：单选列表 -->
      <template v-if="worktreeMode">
        <div class="form-group">
          <label>选择要合并的任务 <span class="hint">(Agent 完成的任务)</span></label>
          <div class="worktree-list">
            <label v-for="option in worktreeOptions" :key="option.value"
                   class="worktree-item" :class="{ selected: selectedWorktree === option.value }">
              <input type="radio" :value="option.value"
                     :checked="selectedWorktree === option.value"
                     @change="emit('update:selectedWorktree', option.value);
                              emit('update:sourceBranch', option.branchName)" />
              <div class="worktree-info">
                <span class="worktree-name">{{ option.label }}</span>
                <span class="worktree-branch">{{ option.branchName }}</span>
              </div>
            </label>
          </div>
        </div>

        <div class="form-group">
          <label>合并到目标分支 <span class="hint">(将被覆盖)</span></label>
          <GlassSelect :model-value="targetBranch"
                       @update:model-value="emit('update:targetBranch', $event as string)"
                       :options="availableTargetBranches"
                       placeholder="请选择目标分支"
                       width="100%"
                       variant="solid" />
        </div>

        <!-- 批量清理区域 -->
        <div v-if="worktreeMetadata && worktreeMetadata.filter(m => m.intent === 'isolation').length > 0" class="cleanup-section">
          <div class="section-divider"></div>
          <div class="form-group">
            <label>合并后要清理的临时 Git 分支 <span class="hint">(可多选)</span></label>
            <div class="cleanup-list">
              <label v-for="meta in worktreeMetadata.filter(m => m.intent === 'isolation')" :key="meta.branchName" class="cleanup-item">
                <input type="checkbox"
                       :checked="branchesToCleanup?.includes(meta.branchName)"
                       @change="toggleBranchCleanup(meta.branchName)" />
                <span class="cleanup-name">{{ meta.name }}</span>
                <span class="cleanup-branch">{{ meta.branchName }}</span>
                <span v-if="meta.name === selectedWorktree" class="cleanup-badge">已选择合并</span>
              </label>
            </div>
            <p class="cleanup-hint">清理后,这些分支将被永久删除(工作树会自动清理)</p>
          </div>
        </div>
      </template>

      <!-- 传统模式：双下拉框 -->
      <template v-else>
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
      </template>

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

.worktree-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.worktree-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: #22262e;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.2s;

  &:hover {
    background: #2a2f38;
    border-color: rgba(59, 130, 246, 0.3);
  }

  &.selected {
    background: rgba(59, 130, 246, 0.15);
    border-color: #3b82f6;
  }

  input[type="radio"] {
    width: 18px;
    height: 18px;
    accent-color: #3b82f6;
    cursor: pointer;
  }
}

.worktree-info {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
}

.worktree-name {
  font-size: 0.9rem;
  font-weight: 500;
  color: #e0e0e0;
}

.worktree-branch {
  font-size: 0.75rem;
  color: #a0a0a0;
  font-family: 'Consolas', monospace;
}

.cleanup-section {
  margin-top: 20px;
}

.section-divider {
  height: 1px;
  background: rgba(255, 255, 255, 0.1);
  margin-bottom: 16px;
}

.cleanup-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.cleanup-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.2s;

  &:hover {
    background: rgba(255, 255, 255, 0.06);
  }

  input[type="checkbox"] {
    width: 16px;
    height: 16px;
    accent-color: #ef4444;
    cursor: pointer;
  }
}

.cleanup-name {
  flex: 1;
  font-size: 0.85rem;
  color: #e0e0e0;
}

.cleanup-branch {
  font-size: 0.7rem;
  color: #707070;
  font-family: 'Consolas', monospace;
  margin-left: auto;
  margin-right: 8px;
}

.cleanup-badge {
  font-size: 0.7rem;
  padding: 2px 8px;
  background: rgba(34, 197, 94, 0.15);
  border: 1px solid rgba(34, 197, 94, 0.3);
  border-radius: 4px;
  color: #22c55e;
}

.cleanup-hint {
  margin: 8px 0 0 0;
  font-size: 0.75rem;
  color: #ef4444;
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
