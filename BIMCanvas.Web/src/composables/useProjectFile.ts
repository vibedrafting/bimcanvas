import { ref } from 'vue';
import { ChangeSource } from '../types/history';
import { useCanvasStore } from '../stores/canvasStore';
import { ProjectService } from '../services/ProjectService';

// Global state for the conflict dialog (singleton pattern to share state)
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

export function useProjectFile() {
  const store = useCanvasStore();

  // Load Data (only .bcp format)
  const handleLoad = async () => {
    try {
      // Try using File System Access API
      if ('showOpenFilePicker' in window) {
        const [fileHandle] = await (window as any).showOpenFilePicker({
          types: [
            {
              description: 'BIMCanvas Project',
              accept: { 'application/octet-stream': ['.bcp'] }
            }
          ],
          multiple: false,
          startIn: 'desktop'
        });

        const file = await fileHandle.getFile();
        await processFile(file);
      } else {
        // Fallback: Return a trigger for hidden input (handled by component)
        return 'fallback';
      }
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        console.error('Failed to open file:', err);
      }
    }
  };

  // Process the selected file (only .bcp)
  const processFile = async (file: File) => {
    const fileName = file.name.toLowerCase();

    if (!fileName.endsWith('.bcp')) {
      alert('只支持 .bcp 格式的文件');
      return;
    }

    // Upload via API
    const result = await ProjectService.uploadProject(file);

    if (result.status === 'Conflict') {
      // Show conflict dialog
      pendingFile.value = file;
      conflictProjectName.value = result.projectName || '';
      conflictExistingPath.value = result.existingPath || '';
      showConflictDialog.value = true;
    } else if (result.status === 'Success') {
      // Reload project data
      await store.loadProject(ChangeSource.UserUpload);
    } else {
      alert(`Failed to open project: ${result.message}`);
    }
  };

  // Handle Conflict Resolution
  const handleConflictResolve = async (resolution: 'Overwrite' | 'UseExisting' | 'Cancel') => {
    showConflictDialog.value = false;

    if (resolution === 'Cancel') {
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
        await store.loadProject(ChangeSource.SystemRestore);
      } else {
        alert(`Failed to resolve conflict: ${result.message}`);
      }
    } catch (err: any) {
      console.error('Failed to resolve conflict:', err);
      alert(`Failed to resolve conflict: ${err.message}`);
    } finally {
      pendingFile.value = null;
    }
  };

  // Export Data - 导出为 BCP 文件
  const handleExport = async () => {
    if (!store.projectData) return;

    try {
      // 调用 Server API 获取 BCP 文件
      const { blob, filename } = await ProjectService.exportProject();

      // 使用 File System Access API（如果支持）
      if ('showSaveFilePicker' in window) {
        try {
          const handle = await (window as any).showSaveFilePicker({
            suggestedName: filename,
            types: [{
              description: 'BIMCanvas Project',
              accept: { 'application/octet-stream': ['.bcp'] }
            }],
            startIn: 'desktop'
          });

          const writable = await handle.createWritable();
          await writable.write(blob);
          await writable.close();
          return;
        } catch (err: any) {
          if (err.name === 'AbortError') return; // 用户取消
          // 回退到传统下载方式
        }
      }

      // Fallback: 传统下载方式
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      a.click();
      URL.revokeObjectURL(url);
    } catch (err: any) {
      console.error('Failed to export project:', err);
      alert(`导出项目失败: ${err.message}`);
    }
  };

  return {
    handleLoad,
    handleExport,
    processFile,
    handleConflictResolve,
    showConflictDialog,
    conflictProjectName,
    conflictExistingPath
  };
}
