<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';

defineProps<{
  targetBranch: string;
  sourceBranch: string;
  isMerging: boolean;
  error: string | null;
}>();

const emit = defineEmits<{
  (e: 'back'): void;
  (e: 'cancel'): void;
  (e: 'confirm'): void;
}>();
</script>

<template>
  <div class="step-content">
    <!-- 确认信息 -->
    <div class="confirm-section">
      <div class="merge-preview">
        <div class="branch-card target">
          <div class="branch-label">目标分支</div>
          <div class="branch-name">{{ targetBranch }}</div>
          <div class="branch-hint">将被覆盖</div>
        </div>

        <div class="merge-arrow">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="5" y1="12" x2="19" y2="12"></line>
            <polyline points="12 5 19 12 12 19"></polyline>
          </svg>
        </div>

        <div class="branch-card source">
          <div class="branch-label">源分支</div>
          <div class="branch-name">{{ sourceBranch }}</div>
          <div class="branch-hint">数据来源</div>
        </div>
      </div>

      <div class="warning-box">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path>
          <line x1="12" y1="9" x2="12" y2="13"></line>
          <line x1="12" y1="17" x2="12.01" y2="17"></line>
        </svg>
        <span>源分支的方案数据将 <strong>完全覆盖</strong> 目标分支</span>
      </div>

      <!-- 错误信息 -->
      <div v-if="error" class="error-message">
        {{ error }}
      </div>
    </div>

    <!-- 操作按钮 -->
    <div class="step-actions">
      <GlassButton variant="ghost" @click="emit('back')" :disabled="isMerging">
        返回
      </GlassButton>
      <div class="action-right">
        <GlassButton variant="ghost" @click="emit('cancel')" :disabled="isMerging">
          取消
        </GlassButton>
        <GlassButton variant="primary" @click="emit('confirm')" :disabled="isMerging">
          {{ isMerging ? '合并中...' : '确认合并' }}
        </GlassButton>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.step-content {
  padding: 20px;
}

.confirm-section {
  margin-bottom: 24px;
}

.merge-preview {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 16px;
  margin-bottom: 20px;
}

.branch-card {
  flex: 1;
  padding: 16px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-subtle);
  border-radius: 10px;
  text-align: center;

  &.target {
    border-color: var(--accent-danger);
    background: rgba(239, 68, 68, 0.05);
  }

  &.source {
    border-color: var(--accent-green);
    background: rgba(34, 197, 94, 0.05);
  }
}

.branch-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-secondary);
  margin-bottom: 8px;
}

.branch-name {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
  word-break: break-all;
}

.branch-hint {
  font-size: 0.75rem;
  color: var(--text-tertiary);
  margin-top: 4px;
}

.merge-arrow {
  flex-shrink: 0;
  color: var(--text-secondary);
}

.warning-box {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: rgba(245, 158, 11, 0.1);
  border: 1px solid rgba(245, 158, 11, 0.3);
  border-radius: 8px;
  color: var(--text-primary);
  font-size: 0.85rem;

  svg {
    flex-shrink: 0;
    color: #f59e0b;
  }

  strong {
    color: #f59e0b;
  }
}

.error-message {
  margin-top: 16px;
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: 8px;
  color: var(--accent-danger);
  font-size: 0.85rem;
}

.step-actions {
  display: flex;
  justify-content: space-between;
  padding-top: 16px;
  border-top: 1px solid var(--border-subtle);
}

.action-right {
  display: flex;
  gap: 8px;
}
</style>
