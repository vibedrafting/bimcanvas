# UI Layout and Style Backup (2025-12-17)

This document preserves the layout and styles of the "Layer Manager", "Human/AI Vision", and "Undo/Redo" buttons.
**Note**: As of this backup, `LayerControl.vue` has been merged into `CanvasToolbar.vue`.

## 1. Toolbar & Layer Manager (CanvasToolbar.vue)

This component now handles the main toolbar, including the "Human/AI Vision" toggle, "Undo/Redo" buttons, and the "Layer Manager" dropdown.

```vue
<script setup lang="ts">
import { ref, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import IconBadge from './base/IconBadge.vue';
import { LayerManager } from '../../services/three/LayerManager';

const store = useCanvasStore();
const currentView = ref<'human' | 'ai'>('human');
const showLayerMenu = ref(false);

// Layer States
const layers = ref({
  [LayerManager.LAYER_GRID]: false,
  [LayerManager.LAYER_LABELS]: false,
  [LayerManager.LAYER_BOUNDS]: false,
  [LayerManager.LAYER_SEMANTIC]: false,
  [LayerManager.LAYER_AXES]: false,
});

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
  
  // Update local layer state based on preset
  if (mode === 'human') {
    Object.keys(layers.value).forEach(key => layers.value[key as any] = false);
  } else {
    Object.keys(layers.value).forEach(key => layers.value[key as any] = true);
  }
};

const toggleLayer = (layerId: number) => {
  const isVisible = !layers.value[layerId];
  layers.value[layerId] = isVisible;
  window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', { 
    detail: { layerId, visible: isVisible } 
  }));
  
  // If manual toggle, we might drift from preset, but that's fine.
  // We could reset currentView to null if it doesn't match preset, but keeping it simple.
};

const dispatchAction = (action: 'rotate' | 'delete' | 'move') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};

const handleLoadDemo = async (type: 'basic' | 'proposal') => {
  const url = type === 'basic' 
    ? '/demo/basic_structure.json' 
    : '/demo/layout_proposal.json';
  await store.loadDemoData(url);
};
</script>

<template>
  <header class="toolbar">
    <div class="brand">
      <h1>BIMCanvas.Web</h1>
      <IconBadge label="Calm Tech" />
    </div>
    
    <div class="actions">
      <GlassButton @click="handleLoadDemo('basic')" :disabled="store.isLoading">
        Load Room
      </GlassButton>
      <GlassButton @click="handleLoadDemo('proposal')" :disabled="store.isLoading">
        Load Proposal
      </GlassButton>
      
      <div class="view-controls">
        <div class="view-toggle">
          <GlassButton 
            :active="currentView === 'human'" 
            @click="toggleView('human')"
            variant="ghost"
          >
            Human
          </GlassButton>
          <GlassButton 
            :active="currentView === 'ai'" 
            @click="toggleView('ai')"
            variant="ghost"
          >
            AI Vision
          </GlassButton>
        </div>

        <div class="layer-manager">
          <GlassButton 
            @click="showLayerMenu = !showLayerMenu" 
            variant="ghost" 
            :active="showLayerMenu"
            title="View Options"
          >
            <span class="icon">⚙️</span>
          </GlassButton>
          
          <div v-if="showLayerMenu" class="layer-dropdown">
            <div class="layer-item" @click="toggleLayer(LayerManager.LAYER_GRID)">
              <input type="checkbox" :checked="layers[LayerManager.LAYER_GRID]" />
              <span>Grid (1m)</span>
            </div>
            <div class="layer-item" @click="toggleLayer(LayerManager.LAYER_LABELS)">
              <input type="checkbox" :checked="layers[LayerManager.LAYER_LABELS]" />
              <span>Labels</span>
            </div>
            <div class="layer-item" @click="toggleLayer(LayerManager.LAYER_BOUNDS)">
              <input type="checkbox" :checked="layers[LayerManager.LAYER_BOUNDS]" />
              <span>Bounds</span>
            </div>
            <div class="layer-item" @click="toggleLayer(LayerManager.LAYER_SEMANTIC)">
              <input type="checkbox" :checked="layers[LayerManager.LAYER_SEMANTIC]" />
              <span>Semantic</span>
            </div>
            <div class="layer-item" @click="toggleLayer(LayerManager.LAYER_AXES)">
              <input type="checkbox" :checked="layers[LayerManager.LAYER_AXES]" />
              <span>Axes</span>
            </div>
          </div>
        </div>
      </div>

      <div class="divider"></div>
      
      <GlassButton @click="store.undo()" :disabled="!store.canUndo" title="Undo">
        Undo
      </GlassButton>
      <GlassButton @click="store.redo()" :disabled="!store.canRedo" title="Redo">
        Redo
      </GlassButton>

      <div class="divider"></div>

      <GlassButton @click="dispatchAction('rotate')" :disabled="!store.selectedObject" title="Rotate (R)">
        Rotate
      </GlassButton>
      <GlassButton @click="dispatchAction('move')" :disabled="!store.selectedObject" title="Nudge (Arrows)">
        Move
      </GlassButton>
      <GlassButton 
        variant="danger"
        @click="dispatchAction('delete')" 
        :disabled="!store.selectedObject" 
        title="Delete (Del)"
      >
        Delete
      </GlassButton>
    </div>

  </header>
</template>

<style scoped lang="scss">
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--spacing-md) var(--spacing-lg);
  background: rgba(10, 10, 15, 0.8); /* Keep a bit of opacity for the bar itself */
  backdrop-filter: blur(10px);
  border-bottom: 1px solid var(--border-subtle);
  z-index: 100;

  .brand {
    display: flex;
    align-items: center;
    gap: var(--spacing-md);

    h1 {
      font-size: 1.2rem;
      font-weight: 500;
      color: var(--text-primary);
      margin: 0;
      letter-spacing: 0.5px;
      font-family: var(--font-sans);
    }
  }

  .actions {
    display: flex;
    gap: var(--spacing-sm);
    align-items: center;

    .divider {
      width: 1px;
      height: 24px;
      background: var(--border-subtle);
      margin: 0 var(--spacing-xs);
    }

    .view-controls {
      display: flex;
      align-items: center;
      gap: var(--spacing-sm);
      
      .view-toggle {
        display: flex;
        background: var(--surface-glass);
        border-radius: var(--radius-md);
        padding: 2px;
        border: 1px solid var(--border-subtle);
        gap: 2px;
      }

      .layer-manager {
        position: relative;
        
        .layer-dropdown {
          position: absolute;
          top: 100%;
          right: 0;
          margin-top: var(--spacing-xs);
          background: rgba(20, 20, 25, 0.95);
          border: 1px solid var(--border-subtle);
          border-radius: var(--radius-md);
          padding: var(--spacing-sm);
          min-width: 150px;
          backdrop-filter: blur(10px);
          box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
          display: flex;
          flex-direction: column;
          gap: var(--spacing-xs);

          .layer-item {
            display: flex;
            align-items: center;
            gap: var(--spacing-sm);
            padding: 4px 8px;
            cursor: pointer;
            border-radius: var(--radius-sm);
            color: var(--text-secondary);
            font-size: 0.9rem;
            
            &:hover {
              background: rgba(255, 255, 255, 0.1);
              color: var(--text-primary);
            }

            input {
              cursor: pointer;
            }
          }
        }
      }
    }
  }
}
</style>
```

