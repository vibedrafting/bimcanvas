<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { LayerManager } from '../../services/three/LayerManager';
import GlassButton from './base/GlassButton.vue';

const isOpen = ref(false);
const managerRef = ref<HTMLElement | null>(null);

// Layer States
const layers = ref({
  [LayerManager.LAYER_GRID]: true, // Default to true for User mode
  [LayerManager.LAYER_ARCHITECTURE]: true, // 建筑图层默认启用
  [LayerManager.LAYER_FURNITURE]: true, // 模块图层默认启用
  [LayerManager.LAYER_LABELS]: false,
  [LayerManager.LAYER_BOUNDS]: false,
  [LayerManager.LAYER_OUTLINE]: false,
  [LayerManager.LAYER_SVG]: false,
  [LayerManager.LAYER_ZONES]: false,
  [LayerManager.LAYER_SEMANTIC]: false,
  [LayerManager.LAYER_AI_VISION]: false,
});

// Grid Spacing (600mm or 1000mm)
const gridSpacing = ref<600 | 1000>(1000);

const toggleLayer = (layerId: number) => {
  const isVisible = !layers.value[layerId];
  layers.value[layerId] = isVisible;
  window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', { 
    detail: { layerId, visible: isVisible } 
  }));
};

const onGridSpacingChange = () => {
  window.dispatchEvent(new CustomEvent('bimcanvas:grid-spacing-change', {
    detail: { spacing: gridSpacing.value }
  }));
};

const currentView = ref<'human' | 'ai'>('human');

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
};

// Close on click outside
const handleClickOutside = (event: MouseEvent) => {
  if (managerRef.value && !managerRef.value.contains(event.target as Node)) {
    isOpen.value = false;
  }
};

onMounted(() => {
  document.addEventListener('mousedown', handleClickOutside);

  // 监听视图模式切换事件
  window.addEventListener('bimcanvas:view-mode-change', ((e: CustomEvent) => {
    const mode = e.detail;
    currentView.value = mode; // 同步本地视图模式状态
    // 注意：不再在此处硬编码图层状态，由 layer-state-change 事件处理
  }) as EventListener);

  // 监听图层状态变化事件（来自 LayerManager.applyPresetFromConfig）
  window.addEventListener('bimcanvas:layer-state-change', ((e: CustomEvent) => {
    const { layerStates } = e.detail as { preset: string; layerStates: Record<number, boolean> };
    // 同步 UI 复选框状态
    Object.keys(layers.value).forEach(key => {
      const layerId = Number(key);
      if (layerStates[layerId] !== undefined) {
        layers.value[layerId] = layerStates[layerId];
      }
    });
    console.log('[FloatingLayerManager] UI 图层状态已同步:', layers.value);
  }) as EventListener);
});

onUnmounted(() => {
  document.removeEventListener('mousedown', handleClickOutside);
});
</script>

<template>
  <div 
    class="floating-layer-manager" 
    ref="managerRef"
    @mouseenter="isOpen = true"
    @mouseleave="isOpen = false"
  >
    <!-- Popover Menu -->
    <transition name="fade-slide">
      <div v-if="isOpen" class="layer-menu">
        <div class="menu-header">
          <span>View Layers</span>
        </div>
        <div class="menu-content">
          <!-- Vision Mode -->
          <div class="vision-section">
            <div class="vision-toggle">
              <button 
                class="vision-btn" 
                :class="{ active: currentView === 'human' }" 
                @click="toggleView('human')"
              >
                User
              </button>
              <button 
                class="vision-btn" 
                :class="{ active: currentView === 'ai' }" 
                @click="toggleView('ai')"
              >
                Agent
              </button>
            </div>
          </div>
          
          <div class="divider"></div>

          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_GRID]" @change="toggleLayer(LayerManager.LAYER_GRID)">
            <span>Grid</span>
            <select
              class="grid-spacing-select"
              v-model="gridSpacing"
              @change="onGridSpacingChange"
              @click.stop
            >
              <option :value="1000">1m</option>
              <option :value="600">600mm</option>
            </select>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_ARCHITECTURE]" @change="toggleLayer(LayerManager.LAYER_ARCHITECTURE)">
            <span>Architecture</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_FURNITURE]" @change="toggleLayer(LayerManager.LAYER_FURNITURE)">
            <span>Furniture</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_LABELS]" @change="toggleLayer(LayerManager.LAYER_LABELS)">
            <span>Labels</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_BOUNDS]" @change="toggleLayer(LayerManager.LAYER_BOUNDS)">
            <span>Bounds</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_OUTLINE]" @change="toggleLayer(LayerManager.LAYER_OUTLINE)">
            <span>Outline</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_SVG]" @change="toggleLayer(LayerManager.LAYER_SVG)">
            <span>SVG Preview</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_ZONES]" @change="toggleLayer(LayerManager.LAYER_ZONES)">
            <span>Zones</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_SEMANTIC]" @change="toggleLayer(LayerManager.LAYER_SEMANTIC)">
            <span>Semantic</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_AI_VISION]" @change="toggleLayer(LayerManager.LAYER_AI_VISION)">
            <span>AI Vision</span>
          </label>
        </div>
      </div>
    </transition>

    <!-- FAB Trigger -->
    <button class="fab-btn" :class="{ active: isOpen }" title="Layer Manager">
      <svg width="24" height="24" viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M12 2L2 7L12 12L22 7L12 2Z" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="M2 17L12 22L22 17" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        <path d="M2 12L12 17L22 12" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
      </svg>
    </button>
  </div>
