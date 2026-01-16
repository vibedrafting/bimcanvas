<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import ConflictDialog from './ConflictDialog.vue';
import SaveConfirmDialog from './SaveConfirmDialog.vue';
import { useProjectFile } from '../../composables/useProjectFile';
import { useSave } from '../../composables/useSave';

const store = useCanvasStore();
const fileInputRef = ref<HTMLInputElement | null>(null);

const { 
  handleLoad, 
  handleExport, 
  processFile,
  handleConflictResolve, 
  showConflictDialog, 
  conflictProjectName, 
  conflictExistingPath 
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
  if (canSave.value && !isSaving.value) {
    showSaveDialog.value = true;
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
    onSaveClick();
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
      
      <GlassButton @click="onSaveClick" :disabled="!canSave || isSaving" variant="ghost" title="Save (Ctrl+S)" class="icon-btn">
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

      <GlassButton @click="handleExport" :disabled="!store.projectData" variant="ghost" title="Export Data" class="icon-btn">
        <!-- Export Icon (Arrow Up) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="17 8 12 3 7 8"></polyline>
          <line x1="12" y1="3" x2="12" y2="15"></line>
        </svg>
      </GlassButton>

      <!-- 隐藏的文件输入 -->
      <input
        ref="fileInputRef"
        type="file"
        accept=".bcp"
        style="display: none"
        @change="onFileSelected"
      />
    </div>

    <!-- 冲突对话框 -->
    <ConflictDialog
      :visible="showConflictDialog"
      :project-name="conflictProjectName"
      :existing-path="conflictExistingPath"
      @resolve="handleConflictResolve"
    />

    <!-- 保存确认对话框 -->
    <SaveConfirmDialog
      :visible="showSaveDialog"
      @confirm="onSaveConfirm"
      @cancel="onSaveCancel"
    />
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
</style>
