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

// 初始化
onMounted(async () => {
  await moduleLibraryService.load();
  allModules.value = moduleLibraryService.getAllModules();
  allTags.value = moduleLibraryService.getAllTags();
});

// 拖拽逻辑
const onTitleMouseDown = (e: MouseEvent) => {
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
        :style="{ left: panelX + 'px', top: panelY + 'px' }"
      >
        <!-- 标题栏 -->
        <div class="panel-header" @mousedown.prevent="onTitleMouseDown">
          <span class="panel-title">Module Library</span>
          <button class="close-btn" @click="emit('close')">&times;</button>
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
            :title="mod.description || mod.name"
            @click="onModuleClick(mod)"
          >
            <div class="card-thumbnail">
              <img
                :src="getSvgUrl(mod)"
                :alt="mod.name"
                @error="onImgError"
              />
            </div>
            <div class="card-info">
              <span class="card-name">{{ mod.name }}</span>
              <span class="card-size">{{ mod.size.width }} × {{ mod.size.depth }}</span>
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
  width: 320px;
  max-height: 70vh;
  display: flex;
  flex-direction: column;

  // 毛玻璃
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.15);
  border-radius: 12px;
  box-shadow: 0 12px 40px rgba(0, 0, 0, 0.4);
  overflow: hidden;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 14px;
  border-bottom: 1px solid var(--border-subtle);
  cursor: grab;
  user-select: none;

  &:active {
    cursor: grabbing;
  }
}

.panel-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
  letter-spacing: 0.02em;
}

.close-btn {
  background: none;
  border: none;
  color: var(--text-secondary);
  font-size: 1.2rem;
  cursor: pointer;
  padding: 0 4px;
  line-height: 1;
  border-radius: 4px;
  transition: color 0.15s, background 0.15s;

  &:hover {
    color: var(--text-primary);
    background: var(--surface-glass-hover);
  }
}

.tag-bar {
  display: flex;
  gap: 4px;
  padding: 8px 12px;
  overflow-x: auto;
  border-bottom: 1px solid var(--border-subtle);
  flex-shrink: 0;

  // 隐藏滚动条
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
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  padding: 10px 12px;
  overflow-y: auto;
  flex: 1;

  // 自定义滚动条
  scrollbar-width: thin;
  scrollbar-color: rgba(255,255,255,0.15) transparent;
}

.module-card {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--border-subtle);
  border-radius: 8px;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s;
  background: rgba(0, 0, 0, 0.15);

  &:hover {
    border-color: var(--accent-primary, #00aaff);
    transform: scale(1.02);
  }
}

.card-thumbnail {
  width: 100%;
  height: 100px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(10, 12, 20, 0.6);
  padding: 8px;

  img {
    max-width: 100%;
    max-height: 100%;
    object-fit: contain;
  }
}

.card-info {
  padding: 6px 8px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.card-name {
  font-size: 0.75rem;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.card-size {
  font-size: 0.65rem;
  color: var(--text-secondary);
  font-family: var(--font-mono, 'JetBrains Mono', monospace);
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
