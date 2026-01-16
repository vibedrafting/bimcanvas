<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { SchemeService, type SchemeModulesResponse } from '../../../services/SchemeService';
import { useCanvasStore } from '../../../stores/canvasStore';
import { ChangeSource } from '../../../types/history';
import GlassButton from '../base/GlassButton.vue';

const API_BASE = 'http://localhost:5000';

// Worktree 信息
interface WorktreeInfo {
  name: string;
  path: string;
  branch: string | null;
  commitHash: string | null;
  isMain: boolean;
}

// 组件状态
const isVisible = ref(false);
const isLoading = ref(false);
const isLoadingWorktrees = ref(false);
const error = ref<string | null>(null);

// 数据
const worktrees = ref<WorktreeInfo[]>([]);
const selectedWorktree = ref<string>('');
const mainData = ref<SchemeModulesResponse | null>(null);
const worktreeData = ref<SchemeModulesResponse | null>(null);

// Store
const canvasStore = useCanvasStore();

// 计算属性：可选的 worktree（排除主仓库）
const availableWorktrees = computed(() =>
  worktrees.value.filter(wt => !wt.isMain)
);

// 计算属性：差异统计
const diffSummary = computed(() => {
  if (!mainData.value || !worktreeData.value) return null;

  const mainCount = mainData.value.modules.length;
  const wtCount = worktreeData.value.modules.length;
  const diff = wtCount - mainCount;

  return {
    mainCount,
    wtCount,
    diff,
    label: diff > 0 ? `+${diff}` : diff < 0 ? `${diff}` : '0',
    color: diff > 0 ? 'var(--accent-green)' : diff < 0 ? 'var(--accent-danger)' : 'var(--text-secondary)'
  };
});

// 获取 Worktree 列表
async function fetchWorktrees() {
  isLoadingWorktrees.value = true;
  try {
    const response = await fetch(`${API_BASE}/api/git/worktrees`);
    if (response.ok) {
      worktrees.value = await response.json();
    }
  } catch (e) {
    console.error('[SchemeDiffPanel] 获取 worktree 列表失败:', e);
  } finally {
    isLoadingWorktrees.value = false;
  }
}

// 加载数据
async function loadData() {
  if (!selectedWorktree.value) return;

  isLoading.value = true;
  error.value = null;

  try {
    const [main, wt] = await Promise.all([
      SchemeService.getModules('main'),
      SchemeService.getModules(`worktree:${selectedWorktree.value}`)
    ]);
    mainData.value = main;
    worktreeData.value = wt;
  } catch (e: any) {
    console.error('[SchemeDiffPanel] 加载数据失败:', e);
    error.value = e.response?.data?.error || '加载失败';
  } finally {
    isLoading.value = false;
  }
}

// 接受变更
async function acceptChanges() {
  if (!worktreeData.value) return;

  isLoading.value = true;
  error.value = null;

  try {
    await SchemeService.saveModules(
      'main',
      worktreeData.value.modules,
      `接受 ${selectedWorktree.value} 的修改`
    );

    // 刷新画布
    await canvasStore.loadProject({ source: ChangeSource.ServerSync, preserveView: true });

    // 关闭面板
    close();
  } catch (e: any) {
    console.error('[SchemeDiffPanel] 保存失败:', e);
    error.value = e.response?.data?.error || '保存失败';
  } finally {
    isLoading.value = false;
  }
}

// 拒绝变更
function rejectChanges() {
  close();
}

// 打开面板
function open() {
  isVisible.value = true;
  selectedWorktree.value = '';
  mainData.value = null;
  worktreeData.value = null;
  error.value = null;
  fetchWorktrees();
}

// 关闭面板
function close() {
  isVisible.value = false;
}

// 监听 worktree 选择变化
watch(selectedWorktree, (val) => {
  if (val) {
    loadData();
  }
});

// 暴露方法供外部调用
defineExpose({ open, close });
</script>

