<script setup lang="ts">
import { computed, watch } from 'vue';
import { useMergeStore, type ZoneDiff } from '../../../stores/mergeStore';
import GlassButton from '../base/GlassButton.vue';

const mergeStore = useMergeStore();

// 是否显示面板
const isVisible = computed(() => mergeStore.showMergePanel);

// 当前选择的源分支
const selectedBranch = computed(() => mergeStore.selectedSourceBranch);

// 分区差异列表
const zoneDiffs = computed(() => mergeStore.zoneDiffs);

// 选中的分区
const selectedZones = computed(() => mergeStore.selectedZones);

// 选择分支
const handleBranchSelect = async (branchName: string) => {
  await mergeStore.fetchZoneDiffs(branchName);
};

// 切换分区选中
const handleToggleZone = (zoneId: string) => {
  mergeStore.toggleZoneSelection(zoneId);
};

// 全选/取消全选
const handleToggleAll = () => {
  mergeStore.toggleSelectAll();
};

// 执行合并
const handleMerge = async () => {
  const result = await mergeStore.executeSelectiveMerge();
  if (result?.success) {
    alert(`合并成功: ${result.mergedZones.length} 个分区`);
  } else if (result) {
    alert(`合并部分失败: 成功 ${result.mergedZones.length}, 失败 ${result.failedZones.length}`);
  }
};

// 关闭面板
const handleClose = () => {
  mergeStore.closeMergePanel();
};

// 获取差异统计文本
const getDiffSummary = (diff: ZoneDiff): string => {
  const parts = [];
  if (diff.addedModules.length > 0) parts.push(`+${diff.addedModules.length}`);
  if (diff.removedModules.length > 0) parts.push(`-${diff.removedModules.length}`);
  if (diff.modifiedModules.length > 0) parts.push(`~${diff.modifiedModules.length}`);
  return parts.join(' ') || 'No changes';
};

// 监听显示状态，自动获取分支列表
watch(isVisible, async (visible) => {
  if (visible) {
    await mergeStore.fetchMergeableBranches();
  }
});
</script>

<template>
  <Transition name="slide-up">
    <div v-if="isVisible" class="merge-review-panel">
      <!-- 标题栏 -->
      <div class="panel-header">
        <h3>Merge Review</h3>
        <button class="close-btn" @click="handleClose">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>

      <!-- 分支选择 -->
      <div class="branch-selector">
        <label>Source Branch:</label>
        <div class="branch-list">
          <div
            v-for="branch in mergeStore.mergeableBranches"
            :key="branch.name"
            class="branch-item"
            :class="{ selected: selectedBranch === branch.name, ai: branch.isAiBranch }"
            @click="handleBranchSelect(branch.name)"
          >
            <span class="branch-name">{{ branch.name }}</span>
            <span class="branch-meta">{{ branch.lastCommitTime }}</span>
            <span v-if="branch.isAiBranch" class="ai-badge">AI</span>
          </div>
        </div>
      </div>

      <!-- 加载状态 -->
      <div v-if="mergeStore.isLoading" class="loading-state">
        Loading...
      </div>

      <!-- 分区差异列表 -->
      <div v-else-if="zoneDiffs.length > 0" class="diff-section">
        <div class="diff-header">
          <label>
            <input
              type="checkbox"
              :checked="mergeStore.isAllSelected"
              @change="handleToggleAll"
            />
            Select All ({{ zoneDiffs.length }} zones)
          </label>
        </div>

        <div class="diff-list">
          <div
            v-for="diff in zoneDiffs"
            :key="diff.zoneId"
            class="diff-item"
            :class="{ selected: selectedZones.has(diff.zoneId) }"
            @click="handleToggleZone(diff.zoneId)"
          >
            <input
              type="checkbox"
              :checked="selectedZones.has(diff.zoneId)"
              @click.stop="handleToggleZone(diff.zoneId)"
            />
            <div class="zone-info">
              <span class="zone-id">{{ diff.zoneId }}</span>
              <span class="diff-summary">{{ getDiffSummary(diff) }}</span>
            </div>
            <div class="diff-details">
              <span v-if="diff.addedModules.length > 0" class="added">
                +{{ diff.addedModules.length }}
              </span>
              <span v-if="diff.removedModules.length > 0" class="removed">
                -{{ diff.removedModules.length }}
              </span>
              <span v-if="diff.modifiedModules.length > 0" class="modified">
                ~{{ diff.modifiedModules.length }}
              </span>
            </div>
          </div>
        </div>
      </div>

      <!-- 无差异提示 -->
      <div v-else-if="selectedBranch && !mergeStore.isLoading" class="empty-state">
        No differences found
      </div>

      <!-- 操作按钮 -->
      <div class="panel-actions">
        <GlassButton variant="ghost" @click="handleClose">Cancel</GlassButton>
        <GlassButton
          variant="primary"
          :disabled="mergeStore.selectedCount === 0 || mergeStore.isMerging"
          @click="handleMerge"
        >
          {{ mergeStore.isMerging ? 'Merging...' : `Merge (${mergeStore.selectedCount})` }}
        </GlassButton>
      </div>

      <!-- 错误提示 -->
      <div v-if="mergeStore.error" class="error-message">
        {{ mergeStore.error }}
      </div>
    </div>
  </Transition>
