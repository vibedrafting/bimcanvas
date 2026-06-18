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
  projectCreation: CapabilityEntry;
  serverPersistence: CapabilityEntry;
  bcpExport: CapabilityEntry;
  webSnapshotImport: CapabilityEntry;
  webSnapshotExport: CapabilityEntry;
  moduleLibrary: CapabilityEntry;
  /** 用户主动绑定/清空磁盘上的模块库目录 (仅 Standalone + Chromium) */
  moduleLibraryBinding: CapabilityEntry;
  inMemoryEdit: CapabilityEntry;
  undoRedo: CapabilityEntry;
  realtimeProjectSync: CapabilityEntry;
  gitBranching: CapabilityEntry;
  worktreeReview: CapabilityEntry;
  agentChat: CapabilityEntry;
  runtimeSettings: CapabilityEntry;
  /** AI 效果图生成面板（需要 envision plugin 激活） */
  envisionPanel: CapabilityEntry;
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
  /**
   * 保存模块到持久层。
   * @param variantSelection 可选：设计区 id → 方案 slug 的映射。命中的设计区由服务端按指针模型
   *   写入该方案路径 schemes/{dz}/{slug}/[{leaf}/]modules.json；未命中走 canonical（父 adopted 指向）。缺省/空 = 全 canonical。
   * @param scope 可选：写入范围（设计区 id 列表）。给定时只写范围内设计区，范围外文件不碰（定向落盘）。缺省 = 全工程。
   */
  saveModules(modules: Module[], variantSelection?: Record<string, string>, scope?: string[]): Promise<boolean>;
  getModuleLibrary(): Promise<ModuleLibrary | null>;
  getModuleAsset(moduleId: string): Promise<string | null>;
  exportSnapshot(projectData: ProjectData): Promise<Blob>;
  exportBcpProject(): Promise<{ blob: Blob; filename: string } | null>;
  /**
   * 在 Standalone 下让用户用 showDirectoryPicker 选一个磁盘目录,
   * 校验并绑定为模块库;句柄持久化到 IDB,下次会话可恢复。
   * 返回加载到的模块数量。失败抛友好错误。
   */
  bindModuleLibraryFolder(): Promise<{ count: number }>;
  /** 解绑当前模块库目录,清空 IDB 中的句柄与内存缓存。 */
  clearModuleLibraryBinding(): Promise<void>;
}

export const supports = (cap: CapabilityEntry) => cap.level !== 'unsupported';

export const WebRuntimeKey: InjectionKey<WebRuntime> = Symbol('WebRuntime');
