/**
 * 模块库服务
 * 负责从后端 API 加载模块库，提供模块元数据查询和 SVG URL 获取
 */

// 后端 API 基地址
const API_BASE = 'http://localhost:5000/api/modules';

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
}

export interface ModuleLibrary {
  version: string;
  modules: ModuleDefinition[];
}

class ModuleLibraryService {
  private library: ModuleLibrary | null = null;
  private moduleMap: Map<string, ModuleDefinition> = new Map();
  private loadPromise: Promise<void> | null = null;

  /**
   * 加载模块库（从后端 API）
   * 单例加载：多次调用返回同一个 Promise
   */
  async load(): Promise<void> {
    if (this.loadPromise) {
      return this.loadPromise;
    }

    this.loadPromise = (async () => {
      try {
        const response = await fetch(`${API_BASE}/library`);
        if (!response.ok) {
          // 404 可能是因为没有加载项目，这是正常情况
          if (response.status === 404) {
            console.warn('[ModuleLibraryService] 模块库未加载（可能没有打开项目）');
            return;
          }
          throw new Error(`Failed to load module library: ${response.statusText}`);
        }

        this.library = await response.json();

        // 构建快速查询 Map
        if (this.library?.modules) {
          this.library.modules.forEach(mod => {
            this.moduleMap.set(mod.id, mod);
          });
        }

        console.log(`[ModuleLibraryService] Loaded ${this.library?.modules?.length ?? 0} modules from API`);
      } catch (error) {
        console.error('[ModuleLibraryService] Failed to load module library:', error);
        // 不抛出错误，允许应用继续运行（模块库加载失败不应阻塞整个应用）
      }
    })();

    return this.loadPromise;
  }

  /**
   * 重新加载模块库（清除缓存后重新加载）
   * 用于项目切换后刷新
   */
  async reload(): Promise<void> {
    this.library = null;
    this.moduleMap.clear();
    this.loadPromise = null;
    return this.load();
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
   * @returns 完整的 SVG API URL
   */
  getSvgUrl(moduleId: string): string {
    return `${API_BASE}/svg/${moduleId}`;
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
}

// 导出单例
export const moduleLibraryService = new ModuleLibraryService();
