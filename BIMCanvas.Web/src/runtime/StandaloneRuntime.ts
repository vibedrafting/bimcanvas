import type { Module, ProjectData } from '../types/canvas';
import type { ModuleLibrary } from '../services/ModuleLibraryService';
import { standaloneCapabilities } from './capabilities';
import type { WebCapabilities, WebRuntime } from './WebRuntimeProtocol';
import { SnapshotReader } from './standalone/SnapshotReader';
import { SnapshotWriter } from './standalone/SnapshotWriter';

export class StandaloneRuntime implements WebRuntime {
  readonly mode = 'standalone' as const;
  readonly capabilities: WebCapabilities = standaloneCapabilities;

  private moduleLibrary: ModuleLibrary | null = null;
  private moduleAssets: Record<string, string> = {};

  async loadInitialProject(): Promise<ProjectData | null> {
    return null;
  }

  async importSnapshot(file: File): Promise<ProjectData> {
    const snapshot = await SnapshotReader.parse(file);
    this.moduleLibrary = snapshot.moduleLibrary;
    this.moduleAssets = snapshot.moduleAssets;
    return snapshot.projectData;
  }

  async closeProject(): Promise<void> {
    this.moduleLibrary = null;
    this.moduleAssets = {};
  }

  async saveModules(_modules: Module[]): Promise<boolean> {
    return true;
  }

  async getModuleLibrary(): Promise<ModuleLibrary | null> {
    return this.moduleLibrary;
  }

  async getModuleAsset(moduleId: string): Promise<string | null> {
    return this.moduleAssets[moduleId] ?? null;
  }

  async exportSnapshot(projectData: ProjectData): Promise<Blob> {
    const moduleAssets = await this.collectModuleAssets(this.moduleLibrary);
    return SnapshotWriter.createBlob({
      runtime: this.mode,
      projectData,
      moduleLibrary: this.moduleLibrary,
      moduleAssets
    });
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
