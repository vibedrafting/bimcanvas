<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { useAppStore } from '../../stores/appStore';
import GlassButton from './base/GlassButton.vue';
import ConflictDialog from './ConflictDialog.vue';
import SaveConfirmDialog from './SaveConfirmDialog.vue';
import ExportFormatDialog from './ExportFormatDialog.vue';
import { useProjectFile } from '../../composables/useProjectFile';
import { useSave } from '../../composables/useSave';
import { getWebRuntime } from '../../runtime/runtimeRegistry';
import { supports } from '../../runtime/WebRuntimeProtocol';
import { LayoutValidationService, type Diagnostic, type ModuleNormalizationReport, type SchemeValidationReport } from '../../services/LayoutValidationService';

const store = useCanvasStore();
const appStore = useAppStore();
const runtime = getWebRuntime();
const canServerPersistence = supports(runtime.capabilities.serverPersistence);
const canProjectCatalog = supports(runtime.capabilities.projectCatalog);
const fileInputRef = ref<HTMLInputElement | null>(null);
const isSyncing = ref(false);
const showExportDialog = ref(false);

// 返回首页
const showCloseConfirm = ref(false);
const isClosing = ref(false);

const handleGoHome = async () => {
  if (isClosing.value) return;

  // 检查未保存变更
  if (store.isDirty) {
    showCloseConfirm.value = true;
    return;
  }

  // 无未保存变更，直接关闭
  isClosing.value = true;
  await appStore.closeProject(true);
  isClosing.value = false;
};

const handleCloseConfirm = async (action: 'save' | 'discard' | 'cancel') => {
  showCloseConfirm.value = false;
  if (action === 'cancel') return;

  isClosing.value = true;
  if (action === 'save') {
    const saved = canServerPersistence
      ? await handleSave(`自动存档_${new Date().toISOString().replace(/[-:T]/g, '').slice(0, 15)}`)
      : await handleExportSnapshot();
    if (!saved) {
      isClosing.value = false;
      return;
    }
  }
  await appStore.closeProject(true);
  isClosing.value = false;
};

const notifySyncCheck = (type: 'info' | 'success' | 'warning' | 'error', title: string, message: string) => {
  window.dispatchEvent(new CustomEvent('bimcanvas:agent-notification', {
    detail: {
      type,
      title,
      message,
      timestamp: new Date().toISOString()
    }
  }));
};

const formatDiagnosticItem = (diagnostic: Diagnostic): string => {
  const name = diagnostic.moduleName ? ` (${diagnostic.moduleName})` : '';
  const conflict = diagnostic.conflictType && diagnostic.conflictId
    ? ` -> ${diagnostic.conflictType}:${diagnostic.conflictId}`
    : '';
  return `${diagnostic.moduleId || '?'}${name}${conflict}: ${diagnostic.message}`;
};

const formatDiagnostics = (diagnostics: Diagnostic[], maxItems = 6): string => {
  if (diagnostics.length === 0) return '没有诊断详情。';

  const groups = new Map<string, Diagnostic[]>();
  diagnostics.forEach(diagnostic => {
    const key = diagnostic.code || 'UNKNOWN';
    groups.set(key, [...(groups.get(key) || []), diagnostic]);
  });

  const lines: string[] = [];
  let shownDiagnostics = 0;
  for (const [code, items] of groups) {
    lines.push(`${code} x${items.length}`);
    const remainingSlots = Math.max(0, maxItems - shownDiagnostics);
    items.slice(0, remainingSlots).forEach(item => {
      lines.push(formatDiagnosticItem(item));
      shownDiagnostics += 1;
    });
    if (shownDiagnostics >= maxItems) break;
  }

  const remaining = diagnostics.length - shownDiagnostics;
  if (remaining > 0) {
    lines.push(`还有 ${remaining} 条诊断，详见控制台或接口返回。`);
  }

  return lines.join('；');
};

const notifyReportDiagnostics = (
  titlePrefix: string,
  report: Pick<ModuleNormalizationReport | SchemeValidationReport, 'errorCount' | 'warningCount' | 'diagnostics'>
) => {
  if (report.errorCount <= 0 && report.warningCount <= 0) return;

  const type = report.errorCount > 0 ? 'error' : 'warning';
  const title = report.errorCount > 0
    ? `${titlePrefix}失败`
    : `${titlePrefix}警告`;
  const message = `${report.errorCount} 个错误，${report.warningCount} 个警告。${formatDiagnostics(report.diagnostics)}`;
  notifySyncCheck(type, title, message);
};

const stringifyErrorValue = (value: unknown): string[] => {
  if (value == null) return [];
  if (Array.isArray(value)) {
    return value.flatMap(item => stringifyErrorValue(item));
  }
  if (typeof value === 'object') {
    return Object.entries(value as Record<string, unknown>).flatMap(([key, item]) =>
      stringifyErrorValue(item).map(message => `${key}: ${message}`)
    );
  }
  return [String(value)];
};

