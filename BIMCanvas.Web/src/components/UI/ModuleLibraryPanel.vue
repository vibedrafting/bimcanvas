<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue';
import type { ModuleDefinition } from '../../services/ModuleLibraryService';
import { moduleLibraryService } from '../../services/ModuleLibraryService';
import ModuleLibraryGrid from './moduleLibrary/ModuleLibraryGrid.vue';
import { useModuleLibraryPanelState } from '../../composables/useModuleLibraryPanelState';
import { getWebRuntime } from '../../runtime/runtimeRegistry';
import { supports } from '../../runtime/WebRuntimeProtocol';
import { useSystemStore } from '../../stores/systemStore';

const sys = useSystemStore();

defineProps<{
  visible: boolean;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
  (e: 'select-module', module: ModuleDefinition): void;
}>();

const panelX = ref(100);
const panelY = ref(120);
// Dragging logic removed


const isExpanded = ref(false);
const isSearchExpanded = ref(false);
const searchInputRef = ref<HTMLInputElement | null>(null);
const tagBarRef = ref<HTMLElement | null>(null);

const isCollapsed = computed(() => !isExpanded.value);
const showSearchInput = computed(() => isExpanded.value || isSearchExpanded.value);

const {
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
  getTagLabel
} = useModuleLibraryPanelState();

const runtime = getWebRuntime();
const bindingSupported = computed(() => supports(runtime.capabilities.moduleLibraryBinding));
const isBinding = ref(false);

const reloadLibrary = async () => {
  await moduleLibraryService.reload();
  await loadModules();
};

const onBindClick = async () => {
  if (isBinding.value) return;
  isBinding.value = true;
  try {
    const { count } = await runtime.bindModuleLibraryFolder();
    await reloadLibrary();
    console.info(`[ModuleLibraryPanel] 模块库已绑定 · ${count} 个模块`);
  } catch (err) {
    const msg = (err as Error)?.message ?? String(err);
    // 用户取消 picker 时浏览器抛 AbortError;静默忽略,其它错误才弹
    if (!/AbortError|user activation|abort/i.test(msg)) {
      sys.pushToast({ type: 'error', title: '绑定模块库失败', message: msg });
    }
  } finally {
    isBinding.value = false;
  }
};

const onClearClick = async () => {
  if (!confirm('确定清空当前模块库绑定?\n下次需重新选择模块库文件夹。')) return;
  try {
    await runtime.clearModuleLibraryBinding();
    await reloadLibrary();
  } catch (err) {
    sys.pushToast({
      type: 'error',
      title: '清空模块库失败',
      message: (err as Error)?.message ?? String(err),
    });
  }
};

const panelStyle = computed(() => {
  if (isExpanded.value) {
    return {};
  }
  return {
    left: `${panelX.value}px`,
    top: `${panelY.value}px`
  };
});

// Drag event handlers removed


const expandSearch = async () => {
  if (!isCollapsed.value) return;
  isSearchExpanded.value = true;
  await nextTick();
  searchInputRef.value?.focus();
};

const onSearchBlur = () => {
  if (!isCollapsed.value) return;
  if (!searchQuery.value.trim()) {
    isSearchExpanded.value = false;
  }
};

const onSearchKeyDown = (event: KeyboardEvent) => {
  if (event.key !== 'Escape') return;

  if (isCollapsed.value) {
    clearSearch();
    isSearchExpanded.value = false;
    searchInputRef.value?.blur();
  } else {
    clearSearch();
  }

  event.preventDefault();
  event.stopPropagation();
};

const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;

  if (isExpanded.value) {
    isSearchExpanded.value = true;
    return;
  }

  isSearchExpanded.value = normalizedSearchQuery.value.length > 0;
};

// --- tag-bar 滚动交互 ---
const isTagBarDragging = ref(false);
const tagBarDragStartX = ref(0);
const tagBarScrollStart = ref(0);
const DRAG_THRESHOLD = 3;

const onTagBarWheel = (event: WheelEvent) => {
  if (!tagBarRef.value) return;
  event.preventDefault();
  tagBarRef.value.scrollLeft += event.deltaY;
};

const onTagBarDragStart = (event: MouseEvent) => {
  if (!tagBarRef.value) return;
  isTagBarDragging.value = false;
  tagBarDragStartX.value = event.clientX;
  tagBarScrollStart.value = tagBarRef.value.scrollLeft;
  document.addEventListener('mousemove', onTagBarDragMove);
  document.addEventListener('mouseup', onTagBarDragEnd);
  document.documentElement.addEventListener('mouseleave', onTagBarDragEnd);
};

const onTagBarDragMove = (event: MouseEvent) => {
  if (!tagBarRef.value) return;
  const dx = event.clientX - tagBarDragStartX.value;
  if (!isTagBarDragging.value && Math.abs(dx) > DRAG_THRESHOLD) {
    isTagBarDragging.value = true;
  }
  if (isTagBarDragging.value) {
    tagBarRef.value.scrollLeft = tagBarScrollStart.value - dx;
  }
};

