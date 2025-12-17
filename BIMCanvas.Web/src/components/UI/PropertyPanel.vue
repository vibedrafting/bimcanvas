<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { computed } from 'vue';

const store = useCanvasStore();

const selectedData = computed(() => store.selectedObject);

const properties = computed(() => {
  if (!selectedData.value) return [];
  
  // Flatten object for display, but keep it editable
  // For MVP, we only support editing top-level properties or specific known ones
  // Let's focus on 'facing' and 'bounds' (center?)
  // Actually, let's just show raw JSON for complex types, and inputs for simple ones
  return Object.entries(selectedData.value).map(([key, value]) => ({
    key,
    value,
    type: typeof value
  }));
});

const updateProperty = (key: string, newValue: any) => {
  if (!selectedData.value) return;
  
  // Parse numbers if needed
  let parsedValue = newValue;
  const originalValue = selectedData.value[key];
  if (typeof originalValue === 'number') {
      parsedValue = Number(newValue);
  }

  store.updateModule(selectedData.value.id, { [key]: parsedValue });
};

</script>

<template>
  <aside class="property-panel" v-if="selectedData">
    <header>
      <h2>Properties</h2>
    </header>
    <div class="content">
      <div v-for="prop in properties" :key="prop.key" class="prop-row">
        <span class="label">{{ prop.key }}</span>
        
        <!-- Editable Input for Strings/Numbers -->
        <input 
            v-if="prop.type === 'string' || prop.type === 'number'"
            :value="prop.value"
            @change="(e) => updateProperty(prop.key, (e.target as HTMLInputElement).value)"
            class="value-input"
        />
        
        <!-- Read-only for Objects/Arrays -->
        <span v-else class="value readonly">{{ JSON.stringify(prop.value) }}</span>
      </div>

    </div>
  </aside>
</template>

<style scoped lang="scss">
.property-panel {
  /* position: absolute; removed */
  /* right: 20px; removed */
  /* top: 80px; removed */
  width: 300px;
  height: 100%; /* Fill grid area */
  background: rgba(10, 10, 15, 0.9);
  backdrop-filter: blur(10px);
  border-left: 1px solid rgba(255, 255, 255, 0.1); /* Changed to border-left */
  /* border-radius: 8px; removed */
  color: #e0e0e0;
  overflow-y: auto; /* Allow scrolling */
  box-shadow: -4px 0 12px rgba(0, 0, 0, 0.3); /* Shadow to left */

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

      .value-input {
        background: rgba(255, 255, 255, 0.1);
        border: 1px solid rgba(255, 255, 255, 0.2);
        color: #e0e0e0;
        text-align: right;
        max-width: 60%;
        padding: 2px 4px;
        border-radius: 4px;
        font-family: inherit;
        font-size: inherit;

        &:focus {
            outline: none;
            border-color: #3b82f6;
            background: rgba(255, 255, 255, 0.15);
        }
      }

      .value.readonly {
        color: #888;
        font-style: italic;
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
