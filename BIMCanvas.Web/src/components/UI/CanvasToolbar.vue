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
</script>

<template>
  <header class="toolbar">
    <div class="brand">
      <h1>BIMCanvas.Web</h1>
      <IconBadge label="Calm Tech" />
    </div>
    
    <div class="actions">
      <div class="view-controls">
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
            <!-- Vision Mode Section -->
            <div class="dropdown-section">
              <span class="section-label">Vision Mode</span>
              <div class="vision-toggle-group">
                <GlassButton 
                  :active="currentView === 'human'" 
                  @click="toggleView('human')"
                  variant="ghost"
                  size="sm"
                >
                  Human
                </GlassButton>
                <GlassButton 
                  :active="currentView === 'ai'" 
                  @click="toggleView('ai')"
                  variant="ghost"
                  size="sm"
                >
                  AI Vision
                </GlassButton>
              </div>
            </div>
            
            <div class="dropdown-divider"></div>

            <!-- Layers Section -->
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
<content>
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
</script>

<template>
  <header class="toolbar">
    <div class="brand">
      <h1>BIMCanvas.Web</h1>
      <IconBadge label="Calm Tech" />
    </div>
    
    <div class="actions">
      <div class="view-controls">
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
            <!-- Vision Mode Section -->
            <div class="dropdown-section">
              <span class="section-label">Vision Mode</span>
              <div class="vision-toggle-group">
                <GlassButton 
                  :active="currentView === 'human'" 
                  @click="toggleView('human')"
                  variant="ghost"
                  size="sm"
                >
                  Human
                </GlassButton>
                <GlassButton 
                  :active="currentView === 'ai'" 
                  @click="toggleView('ai')"
                  variant="ghost"
                  size="sm"
                >
                  AI Vision
                </GlassButton>
              </div>
            </div>
            
            <div class="dropdown-divider"></div>

            <!-- Layers Section -->
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
            min-width: 180px; /* Increased width */
            backdrop-filter: blur(10px);
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
            display: flex;
            flex-direction: column;
            gap: var(--spacing-xs);

            .dropdown-section {
              display: flex;
              flex-direction: column;
              gap: 4px;
              padding-bottom: 4px;

              .section-label {
                font-size: 0.75rem;
                text-transform: uppercase;
                letter-spacing: 1px;
                color: var(--text-tertiary);
                padding-left: 4px;
                margin-bottom: 4px;
              }

              .vision-toggle-group {
                display: flex;
                gap: 4px;
                background: rgba(255, 255, 255, 0.05);
                padding: 4px;
                border-radius: var(--radius-sm);
                
                /* Make buttons fill space */
                :deep(button) {
                  flex: 1;
                  justify-content: center;
                }
              }
            }

            .dropdown-divider {
              height: 1px;
              background: var(--border-subtle);
              margin: 2px 0;
            }

            .layer-item {
              display: flex;
              align-items: center;
              gap: var(--spacing-sm);
              padding: 6px 8px; /* Slightly more padding */
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
</content>
