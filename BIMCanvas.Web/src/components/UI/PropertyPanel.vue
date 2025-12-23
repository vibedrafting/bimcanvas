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

// 判断值是否为基础类型（应该显示）
const isPrimitiveValue = (value: any): boolean => {
  if (value === null || value === undefined) return false;
  const type = typeof value;
  return type === 'string' || type === 'number' || type === 'boolean';
};

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
        { key: 'Walls', value: doc.revit?.walls?.length || 0, readonly: true },
        { key: 'Modules', value: doc.layout?.modules?.length || 0, readonly: true },
    ];
});

const properties = computed(() => {
  // 多选模式
  if (selectionCount.value > 1) {
    return [
      { key: 'Selection', value: `${selectionCount.value} objects selected`, readonly: true },
    ];
  }

  // 无选择 → 项目属性
  if (!selectedObject.value) return projectProperties.value;

  const obj = selectedObject.value;
  const data = obj.data || obj;
  const props: Array<{ key: string; value: any; readonly: boolean }> = [];

  // 遍历对象属性，只显示基础类型
  for (const [key, value] of Object.entries(data)) {
    if (isPrimitiveValue(value)) {
      props.push({ key, value, readonly: true });
    }
  }

  return props;
});

</script>

<template>
  <aside 
    class="property-panel" 
    :class="{ expanded: isExpanded }"
    @mouseenter="isExpanded = true"
    @mouseleave="isExpanded = false"
  >
    <div class="header" @click="toggleExpand">
      <svg class="indicator" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="15 18 9 12 15 6"></polyline>
      </svg>
    </div>

    <div class="content" v-if="isExpanded">
        <header class="panel-header">
            <h2>{{ selectionCount > 1 ? 'MULTI-SELECT' : (selectedObject ? (selectedObject.type || 'Selection').toUpperCase() : 'PROJECT INFO') }}</h2>
        </header>
        
        <div class="prop-list">
            <div v-for="prop in properties" :key="prop.key" class="prop-row">
                <span class="label">{{ prop.key }}</span>
                <span class="value readonly" :title="String(prop.value)">{{ prop.value }}</span>
            </div>
            

        </div>
    </div>
  </aside>
</template>

<style scoped lang="scss">
.property-panel {
  width: 48px; /* Collapsed state */
  height: 100%; /* Full height */
  margin: 0; /* Anchored to edges */
  padding-top: 0; /* Let internal elements handle spacing */
  
  /* Aurora Glass - Matching Dynamic Island */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  
  /* Anchored Borders */
  border: none;
  border-right: var(--glass-border);
  border-radius: 0 24px 24px 0; /* Round inner corners only */
  
  /* Glare Overlay */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  display: flex;
  flex-direction: column;
  transition: all 0.6s cubic-bezier(0.19, 1, 0.22, 1); /* Premium Spring Curve */
  overflow: hidden;
  z-index: 90;
  
  /* Left aligned behavior */
  margin-right: auto;
  margin-left: 0; /* Anchored */
  
  &.expanded {
    width: 300px;
    /* Maintain same glass effect, just add shadow */
    box-shadow: var(--shadow-panel), var(--glass-inner-highlight);

    .header .indicator {
      transform: rotate(0deg);
    }
  }

  .header {
    height: 100%;
    width: 48px;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding-top: 40vh; /* Position slightly up from center */
    cursor: pointer;
    flex-shrink: 0;
    
    /* Keep header on the left side */
    position: absolute;
    left: 0;
    top: 0;

    .indicator {
      width: 24px;
      height: 24px;
      color: var(--text-secondary);
      transition: transform 0.4s var(--ease-spring), color 0.2s;
      transform: rotate(180deg); /* Default points Right (flipped left arrow) */
      opacity: 0.8;
    }
    
    &:hover .indicator {
      color: var(--text-primary);
      opacity: 1;
      transform: scale(1.1);
    }
  }

  .content {
    position: absolute;
    left: 48px; /* Content to the right of header */
    top: 100px; /* Clear the top toolbar buttons */
    width: calc(100% - 48px);
    height: calc(100% - 100px);
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
            align-items: flex-start; /* Align to top for multi-line text */
            font-size: 0.9rem;
            padding: 0.5rem 0; /* Increase padding for better readability */
            border-bottom: 1px solid var(--border-subtle);
            gap: 12px; /* Consistent gap */

            &:last-child {
                border-bottom: none;
            }

            .label {
                color: var(--text-secondary);
                flex-shrink: 0; /* Prevent label from shrinking */
                max-width: 40%; /* Limit label width */
                word-break: break-word; /* Allow label to wrap if really long */
                line-height: 1.4;
                padding-top: 2px; /* Align with value text */
            }

            .value-input {
                background: var(--surface-solid);
                border: 1px solid var(--border-strong);
                color: var(--text-primary);
                text-align: left; /* Align left for better readability of long text */
                flex: 1; /* Take remaining space */
                min-width: 0; /* Allow flex item to shrink below content size */
                padding: 4px 6px;
                border-radius: 4px;
                font-family: inherit;
                font-size: inherit;
                line-height: 1.4;

                &:focus {
                    outline: none;
                    border-color: var(--accent-blue);
                    background: var(--surface-glass-hover);
                }
            }

            .value.readonly {
                color: var(--text-secondary);
                font-style: italic;
                text-align: right; /* Keep right alignment for short values, looks better */
                flex: 1; /* Take remaining space */
                min-width: 0; /* Allow flex item to shrink below content size */
                white-space: pre-wrap; /* Allow wrapping */
                word-break: break-word; /* Break long words */
                line-height: 1.4;
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
