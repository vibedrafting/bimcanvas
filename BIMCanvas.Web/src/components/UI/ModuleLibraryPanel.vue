<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { moduleLibraryService, type ModuleDefinition } from '../../services/ModuleLibraryService';

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'select-module', module: ModuleDefinition): void;
}>();

// 面板拖拽位置
const panelX = ref(100);
const panelY = ref(120);
const isDragging = ref(false);
const dragOffset = ref({ x: 0, y: 0 });

// 展开状态
const isExpanded = ref(false);

// Tag + 搜索
const allTags = ref<string[]>([]);
const activeTag = ref<string | null>(null);
const searchQuery = ref('');

// 模块列表
const allModules = ref<ModuleDefinition[]>([]);

// Tag 中文显示名
const tagLabels: Record<string, string> = {
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

const getTagLabel = (tag: string) => tagLabels[tag] || tag;

const normalizedSearchQuery = computed(() => searchQuery.value.trim().toLowerCase());

const displayModules = computed(() => {
  let result = allModules.value;

  if (activeTag.value) {
    result = result.filter(mod => mod.tags?.includes(activeTag.value!));
  }

  const query = normalizedSearchQuery.value;
  if (!query) {
    return result;
  }

  return result.filter(mod => {
    const name = (mod.name || '').toLowerCase();
    const description = (mod.description || '').toLowerCase();
    const tags = mod.tags || [];
    const rawTags = tags.join(' ').toLowerCase();
    const localizedTags = tags.map(tag => getTagLabel(tag)).join(' ').toLowerCase();

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
  if (normalizedSearchQuery.value) {
    return '未找到匹配模块';
  }
  return '暂无模块';
});

// Tooltip 文本
const getTooltip = (mod: ModuleDefinition) => {
  const lines = [mod.name];
  lines.push(`${mod.size.width} × ${mod.size.depth} mm`);
  if (mod.description) lines.push(mod.description);
  return lines.join('\n');
};

// 初始化
onMounted(async () => {
  await moduleLibraryService.load();
  allModules.value = moduleLibraryService.getAllModules();
  allTags.value = moduleLibraryService.getAllTags();
});

// 拖拽逻辑
const onTitleMouseDown = (e: MouseEvent) => {
  if (isExpanded.value) return; // 展开模式不允许拖拽

  const target = e.target as HTMLElement;
  if (target.closest('.header-actions')) {
    return;
  }

  e.preventDefault();
  isDragging.value = true;
  dragOffset.value = {
    x: e.clientX - panelX.value,
    y: e.clientY - panelY.value
  };
  document.addEventListener('mousemove', onDrag);
  document.addEventListener('mouseup', onDragEnd);
};

const onDrag = (e: MouseEvent) => {
  if (!isDragging.value) return;
  panelX.value = e.clientX - dragOffset.value.x;
  panelY.value = e.clientY - dragOffset.value.y;
};

const onDragEnd = () => {
  isDragging.value = false;
  document.removeEventListener('mousemove', onDrag);
  document.removeEventListener('mouseup', onDragEnd);
};

// SVG 缩略图加载失败回退
const onImgError = (e: Event) => {
  const img = e.target as HTMLImageElement;
  img.style.display = 'none';
};

const clearSearch = () => {
  searchQuery.value = '';
};

// 获取 SVG URL
const getSvgUrl = (mod: ModuleDefinition) => moduleLibraryService.getSvgUrl(mod.id);

// 选择模块
const onModuleClick = (mod: ModuleDefinition) => {
  emit('select-module', mod);
};

// 展开/收起
const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;
};

// 面板样式
const panelStyle = computed(() => {
  if (isExpanded.value) {
    return {}; // 展开模式用 CSS class 控制
  }
  return {
    left: panelX.value + 'px',
    top: panelY.value + 'px'
  };
});

onUnmounted(() => {
  document.removeEventListener('mousemove', onDrag);
  document.removeEventListener('mouseup', onDragEnd);
});
</script>

<template>
  <Teleport to="body">
    <transition name="panel-fade">
      <div
        v-if="visible"
        class="module-library-panel"
        :class="{ expanded: isExpanded }"
        :style="panelStyle"
      >
        <div class="panel-header" @mousedown="onTitleMouseDown">
          <button class="icon-btn back-btn" @click="emit('close')" title="关闭">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="19" y1="12" x2="5" y2="12"></line>
              <polyline points="12 19 5 12 12 5"></polyline>
            </svg>
          </button>

          <div class="title">MODULE LIBRARY</div>

          <div class="header-actions" @mousedown.stop>
            <div class="search-box">
              <svg class="search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <input
                v-model.trim="searchQuery"
                class="search-input"
                type="text"
                placeholder="搜索名称/标签/描述"
              />
              <button
                v-if="searchQuery"
                class="clear-search-btn"
                title="清空搜索"
                @click="clearSearch"
              >×</button>
            </div>

            <button class="icon-btn expand-btn" @click="toggleExpand" :title="isExpanded ? '收起' : '展开'">
              <svg v-if="!isExpanded" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="15 3 21 3 21 9"></polyline>
                <polyline points="9 21 3 21 3 15"></polyline>
                <line x1="21" y1="3" x2="14" y2="10"></line>
                <line x1="3" y1="21" x2="10" y2="14"></line>
              </svg>
              <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="4 14 10 14 10 20"></polyline>
                <polyline points="20 10 14 10 14 4"></polyline>
                <line x1="14" y1="10" x2="21" y2="3"></line>
                <line x1="3" y1="21" x2="10" y2="14"></line>
              </svg>
            </button>
          </div>
        </div>

        <div class="tag-bar">
          <button
            class="tag-chip"
            :class="{ active: activeTag === null }"
            @click="activeTag = null"
          >全部</button>
          <button
            v-for="tag in allTags"
            :key="tag"
            class="tag-chip"
            :class="{ active: activeTag === tag }"
            @click="activeTag = tag"
          >{{ getTagLabel(tag) }}</button>
        </div>

        <div class="module-grid">
          <div
            v-for="mod in displayModules"
            :key="mod.id"
            class="module-card"
            :class="{ expanded: isExpanded }"
            :title="getTooltip(mod)"
            @click="onModuleClick(mod)"
          >
            <div class="thumbnail-area">
              <img
                :src="getSvgUrl(mod)"
                :alt="mod.name"
                @error="onImgError"
              />
            </div>

            <div class="name-area">
              <div class="card-name">{{ mod.name }}</div>
            </div>

            <div v-if="isExpanded" class="tag-area">
              <div class="card-tags">
                <span
                  v-for="tag in (mod.tags || [])"
                  :key="tag"
                  class="mini-tag"
                >{{ getTagLabel(tag) }}</span>
              </div>
            </div>
          </div>

          <div v-if="displayModules.length === 0" class="empty-state">
            {{ emptyStateText }}
          </div>
        </div>
      </div>
    </transition>
  </Teleport>
</template>

<style scoped lang="scss">
.module-library-panel {
  position: fixed;
  z-index: 300;
  width: 360px;
  height: min(75vh, 620px);
  min-width: 300px;
  min-height: 320px;
  max-width: 80vw;
  display: flex;
  flex-direction: column;

  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);

  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 20px;

  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow:
    0 12px 40px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(255, 255, 255, 0.1) inset,
    0 0 20px rgba(255, 255, 255, 0.15);

  overflow: hidden;
  resize: both;

  transition:
    width 0.3s cubic-bezier(0.19, 1, 0.22, 1),
    height 0.3s cubic-bezier(0.19, 1, 0.22, 1),
    top 0.3s cubic-bezier(0.19, 1, 0.22, 1),
    left 0.3s cubic-bezier(0.19, 1, 0.22, 1),
    transform 0.3s cubic-bezier(0.19, 1, 0.22, 1);

  &.expanded {
    left: 50% !important;
    top: 50% !important;
    transform: translate(-50%, -50%);
    width: min(1280px, calc(100vw - 64px));
    height: min(620px, calc(100vh - 96px));
    min-width: 0;
    min-height: 0;
    max-width: none;
    resize: none;
  }
}

.panel-header {
  display: grid;
  grid-template-columns: 28px 1fr auto;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-subtle);
  flex-shrink: 0;
  cursor: grab;
  user-select: none;

  &:active {
    cursor: grabbing;
  }

  .title {
    font-weight: 600;
    font-size: 0.9rem;
    color: var(--text-primary);
    letter-spacing: 0.5px;
    text-align: center;
  }
}

