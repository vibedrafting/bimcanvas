<script setup lang="ts">
import { onMounted, ref, onUnmounted } from 'vue';
import { ThreeSceneService } from './services/three/ThreeSceneService';
import { useCanvasStore } from './stores/canvasStore';

const canvasContainer = ref<HTMLElement | null>(null);
let sceneService: ThreeSceneService | null = null;
const store = useCanvasStore();

onMounted(() => {
  try {
    if (canvasContainer.value) {
      console.log('App: Initializing ThreeSceneService...');
      sceneService = new ThreeSceneService(canvasContainer.value);
      sceneService.animate();
      console.log('App: ThreeSceneService initialized successfully.');
    } else {
      console.error('App: Canvas container is null.');
    }
  } catch (error) {
    console.error('App: Failed to initialize ThreeSceneService:', error);
  }
});

onUnmounted(() => {
  if (sceneService) {
    sceneService.dispose();
  }
});

const handleLoadDemo = async (type: 'basic' | 'proposal') => {
  const url = type === 'basic' 
    ? '/demo/basic_structure.json' 
    : '/demo/layout_proposal.json';
  await store.loadDemoData(url);
};
</script>

<template>
  <div class="app-container">
    <div ref="canvasContainer" class="canvas-container"></div>
    
    <div class="ui-overlay">
      <header class="toolbar">
        <div class="brand">
          <h1>BIMCanvas.Web</h1>
          <span class="badge">Calm Tech Mode</span>
        </div>
        <div class="actions">
          <button @click="handleLoadDemo('basic')" :disabled="store.isLoading">
            Load Room Boundaries
          </button>
          <button @click="handleLoadDemo('proposal')" :disabled="store.isLoading">
            Load Layout Proposal
          </button>
        </div>
      </header>
    </div>

    <div v-if="store.isLoading" class="loading-overlay">
      <div class="spinner"></div>
      <p>Loading Calm Tech Space...</p>
    </div>
  </div>
</template>

<style scoped lang="scss">
.app-container {
  position: relative;
  width: 100%;
  height: 100%;
}

.canvas-container {
  width: 100%;
  height: 100%;
  display: block;
}

.ui-overlay {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  pointer-events: none; // Let clicks pass through to canvas
  
  .toolbar {
    pointer-events: auto; // Re-enable clicks for toolbar
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 1rem 2rem;
    background: rgba(10, 10, 15, 0.8); // Glassmorphism base
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
      }
    }
  }
}

.loading-overlay {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background: rgba(10, 10, 15, 0.8);
  backdrop-filter: blur(5px);
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  color: #e0e0e0;
  z-index: 100;

  .spinner {
    width: 40px;
    height: 40px;
    border: 3px solid rgba(59, 130, 246, 0.3);
    border-top-color: #3b82f6;
    border-radius: 50%;
    animation: spin 1s linear infinite;
    margin-bottom: 1rem;
  }
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
