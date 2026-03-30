<script setup lang="ts">
import { ref, computed, onMounted, watch } from 'vue';
import { useAppStore } from '../stores/appStore';
import { useCanvasStore } from '../stores/canvasStore';
import GlassButton from '../components/UI/base/GlassButton.vue';
import ConflictDialog from '../components/UI/ConflictDialog.vue';
import HomeSettingsPanel from '../components/UI/HomeSettingsPanel.vue';
import { useProjectFile } from '../composables/useProjectFile';
import type { ProjectSummary } from '../types/homepage';

const appStore = useAppStore();
const canvasStore = useCanvasStore();

// Tab 状态
const activeTab = ref<'all' | 'recent'>('all');
const homeMode = ref<'projects' | 'settings'>('projects');

// 删除确认
const showDeleteDialog = ref(false);
const deleteTargetName = ref('');

// 导入相关（复用现有 composable）
const fileInputRef = ref<HTMLInputElement | null>(null);
const {
  handleLoad,
  processFile,
  handleConflictResolve,
  showConflictDialog,
  conflictProjectName,
  conflictExistingPath
} = useProjectFile();

// ============================================================
// 核心：监听 canvasStore.projectData 变化
// 当数据出现时（来自导入成功或冲突解决），自动跳转到工作区
// 这是导入流程和冲突解决流程的唯一过渡点
// ============================================================
watch(() => canvasStore.projectData, (newData, oldData) => {
  if (newData && !oldData && appStore.currentView === 'homepage') {
    console.log('[HomePage] Project data loaded, transitioning to workspace...');
    appStore.goToWorkspace();
  }
});

// 导入 .bcp
const handleImport = async () => {
  const result = await handleLoad();
  if (result === 'fallback') {
    fileInputRef.value?.click();
  }
  // 不需要手动跳转 — watch 监听 projectData 变化后自动跳转
};

const onFileSelected = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  await processFile(file);
  input.value = '';
  // 不需要手动跳转 — watch 监听 projectData 变化后自动跳转
  // 冲突情况下 processFile 会显示 ConflictDialog，解决后也由 watch 处理
};

// 打开项目
const isOpening = ref(false);
const openError = ref<string | null>(null);

const handleOpen = async (project: ProjectSummary) => {
  if (!project.isValid || isOpening.value) return;
  isOpening.value = true;
  openError.value = null;
  try {
    // openProject 调用 POST /api/project/open-folder
    // 成功后设置 currentView = 'workspace'
    // App.vue 的 watch 触发 enterWorkspace() → loadProject()
    const success = await appStore.openProject(project.folderPath);
    if (!success) {
      openError.value = `无法打开项目 "${project.name}"`;
    }
    // 成功时 appStore.openProject 已设置 currentView = 'workspace'
    // App.vue 的 watch 会触发加载 + cinematic
  } catch (err: any) {
    openError.value = err.message || '打开项目失败';
  } finally {
    isOpening.value = false;
  }
};

// 删除项目
const confirmDelete = (project: ProjectSummary) => {
  deleteTargetName.value = project.name;
  showDeleteDialog.value = true;
};

const handleDelete = async () => {
  showDeleteDialog.value = false;
  if (deleteTargetName.value) {
    const success = await appStore.deleteProject(deleteTargetName.value);
    if (!success) {
      openError.value = `删除项目 "${deleteTargetName.value}" 失败`;
    }
    deleteTargetName.value = '';
  }
};

const cancelDelete = () => {
  showDeleteDialog.value = false;
  deleteTargetName.value = '';
};

// 格式化日期
const formatDate = (dateStr: string | null): string => {
  if (!dateStr) return '--';
  try {
    const d = new Date(dateStr);
    return d.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })
      + ' ' + d.toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '--';
  }
};

// 计算显示列表
const displayProjects = computed(() => {
  if (activeTab.value === 'recent') {
    const recentPaths = new Set(appStore.recentProjects.map(r => r.folderPath));
    return appStore.projectList.filter(p => recentPaths.has(p.folderPath));
  }
  return appStore.projectList;
});

// 加载数据
onMounted(() => {
  appStore.fetchProjectList();
});
</script>

