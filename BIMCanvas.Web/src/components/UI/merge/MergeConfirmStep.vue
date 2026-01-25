<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';

defineProps<{
  targetBranch: string;
  sourceBranch: string;
  isMerging: boolean;
  error: string | null;
  worktreesToCleanup?: string[];
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
        <div class="branch-card source">
          <div class="branch-label">源分支</div>
          <div class="branch-name">{{ sourceBranch }}</div>
          <div class="branch-hint">数据来源</div>
        </div>

        <div class="merge-arrow">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="5" y1="12" x2="19" y2="12"></line>
            <polyline points="12 5 19 12 12 19"></polyline>
          </svg>
        </div>

        <div class="branch-card target">
          <div class="branch-label">目标分支</div>
          <div class="branch-name">{{ targetBranch }}</div>
          <div class="branch-hint">将被覆盖</div>
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

      <!-- Worktree 清理提示 -->
      <div v-if="worktreesToCleanup && worktreesToCleanup.length > 0" class="cleanup-info">
        <div class="info-icon">🗑️</div>
        <div class="info-content">
          <div class="info-title">合并后将清理 {{ worktreesToCleanup.length }} 个 worktree</div>
          <div class="info-list">
            <code v-for="name in worktreesToCleanup" :key="name">{{ name }}</code>
          </div>
        </div>
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
  background: #1a1d24;
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
  background: #22262e;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 10px;
  text-align: center;

  &.target {
    border-color: #ef4444;
    background: rgba(239, 68, 68, 0.1);
  }

  &.source {
    border-color: #22c55e;
    background: rgba(34, 197, 94, 0.1);
  }
}

.branch-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: #a0a0a0;
  margin-bottom: 8px;
}

.branch-name {
  font-size: 1rem;
  font-weight: 600;
  color: #e0e0e0;
  word-break: break-all;
}

.branch-hint {
  font-size: 0.75rem;
  color: #707070;
  margin-top: 4px;
}

.merge-arrow {
  flex-shrink: 0;
  color: #a0a0a0;
}

.warning-box {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: rgba(245, 158, 11, 0.1);
  border: 1px solid rgba(245, 158, 11, 0.3);
  border-radius: 8px;
  color: #e0e0e0;
  font-size: 0.85rem;

  svg {
    flex-shrink: 0;
    color: #f59e0b;
  }

  strong {
    color: #f59e0b;
  }
}

.cleanup-option {
  margin-top: 16px;
  padding: 12px 16px;
  background: rgba(59, 130, 246, 0.1);
  border: 1px solid rgba(59, 130, 246, 0.3);
  border-radius: 8px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;

  input[type="checkbox"] {
    width: 18px;
    height: 18px;
    accent-color: #3b82f6;
    cursor: pointer;
  }

  code {
    font-family: 'Consolas', monospace;
    background: rgba(255, 255, 255, 0.1);
    padding: 2px 6px;
    border-radius: 4px;
    color: #60a5fa;
  }
}

.cleanup-hint {
  margin: 8px 0 0 28px;
  font-size: 0.75rem;
  color: #a0a0a0;
}

.cleanup-info {
  margin-top: 16px;
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  border: 1px solid rgba(239, 68, 68, 0.3);
  border-radius: 8px;
  display: flex;
  gap: 12px;
  align-items: flex-start;
}

.info-icon {
  font-size: 20px;
  line-height: 1;
}

.info-content {
  flex: 1;
}

.info-title {
  font-size: 0.9rem;
  font-weight: 500;
  color: #ef4444;
  margin-bottom: 8px;
}

.info-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;

  code {
    font-family: 'Consolas', monospace;
    background: rgba(255, 255, 255, 0.1);
    padding: 2px 8px;
    border-radius: 4px;
    color: #ef4444;
    font-size: 0.8rem;
  }
}

.error-message {
  margin-top: 16px;
  padding: 12px 16px;
  background: rgba(239, 68, 68, 0.1);
  border-radius: 8px;
  color: #ef4444;
  font-size: 0.85rem;
}

.step-actions {
  display: flex;
  justify-content: space-between;
  padding-top: 16px;
  border-top: 1px solid rgba(255, 255, 255, 0.1);
}

.action-right {
  display: flex;
  gap: 8px;
}
</style>
