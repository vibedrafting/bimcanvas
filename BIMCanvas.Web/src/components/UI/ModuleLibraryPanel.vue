<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import { moduleLibraryService, type ModuleDefinition } from '../../services/ModuleLibraryService';

const props = defineProps<{
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

// Tag 筛选
const allTags = ref<string[]>([]);
const activeTag = ref<string | null>(null);

// 模块列表
const allModules = ref<ModuleDefinition[]>([]);
const filteredModules = computed(() => {
  if (!activeTag.value) return allModules.value;
  return allModules.value.filter(m => m.tags?.includes(activeTag.value!));
});

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
        <!-- 标题栏 (PropertyPanel 风格) -->
        <div class="panel-header" @mousedown.prevent="onTitleMouseDown">
          <button class="icon-btn back-btn" @click="emit('close')" title="关闭">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <line x1="19" y1="12" x2="5" y2="12"></line>
              <polyline points="12 19 5 12 12 5"></polyline>
            </svg>
          </button>

          <div class="title">MODULE LIBRARY</div>

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

        <!-- Tag 筛选栏 -->
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

        <!-- 模块网格 -->
        <div class="module-grid">
          <div
            v-for="mod in filteredModules"
            :key="mod.id"
            class="module-card"
            :title="getTooltip(mod)"
            @click="onModuleClick(mod)"
          >
            <div class="card-thumbnail">
              <img
                :src="getSvgUrl(mod)"
                :alt="mod.name"
                @error="onImgError"
              />
            </div>
            <div class="card-name">{{ mod.name }}</div>
            <div class="card-tags">
              <span
                v-for="tag in (mod.tags || [])"
                :key="tag"
                class="mini-tag"
              >{{ getTagLabel(tag) }}</span>
            </div>
          </div>

          <div v-if="filteredModules.length === 0" class="empty-state">
            暂无模块
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
  max-height: 75vh;
  display: flex;
  flex-direction: column;

  // Aurora Glass (复用 PropertyPanel 风格)
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);

  // 增强边框 + 发光
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 20px;

  // Glare + 深阴影 + 边缘发光
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow:
    0 12px 40px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(255, 255, 255, 0.1) inset,
    0 0 20px rgba(255, 255, 255, 0.15);

  overflow: hidden;

  // 可拖拽调整大小
  resize: both;
  min-width: 280px;
  min-height: 300px;
  max-width: 80vw;

  // 过渡
  transition:
    width 0.4s cubic-bezier(0.19, 1, 0.22, 1),
    height 0.4s cubic-bezier(0.19, 1, 0.22, 1),
    top 0.4s cubic-bezier(0.19, 1, 0.22, 1),
    left 0.4s cubic-bezier(0.19, 1, 0.22, 1),
    max-height 0.4s cubic-bezier(0.19, 1, 0.22, 1);

  // 展开模式
  &.expanded {
    left: 50% !important;
    top: 50% !important;
    transform: translate(-50%, -50%);
    width: 60vw;
    max-height: 80vh;
    resize: none;
  }
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 12px 16px;
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
}

// 展开模式下不允许拖拽
.module-library-panel.expanded .panel-header {
  cursor: default;
  &:active {
    cursor: default;
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
  &::-webkit-scrollbar { display: none; }
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
  grid-template-columns: repeat(auto-fill, minmax(100px, 1fr));
  gap: 8px;
  padding: 12px;
  overflow-y: auto;
  flex: 1;

  // 自定义滚动条
  &::-webkit-scrollbar { width: 4px; }
  &::-webkit-scrollbar-track { background: transparent; }
  &::-webkit-scrollbar-thumb {
    background: var(--border-strong);
    border-radius: 2px;
  }
  scrollbar-width: thin;
  scrollbar-color: rgba(255,255,255,0.15) transparent;
}

.module-card {
  display: flex;
  flex-direction: column;
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

.card-thumbnail {
  width: 100%;
  aspect-ratio: 4 / 3;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  padding: 10px;

  img {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
  }
}

.card-name {
  padding: 4px 6px 1px;
  font-size: 0.72rem;
  color: var(--text-primary);
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-tags {
  display: flex;
  justify-content: center;
  flex-wrap: wrap;
  gap: 3px;
  padding: 1px 4px 4px;
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

// 面板进出动画
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