.module-library-panel.expanded .panel-header {
  cursor: default;

  &:active {
    cursor: default;
  }
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.search-box {
  display: flex;
  align-items: center;
  width: 170px;
  height: 30px;
  padding: 0 8px;
  border: 1px solid var(--border-subtle);
  border-radius: 999px;
  background: rgba(10, 14, 32, 0.45);
  transition: border-color 0.2s ease, box-shadow 0.2s ease;

  &:focus-within {
    border-color: rgba(0, 170, 255, 0.65);
    box-shadow: 0 0 0 2px rgba(0, 170, 255, 0.18);
  }
}

.module-library-panel.expanded .search-box {
  width: 250px;
}

.search-icon {
  width: 14px;
  height: 14px;
  color: var(--text-secondary);
  flex-shrink: 0;
}

.search-input {
  flex: 1;
  min-width: 0;
  border: none;
  outline: none;
  background: transparent;
  color: var(--text-primary);
  font-size: 0.74rem;

  &::placeholder {
    color: var(--text-secondary);
  }
}

.clear-search-btn {
  border: none;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  font-size: 0.9rem;
  line-height: 1;
  padding: 0;
  width: 16px;
  height: 16px;
  display: flex;
  align-items: center;
  justify-content: center;

  &:hover {
    color: var(--text-primary);
  }
}

.icon-btn {
  background: transparent;
  border: none;
  color: var(--text-secondary);
  cursor: pointer;
  padding: 4px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.2s ease;

  svg {
    width: 18px;
    height: 18px;
  }

  &:hover {
    background: var(--surface-hover);
    color: var(--text-primary);
  }
}

.tag-bar {
  display: flex;
  gap: 4px;
  padding: 8px 12px;
  overflow-x: auto;
  border-bottom: 1px solid var(--border-subtle);
  flex-shrink: 0;

  scrollbar-width: none;

  &::-webkit-scrollbar {
    display: none;
  }
}

.tag-chip {
  flex-shrink: 0;
  padding: 3px 10px;
  font-size: 0.72rem;
  border-radius: 12px;
  border: 1px solid var(--border-subtle);
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.15s;
  white-space: nowrap;

  &:hover {
    color: var(--text-primary);
    background: var(--surface-glass-hover);
  }

  &.active {
    background: var(--accent-primary, #00aaff);
    color: #fff;
    border-color: var(--accent-primary, #00aaff);
  }
}

.module-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 8px;
  padding: 12px;
  overflow-y: auto;
  flex: 1;
  align-content: start;

  &::-webkit-scrollbar {
    width: 4px;
  }

  &::-webkit-scrollbar-track {
    background: transparent;
  }

  &::-webkit-scrollbar-thumb {
    background: var(--border-strong);
    border-radius: 2px;
  }

  scrollbar-width: thin;
  scrollbar-color: rgba(255, 255, 255, 0.15) transparent;
}

.module-library-panel.expanded .module-grid {
  grid-template-columns: repeat(auto-fill, minmax(136px, 1fr));
  gap: 12px;
  padding: 14px;
}

.module-card {
  display: flex;
  flex-direction: column;
  height: 122px;
  border: 1px solid var(--border-subtle);
  border-radius: 10px;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s, box-shadow 0.15s;
  background: rgba(0, 0, 0, 0.15);

  &:hover {
    border-color: var(--accent-primary, #00aaff);
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 170, 255, 0.15);
  }
}

.module-card.expanded {
  height: 232px;
}

.thumbnail-area {
  width: 100%;
  height: 88px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  padding: 8px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.16);

  img {
    width: 100%;
    height: 100%;
    object-fit: contain;
  }
}

.module-card.expanded .thumbnail-area {
  height: auto;
  aspect-ratio: 1 / 1;
  padding: 10px;
}

.name-area {
  height: 34px;
  padding: 0 6px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.card-name {
  width: 100%;
  font-size: 0.72rem;
  color: var(--text-primary);
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.2;
}

.module-card.expanded .name-area {
  height: 42px;
  padding: 4px 8px 2px;
}

.module-card.expanded .card-name {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  white-space: normal;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.2;
}

.tag-area {
  height: 52px;
  padding: 2px 6px 8px;
}

.card-tags {
  height: 100%;
  display: flex;
  justify-content: center;
  align-content: flex-start;
  flex-wrap: wrap;
  gap: 4px;
  overflow: hidden;
}

.mini-tag {
  padding: 1px 6px;
  font-size: 0.62rem;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.08);
  color: var(--text-secondary);
  white-space: nowrap;
}

.empty-state {
  grid-column: 1 / -1;
  text-align: center;
  padding: 24px 0;
  color: var(--text-secondary);
  font-size: 0.8rem;
}

.panel-fade-enter-active {
  transition: opacity 0.2s ease-out, transform 0.2s ease-out;
}

.panel-fade-leave-active {
  transition: opacity 0.15s ease-in, transform 0.15s ease-in;
}

.panel-fade-enter-from,
.panel-fade-leave-to {
  opacity: 0;
  transform: scale(0.95) translateY(-8px);
}
</style>
