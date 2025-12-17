<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue';
import { LayerManager } from '../../services/three/LayerManager';
import GlassButton from './base/GlassButton.vue';

const isOpen = ref(false);
const managerRef = ref<HTMLElement | null>(null);

// Layer States
const layers = ref({
  [LayerManager.LAYER_GRID]: false,
  [LayerManager.LAYER_LABELS]: false,
  [LayerManager.LAYER_BOUNDS]: false,
  [LayerManager.LAYER_SEMANTIC]: false,
  [LayerManager.LAYER_AXES]: false,
});

const toggleLayer = (layerId: number) => {
  const isVisible = !layers.value[layerId];
  layers.value[layerId] = isVisible;
  window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', { 
    detail: { layerId, visible: isVisible } 
  }));
};

// Close on click outside
const handleClickOutside = (event: MouseEvent) => {
  if (managerRef.value && !managerRef.value.contains(event.target as Node)) {
    isOpen.value = false;
  }
};

onMounted(() => {
  document.addEventListener('mousedown', handleClickOutside);
  
  // Listen for external layer updates (e.g. from View Mode toggle)
  // Ideally we should use a store, but for now we listen to events or just rely on the fact 
  // that this component is the main driver for these specific layers.
  // However, RibbonToolbar toggles View Mode which affects layers.
  // We should listen to 'bimcanvas:view-mode-change' to update UI state.
  window.addEventListener('bimcanvas:view-mode-change', ((e: CustomEvent) => {
    const mode = e.detail;
    if (mode === 'human') {
      Object.keys(layers.value).forEach(key => layers.value[key as any] = false);
    } else {
      Object.keys(layers.value).forEach(key => layers.value[key as any] = true);
    }
  }) as EventListener);
});

onUnmounted(() => {
  document.removeEventListener('mousedown', handleClickOutside);
});
</script>

<template>
  <div class="floating-layer-manager" ref="managerRef">
    <!-- Popover Menu -->
    <transition name="fade-slide">
      <div v-if="isOpen" class="layer-menu">
        <div class="menu-header">
          <span>View Layers</span>
        </div>
        <div class="menu-content">
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_GRID]" @change="toggleLayer(LayerManager.LAYER_GRID)">
            <span>Grid (1m)</span>
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
            <input type="checkbox" :checked="layers[LayerManager.LAYER_SEMANTIC]" @change="toggleLayer(LayerManager.LAYER_SEMANTIC)">
            <span>Semantic</span>
          </label>
          <label class="layer-item">
            <input type="checkbox" :checked="layers[LayerManager.LAYER_AXES]" @change="toggleLayer(LayerManager.LAYER_AXES)">
            <span>Axes</span>
          </label>
        </div>
      </div>
    </transition>

    <!-- FAB Trigger -->
    <button class="fab-btn" @click="isOpen = !isOpen" :class="{ active: isOpen }" title="Layer Manager">
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
  bottom: 20px;
  right: 20px;
  z-index: 1000;
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: 12px;
}

.fab-btn {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  background: rgba(30, 30, 35, 0.8);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  color: var(--text-secondary);
  display: flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);

  &:hover {
    background: rgba(40, 40, 45, 0.9);
    color: var(--text-primary);
    transform: translateY(-2px);
    box-shadow: 0 6px 16px rgba(0, 0, 0, 0.3);
  }

  &.active {
    background: var(--primary-color, #4a9eff);
    color: #fff;
    border-color: transparent;
  }
}

.layer-menu {
  background: rgba(20, 20, 25, 0.95);
  backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  padding: 0;
  min-width: 180px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.4);
  overflow: hidden;
  transform-origin: bottom right;

  .menu-header {
    padding: 12px 16px;
    border-bottom: 1px solid rgba(255, 255, 255, 0.05);
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
      background: rgba(255, 255, 255, 0.05);
      color: var(--text-primary);
    }

    input[type="checkbox"] {
      accent-color: var(--primary-color, #4a9eff);
      width: 16px;
      height: 16px;
      cursor: pointer;
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
