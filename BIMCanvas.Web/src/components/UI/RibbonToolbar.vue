<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';

import { storeToRefs } from 'pinia';

const store = useCanvasStore();
const { selectedObject } = storeToRefs(store);
const isExpanded = ref(false);

// Load Demo
const handleLoadDemo = async () => {
  await store.loadDemoData('/demo/layout_proposal.json');
};

// Actions
const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};

// View Logic
const currentView = ref<'human' | 'ai'>('human');

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
};

// Auto-expand on selection for a brief moment (optional, but nice)
/*
watch(selectedObject, (newVal) => {
  if (newVal) {
    // Flash expand could be annoying, let's just update text for now
    // isExpanded.value = true; 
    // setTimeout(() => isExpanded.value = false, 2000);
  }
});
*/
</script>

<template>
  <div class="toolbar-container">
    <!-- Top Bar: Branding & Quick Access (Transparent, Top-aligned) -->
    <div class="top-bar">
      <div class="brand-area">
        <span class="brand-text">BIMCanvas</span>
        <div class="divider"></div>
        <GlassButton @click="store.undo()" :disabled="!store.canUndo" variant="ghost" title="Undo" class="icon-btn">
          ↩
        </GlassButton>
        <GlassButton @click="store.redo()" :disabled="!store.canRedo" variant="ghost" title="Redo" class="icon-btn">
          ↪
        </GlassButton>
        <div class="divider"></div>
        <GlassButton @click="handleLoadDemo" :disabled="store.isLoading" variant="ghost">
          Load Example
        </GlassButton>
      </div>
    </div>

    <!-- Dynamic Command Island -->
    <div 
      class="command-island" 
      :class="{ expanded: isExpanded }"
      @mouseenter="isExpanded = true"
      @mouseleave="isExpanded = false"
    >
      
      <!-- Collapsed View -->
      <div class="island-collapsed" v-show="!isExpanded">
        <div class="status-indicator" :class="{ active: !!store.selectedObject }"></div>
        <span class="status-text">
          {{ store.selectedObject ? `Selected: ${store.selectedObject.userData?.type || 'Object'}` : 'BIMCanvas Ready' }}
        </span>
      </div>

      <!-- Expanded View -->
      <div class="island-expanded" v-show="isExpanded">
        <!-- BASIC Group -->
        <div class="group">
          <GlassButton variant="ghost" title="Select" active class="compact-btn">
            <span class="icon">↖</span>
          </GlassButton>
        </div>

        <div class="divider-vertical"></div>

        <!-- TRANSFORM Group -->
        <div class="group">
          <GlassButton @click="dispatchAction('move')" :disabled="!store.selectedObject" variant="ghost" class="compact-btn">
            <span class="icon">✥</span> Move
          </GlassButton>
          <GlassButton @click="dispatchAction('rotate')" :disabled="!store.selectedObject" variant="ghost" class="compact-btn">
            <span class="icon">↻</span> Rotate
          </GlassButton>
          <GlassButton @click="dispatchAction('delete')" :disabled="!store.selectedObject" variant="danger" class="compact-btn">
            <span class="icon">🗑</span> Delete
          </GlassButton>
        </div>

        <div class="divider-vertical"></div>

        <!-- VISION Group -->
        <div class="group">
          <GlassButton :active="currentView === 'human'" @click="toggleView('human')" variant="ghost" class="compact-btn">
             Human
          </GlassButton>
          <GlassButton :active="currentView === 'ai'" @click="toggleView('ai')" variant="ghost" class="compact-btn">
             AI Vision
          </GlassButton>
        </div>
      </div>

    </div>
    
    <!-- DEBUG OVERLAY -->
    <div style="position:fixed; top:10px; left:10px; color:lime; z-index:9999; background:rgba(0,0,0,0.8); padding:5px;">
      DEBUG: {{ store.selectedObject ? 'OBJ' : 'NULL' }} | {{ store.selectedObject?.userData?.type }} <br>
      MSG: {{ store.debugMsg }} <br>
      STORE ID: {{ store.instanceId }}
    </div>
  </div>
</template>

<style scoped lang="scss">
.toolbar-container {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 0;
  z-index: 100;
  pointer-events: none;
}

.top-bar {
  display: flex;
  align-items: center;
  height: 40px;
  padding: 0 var(--spacing-md);
  background: linear-gradient(to bottom, rgba(0,0,0,0.4) 0%, rgba(0,0,0,0) 100%);
  pointer-events: auto;

  .brand-area {
    display: flex;
    align-items: center;
    gap: var(--spacing-sm);

    .brand-text {
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-sm);
      color: rgba(255, 255, 255, 0.9);
      text-shadow: 0 1px 2px rgba(0,0,0,0.5);
    }
  }
}

.command-island {
  position: absolute;
  top: 60px;
  left: 50%;
  transform: translateX(-50%);
  
  /* Dynamic Sizing */
  min-width: 160px;
  height: 36px;
  
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0 16px;
  
  /* Glassmorphism */
  background: rgba(20, 20, 25, 0.85);
  backdrop-filter: blur(20px) saturate(180%);
  -webkit-backdrop-filter: blur(20px) saturate(180%);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 100px;
  box-shadow: 
    0 8px 32px rgba(0, 0, 0, 0.4),
    0 2px 8px rgba(0, 0, 0, 0.2),
    inset 0 1px 0 rgba(255, 255, 255, 0.1);
    
  pointer-events: auto;
  transition: all 0.4s cubic-bezier(0.25, 0.8, 0.25, 1);
  overflow: hidden;

  /* Expanded State Override */
  &.expanded {
    height: 52px;
    min-width: 480px; /* Approximate width for full toolbar */
    padding: 0 20px;
    background: rgba(25, 25, 30, 0.95);
    box-shadow: 
      0 12px 40px rgba(0, 0, 0, 0.6),
      0 4px 12px rgba(0, 0, 0, 0.3),
      inset 0 1px 0 rgba(255, 255, 255, 0.15);
  }

  /* Collapsed Content */
  .island-collapsed {
    display: flex;
    align-items: center;
    gap: 10px;
    white-space: nowrap;
    
    .status-indicator {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: rgba(255, 255, 255, 0.3);
      box-shadow: 0 0 4px rgba(255, 255, 255, 0.2);
      transition: all 0.3s;
      
      &.active {
        background: #4a9eff;
        box-shadow: 0 0 8px #4a9eff;
      }
    }
    
    .status-text {
      font-size: 0.85rem;
      color: rgba(255, 255, 255, 0.8);
      font-weight: 500;
      letter-spacing: 0.3px;
    }
  }

  /* Expanded Content */
  .island-expanded {
    display: flex;
    align-items: center;
    gap: var(--spacing-md);
    width: 100%;
    justify-content: center;
    animation: fadeIn 0.3s ease-out forwards;
  }
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(5px); }
  to { opacity: 1; transform: translateY(0); }
}

.group {
  display: flex;
  gap: 4px;
  align-items: center;
}

.divider {
  width: 1px;
  height: 14px;
  background: rgba(255, 255, 255, 0.2);
  margin: 0 var(--spacing-xs);
}

.divider-vertical {
  width: 1px;
  height: 20px;
  background: rgba(255, 255, 255, 0.1);
  margin: 0 var(--spacing-xs);
}

.icon-btn {
  padding: 4px 8px;
  font-size: 1.1rem;
}

.compact-btn {
  padding: 6px 12px;
  font-size: 0.9rem;
  border-radius: 20px !important;
  
  .icon {
    margin-right: 6px;
    font-size: 1.1em;
  }
}
</style>
