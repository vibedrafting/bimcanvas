<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { computed, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';

const store = useCanvasStore();
const { currentOperation, selectedIds } = storeToRefs(store);
const isExpanded = ref(false); // Default collapsed

const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;
};

const selectedObject = computed(() => store.selectedObject);
const selectionCount = computed(() => store.selectedIds.length);

// 是否处于编辑模式（移动/旋转）
const isInEditMode = computed(() => {
    return currentOperation.value === 'moving' || currentOperation.value === 'rotating';
});

// 需求1：进入编辑模式时自动收起面板
watch(currentOperation, (newVal) => {
    if (newVal === 'moving' || newVal === 'rotating') {
        isExpanded.value = false;
    }
});

// 需求2：根据选择集大小控制面板展开/收起
// - 单选（selectedIds.length === 1）：展开
// - 多选（selectedIds.length > 1）：折叠
// - 无选择（selectedIds.length === 0）：折叠
// - 编辑模式：不自动展开
watch(selectedIds, (newIds) => {
    // 如果处于编辑模式，不自动展开
    if (isInEditMode.value) {
        return;
    }
    
    // 只有单选时展开面板
    if (newIds.length === 1) {
        isExpanded.value = true;
    } else {
        // 多选或无选择时折叠
        isExpanded.value = false;
    }
}, { deep: true });

// Project Properties
const projectProperties = computed(() => {
    const doc = store.document;
    if (!doc) return [];
    return [
        { key: 'Project ID', value: doc.id, readonly: true },
        { key: 'Version', value: `v${doc.version}`, readonly: true },
        { key: 'Coordinate System', value: doc.coordinateSystem, readonly: true },
        { key: 'Walls', value: doc.walls?.length || 0, readonly: true },
        { key: 'Modules', value: doc.modules?.length || 0, readonly: true },
    ];
});

const properties = computed(() => {
  // 多选模式：显示简化信息
  if (selectionCount.value > 1) {
    return [
      { key: 'Selection', value: `${selectionCount.value} objects selected`, readonly: true },
      { key: 'Tip', value: 'Use Move/Rotate to edit', readonly: true },
    ];
  }

  if (!selectedObject.value) return projectProperties.value;
  
  const obj = selectedObject.value;
  const type = obj.type || 'Unknown';
  const data = obj.data || obj; // Some objects might have data nested, others might be direct

  const props = [
      { key: 'ID', value: obj.id, readonly: true },
      { key: 'Type', value: type, readonly: true },
  ];

  if (type === 'wall') {
      props.push({ key: 'Thickness', value: `${data.thickness || 200} mm`, readonly: true });
      props.push({ key: 'Points', value: data.polygon?.length || 0, readonly: true });
  } else if (type === 'column') {
      props.push({ key: 'Structural', value: data.isStructural ? 'Yes' : 'No', readonly: true });
  } else if (type === 'door' || type === 'window') {
       // Calculate width/height from line if possible, or just show ID
       // Opening data has 'line' [p1, p2]
       if (data.line) {
           const p1 = data.line[0];
           const p2 = data.line[1];
           const dx = p2[0] - p1[0];
           const dy = p2[1] - p1[1];
           const width = Math.sqrt(dx*dx + dy*dy);
           props.push({ key: 'Width', value: `${Math.round(width)} mm`, readonly: true });
       }
  } else if (type === 'module') {
      props.push({ key: 'Facing', value: JSON.stringify(data.facing), readonly: false }); // Editable?
      // Add more module props here
  }

  return props;
});

const updateProperty = (key: string, newValue: any) => {
  if (!selectedObject.value) return;
  
  // Only allow updating modules for now
  if (selectedObject.value.type !== 'module') return;

  // Parse numbers if needed
  let parsedValue = newValue;
  // Simple heuristic for now
  if (!isNaN(Number(newValue)) && newValue.trim() !== '') {
      parsedValue = Number(newValue);
  }

  store.updateModule(selectedObject.value.id, { [key.toLowerCase()]: parsedValue });
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
            <h2>{{ selectionCount > 1 ? 'MULTI-SELECT' : (selectedObject ? (selectedObject.type || 'Selection').toUpperCase() : 'PROJECT INFO') }}</h2>
        </header>
        
        <div class="prop-list">
            <div v-for="prop in properties" :key="prop.key" class="prop-row">
                <span class="label">{{ prop.key }}</span>
                
                <!-- Editable Input for Modules (specific keys) -->
                <input 
                    v-if="!prop.readonly"
                    :value="prop.value"
                    @change="(e) => updateProperty(prop.key, (e.target as HTMLInputElement).value)"
                    class="value-input"
                />
                
                <!-- Read-only -->
                <span v-else class="value readonly" :title="String(prop.value)">{{ prop.value }}</span>
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
    background: var(--surface-elevated);
    border-color: var(--border-subtle);
    box-shadow: 10px 0 30px rgba(0, 0, 0, 0.1); /* Shadow to right */

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
        background: transparent; /* Unified with panel background */
        border-bottom: 1px solid var(--border-subtle);

        h2 {
            margin: 0;
            font-size: 1rem;
            font-weight: 500;
            color: var(--text-primary);
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
            border-bottom: 1px solid var(--border-subtle);

            &:last-child {
                border-bottom: none;
            }

            .label {
                color: var(--text-secondary);
            }

            .value-input {
                background: var(--surface-solid);
                border: 1px solid var(--border-strong);
                color: var(--text-primary);
                text-align: right;
                max-width: 60%;
                padding: 2px 4px;
                border-radius: 4px;
                font-family: inherit;
                font-size: inherit;

                &:focus {
                    outline: none;
                    border-color: var(--accent-blue);
                    background: var(--surface-glass-hover);
                }
            }

            .value.readonly {
                color: var(--text-secondary);
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
