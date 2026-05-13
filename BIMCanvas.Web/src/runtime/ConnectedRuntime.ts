import type { Module, ProjectData } from '../types/canvas';
import type { ModuleLibrary } from '../services/ModuleLibraryService';
import { SERVER_API } from '../config/api';
import { ProjectService } from '../services/ProjectService';
import { SignalRService } from '../services/SignalRService';
import { connectedCapabilities } from './capabilities';
import type { WebCapabilities, WebRuntime } from './WebRuntimeProtocol';
import { SnapshotWriter } from './standalone/SnapshotWriter';

export class ConnectedRuntime implements WebRuntime {
  readonly mode = 'connected' as const;
  readonly capabilities: WebCapabilities = connectedCapabilities;

  private readonly signalR = SignalRService.getInstance();
  private moduleLibrary: ModuleLibrary | null = null;
  private moduleLibraryLoadPromise: Promise<ModuleLibrary | null> | null = null;
  private readonly moduleAssets = new Map<string, string>();

  private readonly handleLocalUpdate = (event: Event) => {
    const detail = (event as CustomEvent).detail;
    if (detail) {
      void this.signalR.sendUpdate(detail);
    }
  };

  constructor() {
    void this.signalR.start();
    window.addEventListener('bimcanvas:local-update', this.handleLocalUpdate);
  }

  async loadInitialProject(): Promise<ProjectData | null> {
    const status = await ProjectService.getStatus();
    if (!status.isLoaded) {
      return null;
    }

    const response = await fetch(`${SERVER_API}/project`);
    if (!response.ok) {
      throw new Error(`Failed to load project: ${response.statusText}`);
    }
    this.moduleLibrary = null;
    this.moduleLibraryLoadPromise = null;
    this.moduleAssets.clear();
    return await response.json() as ProjectData;
  }

  async importSnapshot(_file: File): Promise<ProjectData> {
    throw new Error('Connected Runtime 不支持直接导入 WebSnapshot');
  }

  async closeProject(): Promise<void> {
    await ProjectService.closeProject(true);
    this.moduleLibrary = null;
    this.moduleLibraryLoadPromise = null;
    this.moduleAssets.clear();
  }

  async saveModules(modules: Module[], variantSelection?: Record<string, string>): Promise<boolean> {
    const response = await fetch(`${SERVER_API}/project/save`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ modules, variantSelection: variantSelection ?? {} })
    });
    return response.ok;
  }

  async getModuleLibrary(): Promise<ModuleLibrary | null> {
    if (this.moduleLibrary) {
      return this.moduleLibrary;
    }

    if (this.moduleLibraryLoadPromise) {
      return this.moduleLibraryLoadPromise;
    }

    this.moduleLibraryLoadPromise = this.fetchModuleLibrary();
    try {
      this.moduleLibrary = await this.moduleLibraryLoadPromise;
      return this.moduleLibrary;
    } finally {
      this.moduleLibraryLoadPromise = null;
    }
  }

  async getModuleAsset(moduleId: string): Promise<string | null> {
    const cached = this.moduleAssets.get(moduleId);
    if (cached) {
      return cached;
    }

    const response = await fetch(`${SERVER_API}/modules/svg/${encodeURIComponent(moduleId)}`);
    if (!response.ok) {
      return null;
    }

    const svgText = await response.text();
    this.moduleAssets.set(moduleId, svgText);
    return svgText;
  }

  async exportSnapshot(projectData: ProjectData): Promise<Blob> {
    const moduleLibrary = await this.getModuleLibrary();
    const moduleAssets = await this.collectModuleAssets(moduleLibrary);
    return SnapshotWriter.createBlob({
      runtime: this.mode,
      projectData,
      moduleLibrary,
      moduleAssets
    });
  }

  async exportBcpProject(): Promise<{ blob: Blob; filename: string }> {
    return ProjectService.exportProject();
  }

  async bindModuleLibraryFolder(): Promise<{ count: number }> {
    throw new Error('Connected Runtime 不支持绑定模块库目录;模块库由 Server 提供');
  }

  async clearModuleLibraryBinding(): Promise<void> {
    throw new Error('Connected Runtime 不支持清空模块库绑定;模块库由 Server 提供');
  }

  private async fetchModuleLibrary(): Promise<ModuleLibrary | null> {
    const response = await fetch(`${SERVER_API}/modules/library`);
    if (response.status === 404) {
      return null;
    }
    if (!response.ok) {
      throw new Error(`Failed to load module library: ${response.statusText}`);
    }
    return await response.json() as ModuleLibrary;
  }

  private async collectModuleAssets(moduleLibrary: ModuleLibrary | null): Promise<Record<string, string> | undefined> {
    if (!moduleLibrary) {
      return undefined;
    }

    const moduleAssets: Record<string, string> = {};
    for (const mod of moduleLibrary.modules) {
      const svgText = await this.getModuleAsset(mod.id);
      if (svgText) {
        moduleAssets[mod.id] = svgText;
      }
    }
    return moduleAssets;
  }
}
