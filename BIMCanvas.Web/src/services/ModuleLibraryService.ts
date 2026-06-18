/**
 * 模块库服务
 * 负责通过当前 WebRuntime 加载模块库，提供模块元数据查询和 SVG URL 获取
 */
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { normalizeMorphology, type ModuleMorphology, type RawModuleMorphology } from '../utils/moduleSize';
import { createLogger } from '../utils/logger';

const log = createLogger('SYS');

export type { ModuleMorphology, DimensionLimit } from '../utils/moduleSize';

export interface ModuleDefinition {
  id: string;
  name: string;
  tags: string[];
  size: {
    width: number;
    depth: number;
  };
  description?: string;
  svgPath: string;  // 后端已转换为 API 路径格式
  /**
   * 模块形态（strategy + limits）。Server 从 agent_config.morphology 抽取下发。
   * fixed 模块通常为 undefined；undefined 视同 fixed。
   */
  morphology?: ModuleMorphology;
}

export interface ModuleLibrary {
  version: string;
  modules: ModuleDefinition[];
}

class ModuleLibraryService {
  private library: ModuleLibrary | null = null;
  private moduleMap: Map<string, ModuleDefinition> = new Map();
  private loadPromise: Promise<void> | null = null;
  private svgUrlCache: Map<string, string> = new Map();

  /**
   * 加载模块库（从当前 Runtime）
   * 单例加载：多次调用返回同一个 Promise
   */
  async load(): Promise<void> {
    if (this.loadPromise) {
      return this.loadPromise;
    }

    this.loadPromise = (async () => {
      try {
        const raw = await getWebRuntime().getModuleLibrary();
        if (raw?.modules) {
          // 归一化 morphology：把 Server DTO 的 { range: [...] } / { enum: [...] } 折叠成 kind-tagged union
          for (const mod of raw.modules) {
            const rawMorph = (mod as ModuleDefinition & { morphology?: RawModuleMorphology }).morphology as RawModuleMorphology | undefined;
            mod.morphology = normalizeMorphology(rawMorph);
          }
        }
        this.library = raw;

        // 构建快速查询 Map
        if (this.library?.modules) {
          this.library.modules.forEach(mod => {
            this.moduleMap.set(mod.id, mod);
          });
        }

        if (!this.library) {
          log.warn('module library unavailable from runtime or snapshot');
          return;
        }

        log.debug('module library loaded', { count: this.library.modules.length });
      } catch (error) {
        log.error('failed to load module library', { error });
        // 不抛出错误，允许应用继续运行（模块库加载失败不应阻塞整个应用）
      }
    })();

    return this.loadPromise;
  }

  /**
   * 重新加载模块库（原子 swap：先拉新数据、再一次性替换）。
   *
   * 不在 await 期间暴露空 moduleMap：
   * 模块库是 Vue computed（如 PropertyPanel 的 selectedModuleDef）通过 getModuleById 查询的
   * 非响应式数据源——若中途清空 Map，恰好命中重算的 computed 会缓存 undefined，
   * 而 fetch 完成后填回 Map 不会再触发 computed 重算（无响应式依赖）。
   * 表现为：保存模块后 Width/Depth 的灰色 hint 永久消失，直到重新选择模块。
   */
  async reload(): Promise<void> {
    let newLibrary: ModuleLibrary | null = null;
    const newMap = new Map<string, ModuleDefinition>();

    try {
      const raw = await getWebRuntime().getModuleLibrary();
      if (raw?.modules) {
        for (const mod of raw.modules) {
          const rawMorph = (mod as ModuleDefinition & { morphology?: RawModuleMorphology }).morphology as RawModuleMorphology | undefined;
          mod.morphology = normalizeMorphology(rawMorph);
          newMap.set(mod.id, mod);
        }
      }
      newLibrary = raw;
    } catch (error) {
      log.error('failed to reload module library', { error });
      // 拉取失败：保留旧状态，避免 UI 退化
      return;
    }

    // 原子 swap：到这里才替换内部状态，外部观察者全程看到一致的 Map
    this.dispose();
    this.library = newLibrary;
    this.moduleMap = newMap;
    this.loadPromise = Promise.resolve();

    if (newLibrary?.modules) {
      log.debug('module library reloaded', { count: newLibrary.modules.length });
    } else {
      log.warn('module library unavailable from runtime or snapshot');
    }
  }

  /**
   * 根据模块 ID 获取模块定义
   */
  getModuleById(moduleId: string): ModuleDefinition | undefined {
    return this.moduleMap.get(moduleId);
  }

  /**
   * 获取模块 SVG 的完整 URL
   * @param moduleId 模块 ID
   * @returns 可供 img / SVGLoader 使用的 Blob URL
   */
  async getSvgUrl(moduleId: string): Promise<string> {
    const cached = this.svgUrlCache.get(moduleId);
    if (cached) {
      return cached;
    }

    const svgText = await getWebRuntime().getModuleAsset(moduleId);
    if (!svgText) {
      return '';
    }

    const url = URL.createObjectURL(new Blob([svgText], { type: 'image/svg+xml' }));
    this.svgUrlCache.set(moduleId, url);
    return url;
  }

  /**
   * 根据标签过滤模块
   */
  getModulesByTag(tag: string): ModuleDefinition[] {
    if (!this.library) return [];
    return this.library.modules.filter(mod => mod.tags?.includes(tag));
  }

  /**
   * 根据多个标签过滤模块（任一标签匹配即可）
   */
  getModulesByTags(tags: string[]): ModuleDefinition[] {
    if (!this.library) return [];
    return this.library.modules.filter(mod =>
      mod.tags?.some(tag => tags.includes(tag))
    );
  }

  /**
   * 获取所有模块
   */
  getAllModules(): ModuleDefinition[] {
    return this.library?.modules || [];
  }

  /**
   * 获取所有唯一的 tags
   */
  getAllTags(): string[] {
    if (!this.library) return [];
    const tagSet = new Set<string>();
    this.library.modules.forEach(mod => {
      mod.tags?.forEach(tag => tagSet.add(tag));
    });
    return Array.from(tagSet);
  }

  /**
   * 检查是否已加载
   */
  isLoaded(): boolean {
    return this.library !== null && this.library.modules !== null;
  }

  dispose(): void {
    for (const url of this.svgUrlCache.values()) {
      URL.revokeObjectURL(url);
    }
    this.svgUrlCache.clear();
  }
}

// 导出单例
export const moduleLibraryService = new ModuleLibraryService();