## 2. Base Button Component (GlassButton.vue)

```vue
<script setup lang="ts">
import { computed } from 'vue';

interface Props {
  variant?: 'primary' | 'ghost' | 'danger';
  active?: boolean;
  disabled?: boolean;
  title?: string;
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'ghost',
  active: false,
  disabled: false,
});

const emit = defineEmits<{
  (e: 'click', event: MouseEvent): void;
}>();

const classes = computed(() => {
  return [
    'glass-btn',
    `variant-${props.variant}`,
    { active: props.active }
  ];
});
</script>

<template>
  <button 
    :class="classes" 
    :disabled="disabled" 
    :title="title"
    @click="emit('click', $event)"
  >
    <slot></slot>
  </button>
</template>

<style scoped>
.glass-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-md);
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  border: 1px solid transparent;
  outline: none;
  color: var(--text-primary);
  background: transparent;
  
  /* Glass Effect Base */
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
}

.glass-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  pointer-events: none;
}

/* Variants */

/* Ghost (Default) */
.glass-btn.variant-ghost {
  background: var(--surface-glass);
  border-color: var(--border-subtle);
}

.glass-btn.variant-ghost:hover:not(:disabled) {
  background: var(--surface-glass-hover);
  border-color: rgba(255, 255, 255, 0.2);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.glass-btn.variant-ghost:active:not(:disabled) {
  transform: translateY(0);
}

.glass-btn.variant-ghost.active {
  background: rgba(59, 130, 246, 0.15);
  border-color: var(--accent-blue);
  color: var(--accent-blue);
  box-shadow: 0 0 10px var(--accent-glow);
}

/* Primary */
.glass-btn.variant-primary {
  background: rgba(59, 130, 246, 0.2);
  border-color: rgba(59, 130, 246, 0.4);
  color: #fff;
}

.glass-btn.variant-primary:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.3);
  border-color: var(--accent-blue);
  box-shadow: 0 0 15px var(--accent-glow);
  transform: translateY(-1px);
}

/* Danger */
.glass-btn.variant-danger {
  background: rgba(255, 107, 107, 0.1);
  border-color: rgba(255, 107, 107, 0.3);
  color: var(--accent-danger);
}

.glass-btn.variant-danger:hover:not(:disabled) {
  background: rgba(255, 107, 107, 0.2);
  border-color: var(--accent-danger);
  box-shadow: 0 0 10px var(--accent-danger-glow);
}
</style>
```

## 3. Icon Badge Component (IconBadge.vue)

Used in the toolbar brand section.

```vue
<script setup lang="ts">
interface Props {
  label: string;
  icon?: string; // Optional icon class or name
}

defineProps<Props>();
</script>

<template>
  <div class="icon-badge">
    <span v-if="icon" class="icon">{{ icon }}</span>
    <span class="label">{{ label }}</span>
  </div>
</template>

<style scoped>
.icon-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 2px var(--spacing-sm);
  border-radius: var(--radius-sm);
  font-family: var(--font-sans);
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.5px;
  
  /* Style */
  background: rgba(59, 130, 246, 0.1);
  border: 1px solid rgba(59, 130, 246, 0.2);
  color: var(--accent-blue);
  
  /* Glow */
  box-shadow: 0 0 5px rgba(59, 130, 246, 0.05);
}

.icon {
  font-size: 1.1em;
}
</style>
```