const getRequestErrorMessage = (error: any): string => {
  const response = error?.response;
  const data = response?.data;

  if (typeof data === 'string' && data.trim()) {
    return data;
  }

  if (data && typeof data === 'object') {
    const parts = [
      ...stringifyErrorValue(data.message),
      ...stringifyErrorValue(data.detail),
      ...stringifyErrorValue(data.errors)
    ].filter(Boolean);

    if (parts.length > 0) {
      return parts.join('；');
    }

    if (typeof data.title === 'string' && data.title.trim()) {
      return data.title;
    }
  }

  if (response?.status) {
    const statusText = response.statusText ? ` ${response.statusText}` : '';
    return `HTTP ${response.status}${statusText}`;
  }

  return error?.message || String(error);
};

const handleSync = async () => {
  if (isSyncing.value) return;
  isSyncing.value = true;
  try {
    let shouldValidate = true;
    try {
      const normalizeReport = await LayoutValidationService.normalizeModules();
      notifyReportDiagnostics('模块规范化', normalizeReport);
      shouldValidate = normalizeReport.errorCount <= 0;
    } catch (error: any) {
      shouldValidate = false;
      notifySyncCheck('error', '模块规范化失败', getRequestErrorMessage(error));
    }

    if (shouldValidate) {
      try {
        const validationReport = await LayoutValidationService.validateLayout();
        notifyReportDiagnostics('布局验证', validationReport);
      } catch (error: any) {
        notifySyncCheck('error', '布局验证失败', getRequestErrorMessage(error));
      }
    }

    await store.forceSync();
  } finally {
    setTimeout(() => { isSyncing.value = false; }, 600);
  }
};

const { 
  handleLoad, 
  handleExportSnapshot,
  handleExportBcp,
  processFile,
  handleConflictResolve, 
  showConflictDialog, 
  conflictProjectName, 
  conflictExistingPath,
  fileAccept,
  canExportBcp
} = useProjectFile();

// 使用统一的保存逻辑
const { handleSave, canSave, isSaving } = useSave();

// 保存对话框状态
const showSaveDialog = ref(false);

// Wrapper for load to handle fallback
const onHandleLoad = async () => {
  const result = await handleLoad();
  if (result === 'fallback') {
    fileInputRef.value?.click();
  }
};

const onFileSelected = (event: Event) => {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  
  processFile(file);
  input.value = '';
};

// 点击保存按钮时显示对话框
const onSaveClick = () => {
  if (canServerPersistence && canSave.value && !isSaving.value) {
    showSaveDialog.value = true;
  }
};

const onExportClick = async () => {
  if (!store.projectData) return;
  if (canExportBcp) {
    showExportDialog.value = true;
    return;
  }
  await handleExportSnapshot();
};

const onExportFormatSelected = async (format: 'snapshot' | 'bcp') => {
  showExportDialog.value = false;
  if (format === 'snapshot') {
    await handleExportSnapshot();
  } else {
    await handleExportBcp();
  }
};

// 确认保存
const onSaveConfirm = async (commitMessage: string) => {
  showSaveDialog.value = false;
  await handleSave(commitMessage);
};

// 取消保存
const onSaveCancel = () => {
  showSaveDialog.value = false;
};

// 注册 Ctrl+S 快捷键（显示保存对话框）
const handleKeydown = (e: KeyboardEvent) => {
  if ((e.ctrlKey || e.metaKey) && e.key === 's') {
    e.preventDefault();
    if (canServerPersistence) {
      onSaveClick();
    } else {
      void handleExportSnapshot();
    }
  }
};

onMounted(() => {
  window.addEventListener('keydown', handleKeydown);
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});
</script>

