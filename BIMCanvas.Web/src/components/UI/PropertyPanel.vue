<script setup lang="ts">
import { computed } from 'vue';
import { useCanvasStore } from '@/stores/canvasStore';

const store = useCanvasStore();

const selectedElement = computed(() => {
  if (!store.selectedElementId || !store.document) return null;
  
  // Search in all collections
  const collections = [
    { list: store.document.modules, type: 'Module' },
    { list: store.document.walls, type: 'Wall' },
    { list: store.document.columns, type: 'Column' },
    { list: store.document.zones, type: 'Zone' },
    { list: store.document.openings, type: 'Opening' }
  ];

  for (const { list, type } of collections) {
    if (!list) continue;
    const found = list.find((item: any) => item.id === store.selectedElementId);
    if (found) {
      return { ...found, type };
    }
  }
  return null;
});

const formatValue = (key: string, value: any) => {
  if (typeof value === 'number') return value.toFixed(2);
  if (Array.isArray(value)) return `[${value.length} items]`;
  if (typeof value === 'object') return '{...}';
  return value;
};
</script>

<template>
  <transition name="slide-fade">
    <div v-if="selectedElement" class="property-panel cyber-panel">
      <div class="header">
        <h3>PROPERTIES</h3>
        <div class="element-id">{{ selectedElement.id }}</div>
        <button class="close-btn" @click="store.select(null)">×</button>
      </div>
      
      <div class="content">
        <div class="prop-row">
          <span class="label">TYPE</span>
          <span class="value highlight">{{ selectedElement.type }}</span>
        </div>

        <template v-for="(value, key) in selectedElement" :key="key">
          <div class="prop-row" v-if="key !== 'id' && key !== 'type' && key !== 'polygon' && key !== 'bounds' && key !== 'innerBoundary'">
            <span class="label">{{ key.toString().toUpperCase() }}</span>
            <span class="value">{{ formatValue(key, value) }}</span>
          </div>
        </template>
      </div>
    </div>
  </transition>
</template>

<style scoped lang="scss">
.property-panel {
  position: fixed;
  top: 60px; /* Height of Toolbar */
  right: 20px;
  width: 300px;
  max-height: calc(100vh - 60px - 30px); /* Subtract Toolbar and StatusBar */
  overflow-y: auto;
  padding: 20px;
  z-index: 90; /* Lower than Toolbar (100) */
  
  .header {
    display: flex;
    justify-content: space-between;
    align-items: flex-start;
    border-bottom: 1px solid var(--neon-cyan);
    padding-bottom: 10px;
    margin-bottom: 15px;
    
    h3 {
      margin: 0;
      color: var(--neon-cyan);
      font-size: 1.2rem;
      letter-spacing: 2px;
    }
    
    .element-id {
      font-family: 'Courier New', monospace;
      font-size: 0.8rem;
      color: rgba(255, 255, 255, 0.6);
      margin-top: 5px;
    }

    .close-btn {
      background: none;
      border: none;
      color: var(--neon-cyan);
      font-size: 1.5rem;
      line-height: 1;
      cursor: pointer;
      padding: 0 5px;
      
      &:hover {
        color: var(--neon-pink);
      }
    }
  }
  
  .content {
    .prop-row {
      display: flex;
      justify-content: space-between;
      margin-bottom: 8px;
      font-size: 0.9rem;
      
      .label {
        color: rgba(255, 255, 255, 0.7);
      }
      
      .value {
        color: #fff;
        font-family: 'Courier New', monospace;
        text-align: right;
        
        &.highlight {
          color: var(--neon-pink);
          text-shadow: 0 0 5px var(--neon-pink);
        }
      }
    }
  }
}

.slide-fade-enter-active,
.slide-fade-leave-active {
  transition: all 0.3s ease-out;
}

.slide-fade-enter-from,
.slide-fade-leave-to {
  transform: translateX(20px);
  opacity: 0;
}
</style>
