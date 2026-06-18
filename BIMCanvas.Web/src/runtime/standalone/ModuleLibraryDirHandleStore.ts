/**
 * 模块库目录句柄持久化 (IndexedDB)
 *
 * 在 StandaloneRuntime 下,把用户通过 showDirectoryPicker 选定的
 * FileSystemDirectoryHandle 存入 IDB,下次会话可恢复。
 * 不复制文件内容到 IDB,数据始终从磁盘读最新。
 *
 * 借鉴自 docs/ppt/v2.1/bimcanvas-overview.html 的句柄持久化模式。
 */

import { createLogger } from '../../utils/logger';

const log = createLogger('SYS');

const DB_NAME = 'bimcanvas-web-standalone';
const DB_VERSION = 1;
const STORE_NAME = 'handles';
const HANDLE_KEY = 'module-library-dir';

const isIdbAvailable = (): boolean =>
  typeof indexedDB !== 'undefined' && indexedDB !== null;

const openDb = (): Promise<IDBDatabase> =>
  new Promise((resolve, reject) => {
    const req = indexedDB.open(DB_NAME, DB_VERSION);
    req.onupgradeneeded = (event) => {
      const db = (event.target as IDBOpenDBRequest).result;
      if (!db.objectStoreNames.contains(STORE_NAME)) {
        db.createObjectStore(STORE_NAME);
      }
    };
    req.onsuccess = (event) => resolve((event.target as IDBOpenDBRequest).result);
    req.onerror = () => reject(req.error);
  });

export const ModuleLibraryDirHandleStore = {
  async load(): Promise<FileSystemDirectoryHandle | null> {
    if (!isIdbAvailable()) return null;
    try {
      const db = await openDb();
      try {
        return await new Promise<FileSystemDirectoryHandle | null>((resolve) => {
          const tx = db.transaction(STORE_NAME, 'readonly');
          const req = tx.objectStore(STORE_NAME).get(HANDLE_KEY);
          req.onsuccess = () => resolve((req.result as FileSystemDirectoryHandle | undefined) ?? null);
          req.onerror = () => resolve(null);
        });
      } finally {
        db.close();
      }
    } catch (err) {
      log.warn('handle load failed', { error: err });
      return null;
    }
  },

  async save(handle: FileSystemDirectoryHandle): Promise<void> {
    if (!isIdbAvailable()) return;
    try {
      const db = await openDb();
      try {
        await new Promise<void>((resolve, reject) => {
          const tx = db.transaction(STORE_NAME, 'readwrite');
          tx.objectStore(STORE_NAME).put(handle, HANDLE_KEY);
          tx.oncomplete = () => resolve();
          tx.onerror = () => reject(tx.error);
          tx.onabort = () => reject(tx.error);
        });
      } finally {
        db.close();
      }
    } catch (err) {
      log.warn('handle save failed', { error: err });
    }
  },

  async clear(): Promise<void> {
    if (!isIdbAvailable()) return;
    try {
      const db = await openDb();
      try {
        await new Promise<void>((resolve) => {
          const tx = db.transaction(STORE_NAME, 'readwrite');
          tx.objectStore(STORE_NAME).delete(HANDLE_KEY);
          tx.oncomplete = () => resolve();
          tx.onerror = () => resolve();
          tx.onabort = () => resolve();
        });
      } finally {
        db.close();
      }
    } catch (err) {
      log.warn('handle clear failed', { error: err });
    }
  }
};