<template>
  <Transition name="slide-up">
    <div v-if="isVisible" class="scheme-diff-panel">
      <!-- 标题栏 -->
      <div class="panel-header">
        <h3>Diff</h3>
        <button class="close-btn" @click="close">
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>

      <!-- Worktree 选择 -->
      <div class="worktree-selector">
        <label>Agent Worktree:</label>
        <select v-model="selectedWorktree" :disabled="isLoading || isLoadingWorktrees">
          <option value="" disabled>{{ isLoadingWorktrees ? 'Loading...' : '-- Select --' }}</option>
          <option v-for="wt in availableWorktrees" :key="wt.name" :value="wt.name">
            {{ wt.name }} ({{ wt.branch || 'detached' }})
          </option>
        </select>
      </div>

      <!-- 加载状态 -->
      <div v-if="isLoading" class="loading-state">
        Loading...
      </div>

      <!-- 无 Worktree 提示 -->
      <div v-else-if="availableWorktrees.length === 0" class="empty-state">
        No AI worktrees found
      </div>

      <!-- 差异展示 -->
      <div v-else-if="diffSummary" class="diff-display">
        <div class="diff-cards">
          <div class="diff-card">
            <div class="card-label">Main</div>
            <div class="card-value">{{ diffSummary.mainCount }}</div>
            <div class="card-sub">{{ mainData?.branch || 'current' }}</div>
          </div>
          <div class="diff-arrow">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="5" y1="12" x2="19" y2="12"></line>
              <polyline points="12 5 19 12 12 19"></polyline>
            </svg>
          </div>
          <div class="diff-card">
            <div class="card-label">Worktree</div>
            <div class="card-value">{{ diffSummary.wtCount }}</div>
            <div class="card-sub">{{ worktreeData?.branch || selectedWorktree }}</div>
          </div>
        </div>

        <div class="diff-summary" :style="{ color: diffSummary.color }">
          <span class="diff-label">Diff:</span>
          <span class="diff-value">{{ diffSummary.label }} modules</span>
        </div>
      </div>

      <!-- 未选择提示 -->
      <div v-else-if="!selectedWorktree" class="hint-state">
        Select a worktree to compare
      </div>

      <!-- 错误提示 -->
      <div v-if="error" class="error-message">
        {{ error }}
      </div>

      <!-- 操作按钮 -->
      <div class="panel-actions">
        <GlassButton variant="ghost" @click="rejectChanges" :disabled="isLoading">
          Reject
        </GlassButton>
        <GlassButton
          variant="primary"
          :disabled="!diffSummary || isLoading"
          @click="acceptChanges"
        >
          {{ isLoading ? 'Saving...' : 'Accept' }}
        </GlassButton>
      </div>
    </div>
  </Transition>
</template>

<style scoped lang="scss">
.scheme-diff-panel {
  position: fixed;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 400px;
  background: rgba(30, 35, 45, 0.98);
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4);
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
  color: var(--text-primary);
  transition: background 0.15s ease;

  &:hover {
    background: var(--bg-hover);
  }
}

.worktree-selector {
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

  select {
    width: 100%;
    padding: 8px 12px;
    border: 1px solid var(--border-subtle);
    border-radius: 6px;
    background: var(--bg-secondary);
    color: var(--text-primary);
    font-size: 0.875rem;
    cursor: pointer;

    &:focus {
      outline: none;
      border-color: var(--accent-blue);
    }

    &:disabled {
      opacity: 0.5;
      cursor: not-allowed;
    }

    // 下拉选项样式（浏览器原生）
    option {
      background: #1e232d;
      color: #e0e0e0;
      padding: 8px;
    }
  }
}

.loading-state,
.empty-state,
.hint-state {
  padding: 32px 16px;
  text-align: center;
  color: var(--text-secondary);
  font-size: 0.875rem;
}

.diff-display {
  padding: 16px;
}

.diff-cards {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  margin-bottom: 16px;
}

.diff-card {
  flex: 1;
  padding: 12px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-subtle);
  border-radius: 8px;
  text-align: center;
}

.card-label {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 4px;
}

.card-value {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--text-primary);
}

.card-sub {
  font-size: 0.7rem;
  color: var(--text-secondary);
  margin-top: 4px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.diff-arrow {
  color: var(--text-secondary);
  flex-shrink: 0;
}

.diff-summary {
  text-align: center;
  padding: 8px;
  background: var(--bg-secondary);
  border-radius: 6px;
}

.diff-label {
  font-size: 0.75rem;
  color: var(--text-secondary);
  margin-right: 8px;
}

.diff-value {
  font-size: 0.875rem;
  font-weight: 600;
}

.error-message {
  padding: 8px 16px;
  background: rgba(255, 107, 107, 0.1);
  color: var(--accent-danger);
  font-size: 0.8rem;
  text-align: center;
}

.panel-actions {
  display: flex;
  gap: 8px;
  padding: 12px 16px;
  border-top: 1px solid var(--border-subtle);

  :deep(.glass-button) {
    flex: 1;
  }
}

// 动画
.slide-up-enter-active,
.slide-up-leave-active {
  transition: transform 0.25s ease, opacity 0.25s ease;
}

.slide-up-enter-from,
.slide-up-leave-to {
  transform: translate(-50%, -50%) scale(0.95);
  opacity: 0;
}
</style>