<template>
  <div class="top-bar">
    <div class="brand-area">
      <!-- 返回首页按钮 -->
      <GlassButton @click="handleGoHome" :disabled="isClosing" variant="ghost" title="返回首页" class="icon-btn home-btn">
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
          <polyline points="9 22 9 12 15 12 15 22"></polyline>
        </svg>
      </GlassButton>

      <span class="brand-text">BIMCanvas</span>
      <div class="divider"></div>
      
      <!-- File Operations Group -->
      <GlassButton @click="onHandleLoad" variant="ghost" title="Load Data" class="icon-btn">
        <!-- Load/Import Icon (Arrow Down) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
      </GlassButton>
      
      <GlassButton v-if="canServerPersistence" @click="onSaveClick" :disabled="!canSave || isSaving" variant="ghost" title="Save (Ctrl+S)" class="icon-btn">
        <!-- Save Icon -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path>
          <polyline points="17 21 17 13 7 13 7 21"></polyline>
          <polyline points="7 3 7 8 15 8"></polyline>
        </svg>
      </GlassButton>

      <div class="divider"></div>

      <!-- Edit Operations Group -->
      <GlassButton @click="store.undo()" :disabled="!store.canUndo" variant="ghost" title="Undo" class="icon-btn">
        <!-- Undo Icon (Revit-like curved arrow) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M9 14L4 9l5-5"></path>
          <path d="M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5v0a5.5 5.5 0 0 1-5.5 5.5H11"></path>
        </svg>
      </GlassButton>
      <GlassButton @click="store.redo()" :disabled="!store.canRedo" variant="ghost" title="Redo" class="icon-btn">
        <!-- Redo Icon (Revit-like curved arrow) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M15 14l5-5-5-5"></path>
          <path d="M20 9H9.5A5.5 5.5 0 0 0 4 14.5v0A5.5 5.5 0 0 0 9.5 20H13"></path>
        </svg>
      </GlassButton>

      <div class="divider"></div>

      <GlassButton @click="onExportClick" :disabled="!store.projectData" variant="ghost" title="Export" class="icon-btn">
        <!-- Export Icon (Arrow Up) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="17 8 12 3 7 8"></polyline>
          <line x1="12" y1="3" x2="12" y2="15"></line>
        </svg>
      </GlassButton>

      <div class="divider"></div>

      <GlassButton v-if="canServerPersistence" @click="handleSync" :disabled="isSyncing" variant="ghost" title="Sync Data" class="icon-btn">
        <!-- Sync Icon (Refresh Arrows) -->
        <svg :class="{ 'spin-icon': isSyncing }" viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="23 4 23 10 17 10"></polyline>
          <polyline points="1 20 1 14 7 14"></polyline>
          <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"></path>
        </svg>
      </GlassButton>

      <!-- 隐藏的文件输入 -->
      <input
        ref="fileInputRef"
        type="file"
        :accept="fileAccept"
        style="display: none"
        @change="onFileSelected"
      />
    </div>

    <!-- 冲突对话框 -->
    <ConflictDialog
      v-if="canProjectCatalog"
      :visible="showConflictDialog"
      :project-name="conflictProjectName"
      :existing-path="conflictExistingPath"
      @resolve="handleConflictResolve"
    />

    <!-- 保存确认对话框 -->
    <SaveConfirmDialog
      :visible="showSaveDialog && canServerPersistence"
      @confirm="onSaveConfirm"
      @cancel="onSaveCancel"
    />

    <ExportFormatDialog
      :visible="showExportDialog"
      @select="onExportFormatSelected"
      @cancel="showExportDialog = false"
    />

    <!-- 关闭项目确认对话框 -->
    <Teleport to="body">
      <Transition name="close-dialog">
        <div v-if="showCloseConfirm" class="close-dialog-overlay" @click.self="handleCloseConfirm('cancel')">
          <div class="close-dialog">
            <div class="close-dialog-header">
              <svg viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="var(--accent-yellow, #ffcc00)" stroke-width="2">
                <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              <h3>未保存的变更</h3>
            </div>
            <div class="close-dialog-content">
              <p>有未保存的设计变更，是否仍要关闭？</p>
            </div>
            <div class="close-dialog-actions">
              <GlassButton variant="primary" @click="handleCloseConfirm('save')">{{ canServerPersistence ? '保存并关闭' : '导出并关闭' }}</GlassButton>
              <GlassButton variant="danger" @click="handleCloseConfirm('discard')">不保存关闭</GlassButton>
              <GlassButton variant="ghost" @click="handleCloseConfirm('cancel')">取消</GlassButton>
            </div>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped lang="scss">
.top-bar {
  display: flex;
  align-items: center;
  height: 32px;
  padding: 0 var(--spacing-md);
  background: transparent;
  pointer-events: auto;

  .brand-area {
    display: flex;
    align-items: center;
    gap: 2px; /* Reduced from var(--spacing-sm) for compactness */

    .brand-text {
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-lg); /* Reduced from xl */
      color: var(--text-primary);
    }
  }
}

.divider {
  width: 1px;
  height: 14px;
  background: var(--border-strong);
  margin: 0 4px; /* Reduced margin */
}

.icon-btn {
  padding: 2px 4px; /* Reduced padding for compactness */
  font-size: 1.0rem;
  color: var(--text-secondary);

  &:hover {
    color: var(--text-primary);
  }
}

.home-btn {
  margin-right: 4px;
  color: var(--text-secondary);

  &:hover {
    color: var(--accent-blue);
  }
}

.spin-icon {
  animation: spinSync 0.8s linear infinite;
}

@keyframes spinSync {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>

<style scoped>
/* 关闭确认对话框样式 */
.close-dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.close-dialog {
  background: var(--glass-bg-solid);
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  padding: 24px;
  min-width: 380px;
  max-width: 460px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.close-dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.close-dialog-header h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.close-dialog-content p {
  margin: 0 0 8px;
  color: var(--text-secondary);
  font-size: 14px;
  line-height: 1.5;
}

.close-dialog-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  margin-top: 20px;
}

.close-dialog-enter-active,
.close-dialog-leave-active {
  transition: all 0.2s ease;
}

.close-dialog-enter-from,
.close-dialog-leave-to {
  opacity: 0;
}

.close-dialog-enter-from .close-dialog,
.close-dialog-leave-to .close-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.close-dialog-enter-active .close-dialog,
.close-dialog-leave-active .close-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