<template>
  <div class="homepage">
    <!-- 顶部 Header -->
    <header class="homepage-header" :class="{ compact: homeMode === 'settings' }">
      <template v-if="homeMode === 'projects'">
        <div class="header-left">
          <span class="brand-text">BIMCanvas</span>
        </div>
        <div class="header-right">
          <GlassButton variant="ghost" @click="homeMode = 'settings'">
            <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-right: 6px;">
              <circle cx="12" cy="12" r="3"></circle>
              <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06A1.65 1.65 0 0 0 15 19.4a1.65 1.65 0 0 0-1 .6 1.65 1.65 0 0 0-.33 1V21a2 2 0 1 1-4 0v-.09a1.65 1.65 0 0 0-.33-1A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.6 15a1.65 1.65 0 0 0-.6-1 1.65 1.65 0 0 0-1-.33H3a2 2 0 1 1 0-4h.09a1.65 1.65 0 0 0 1-.33A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.6a1.65 1.65 0 0 0 1-.6 1.65 1.65 0 0 0 .33-1V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 .33 1 1.65 1.65 0 0 0 1 .6 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9c0 .38.14.74.4 1 .26.26.62.4 1 .4H21a2 2 0 1 1 0 4h-.09c-.38 0-.74.14-1 .4-.26.26-.4.62-.4 1z"></path>
            </svg>
            实例设置
          </GlassButton>
          <GlassButton variant="primary" @click="handleImport">
            <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" style="margin-right: 6px;">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
              <polyline points="7 10 12 15 17 10"></polyline>
              <line x1="12" y1="15" x2="12" y2="3"></line>
            </svg>
            导入 .bcp
          </GlassButton>
          <input
            ref="fileInputRef"
            type="file"
            accept=".bcp"
            style="display: none"
            @change="onFileSelected"
          />
        </div>
      </template>

      <template v-else>
        <div class="settings-header-left">
          <button class="back-button" type="button" @click="homeMode = 'projects'">
            <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M15 18l-6-6 6-6" />
            </svg>
          </button>
          <div class="settings-header-copy">
            <span class="brand-text">实例设置</span>
            <span class="settings-subtitle">首页内配置台</span>
          </div>
        </div>
        <div id="settings-header-actions" class="settings-header-actions"></div>
      </template>
    </header>

    <!-- 主内容 -->
    <main class="homepage-content" :class="{ 'settings-mode': homeMode === 'settings' }">
      <HomeSettingsPanel
        v-if="homeMode === 'settings'"
        key="settings"
        @close="homeMode = 'projects'"
      />

      <div v-else key="projects" class="projects-view">
      <!-- Tab 栏 -->
      <div class="tab-bar">
        <button
          class="tab-item"
          :class="{ active: activeTab === 'all' }"
          @click="activeTab = 'all'"
        >
          全部项目
        </button>
        <button
          class="tab-item"
          :class="{ active: activeTab === 'recent' }"
          @click="activeTab = 'recent'"
        >
          最近打开
        </button>
        <div class="tab-spacer"></div>
        <GlassButton variant="ghost" @click="appStore.fetchProjectList()" :disabled="appStore.isLoadingList" title="刷新列表" class="refresh-btn">
          <svg :class="{ spinning: appStore.isLoadingList }" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="23 4 23 10 17 10"></polyline>
            <polyline points="1 20 1 14 7 14"></polyline>
            <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
          </svg>
        </GlassButton>
      </div>

      <!-- 错误提示 -->
      <div v-if="openError" class="error-banner" @click="openError = null">
        {{ openError }}
      </div>

      <!-- 列表错误 -->
      <div v-if="appStore.listError" class="error-banner">
        {{ appStore.listError }}
      </div>

      <!-- 加载中 -->
      <div v-if="appStore.isLoadingList && displayProjects.length === 0" class="loading-state">
        加载中...
      </div>

      <!-- 空状态 -->
      <div v-else-if="displayProjects.length === 0" class="empty-state">
        <svg viewBox="0 0 24 24" width="48" height="48" fill="none" stroke="currentColor" stroke-width="1.5" opacity="0.3">
          <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
        </svg>
        <p class="empty-title">{{ activeTab === 'recent' ? '暂无最近打开的项目' : '暂无项目' }}</p>
        <p class="empty-hint">点击上方「导入 .bcp」按钮导入项目</p>
      </div>

      <!-- 项目卡片列表 -->
      <div v-else class="project-grid">
        <div
          v-for="project in displayProjects"
          :key="project.folderPath"
          class="project-card"
          :class="{ invalid: !project.isValid, opening: isOpening }"
          :title="project.isValid ? `打开 ${project.name}` : project.errorMessage || '项目异常'"
          @click="handleOpen(project)"
        >
          <!-- 项目图标 -->
          <div class="card-icon" :class="{ 'icon-error': !project.isValid }">
            <svg v-if="project.isValid" viewBox="0 0 24 24" width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
            </svg>
            <svg v-else viewBox="0 0 24 24" width="32" height="32" fill="none" stroke="currentColor" stroke-width="1.5">
              <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
          </div>

          <!-- 项目信息 -->
          <div class="card-body">
            <div class="card-name">{{ project.name }}</div>
            <div class="card-meta">
              <span>{{ formatDate(project.updatedAt) }}</span>
              <span v-if="project.schemeCount > 0" class="meta-dot">{{ project.schemeCount }} 个方案</span>
              <span v-if="project.activeScheme" class="meta-dot">{{ project.activeScheme }}</span>
            </div>
            <div v-if="!project.isValid" class="card-error">
              {{ project.errorMessage || '项目异常' }}
            </div>
          </div>

          <!-- 操作按钮 -->
          <div class="card-actions" v-if="project.isValid">
            <GlassButton
              variant="danger"
              class="delete-btn"
              title="删除项目"
              @click.stop="confirmDelete(project)"
            >
              <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
              </svg>
            </GlassButton>
          </div>
        </div>
      </div>
      </div>

    </main>

    <!-- 删除确认对话框 -->
    <Teleport to="body">
      <Transition name="dialog">
        <div v-if="showDeleteDialog" class="dialog-overlay" @click.self="cancelDelete">
          <div class="delete-dialog">
            <div class="dialog-header">
              <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="var(--accent-danger)" stroke-width="2">
                <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <h3>确认删除</h3>
            </div>
            <div class="dialog-content">
              <p>确定删除项目 "<strong>{{ deleteTargetName }}</strong>" 吗？</p>
              <p class="warning-text">此操作不可恢复，项目文件夹将被永久删除。</p>
            </div>
            <div class="dialog-actions">
              <GlassButton variant="danger" @click="handleDelete">删除</GlassButton>
              <GlassButton variant="ghost" @click="cancelDelete">取消</GlassButton>
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>

    <!-- 导入冲突对话框 -->
    <ConflictDialog
      :visible="showConflictDialog"
      :project-name="conflictProjectName"
      :existing-path="conflictExistingPath"
      @resolve="handleConflictResolve"
    />
  </div>
