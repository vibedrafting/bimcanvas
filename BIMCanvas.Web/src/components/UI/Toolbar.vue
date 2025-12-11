<script setup lang="ts">
import { onMounted } from 'vue';
import { useCanvasStore } from '@/stores/canvasStore';

const store = useCanvasStore();

const handleSync = () => {
  console.log('Sync to AI requested');
  // TODO: Implement sync logic
};

const handleDiscard = () => {
  store.discardChanges();
};

const handleLoadDemo = async () => {
  try {
    const response = await fetch('/demo/layout_proposal.json');
    const data = await response.json();
    store.setDocument(data);
    console.log('Loaded TestData.json', data);
  } catch (e) {
    console.error('Failed to load TestData.json', e);
  }
};

onMounted(() => {
  // Auto-load demo data for verification
  handleLoadDemo();
});
</script>

<template>
  <div class="toolbar cyber-panel">
    <div class="logo">BIMCanvas <span class="version">v2.8</span></div>
    
    <div class="actions">
      <button class="cyber-button" @click="handleLoadDemo">
        LOAD DEMO
      </button>
      <button class="cyber-button" @click="handleSync" :disabled="!store.hasChanges">
        SYNC TO AI
      </button>
      <button class="cyber-button warning" @click="handleDiscard" :disabled="!store.hasChanges">
        DISCARD
      </button>
    </div>
  </div>
</template>

<style scoped lang="scss">
.toolbar {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 60px;
  box-sizing: border-box; /* Prevent padding from adding to width */
  background: rgba(5, 5, 16, 0.9);
  border-bottom: 1px solid var(--neon-cyan);
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0 30px;
  z-index: 100;
  border-radius: 0;
  transform: none;

  .logo {
    font-size: 1.2rem;
    font-weight: bold;
    color: var(--neon-cyan);
    text-shadow: 0 0 10px var(--neon-cyan);
    
    .version {
      font-size: 0.8rem;
      color: var(--text-secondary);
      margin-left: 5px;
    }
  }

  .actions {
    display: flex;
    gap: 15px;
  }

  .warning {
    border-color: var(--neon-pink);
    color: var(--neon-pink);
    
    &:hover {
      background: rgba(255, 0, 255, 0.1);
      box-shadow: 0 0 15px var(--neon-pink);
      text-shadow: 0 0 5px var(--neon-pink);
    }
  }
}
</style>
