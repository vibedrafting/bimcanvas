import { ref } from 'vue';
import { ChangeSource } from '../types/history';
import { useCanvasStore } from '../stores/canvasStore';
import { useAppStore } from '../stores/appStore';
import { useSystemStore } from '../stores/systemStore';
import { ProjectService, AtlasService, type ProjectLoadResult, type AtlasSceneItem, type AtlasScenesResponse } from '../services/ProjectService';
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { supports } from '../runtime/WebRuntimeProtocol';

// Global state for the conflict dialog (singleton pattern to share state)
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);
const pendingCreateName = ref<string | null>(null);

// 导入流程的健康检查挂起状态：Server 已接收 .bcp，但还没 loadInitialProject —
// 等 RepairDialog (mode=import) 给出 proceed / abort 决策后才继续
interface PendingHealthCheck {
    projectPath: string;
    projectName: string;
    source: ChangeSource;
}
const pendingHealthCheck = ref<PendingHealthCheck | null>(null);

export function useProjectFile() {
  const store = useCanvasStore();
  const appStore = useAppStore();
  const sys = useSystemStore();
  const runtime = getWebRuntime();
  const canImportSnapshot = supports(runtime.capabilities.webSnapshotImport);
  const canExportBcp = supports(runtime.capabilities.bcpExport);
  const fileAccept = canImportSnapshot ? '.json' : '.bcp';

  const pickerType = canImportSnapshot
    ? {
        description: 'BIMCanvas Snapshot',
        accept: { 'application/json': ['.json'] }
      }
    : {
        description: 'BIMCanvas Project',
        accept: { 'application/octet-stream': ['.bcp'] }
      };

  const completeLoad = async (source: ChangeSource) => {
    const loaded = await store.loadInitialProject(source);
    if (loaded) {
      appStore.applyPendingProjectWarning();
    } else {
      appStore.clearPendingProjectWarnings();
    }
  };

  // 上传/冲突解决成功后挂起，让 RepairDialog (mode=import) 决定 proceed/abort。
  // 返回 true 表示已挂起、调用方应立即 return；返回 false 表示没有 projectPath，按原流程继续。
  const suspendForHealthCheck = async (
    result: ProjectLoadResult,
    file: File | null,
    source: ChangeSource
  ): Promise<boolean> => {
    if (!result.projectPath) return false;
    const fallbackName = file ? file.name.replace(/\.bcp$/i, '') : '';
    pendingHealthCheck.value = {
      projectPath: result.projectPath,
      projectName: result.projectName || fallbackName || '未命名项目',
      source
    };
    return true;
  };

  const continueLoadAfterHealthCheck = async () => {
    const pending = pendingHealthCheck.value;
    if (!pending) return;
    pendingHealthCheck.value = null;
    await completeLoad(pending.source);
  };

  const abortLoadAfterHealthCheck = () => {
    pendingHealthCheck.value = null;
    appStore.clearPendingProjectWarnings();
  };

  // Load Data: Connected 读 .bcp，Standalone 读 Snapshot JSON。
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
      if (!fileName.endsWith('.json')) {
        sys.pushToast({
          type: 'warning',
          title: '文件类型不支持',
          message: 'Standalone 模式只支持 Snapshot JSON 文件',
        });
        return;
      }

      const loaded = await store.importSnapshot(file, ChangeSource.UserUpload);
      if (!loaded) {
        sys.pushToast({
          type: 'error',
          title: '导入 Snapshot 失败',
          message: store.error || '未知错误',
        });
      }
      return;
    }

    if (!fileName.endsWith('.bcp')) {
      sys.pushToast({
        type: 'warning',
        title: '文件类型不支持',
        message: '只支持 .bcp 格式的文件',
      });
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
      if (await suspendForHealthCheck(result, file, ChangeSource.UserUpload)) return;
      await completeLoad(ChangeSource.UserUpload);
    } else {
      appStore.clearPendingProjectWarnings();
      sys.pushToast({
        type: 'error',
        title: '打开项目失败',
        message: result.message ?? '未知错误',
      });
    }
  };

  // Handle Conflict Resolution
  const handleConflictResolve = async (resolution: 'Overwrite' | 'UseExisting' | 'Cancel') => {
    showConflictDialog.value = false;

    if (resolution === 'Cancel') {
      pendingFile.value = null;
      pendingCreateName.value = null;
      return;
    }

    // 分支 1：新建空项目的冲突
    if (pendingCreateName.value) {
      const name = pendingCreateName.value;
      try {
        const result = await ProjectService.createResolveConflict(name, resolution);
        if (result.status === 'Success') {
          appStore.stageProjectWarnings(result.warnings);
          if (await suspendForHealthCheck(result, null, ChangeSource.UserCreate)) return;
          await completeLoad(ChangeSource.UserCreate);
        } else {
          appStore.clearPendingProjectWarnings();
          sys.pushToast({
            type: 'error',
            title: '冲突解决失败',
            message: result.message ?? '未知错误',
          });
        }
      } catch (err: any) {
        appStore.clearPendingProjectWarnings();
        console.error('Failed to resolve create conflict:', err);
        sys.pushToast({
          type: 'error',
          title: '冲突解决失败',
          message: err?.message ?? String(err),
        });
      } finally {
        pendingCreateName.value = null;
      }
      return;
    }

    // 分支 2：导入 .bcp 的冲突
    if (!pendingFile.value) {
      console.error('No pending file for conflict resolution');
      return;
    }

    try {
      const result = await ProjectService.uploadResolveConflict(pendingFile.value, resolution);

      if (result.status === 'Success') {
        appStore.stageProjectWarnings(result.warnings);
        if (await suspendForHealthCheck(result, pendingFile.value, ChangeSource.SystemRestore)) return;
        await completeLoad(ChangeSource.SystemRestore);
      } else {
        appStore.clearPendingProjectWarnings();
        sys.pushToast({
          type: 'error',
          title: '冲突解决失败',
          message: result.message ?? '未知错误',
        });
      }
    } catch (err: any) {
      appStore.clearPendingProjectWarnings();
      console.error('Failed to resolve conflict:', err);
      sys.pushToast({
        type: 'error',
        title: '冲突解决失败',
        message: err?.message ?? String(err),
      });
    } finally {
      pendingFile.value = null;
    }
  };

  // 新建空白项目（Connected 模式）
  const handleCreate = async (name: string) => {
    const trimmed = name.trim();
    if (!trimmed) {
      sys.pushToast({
        type: 'warning',
        title: '项目名不能为空',
        message: '请输入有效的项目名称',
      });
      return;
    }

    const result = await ProjectService.createProject(trimmed);

    if (result.status === 'Conflict') {
      appStore.clearPendingProjectWarnings();
      pendingCreateName.value = trimmed;
      conflictProjectName.value = result.projectName || trimmed;
      conflictExistingPath.value = result.existingPath || '';
      showConflictDialog.value = true;
    } else if (result.status === 'Success') {
      appStore.stageProjectWarnings(result.warnings);
      if (await suspendForHealthCheck(result, null, ChangeSource.UserCreate)) return;
      await completeLoad(ChangeSource.UserCreate);
    } else {
      appStore.clearPendingProjectWarnings();
      sys.pushToast({
        type: 'error',
        title: '新建项目失败',
        message: result.message ?? '未知错误',
      });
    }
  };

  const saveBlobToDisk = async (blob: Blob, filename: string, fileKind: 'snapshot' | 'bcp'): Promise<boolean> => {
    if ('showSaveFilePicker' in window) {
      try {
        const handle = await (window as any).showSaveFilePicker({
          suggestedName: filename,
          types: [{
            description: fileKind === 'snapshot' ? 'BIMCanvas Snapshot' : 'BIMCanvas Project',
            accept: fileKind === 'snapshot'
              ? { 'application/json': ['.json'] }
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

  // Export Data - 两个 Runtime 都导出 Snapshot JSON。
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
      sys.pushToast({
        type: 'error',
        title: '导出 Snapshot 失败',
        message: err?.message ?? String(err),
      });
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
      sys.pushToast({
        type: 'error',
        title: '导出 .bcp 失败',
        message: err?.message ?? String(err),
      });
      return false;
    }
  };

  // 从 Atlas 场景创建项目
  const handleCreateFromAtlasScene = async (sceneId: string, projectName: string) => {
    const trimmed = projectName.trim();
    if (!trimmed) {
      sys.pushToast({
        type: 'warning',
        title: '项目名不能为空',
        message: '请输入有效的项目名称',
      });
      return;
    }

    const result = await AtlasService.createProjectFromScene(sceneId, trimmed);

    if (result.status === 'Conflict') {
      appStore.clearPendingProjectWarnings();
      pendingCreateName.value = trimmed;
      conflictProjectName.value = result.projectName || trimmed;
      conflictExistingPath.value = result.existingPath || '';
      showConflictDialog.value = true;
    } else if (result.status === 'Success') {
      appStore.stageProjectWarnings(result.warnings);
      if (await suspendForHealthCheck(result, null, ChangeSource.UserCreate)) return;
      await completeLoad(ChangeSource.UserCreate);
    } else {
      appStore.clearPendingProjectWarnings();
      sys.pushToast({
        type: 'error',
        title: '从场景创建项目失败',
        message: result.message ?? '未知错误',
      });
    }
  };

  return {
    handleLoad,
    handleCreate,
    handleCreateFromAtlasScene,
    handleExport,
    handleExportSnapshot,
    handleExportBcp,
    processFile,
    handleConflictResolve,
    showConflictDialog,
    conflictProjectName,
    conflictExistingPath,
    fileAccept,
    canExportBcp,
    pendingHealthCheck,
    continueLoadAfterHealthCheck,
    abortLoadAfterHealthCheck
  };
}
