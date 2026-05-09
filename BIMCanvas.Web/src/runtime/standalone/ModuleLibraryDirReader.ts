/**
 * 模块库目录句柄读取器
 *
 * 给定一个 FileSystemDirectoryHandle (用户在 showDirectoryPicker 中选中的目录),
 * 读取其中的 module_library.json 与 SVG 资产。SVG 懒加载,不一次性全读。
 */
import type { ModuleLibrary } from '../../services/ModuleLibraryService';

export type DirPermissionState = 'granted' | 'denied' | 'prompt';

const isObject = (v: unknown): v is Record<string, unknown> =>
  typeof v === 'object' && v !== null && !Array.isArray(v);

export const isDirectoryPickerSupported = (): boolean =>
  typeof window !== 'undefined' && typeof (window as unknown as { showDirectoryPicker?: unknown }).showDirectoryPicker === 'function';

/**
 * 查询权限。queryPermission 不存在时退化为 'prompt' (老浏览器),由调用方决定是否再走 request。
 */
export const queryDirPermission = async (handle: FileSystemDirectoryHandle): Promise<DirPermissionState> => {
  const h = handle as unknown as { queryPermission?: (opts: { mode: string }) => Promise<DirPermissionState> };
  if (typeof h.queryPermission !== 'function') return 'prompt';
  try {
    return await h.queryPermission({ mode: 'read' });
  } catch {
    return 'prompt';
  }
};

/**
 * 在用户手势上下文中请求权限。返回最终状态。
 */
export const requestDirPermission = async (handle: FileSystemDirectoryHandle): Promise<DirPermissionState> => {
  const h = handle as unknown as { requestPermission?: (opts: { mode: string }) => Promise<DirPermissionState> };
  if (typeof h.requestPermission !== 'function') return 'denied';
  try {
    return await h.requestPermission({ mode: 'read' });
  } catch {
    return 'denied';
  }
};

/**
 * 确保权限到 'granted'。query 已 granted 直接 OK;'prompt' 时仅当允许提示才 request;'denied' 直接失败。
 */
export const ensureReadPermission = async (
  handle: FileSystemDirectoryHandle,
  promptIfNeeded: boolean
): Promise<DirPermissionState> => {
  const cur = await queryDirPermission(handle);
  if (cur === 'granted') return 'granted';
  if (cur === 'denied') return 'denied';
  if (!promptIfNeeded) return 'prompt';
  return await requestDirPermission(handle);
};

/**
 * 从根目录句柄读出 module_library.json 文本并解析为 ModuleLibrary。
 * 失败时抛友好错误。
 */
export const loadLibraryJson = async (root: FileSystemDirectoryHandle): Promise<ModuleLibrary> => {
  let fileHandle: FileSystemFileHandle;
  try {
    fileHandle = await root.getFileHandle('module_library.json', { create: false });
  } catch {
    throw new Error('未在所选目录中找到 module_library.json (期望它在文件夹根部)');
  }
  const file = await fileHandle.getFile();
  const text = await file.text();
  let parsed: unknown;
  try {
    parsed = JSON.parse(text);
  } catch (err) {
    throw new Error(`module_library.json 不是合法 JSON: ${(err as Error).message}`);
  }
  if (!isObject(parsed) || !Array.isArray((parsed as { modules?: unknown }).modules)) {
    throw new Error('module_library.json 结构不正确: 缺少 modules 数组');
  }
  return parsed as unknown as ModuleLibrary;
};

/**
 * 按相对路径 (例如 'assets/mod_bed_001.svg') 从根目录读取 SVG 文本。
 * 找不到返回 null,不抛错,单个 SVG 缺失不应阻塞整体使用。
 */
export const loadAssetText = async (
  root: FileSystemDirectoryHandle,
  relativePath: string
): Promise<string | null> => {
  if (!relativePath) return null;
  const segments = relativePath.split(/[\\/]+/).filter((s) => s.length > 0 && s !== '.');
  if (segments.length === 0) return null;
  try {
    let cursor: FileSystemDirectoryHandle = root;
    for (let i = 0; i < segments.length - 1; i++) {
      const seg = segments[i];
      if (!seg) return null;
      cursor = await cursor.getDirectoryHandle(seg, { create: false });
    }
    const last = segments[segments.length - 1];
    if (!last) return null;
    const fileHandle = await cursor.getFileHandle(last, { create: false });
    const file = await fileHandle.getFile();
    return await file.text();
  } catch {
    return null;
  }
};

/**
 * 调用 showDirectoryPicker。失败 (用户取消 / 不支持) 抛错。
 */
export const pickModuleLibraryDirectory = async (): Promise<FileSystemDirectoryHandle> => {
  const w = window as unknown as {
    showDirectoryPicker?: (opts?: { id?: string; mode?: string; startIn?: unknown }) => Promise<FileSystemDirectoryHandle>;
  };
  if (typeof w.showDirectoryPicker !== 'function') {
    throw new Error('当前浏览器不支持目录选择 · 请使用 Chrome / Edge');
  }
  return await w.showDirectoryPicker({ id: 'bimcanvas-module-library', mode: 'read' });
};
