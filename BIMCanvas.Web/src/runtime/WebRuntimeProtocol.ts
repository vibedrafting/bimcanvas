import type { InjectionKey } from 'vue';
import type { Module, ProjectData } from '../types/canvas';
import type { ModuleLibrary } from '../services/ModuleLibraryService';

export type WebRuntimeMode = 'connected' | 'standalone';

export interface CapabilityEntry {
  level: 'supported' | 'optional' | 'unsupported';
  frontendFallback?: string;
}

export interface WebCapabilities {
  projectCatalog: CapabilityEntry;
  serverPersistence: CapabilityEntry;
  bcpExport: CapabilityEntry;
  webSnapshotImport: CapabilityEntry;
  webSnapshotExport: CapabilityEntry;
  moduleLibrary: CapabilityEntry;
  inMemoryEdit: CapabilityEntry;
  undoRedo: CapabilityEntry;
  realtimeProjectSync: CapabilityEntry;
  gitBranching: CapabilityEntry;
  worktreeReview: CapabilityEntry;
  agentChat: CapabilityEntry;
  runtimeSettings: CapabilityEntry;
}

export interface WebSnapshot {
  kind: 'bimcanvas.web.snapshot';
  version: 1;
  exportedAt: string;
  source?: {
    runtime: WebRuntimeMode;
    projectId?: string;
    projectName?: string;
  };
  projectData: ProjectData;
  moduleLibrary?: ModuleLibrary;
  moduleAssets?: Record<string, string>;
}

export interface WebRuntime {
  readonly mode: WebRuntimeMode;
  readonly capabilities: WebCapabilities;

  loadInitialProject(): Promise<ProjectData | null>;
  importSnapshot(file: File): Promise<ProjectData>;
  closeProject(): Promise<void>;
  saveModules(modules: Module[]): Promise<boolean>;
  getModuleLibrary(): Promise<ModuleLibrary | null>;
  getModuleAsset(moduleId: string): Promise<string | null>;
  exportSnapshot(projectData: ProjectData): Promise<Blob>;
  exportBcpProject(): Promise<{ blob: Blob; filename: string } | null>;
}

export const supports = (cap: CapabilityEntry) => cap.level !== 'unsupported';

export const WebRuntimeKey: InjectionKey<WebRuntime> = Symbol('WebRuntime');
