<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { computed, ref } from 'vue';

const store = useCanvasStore();
const isExpanded = ref(false); // Default collapsed

const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;
};

const selectedData = computed(() => store.selectedObject);

// Project Properties (Placeholder)
const projectProperties = [
    { key: 'Project', value: 'BIMCanvas Demo', type: 'string' },
    { key: 'Version', value: 'v2.7', type: 'string' },
    { key: 'Renderer', value: 'Three.js', type: 'string' },
    { key: 'Mode', value: 'Web Client', type: 'string' }
];

const properties = computed(() => {
  if (!selectedData.value) return projectProperties;
  
  // Flatten object for display, but keep it editable
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
  <aside 
    class="property-panel" 
    :class="{ expanded: isExpanded }"
    @mouseenter="isExpanded = true"
    @mouseleave="isExpanded = false"
  >
    <div class="header" @click="toggleExpand">
      <span class="label">Properties</span>
      <span class="indicator">›</span>
    </div>

    <div class="content" v-if="isExpanded">
        <header class="panel-header">
            <h2>{{ selectedData ? 'Selection' : 'Project' }}</h2>
        </header>
        
        <div class="prop-list">
            <div v-for="prop in properties" :key="prop.key" class="prop-row">
                <span class="label">{{ prop.key }}</span>
                
                <!-- Editable Input for Strings/Numbers (Only if selected) -->
                <input 
                    v-if="selectedData && (prop.type === 'string' || prop.type === 'number')"
                    :value="prop.value"
                    @change="(e) => updateProperty(prop.key, (e.target as HTMLInputElement).value)"
                    class="value-input"
                />
                
                <!-- Read-only for Objects/Arrays or Project Props -->
                <span v-else class="value readonly">{{ typeof prop.value === 'object' ? JSON.stringify(prop.value) : prop.value }}</span>
            </div>
        </div>
    </div>
  </aside>
</template>

<style scoped lang="scss">
.property-panel {
  width: 48px; /* Collapsed state */
  height: calc(100% - 80px); /* Fill grid area minus top and bottom margin */
  margin-top: 40px; /* Avoid overlap with top bar */
  margin-bottom: 40px; /* Symmetric bottom margin */
  background: var(--surface-glass);
  backdrop-filter: blur(10px);
  border-right: 1px solid var(--border-subtle); /* Border right for left panel */
  display: flex;
  flex-direction: column;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  overflow: hidden;
  z-index: 90;
  
  /* Left aligned behavior */
  margin-right: auto;
  
  &.expanded {
    width: 300px;
    background: rgba(10, 10, 15, 0.9);
    border-color: rgba(255, 255, 255, 0.1);
    box-shadow: 10px 0 30px rgba(0, 0, 0, 0.3); /* Shadow to right */

    .header .indicator {
      transform: rotate(180deg);
    }
  }

  .header {
    height: 100%;
    width: 48px;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding-top: var(--spacing-lg);
    cursor: pointer;
    flex-shrink: 0;
    
    /* Keep header on the left side */
    position: absolute;
    left: 0;
    top: 0;

    .label {
      writing-mode: vertical-rl;
      text-orientation: mixed;
      color: var(--text-secondary);
      font-size: 0.8rem;
      letter-spacing: 2px;
      font-family: var(--font-sans);
      text-transform: uppercase;
      white-space: nowrap;
      transition: color 0.2s;
    }

    .indicator {
      margin-top: var(--spacing-sm);
      color: var(--text-secondary);
      font-size: 1.2rem;
      transition: transform 0.3s;
    }

    &:hover .label {
      color: var(--text-primary);
    }
  }

  .content {
    position: absolute;
    left: 48px; /* Content to the right of header */
    top: 0;
    width: calc(100% - 48px);
    height: 100%;
    display: flex;
    flex-direction: column;
    opacity: 0;
    animation: fadeIn 0.3s forwards 0.1s;
    
    .panel-header {
        padding: 1rem;
        background: rgba(255, 255, 255, 0.05);
        border-bottom: 1px solid rgba(255, 255, 255, 0.05);

        h2 {
            margin: 0;
            font-size: 1rem;
            font-weight: 500;
            color: #e0e0e0;
        }
    }

    .prop-list {
        padding: 1rem;
        display: flex;
        flex-direction: column;
        gap: 0.5rem;
        overflow-y: auto;

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
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateX(-10px); }
  to { opacity: 1; transform: translateX(0); }
}
</style>
