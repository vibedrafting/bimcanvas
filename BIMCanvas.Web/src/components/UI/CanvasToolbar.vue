<script setup lang="ts">
import { ref } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import IconBadge from './base/IconBadge.vue';

const store = useCanvasStore();
const currentView = ref<'human' | 'ai'>('human');

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
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

    .view-toggle {
      display: flex;
      background: var(--surface-glass);
      border-radius: var(--radius-md);
      padding: 2px;
      margin-left: var(--spacing-md);
      border: 1px solid var(--border-subtle);
      gap: 2px;
    }
  }
}
</style>
