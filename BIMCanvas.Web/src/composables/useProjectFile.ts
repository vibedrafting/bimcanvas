import { ref } from 'vue';
import { ChangeSource } from '../types/history';
import { useCanvasStore } from '../stores/canvasStore';
import { ProjectService } from '../services/ProjectService';
import { saveDirectoryHandle, loadDirectoryHandle } from '../utils/fileHandleStore';

const IMPORT_DIR_KEY = 'bcp-import-dir';

// Global state for the conflict dialog (singleton pattern to share state)
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

// 是否需要用户首次设置默认导入目录
const needsDirectorySetup = ref(false);
const demosPathHint = ref<string | null>(null);

export function useProjectFile() {
  const store = useCanvasStore();

  /**
   * 获取已存储的导入目录 handle，用作 showOpenFilePicker 的 startIn
   */
  const getImportStartIn = async (): Promise<FileSystemDirectoryHandle | 'desktop'> => {
    try {
      const handle = await loadDirectoryHandle(IMPORT_DIR_KEY);
      if (handle) {
        // 验证权限是否仍有效
        const permission = await (handle as any).queryPermission({ mode: 'read' });
        if (permission === 'granted') return handle;
      }
    } catch {
      // IndexedDB 或权限检查失败，降级
    }
    return 'desktop';
  };

  /**
   * 让用户选择默认导入目录并存储
   * @returns 选中的目录 handle，或 null（用户取消）
   */
  const setupImportDirectory = async (): Promise<FileSystemDirectoryHandle | null> => {
    if (!('showDirectoryPicker' in window)) return null;

    try {
      // 获取 Server 端 demos 路径作为提示
      const demosPath = await ProjectService.getDemosPath();
      if (demosPath) {
        demosPathHint.value = demosPath;
      }

      const dirHandle = await (window as any).showDirectoryPicker({
        id: 'bcp-import-dir',
        mode: 'read'
      });

      await saveDirectoryHandle(IMPORT_DIR_KEY, dirHandle);
      return dirHandle;
    } catch (err: any) {
      if (err.name !== 'AbortError') {
        console.error('Failed to select import directory:', err);
      }
      return null;
    }
  };

  // Load Data (only .bcp format)
  const handleLoad = async () => {
    try {
      // Try using File System Access API
      if ('showOpenFilePicker' in window) {
        let startIn = await getImportStartIn();

        // 首次使用：没有存储的目录，先让用户选择默认目录
        if (startIn === 'desktop') {
          const dirHandle = await setupImportDirectory();
          if (dirHandle) {
            startIn = dirHandle;
          }
          // 用户取消目录选择也继续，用 desktop 降级
        }

        const [fileHandle] = await (window as any).showOpenFilePicker({
          types: [
            {
              description: 'BIMCanvas Project',
              accept: { 'application/octet-stream': ['.bcp'] }
            }
          ],
          multiple: false,
          startIn
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
    conflictExistingPath,
    needsDirectorySetup,
    demosPathHint
  };
}
