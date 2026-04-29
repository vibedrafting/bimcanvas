import { ref } from 'vue';
import { ChangeSource } from '../types/history';
import { useCanvasStore } from '../stores/canvasStore';
import { useAppStore } from '../stores/appStore';
import { ProjectService } from '../services/ProjectService';
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { supports } from '../runtime/WebRuntimeProtocol';

// Global state for the conflict dialog (singleton pattern to share state)
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

export function useProjectFile() {
  const store = useCanvasStore();
  const appStore = useAppStore();
  const runtime = getWebRuntime();
  const canImportSnapshot = supports(runtime.capabilities.webSnapshotImport);
  const canExportBcp = supports(runtime.capabilities.bcpExport);
  const fileAccept = canImportSnapshot ? '.bcweb.json' : '.bcp';

  const pickerType = canImportSnapshot
    ? {
        description: 'BIMCanvas Web Snapshot',
        accept: { 'application/json': ['.bcweb.json'] }
      }
    : {
        description: 'BIMCanvas Project',
        accept: { 'application/octet-stream': ['.bcp'] }
      };

  // Load Data: Connected 读 .bcp，Standalone 读 .bcweb.json。
  const handleLoad = async () => {
    try {
      // Try using File System Access API
      if ('showOpenFilePicker' in window) {
        const [fileHandle] = await (window as any).showOpenFilePicker({
          types: [pickerType],
          multiple: false,
          id: canImportSnapshot ? 'bcweb-import' : 'bcp-import',
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

  // Process the selected file.
  const processFile = async (file: File) => {
    const fileName = file.name.toLowerCase();

    if (canImportSnapshot) {
      if (!fileName.endsWith('.bcweb.json')) {
        alert('Standalone 模式只支持 .bcweb.json Snapshot 文件');
        return;
      }

      const loaded = await store.importSnapshot(file, ChangeSource.UserUpload);
      if (!loaded) {
        alert(store.error || '导入 Snapshot 失败');
      }
      return;
    }

    if (!fileName.endsWith('.bcp')) {
      alert('只支持 .bcp 格式的文件');
      return;
    }

    // Upload via API
    const result = await ProjectService.uploadProject(file);

    if (result.status === 'Conflict') {
      appStore.clearPendingProjectWarnings();
      // Show conflict dialog
      pendingFile.value = file;
      conflictProjectName.value = result.projectName || '';
      conflictExistingPath.value = result.existingPath || '';
      showConflictDialog.value = true;
    } else if (result.status === 'Success') {
      appStore.stageProjectWarnings(result.warnings);
      // Reload project data
      const loaded = await store.loadInitialProject(ChangeSource.UserUpload);
      if (loaded) {
        appStore.applyPendingProjectWarning();
      } else {
        appStore.clearPendingProjectWarnings();
      }
    } else {
      appStore.clearPendingProjectWarnings();
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
        appStore.stageProjectWarnings(result.warnings);
        const loaded = await store.loadInitialProject(ChangeSource.SystemRestore);
        if (loaded) {
          appStore.applyPendingProjectWarning();
        } else {
          appStore.clearPendingProjectWarnings();
        }
      } else {
        appStore.clearPendingProjectWarnings();
        alert(`Failed to resolve conflict: ${result.message}`);
      }
    } catch (err: any) {
      appStore.clearPendingProjectWarnings();
      console.error('Failed to resolve conflict:', err);
      alert(`Failed to resolve conflict: ${err.message}`);
    } finally {
      pendingFile.value = null;
    }
  };

  const saveBlobToDisk = async (blob: Blob, filename: string, fileKind: 'snapshot' | 'bcp'): Promise<boolean> => {
    if ('showSaveFilePicker' in window) {
      try {
        const handle = await (window as any).showSaveFilePicker({
          suggestedName: filename,
          types: [{
            description: fileKind === 'snapshot' ? 'BIMCanvas Web Snapshot' : 'BIMCanvas Project',
            accept: fileKind === 'snapshot'
              ? { 'application/json': ['.bcweb.json'] }
              : { 'application/octet-stream': ['.bcp'] }
          }],
          startIn: 'desktop'
        });

        const writable = await handle.createWritable();
        await writable.write(blob);
        await writable.close();
        return true;
      } catch (err: any) {
        if (err.name === 'AbortError') return false;
      }
    }

    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    a.click();
    URL.revokeObjectURL(url);
    return true;
  };

  // Export Data - 两个 Runtime 都导出 .bcweb.json Snapshot。
  const handleExport = async (): Promise<boolean> => {
    return handleExportSnapshot();
  };

  const handleExportSnapshot = async (): Promise<boolean> => {
    if (!store.projectData) return false;

    try {
      const snapshot = await store.exportSnapshot();
      if (!snapshot) return false;

      const saved = await saveBlobToDisk(snapshot.blob, snapshot.filename, 'snapshot');
      if (saved) {
        store.clearDirty();
      }
      return saved;
    } catch (err: any) {
      console.error('Failed to export project:', err);
      alert(`导出 Snapshot 失败: ${err.message}`);
      return false;
    }
  };

  const handleExportBcp = async (): Promise<boolean> => {
    if (!store.projectData || !canExportBcp) return false;

    try {
      const project = await store.exportBcpProject();
      if (!project) return false;

      return saveBlobToDisk(project.blob, project.filename, 'bcp');
    } catch (err: any) {
      console.error('Failed to export BCP project:', err);
      alert(`导出 .bcp 失败: ${err.message}`);
      return false;
    }
  };

  return {
    handleLoad,
    handleExport,
    handleExportSnapshot,
    handleExportBcp,
    processFile,
    handleConflictResolve,
    showConflictDialog,
    conflictProjectName,
    conflictExistingPath,
    fileAccept,
    canExportBcp
  };
}
