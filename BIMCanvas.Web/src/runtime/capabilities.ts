import type { CapabilityEntry, WebCapabilities } from './WebRuntimeProtocol';

const supported: CapabilityEntry = { level: 'supported' };
const unsupported: CapabilityEntry = { level: 'unsupported' };

export const connectedCapabilities: WebCapabilities = {
  projectCatalog: supported,
  serverPersistence: supported,
  bcpExport: supported,
  webSnapshotImport: unsupported,
  webSnapshotExport: supported,
  moduleLibrary: supported,
  inMemoryEdit: supported,
  undoRedo: supported,
  realtimeProjectSync: supported,
  gitBranching: supported,
  worktreeReview: supported,
  agentChat: supported,
  runtimeSettings: supported
};

export const standaloneCapabilities: WebCapabilities = {
  projectCatalog: unsupported,
  serverPersistence: unsupported,
  bcpExport: unsupported,
  webSnapshotImport: supported,
  webSnapshotExport: supported,
  moduleLibrary: {
    level: 'optional',
    frontendFallback: 'Snapshot 未包含模块库时，模块放置与 SVG 缩略图不可用'
  },
  inMemoryEdit: supported,
  undoRedo: supported,
  realtimeProjectSync: unsupported,
  gitBranching: unsupported,
  worktreeReview: unsupported,
  agentChat: unsupported,
  runtimeSettings: unsupported
};
