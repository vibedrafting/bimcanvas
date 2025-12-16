<script setup lang="ts">
import { ref } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';

const store = useCanvasStore();
const currentView = ref<'human' | 'ai'>('human');

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
};

const dispatchAction = (action: 'rotate' | 'delete') => {
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
      <span class="badge">Calm Tech</span>
    </div>
    
    <div class="actions">
      <button @click="handleLoadDemo('basic')" :disabled="store.isLoading">
        Load Room
      </button>
      <button @click="handleLoadDemo('proposal')" :disabled="store.isLoading">
        Load Proposal
      </button>
      
      <div class="view-toggle">
        <button 
          :class="{ active: currentView === 'human' }" 
          @click="toggleView('human')"
        >
          Human
        </button>
        <button 
          :class="{ active: currentView === 'ai' }" 
          @click="toggleView('ai')"
        >
          AI Vision
        </button>
      </div>

      <div class="divider"></div>
      
      <button class="tool-btn" @click="store.undo()" :disabled="!store.canUndo" title="Undo">
        Undo
      </button>
      <button class="tool-btn" @click="store.redo()" :disabled="!store.canRedo" title="Redo">
        Redo
      </button>

      <div class="divider"></div>

      <button class="tool-btn" @click="dispatchAction('rotate')" :disabled="!store.selectedObject" title="Rotate (R)">
        Rotate
      </button>
      <button class="tool-btn danger" @click="dispatchAction('delete')" :disabled="!store.selectedObject" title="Delete (Del)">
        Delete
      </button>
    </div>

  </header>
</template>

<style scoped lang="scss">
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 2rem;
  background: rgba(10, 10, 15, 0.8);
  backdrop-filter: blur(10px);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);

  .brand {
    display: flex;
    align-items: center;
    gap: 1rem;

    h1 {
      font-size: 1.2rem;
      font-weight: 500;
      color: #e0e0e0;
      margin: 0;
      letter-spacing: 0.5px;
    }

    .badge {
      font-size: 0.75rem;
      padding: 0.25rem 0.5rem;
      background: rgba(59, 130, 246, 0.1);
      color: #3b82f6;
      border-radius: 4px;
      border: 1px solid rgba(59, 130, 246, 0.2);
    }
  }

  .actions {
    display: flex;
    gap: 1rem;
    align-items: center;

    .divider {
      width: 1px;
      height: 24px;
      background: rgba(255, 255, 255, 0.1);
      margin: 0 0.5rem;
    }

    button {
      background: rgba(255, 255, 255, 0.05);
      border: 1px solid rgba(255, 255, 255, 0.1);
      color: #e0e0e0;
      padding: 0.5rem 1rem;
      border-radius: 6px;
      cursor: pointer;
      transition: all 0.2s ease;
      font-size: 0.9rem;

      &:hover:not(:disabled) {
        background: rgba(255, 255, 255, 0.1);
        border-color: rgba(255, 255, 255, 0.2);
      }

      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      &.danger {
        color: #ff6b6b;
        border-color: rgba(255, 107, 107, 0.3);

        &:hover:not(:disabled) {
          background: rgba(255, 107, 107, 0.1);
          border-color: rgba(255, 107, 107, 0.5);
        }
      }
    }


    .view-toggle {
      display: flex;
      background: rgba(255, 255, 255, 0.05);
      border-radius: 6px;
      padding: 2px;
      margin-left: 1rem;
      border: 1px solid rgba(255, 255, 255, 0.1);

      button {
        background: transparent;
        border: none;
        padding: 0.25rem 0.75rem;
        font-size: 0.8rem;
        color: #888;
        
        &:hover {
          background: rgba(255, 255, 255, 0.05);
          color: #ccc;
        }

        &.active {
          background: rgba(59, 130, 246, 0.2);
          color: #3b82f6;
          font-weight: 500;
        }
      }
    }
  }
}
</style>
