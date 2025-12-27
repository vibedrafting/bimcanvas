import { ref } from 'vue';
import { useCanvasStore } from '../stores/canvasStore';
import { ProjectService } from '../services/ProjectService';

// Global state for the conflict dialog (singleton pattern to share state)
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

export function useProjectFile() {
  const store = useCanvasStore();

  // Load Data (supports .bcp and .json)
  const handleLoad = async () => {
    try {
      // Try using File System Access API
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

  // Process the selected file
  const processFile = async (file: File) => {
    const fileName = file.name.toLowerCase();

    if (fileName.endsWith('.bcp')) {
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
        await store.loadProject();
      } else {
        alert(`Failed to open project: ${result.message}`);
      }
    } else {
      console.warn('Direct JSON loading is deprecated in v3.0');
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
        await store.loadProject();
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

  // Export Data
  const handleExport = async () => {
    if (!store.projectData) return;

    const timestamp = new Date().toISOString()
      .replace(/[-:]/g, '')
      .replace('T', '_')
      .slice(0, 15);
    const filename = `BIMCanvas_${timestamp}.json`;
    const jsonString = JSON.stringify(store.projectData, null, 2);

    try {
      if ('showSaveFilePicker' in window) {
        const handle = await (window as any).showSaveFilePicker({
          suggestedName: filename,
          types: [{
            description: 'BIMCanvas JSON',
            accept: { 'application/json': ['.json'] }
          }],
          startIn: 'desktop'
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
