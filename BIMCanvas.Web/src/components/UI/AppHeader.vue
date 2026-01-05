<script setup lang="ts">
import { ref } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import ConflictDialog from './ConflictDialog.vue';
import { useProjectFile } from '../../composables/useProjectFile';

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
</script>

<template>
  <div class="top-bar">
    <div class="brand-area">
      <span class="brand-text">BIMCanvas</span>
      <div class="divider"></div>
      <GlassButton @click="store.undo()" :disabled="!store.canUndo" variant="ghost" title="Undo" class="icon-btn">
        ↩
      </GlassButton>
      <GlassButton @click="store.redo()" :disabled="!store.canRedo" variant="ghost" title="Redo" class="icon-btn">
        ↪
      </GlassButton>
      <div class="divider"></div>
      <GlassButton @click="onHandleLoad" variant="ghost" title="Load Data" class="icon-btn">
        <!-- Load/Import Icon (Arrow Down) -->
        <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
      </GlassButton>
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
  </div>
</template>

<style scoped lang="scss">
.top-bar {
  display: flex;
  align-items: center;
  height: 32px; /* Updated to 32px per plan */
  padding: 0 var(--spacing-md);
  /* Glass Header - Removed (Moved to MainLayout wrapper) */
  background: transparent;
  /* border-bottom removed to merge with Ribbon */
  pointer-events: auto;
  /* z-index removed, handled by wrapper */

  .brand-area {
    display: flex;
    align-items: center;
    gap: var(--spacing-sm);

    .brand-text {
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-xl); /* Increased spacing */
      color: var(--text-primary);
    }
  }
}

.divider {
  width: 1px;
  height: 14px;
  background: var(--border-strong);
  margin: 0 var(--spacing-xs);
}

.icon-btn {
  padding: 2px 6px; /* Adjusted for 32px height */
  font-size: 1.0rem;
}
</style>