</template>

<style scoped>
.homepage {
  width: 100vw;
  height: 100vh;
  background-color: var(--bg-canvas);
  background-image:
    /* Glow */
    radial-gradient(circle at 50% -20%, rgba(59, 130, 246, 0.1) 0%, transparent 50%),
    /* Major grid (100px) */
    linear-gradient(rgba(60, 75, 95, 0.25) 1px, transparent 1px),
    linear-gradient(90deg, rgba(60, 75, 95, 0.25) 1px, transparent 1px),
    /* Minor grid (20px) */
    linear-gradient(rgba(60, 75, 95, 0.1) 1px, transparent 1px),
    linear-gradient(90deg, rgba(60, 75, 95, 0.1) 1px, transparent 1px);
  background-size:
    100% 100%,
    100px 100px, 100px 100px,
    20px 20px, 20px 20px;
  background-position:
    center top,
    center center, center center,
    center center, center center;
  display: flex;
  flex-direction: column;
  color: var(--text-primary);
  font-family: var(--font-sans);
}

/* Header */
.homepage-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 32px;
  height: 72px;
  box-sizing: border-box;
  border-bottom: var(--glass-border);
  background: var(--surface-glass);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  z-index: 10;
  flex-shrink: 0;
}

.brand-text {
  font-size: 1.2rem;
  font-weight: 700;
  letter-spacing: 0.5px;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 8px;
}

.settings-header-left {
  display: flex;
  align-items: center;
  gap: 14px;
}

