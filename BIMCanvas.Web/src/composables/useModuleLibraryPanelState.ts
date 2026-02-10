import { computed, ref } from 'vue';
import { moduleLibraryService, type ModuleDefinition } from '../services/ModuleLibraryService';

const TAG_LABELS: Record<string, string> = {
  sleep: '睡眠',
  generalStorage: '储物',
  study: '学习',
  vanity: '梳妆',
  washing: '盥洗',
  toilet: '卫浴',
  shower: '淋浴',
  dining: '餐饮',
  rest: '休息',
  tvMedia: '影音',
  shoeStorage: '鞋柜'
};

export const getModuleTagLabel = (tag: string): string => TAG_LABELS[tag] || tag;

export const useModuleLibraryPanelState = () => {
  const allModules = ref<ModuleDefinition[]>([]);
  const allTags = ref<string[]>([]);
  const activeTag = ref<string | null>(null);
  const searchQuery = ref('');

  const normalizedSearchQuery = computed(() => searchQuery.value.trim().toLowerCase());

  const filteredModules = computed(() => {
    let modules = allModules.value;

    if (activeTag.value) {
      modules = modules.filter((mod) => (mod.tags || []).includes(activeTag.value as string));
    }

    const query = normalizedSearchQuery.value;
    if (!query) {
      return modules;
    }

    return modules.filter((mod) => {
      const name = (mod.name || '').toLowerCase();
      const description = (mod.description || '').toLowerCase();
      const tags = mod.tags || [];
      const rawTags = tags.join(' ').toLowerCase();
      const localizedTags = tags.map(getModuleTagLabel).join(' ').toLowerCase();

      return (
        name.includes(query) ||
        description.includes(query) ||
        rawTags.includes(query) ||
        localizedTags.includes(query)
      );
    });
  });

  const emptyStateText = computed(() => {
    if (allModules.value.length === 0) {
      return '暂无模块';
    }
    return '未找到匹配模块';
  });

  const loadModules = async () => {
    await moduleLibraryService.load();
    allModules.value = moduleLibraryService.getAllModules();
    allTags.value = moduleLibraryService.getAllTags();
  };

  const clearSearch = () => {
    searchQuery.value = '';
  };

  const setActiveTag = (tag: string | null) => {
    activeTag.value = tag;
  };

  const getSvgUrl = (moduleId: string) => moduleLibraryService.getSvgUrl(moduleId);

  return {
    allModules,
    allTags,
    activeTag,
    searchQuery,
    normalizedSearchQuery,
    filteredModules,
    emptyStateText,
    loadModules,
    clearSearch,
    setActiveTag,
    getSvgUrl,
    getTagLabel: getModuleTagLabel
  };
};