</template>

<style scoped lang="scss">
.merge-review-panel {
  position: fixed;
  bottom: 0;
  right: 20px;
  width: 400px;
  max-height: 70vh;
  background: var(--bg-primary);
  border: 1px solid var(--border-subtle);
  border-radius: 12px 12px 0 0;
  box-shadow: 0 -4px 20px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  z-index: 200;
  overflow: hidden;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-secondary);

  h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
  }
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  border-radius: 6px;
  cursor: pointer;
  transition: background 0.15s ease;

  &:hover {
    background: var(--bg-hover);
  }
}

.branch-selector {
  padding: 12px 16px;
  border-bottom: 1px solid var(--border-subtle);

  label {
    display: block;
    font-size: 0.75rem;
    font-weight: 600;
    color: var(--text-secondary);
    margin-bottom: 8px;
    text-transform: uppercase;
    letter-spacing: 0.05em;
  }
}

.branch-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  max-height: 150px;
  overflow-y: auto;
}

.branch-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
  background: var(--bg-tertiary);

  &:hover {
    background: var(--bg-hover);
  }

  &.selected {
    background: var(--accent-bg);
    border: 1px solid var(--accent);
  }

  &.ai .branch-name {
    color: var(--accent);
  }
}

.branch-name {
  flex: 1;
  font-size: 0.85rem;
  font-weight: 500;
}

.branch-meta {
  font-size: 0.75rem;
  color: var(--text-tertiary);
}

.ai-badge {
  font-size: 0.65rem;
  font-weight: 600;
  padding: 2px 6px;
  background: var(--accent);
  color: white;
  border-radius: 4px;
}

.loading-state,
.empty-state {
  padding: 32px 16px;
  text-align: center;
  color: var(--text-tertiary);
}

.diff-section {
  flex: 1;
  overflow-y: auto;
  padding: 12px 16px;
}

.diff-header {
  display: flex;
  align-items: center;
  margin-bottom: 12px;

  label {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 0.85rem;
    cursor: pointer;
  }
}

.diff-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.diff-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  background: var(--bg-tertiary);
  border-radius: 8px;
  cursor: pointer;
  transition: all 0.15s ease;

  &:hover {
    background: var(--bg-hover);
  }

  &.selected {
    background: var(--accent-bg);
    border: 1px solid var(--accent);
  }
}

.zone-info {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.zone-id {
  font-weight: 500;
  font-size: 0.85rem;
}

.diff-summary {
  font-size: 0.75rem;
  color: var(--text-tertiary);
}

.diff-details {
  display: flex;
  gap: 6px;
  font-size: 0.75rem;
  font-weight: 600;

  .added {
    color: #22c55e;
  }

  .removed {
    color: #ef4444;
  }

  .modified {
    color: #f59e0b;
  }
}

.panel-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--border-subtle);
  background: var(--bg-secondary);
}

.error-message {
  padding: 8px 16px;
  background: rgba(239, 68, 68, 0.1);
  color: #ef4444;
  font-size: 0.85rem;
  text-align: center;
}

.slide-up-enter-active,
.slide-up-leave-active {
  transition: transform 0.3s ease, opacity 0.3s ease;
}

.slide-up-enter-from,
.slide-up-leave-to {
  transform: translateY(100%);
  opacity: 0;
}
</style>
