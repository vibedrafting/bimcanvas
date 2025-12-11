<script setup lang="ts">
import { useCanvasStore } from '@/stores/canvasStore';

const store = useCanvasStore();
</script>

<template>
  <footer class="status-bar cyber-panel">
    <div class="status-item">
      <span class="indicator" :class="store.connectionStatus">●</span>
      {{ store.connectionStatus.toUpperCase() }}
    </div>
    
    <div class="status-item" v-if="store.document">
      DOC: {{ store.document.id }} (v{{ store.document.version }})
    </div>

    <div class="status-item" v-if="store.selectedElementId">
      SELECTED: {{ store.selectedElementId }}
    </div>
  </footer>
</template>

<style scoped lang="scss">
.status-bar {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 30px;
  display: flex;
  align-items: center;
  gap: 20px;
  padding: 0 20px;
  font-size: 0.8rem;
  border-radius: 0;
  border-left: none;
  border-right: none;
  border-bottom: none;
  z-index: 100;

  .status-item {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--text-secondary);
  }

  .indicator {
    font-size: 1.2rem;
    line-height: 0;
    
    &.connected { color: var(--neon-cyan); text-shadow: 0 0 5px var(--neon-cyan); }
    &.disconnected { color: var(--text-secondary); }
    &.error { color: var(--neon-pink); text-shadow: 0 0 5px var(--neon-pink); }
  }
}
</style>
