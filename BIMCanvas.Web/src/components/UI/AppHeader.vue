<script setup lang="ts">
import { ref } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import ConflictDialog from './ConflictDialog.vue';
import { ProjectService } from '../../services/ProjectService';

const store = useCanvasStore();
const fileInputRef = ref<HTMLInputElement | null>(null);

// === 冲突对话框状态 ===
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

// 加载数据（支持 .bcp 和 .json）
const handleLoad = async () => {
  try {
    // 尝试使用 File System Access API
    if ('showOpenFilePicker' in window) {
      const [fileHandle] = await (window as any).showOpenFilePicker({
        types: [
          {
            description: 'BIMCanvas Project',
            accept: { 'application/octet-stream': ['.bcp'] }
          },
          {
            description: 'BIMCanvas JSON (Legacy)',
            accept: { 'application/json': ['.json'] }
          }
        ],
        multiple: false,
        startIn: 'desktop'
      });

      const file = await fileHandle.getFile();
      const fileName = file.name.toLowerCase();

      if (fileName.endsWith('.bcp')) {
        // 通过文件上传 API 打开项目
        const result = await ProjectService.uploadProject(file);

        if (result.status === 'Conflict') {
          // 显示冲突对话框，保存文件以便后续解决冲突
          pendingFile.value = file;
          conflictProjectName.value = result.projectName || '';
          conflictExistingPath.value = result.existingPath || '';
          showConflictDialog.value = true;
        } else if (result.status === 'Success') {
          // 重新加载项目数据
          await store.loadProject();
        } else {
          // 错误
          alert(`打开项目失败：${result.message}`);
        }
      } else {
        // JSON 文件（已废弃）
        console.warn('Direct JSON loading is deprecated in v3.0');
      }
    } else {
      // Fallback
      fileInputRef.value?.click();
    }
  } catch (err: any) {
    if (err.name !== 'AbortError') {
      console.error('Failed to open file:', err);
    }
  }
};

// 处理冲突解决
const handleConflictResolve = async (resolution: 'Overwrite' | 'UseExisting' | 'Cancel') => {
  showConflictDialog.value = false;

  if (resolution === 'Cancel') {
    // 用户取消，不做任何操作
    pendingFile.value = null;
    return;
  }

  if (!pendingFile.value) {
    console.error('No pending file for conflict resolution');
    return;
  }

  try {
    const result = await ProjectService.uploadResolveConflict(pendingFile.value, resolution);

    if (result.status === 'Success') {
      // 重新加载项目数据
      await store.loadProject();
    } else {
      alert(`解决冲突失败：${result.message}`);
    }
  } catch (err: any) {
    console.error('Failed to resolve conflict:', err);
    alert(`解决冲突失败：${err.message}`);
  } finally {
    pendingFile.value = null;
  }
};

const onFileSelected = (event: Event) => {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;

  const reader = new FileReader();
  reader.onload = (_e) => {
    // Note: Direct JSON loading removed in v3.0 - use loadProject with folder path
    console.warn('Direct JSON loading is deprecated in v3.0. Use ?project=path URL parameter.');
  };
  reader.readAsText(file);
  input.value = ''; // 重置以允许重复选择同一文件
};

// 导出数据
const handleExport = async () => {
  if (!store.projectData) return;

  const timestamp = new Date().toISOString()
    .replace(/[-:]/g, '')
    .replace('T', '_')
    .slice(0, 15);
  const filename = `BIMCanvas_${timestamp}.json`;
  const jsonString = JSON.stringify(store.projectData, null, 2);

  try {
    // 尝试使用 File System Access API
    if ('showSaveFilePicker' in window) {
      const handle = await (window as any).showSaveFilePicker({
        suggestedName: filename,
        types: [{
          description: 'BIMCanvas JSON',
          accept: { 'application/json': ['.json'] }
        }],
        startIn: 'desktop' // 默认打开桌面
      });

      const writable = await handle.createWritable();
      await writable.write(jsonString);
      await writable.close();
    } else {
      // Fallback
      const blob = new Blob([jsonString], { type: 'application/json' });
      const url = URL.createObjectURL(blob);

      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      a.click();

      URL.revokeObjectURL(url);
    }
  } catch (err: any) {
    if (err.name !== 'AbortError') {
      console.error('Failed to save file:', err);
    }
  }
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
      <GlassButton @click="handleLoad" variant="ghost" title="Load Data" class="icon-btn">
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
        accept=".json"
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
  /* Glass Header - Standardized */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  /* border-bottom removed to merge with Ribbon */
  pointer-events: auto;
  z-index: 101; /* Ensure it's above other elements */

  .brand-area {
    display: flex;
    align-items: center;
    gap: var(--spacing-sm);

    .brand-text {
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-sm);
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
