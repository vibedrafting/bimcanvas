import type { CapabilityEntry, WebCapabilities } from './WebRuntimeProtocol';
import { isDirectoryPickerSupported } from './standalone/ModuleLibraryDirReader';

const supported: CapabilityEntry = { level: 'supported' };
const unsupported: CapabilityEntry = { level: 'unsupported' };

export const connectedCapabilities: WebCapabilities = {
  projectCatalog: supported,
  projectCreation: unsupported,
  serverPersistence: supported,
  bcpExport: supported,
  webSnapshotImport: unsupported,
  webSnapshotExport: supported,
  moduleLibrary: supported,
  moduleLibraryBinding: unsupported,
  inMemoryEdit: supported,
  undoRedo: supported,
  realtimeProjectSync: supported,
  gitBranching: supported,
  worktreeReview: supported,
  agentChat: supported,
  runtimeSettings: supported
};

const moduleLibraryBindingCapability: CapabilityEntry = isDirectoryPickerSupported()
  ? supported
  : {
      level: 'unsupported',
      frontendFallback: '当前浏览器不支持目录选择 · 请使用 Chrome / Edge'
    };

export const standaloneCapabilities: WebCapabilities = {
  projectCatalog: unsupported,
  projectCreation: supported,
  serverPersistence: unsupported,
  bcpExport: unsupported,
  webSnapshotImport: supported,
  webSnapshotExport: supported,
  moduleLibrary: {
    level: 'optional',
    frontendFallback: 'Snapshot 未包含模块库时,模块放置与 SVG 缩略图不可用'
  },
  moduleLibraryBinding: moduleLibraryBindingCapability,
  inMemoryEdit: supported,
  undoRedo: supported,
  realtimeProjectSync: unsupported,
  gitBranching: unsupported,
  worktreeReview: unsupported,
  agentChat: unsupported,
  runtimeSettings: unsupported
};