const onTagBarDragEnd = () => {
  if (isTagBarDragging.value) {
    const blockClick = (e: MouseEvent) => {
      e.stopPropagation();
      e.preventDefault();
      document.removeEventListener('click', blockClick, true);
    };
    document.addEventListener('click', blockClick, true);
  }
  isTagBarDragging.value = false;
  document.removeEventListener('mousemove', onTagBarDragMove);
  document.removeEventListener('mouseup', onTagBarDragEnd);
  document.documentElement.removeEventListener('mouseleave', onTagBarDragEnd);
};

const onModuleSelect = (module: ModuleDefinition) => {
  emit('select-module', module);
};

onMounted(async () => {
  await loadModules();
});

onUnmounted(() => {
  document.removeEventListener('mousemove', onTagBarDragMove);

  document.removeEventListener('mouseup', onTagBarDragEnd);
  document.documentElement.removeEventListener('mouseleave', onTagBarDragEnd);
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
        <div class="panel-header">
          <button class="icon-btn back-btn" @click="emit('close')" title="关闭">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="19" y1="12" x2="5" y2="12"></line>
              <polyline points="12 19 5 12 12 5"></polyline>
            </svg>
          </button>

          <!-- Title removed -->
          <div class="title-placeholder"></div>


          <div class="header-actions" @mousedown.stop>
            <button
              v-if="bindingSupported"
              class="icon-btn library-bind-btn"
              :title="allModules.length > 0 ? '重新绑定模块库文件夹' : '绑定模块库文件夹'"
              :disabled="isBinding"
              @click="onBindClick"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
              </svg>
            </button>

            <button
              v-if="bindingSupported && allModules.length > 0"
              class="icon-btn library-clear-btn"
              title="清空模块库绑定"
              @click="onClearClick"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="3 6 5 6 21 6"></polyline>
                <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"></path>
                <path d="M10 11v6"></path>
                <path d="M14 11v6"></path>
                <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"></path>
              </svg>
            </button>

            <button
              v-if="isCollapsed && !showSearchInput"
              class="icon-btn search-toggle-btn"
              title="搜索"
              @click="expandSearch"
            >
              <svg class="search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
            </button>

            <div v-if="showSearchInput" class="search-box" :class="{ collapsed: isCollapsed }">
              <svg class="search-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <input
                ref="searchInputRef"
                v-model.trim="searchQuery"
                class="search-input"
                type="text"
                placeholder="搜索名称/标签/描述"
                @keydown="onSearchKeyDown"
                @blur="onSearchBlur"
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

        <div class="tag-bar" ref="tagBarRef" @wheel="onTagBarWheel" @mousedown="onTagBarDragStart">
          <button
            class="tag-chip"
            :class="{ active: activeTag === null }"
            @click="setActiveTag(null)"
          >全部</button>
          <button
            v-for="tag in allTags"
            :key="tag"
            class="tag-chip"
            :class="{ active: activeTag === tag }"
            @click="setActiveTag(tag)"
          >{{ getTagLabel(tag) }}</button>
        </div>

        <ModuleLibraryGrid
          :modules="filteredModules"
          :expanded="isExpanded"
          :empty-text="emptyStateText"
          :get-svg-url="getSvgUrl"
          :get-tag-label="getTagLabel"
          @select="onModuleSelect"
        >
          <template
            v-if="bindingSupported && allModules.length === 0"
            #empty
          >
            <div class="empty-cta">
              <p class="empty-cta-text">{{ isBinding ? '正在加载...' : '模块库为空' }}</p>
              <button class="empty-cta-btn" :disabled="isBinding" @click="onBindClick">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                </svg>
                <span>选择模块库文件夹</span>
              </button>
              <p class="empty-cta-hint">需要包含 module_library.json 与 assets/ 子目录</p>
            </div>
          </template>
        </ModuleLibraryGrid>
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
  resize: none;

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
    width: min(1000px, 90vw);
    height: min(600px, 80vh);

    min-width: 0;
    min-height: 0;
    max-width: none;
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
  cursor: default;
  user-select: none;
}


.title-placeholder {
  height: 1px;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.search-toggle-btn {
  width: 28px;
  height: 28px;
  padding: 0;
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

  &.collapsed {
    width: 148px;
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

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.empty-cta {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 16px 24px;
  text-align: center;
}

.empty-cta-text {
  margin: 0;
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.empty-cta-btn {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 8px 16px;
  border: 1px solid rgba(0, 170, 255, 0.45);
  background: rgba(0, 170, 255, 0.12);
  color: var(--text-primary);
  border-radius: 999px;
  font-size: 0.82rem;
  cursor: pointer;
  transition: all 0.18s ease;

  svg {
    width: 16px;
    height: 16px;
  }

  &:hover:not(:disabled) {
    background: rgba(0, 170, 255, 0.22);
    border-color: rgba(0, 170, 255, 0.7);
  }

  &:disabled {
    opacity: 0.5;
    cursor: not-allowed;
  }
}

.empty-cta-hint {
  margin: 0;
  font-size: 0.7rem;
  color: var(--text-secondary);
  opacity: 0.7;
}

.tag-bar {
  display: flex;
  gap: 4px;
  padding: 8px 12px;
  overflow-x: auto;
  border-bottom: 1px solid var(--border-subtle);
  flex-shrink: 0;
  cursor: grab;

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
