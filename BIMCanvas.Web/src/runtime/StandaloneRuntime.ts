import type { Module, ProjectData } from '../types/canvas';
import type { ModuleLibrary, ModuleDefinition } from '../services/ModuleLibraryService';
import { standaloneCapabilities } from './capabilities';
import type { WebCapabilities, WebRuntime } from './WebRuntimeProtocol';
import { SnapshotReader } from './standalone/SnapshotReader';
import { SnapshotWriter } from './standalone/SnapshotWriter';
import { ModuleLibraryDirHandleStore } from './standalone/ModuleLibraryDirHandleStore';
import {
  ensureReadPermission,
  loadAssetText,
  loadLibraryJson,
  pickModuleLibraryDirectory,
  queryDirPermission
} from './standalone/ModuleLibraryDirReader';

export class StandaloneRuntime implements WebRuntime {
  readonly mode = 'standalone' as const;
  readonly capabilities: WebCapabilities = standaloneCapabilities;

  // 临时层:由 importSnapshot 写入,closeProject 清空,优先级高于绑定层
  private snapshotLibrary: ModuleLibrary | null = null;
  private snapshotAssets: Record<string, string> = {};

  // 绑定层:跨会话持久化的磁盘目录;启动时从 IDB 异步复活
  private boundDirHandle: FileSystemDirectoryHandle | null = null;
  // 上次绑定过、当前权限未 granted 的句柄(等待用户手势重新授权)
  private pendingDirHandle: FileSystemDirectoryHandle | null = null;
  private boundLibrary: ModuleLibrary | null = null;
  private boundAssetCache = new Map<string, string>();
  private hydrationPromise: Promise<void>;

  constructor() {
    this.hydrationPromise = this.hydrateBoundDirHandle();
  }

  private async hydrateBoundDirHandle(): Promise<void> {
    const handle = await ModuleLibraryDirHandleStore.load();
    if (!handle) return;
    const perm = await queryDirPermission(handle);
    if (perm === 'granted') {
      this.boundDirHandle = handle;
    } else if (perm === 'prompt') {
      // 等用户手势 (bindModuleLibraryFolder) 时再 requestPermission
      this.pendingDirHandle = handle;
    } else {
      // denied: 句柄已失效,清掉
      await ModuleLibraryDirHandleStore.clear();
    }
  }

  async loadInitialProject(): Promise<ProjectData | null> {
    return null;
  }

  async importSnapshot(file: File): Promise<ProjectData> {
    const snapshot = await SnapshotReader.parse(file);
    this.snapshotLibrary = snapshot.moduleLibrary;
    this.snapshotAssets = snapshot.moduleAssets;
    return snapshot.projectData;
  }

  async closeProject(): Promise<void> {
    this.snapshotLibrary = null;
    this.snapshotAssets = {};
    // 绑定层不随项目关闭而清,保持跨项目可用
  }

  async saveModules(_modules: Module[]): Promise<boolean> {
    return true;
  }

  async getModuleLibrary(): Promise<ModuleLibrary | null> {
    if (this.snapshotLibrary) return this.snapshotLibrary;
    await this.hydrationPromise;
    if (!this.boundDirHandle) return null;
    if (this.boundLibrary) return this.boundLibrary;
    try {
      this.boundLibrary = await loadLibraryJson(this.boundDirHandle);
      return this.boundLibrary;
    } catch (err) {
      console.warn('[StandaloneRuntime] 绑定目录的 module_library.json 加载失败', err);
      return null;
    }
  }

  async getModuleAsset(moduleId: string): Promise<string | null> {
    const fromSnapshot = this.snapshotAssets[moduleId];
    if (fromSnapshot) return fromSnapshot;

    await this.hydrationPromise;
    if (!this.boundDirHandle) return null;

    const cached = this.boundAssetCache.get(moduleId);
    if (cached) return cached;

    const library = await this.getModuleLibrary();
    if (!library) return null;
    const def = library.modules.find((m: ModuleDefinition) => m.id === moduleId);
    if (!def?.svgPath) return null;

    const text = await loadAssetText(this.boundDirHandle, def.svgPath);
    if (text) this.boundAssetCache.set(moduleId, text);
    return text;
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

  async exportBcpProject(): Promise<{ blob: Blob; filename: string } | null> {
    return null;
  }

  async bindModuleLibraryFolder(): Promise<{ count: number }> {
    await this.hydrationPromise;

    // 1) 优先复活上次的 pending 句柄 (跳过 picker)
    if (this.pendingDirHandle) {
      const perm = await ensureReadPermission(this.pendingDirHandle, true);
      if (perm === 'granted') {
        const handle = this.pendingDirHandle;
        this.pendingDirHandle = null;
        return await this.adoptHandle(handle);
      }
      // 用户拒绝或仍 prompt → 清掉 pending,走 picker 重新选
      this.pendingDirHandle = null;
      await ModuleLibraryDirHandleStore.clear();
    }

    // 2) 弹 picker 让用户选新目录
    const handle = await pickModuleLibraryDirectory();
    return await this.adoptHandle(handle);
  }

  private async adoptHandle(handle: FileSystemDirectoryHandle): Promise<{ count: number }> {
    // 校验:能成功解析 module_library.json 才算合法
    const library = await loadLibraryJson(handle);

    this.boundDirHandle = handle;
    this.boundLibrary = library;
    this.boundAssetCache.clear();
    await ModuleLibraryDirHandleStore.save(handle);

    return { count: library.modules.length };
  }

  async clearModuleLibraryBinding(): Promise<void> {
    this.boundDirHandle = null;
    this.pendingDirHandle = null;
    this.boundLibrary = null;
    this.boundAssetCache.clear();
    await ModuleLibraryDirHandleStore.clear();
  }

  private async collectModuleAssets(moduleLibrary: ModuleLibrary | null): Promise<Record<string, string> | undefined> {
    if (!moduleLibrary) return undefined;
    const moduleAssets: Record<string, string> = {};
    for (const mod of moduleLibrary.modules) {
      const svgText = await this.getModuleAsset(mod.id);
      if (svgText) moduleAssets[mod.id] = svgText;
    }
    return moduleAssets;
  }
}