.settings-header-copy {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.settings-subtitle,
.settings-header-note {
  color: var(--text-secondary);
  font-size: 0.82rem;
}

.settings-header-actions {
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 16px;
}

.back-button {
  width: 38px;
  height: 38px;
  border-radius: 50%;
  border: 1px solid var(--border-subtle);
  background: rgba(255, 255, 255, 0.04);
  color: var(--text-primary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
}

/* Content */
.homepage-content {
  flex: 1;
  overflow-y: auto;
  padding: 48px 32px 32px;
  max-width: 960px;
  width: 100%;
  margin: 0 auto;
}

.homepage-content.settings-mode {
  max-width: 1400px;
  padding-top: 16px;
  padding-bottom: 0;
}

.projects-view {
  min-height: 100%;
}

/* Tabs */
.tab-bar {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-bottom: 20px;
  border-bottom: 1px solid var(--border-subtle);
  padding-bottom: 0;
}

.tab-item {
  background: none;
  border: none;
  color: var(--text-secondary);
  font-family: var(--font-sans);
  font-size: 0.9rem;
  padding: 8px 16px;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -1px;
  transition: color 0.15s, border-color 0.15s;
}

.tab-item:hover {
  color: var(--text-primary);
}

.tab-item.active {
  color: var(--accent-blue);
  border-bottom-color: var(--accent-blue);
}

.tab-spacer {
  flex: 1;
}

.refresh-btn {
  padding: 4px 8px !important;
  margin-bottom: 2px;
}

.spinning {
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}

/* Error Banner */
.error-banner {
  background: rgba(255, 107, 107, 0.15);
  border: 1px solid rgba(255, 107, 107, 0.3);
  border-radius: var(--radius-md);
  padding: 10px 16px;
  margin-bottom: 16px;
  color: var(--accent-danger);
  font-size: 0.85rem;
  cursor: pointer;
}

/* Loading */
.loading-state {
  text-align: center;
  padding: 60px 0;
  color: var(--text-secondary);
  font-size: 0.9rem;
}

/* Empty State */
.empty-state {
  text-align: center;
  padding: 80px 0;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.empty-title {
  color: var(--text-secondary);
  font-size: 1rem;
  margin: 8px 0 0;
}

.empty-hint {
  color: var(--text-tertiary);
  font-size: 0.85rem;
  margin: 0;
}

/* Project Grid */
.project-grid {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.project-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 18px 24px;
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255,255,255,0.06);
  border-radius: var(--radius-lg, 12px);
  box-shadow: inset 0 1px 1px rgba(255, 255, 255, 0.08), 0 8px 24px rgba(0, 0, 0, 0.5);
  cursor: pointer;
  transition: all 0.25s var(--ease-spring);
}

.project-card:hover:not(.invalid):not(.opening) {
  background: var(--surface-glass-hover);
  border-color: rgba(59, 130, 246, 0.4);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.4), 0 0 0 1px rgba(59, 130, 246, 0.1) inset;
  transform: translateY(-2px);
}

.project-card.invalid {
  opacity: 0.5;
  cursor: not-allowed;
  filter: grayscale(100%);
}

.project-card.opening {
  opacity: 0.7;
  pointer-events: none;
  border-color: var(--accent-blue);
  box-shadow: 0 0 0 1px var(--accent-blue) inset;
}

/* Card Icon */
.card-icon {
  flex-shrink: 0;
  color: var(--accent-blue);
  opacity: 0.7;
}

.card-icon.icon-error {
  color: var(--accent-danger);
}

/* Card Body */
.card-body {
  flex: 1;
  min-width: 0;
}

.card-name {
  font-size: 0.95rem;
  font-weight: 600;
  margin-bottom: 4px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-meta {
  font-size: 0.8rem;
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}

.meta-dot::before {
  content: '\00B7';
  margin-right: 4px;
}

.card-error {
  font-size: 0.75rem;
  color: var(--accent-danger);
  margin-top: 4px;
}

/* Card Actions */
.card-actions {
  flex-shrink: 0;
  opacity: 0;
  transition: opacity 0.15s;
}

.project-card:hover .card-actions {
  opacity: 1;
}

.delete-btn {
  padding: 4px 8px !important;
}

/* Delete Dialog */
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.delete-dialog {
  background: var(--glass-bg-solid);
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  padding: 24px;
  min-width: 380px;
  max-width: 460px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.dialog-header h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}

.dialog-content {
  margin-bottom: 20px;
}

.dialog-content p {
  margin: 0 0 8px;
  color: var(--text-secondary);
  font-size: 14px;
  line-height: 1.5;
}

.dialog-content strong {
  color: var(--text-primary);
  font-weight: 600;
}

.warning-text {
  color: var(--accent-danger) !important;
  font-size: 0.85rem !important;
}

.dialog-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

/* Dialog Transitions */
.dialog-enter-active,
.dialog-leave-active {
  transition: all 0.2s ease;
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;
}

.dialog-enter-from .delete-dialog,
.dialog-leave-to .delete-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.dialog-enter-active .delete-dialog,
.dialog-leave-active .delete-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
