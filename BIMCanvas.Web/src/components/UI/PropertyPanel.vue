<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { computed } from 'vue';

const store = useCanvasStore();

const selectedData = computed(() => store.selectedObject);

const properties = computed(() => {
  if (!selectedData.value) return [];
  
  // Flatten object for display
  return Object.entries(selectedData.value).map(([key, value]) => ({
    key,
    value: typeof value === 'object' ? JSON.stringify(value) : value
  }));
});
</script>

<template>
  <aside class="property-panel" v-if="selectedData">
    <header>
      <h2>Properties</h2>
    </header>
    <div class="content">
      <div v-for="prop in properties" :key="prop.key" class="prop-row">
        <span class="label">{{ prop.key }}</span>
        <span class="value">{{ prop.value }}</span>
      </div>
    </div>
  </aside>
</template>

<style scoped lang="scss">
.property-panel {
  position: absolute;
  right: 20px;
  top: 80px;
  width: 300px;
  background: rgba(10, 10, 15, 0.9);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  color: #e0e0e0;
  overflow: hidden;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);

  header {
    padding: 1rem;
    background: rgba(255, 255, 255, 0.05);
    border-bottom: 1px solid rgba(255, 255, 255, 0.05);

    h2 {
      margin: 0;
      font-size: 1rem;
      font-weight: 500;
    }
  }

  .content {
    padding: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;

    .prop-row {
      display: flex;
      justify-content: space-between;
      font-size: 0.9rem;
      padding: 0.25rem 0;
      border-bottom: 1px solid rgba(255, 255, 255, 0.05);

      &:last-child {
        border-bottom: none;
      }

      .label {
        color: #888;
      }

      .value {
        color: #ccc;
        text-align: right;
        max-width: 60%;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
    }
  }
}
</style>
