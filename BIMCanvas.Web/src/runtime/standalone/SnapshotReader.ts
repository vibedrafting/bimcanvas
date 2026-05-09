import type { ProjectData } from '../../types/canvas';
import type { ModuleLibrary } from '../../services/ModuleLibraryService';
import type { WebSnapshot } from '../WebRuntimeProtocol';

interface ParsedSnapshot {
  projectData: ProjectData;
  moduleLibrary: ModuleLibrary | null;
  moduleAssets: Record<string, string>;
}

const SNAPSHOT_KIND = 'bimcanvas.web.snapshot';
const SNAPSHOT_VERSION = 1;

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

export class SnapshotReader {
  static async parse(file: File): Promise<ParsedSnapshot> {
    const raw = await file.text();
    const parsed = JSON.parse(raw) as unknown;

    if (!isRecord(parsed)) {
      throw new Error('Snapshot 格式错误：根节点必须是对象');
    }

    if (parsed.kind !== SNAPSHOT_KIND) {
      throw new Error('Snapshot 格式错误：kind 不匹配');
    }

    if (parsed.version !== SNAPSHOT_VERSION) {
      throw new Error(`Snapshot 版本不支持：${String(parsed.version)}`);
    }

    if (!isRecord(parsed.projectData)) {
      throw new Error('Snapshot 格式错误：缺少 projectData');
    }

    const snapshot = parsed as unknown as WebSnapshot;
    return {
      projectData: snapshot.projectData,
      moduleLibrary: snapshot.moduleLibrary ?? null,
      moduleAssets: snapshot.moduleAssets ?? {}
    };
  }
}
