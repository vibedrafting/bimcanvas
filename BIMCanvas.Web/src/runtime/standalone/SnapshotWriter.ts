import type { ProjectData } from '../../types/canvas';
import type { ModuleLibrary } from '../../services/ModuleLibraryService';
import type { WebRuntimeMode, WebSnapshot } from '../WebRuntimeProtocol';

export class SnapshotWriter {
  static createBlob(input: {
    runtime: WebRuntimeMode;
    projectData: ProjectData;
    moduleLibrary?: ModuleLibrary | null;
    moduleAssets?: Record<string, string>;
  }): Blob {
    const snapshot: WebSnapshot = {
      kind: 'bimcanvas.web.snapshot',
      version: 1,
      exportedAt: new Date().toISOString(),
      source: {
        runtime: input.runtime,
        projectId: input.projectData.project?.id,
        projectName: input.projectData.project?.name
      },
      projectData: input.projectData,
      moduleLibrary: input.moduleLibrary ?? undefined,
      moduleAssets: input.moduleAssets
    };

    return new Blob([JSON.stringify(snapshot, null, 2)], {
      type: 'application/json'
    });
  }
}