</template>

<style scoped lang="scss">
.floating-layer-manager {
  position: fixed;
  bottom: 60px;
  left: 24px;   /* Moved to left side */
  z-index: 1000;
  display: flex;
  flex-direction: column;
  align-items: flex-start; /* Align to start (left) */
  gap: 12px;
}

.fab-btn {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  
  /* Premium Glass Tokens */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  /* Enhanced Border & Glow */
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  
  /* Glare & Deep Shadow */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow: 
    0 12px 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */
  background-origin: border-box;
  background-clip: padding-box, border-box;

  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);

  &:hover {
    background: var(--surface-glass-hover);
    color: var(--text-primary);
    transform: translateY(-2px);
    box-shadow: 0 6px 16px rgba(0, 0, 0, 0.15);
  }

  &.active {
    background: var(--accent-blue);
    color: #fff;
    border-color: transparent;
  }
}

.layer-menu {
  /* Premium Glass Tokens */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  /* Enhanced Border & Glow */
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  
  /* Glare & Deep Shadow */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow: 
    0 12px 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */
  background-origin: border-box;
  background-clip: padding-box, border-box;

  border-radius: 16px;
  padding: 0;
  min-width: 180px;
  overflow: hidden;
  transform-origin: bottom left; /* Animate from bottom left */

  .menu-header {
    padding: 12px 16px;
    border-bottom: 1px solid var(--border-subtle);
    font-size: 0.85rem;
    font-weight: 600;
    color: var(--text-secondary);
    letter-spacing: 0.5px;
    text-transform: uppercase;
  }

  .menu-content {
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 4px;

    .vision-section {
      padding: 0 4px 4px 4px;
      
      .vision-toggle {
        display: flex;
        gap: 4px;
        background: var(--surface-solid);
        padding: 4px;
        border-radius: 8px;

        .vision-btn {
          flex: 1;
          border: none;
          background: transparent;
          color: var(--text-secondary);
          padding: 6px 12px;
          border-radius: 6px;
          font-size: 0.85rem;
          cursor: pointer;
          transition: all 0.2s;
          font-family: inherit;

          &:hover {
            color: var(--text-primary);
            background: var(--surface-glass-hover);
          }

          &.active {
            background: var(--accent-blue);
            color: white;
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
          }
        }
      }
    }

    .divider {
      height: 1px;
      background: var(--border-subtle);
      margin: 4px 0;
    }
  }

  .layer-item {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px;
    cursor: pointer;
    border-radius: 6px;
    transition: background 0.2s;
    color: var(--text-secondary);
    font-size: 0.9rem;

    &:hover {
      background: var(--surface-glass-hover);
      color: var(--text-primary);
    }

    input[type="checkbox"] {
      appearance: none;
      -webkit-appearance: none;
      width: 18px;
      height: 18px;
      border: 1.5px solid var(--text-tertiary);
      border-radius: 5px;
      background: var(--surface-solid);
      cursor: pointer;
      position: relative;
      transition: all 0.2s ease;
      flex-shrink: 0; /* Prevent shrinking */
      
      &:checked {
        background: var(--accent-blue);
        border-color: var(--accent-blue);
        
        &::after {
          content: '';
          position: absolute;
          left: 5px;
          top: 2px;
          width: 5px;
          height: 9px;
          border: solid white;
          border-width: 0 2px 2px 0;
          transform: rotate(45deg);
        }
      }
      
      &:hover {
        border-color: var(--text-secondary);
      }
    }

    .grid-spacing-select {
      margin-left: auto;
      padding: 4px 8px;
      background: var(--surface-solid);
      border: 1px solid var(--border-subtle);
      border-radius: 4px;
      color: var(--text-secondary);
      font-size: 0.8rem;
      cursor: pointer;
      transition: all 0.2s;

      &:hover {
        border-color: var(--accent-blue);
        color: var(--text-primary);
      }

      &:focus {
        outline: none;
        border-color: var(--accent-blue);
      }
    }
  }
}

/* Transitions */
.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.fade-slide-enter-from,
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(10px) scale(0.95);
}
</style>
