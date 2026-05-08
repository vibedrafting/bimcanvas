<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { computed, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import VariantSwitcherChips from './VariantSwitcherChips.vue';

const store = useCanvasStore();
const { currentOperation, selectedIds } = storeToRefs(store);

// State
// State
const STORAGE_KEY = 'bimcanvas.property-panel.full-height';
const isFullHeight = ref(localStorage.getItem(STORAGE_KEY) === 'true');

// Computed
const isVisible = computed(() => selectedIds.value.length > 0);
const selectionCount = computed(() => selectedIds.value.length);
const selectedObject = computed(() => store.selectedObject);

const panelTitle = computed(() => {
    if (selectionCount.value > 1) return 'MULTI-SELECT';
    if (selectedObject.value) return (selectedObject.value.type || 'OBJECT').toUpperCase();
    return 'SELECTION';
});

// Actions
const closePanel = () => {
    store.clearSelection();
};

const toggleFullHeight = () => {
    isFullHeight.value = !isFullHeight.value;
};

// Logic: Auto-hide on edit
const isInEditMode = computed(() => {
    return currentOperation.value === 'moving' || currentOperation.value === 'rotating';
});

// Watchers
// Watchers
watch(isFullHeight, (newValue) => {
    localStorage.setItem(STORAGE_KEY, String(newValue));
});

watch(selectedIds, () => {
    // Only reset to card mode if it was NOT user-expanded preference? 
    // Actually user requirement says "remember state before closing". 
    // If we reset here, we lose the persistence across selections if that's what "closing" means.
    // "Should record whether it was expanded or collapsed before closing (default collapsed)"
    // If I close the panel (deselect), and then select again, it should restore the state.
    // So I should REMOVE the auto-reset logic here.
    
    // if (newIds.length > 0) {
    //     isFullHeight.value = false;
    // }
});

// Helper: Primitive check
const isPrimitiveValue = (value: any): boolean => {
  if (value === null || value === undefined) return false;
  const type = typeof value;
  return type === 'string' || type === 'number' || type === 'boolean';
};

// 模块布置变体切换器：仅当选中"叶子分区"时显示，给 module-relocation-agent 产出的变体方案做切换/采纳。
// 叶子判定：subZones 为空且自身被识别为 zone；leafZonePath 形如 "rz_3/dz_1"（带 parentZoneId）或 "rz_3"（顶层叶子）。
const variantContext = computed<{ leafZoneId: string; leafZonePath: string } | null>(() => {
    const obj: any = selectedObject.value;
    if (!obj) return null;
    if (obj.type !== 'zone') return null;

    const subZones = obj.subZones;
    const hasChildren = Array.isArray(subZones) && subZones.length > 0;
    if (hasChildren) return null;

    const parentZoneId = obj.parentZoneId as string | undefined;
    const leafZonePath = parentZoneId ? `${parentZoneId}/${obj.id}` : obj.id;
    return { leafZoneId: obj.id, leafZonePath };
});

// Properties Generation
const properties = computed(() => {
  if (selectionCount.value > 1) {
    return [
      { key: 'Count', value: `${selectionCount.value} items`, readonly: true }
    ];
  }

  if (!selectedObject.value) return [];

  const obj = selectedObject.value;
  const data = obj.data || obj;
  const props: Array<{ key: string; value: any; readonly: boolean }> = [];

  // Add ID first
  if (obj.id) props.push({ key: 'ID', value: obj.id, readonly: true });

  for (const [key, value] of Object.entries(data)) {
    if (key === 'id') continue; // Skip ID as it's added manually
    if (key.startsWith('_')) continue; // 跳过内部字段
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
    :class="{ 
        'full-height': isFullHeight, 
        'hidden': !isVisible || isInEditMode 
    }"
  >
    <!-- Header -->
    <div class="panel-header">
        <button class="icon-btn back-btn" @click="closePanel" title="Close / Deselect">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <line x1="19" y1="12" x2="5" y2="12"></line>
                <polyline points="12 19 5 12 12 5"></polyline>
            </svg>
        </button>
        
        <div class="title">{{ panelTitle }}</div>
        
        <button class="icon-btn expand-btn" @click="toggleFullHeight" :title="isFullHeight ? 'Collapse' : 'Expand'">
            <svg v-if="!isFullHeight" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="15 3 21 3 21 9"></polyline>
                <polyline points="9 21 3 21 3 15"></polyline>
                <line x1="21" y1="3" x2="14" y2="10"></line>
                <line x1="3" y1="21" x2="10" y2="14"></line>
            </svg>
            <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="4 14 10 14 10 20"></polyline>
                <polyline points="20 10 14 10 14 4"></polyline>
                <line x1="14" y1="10" x2="21" y2="3"></line>
                <line x1="3" y1="21" x2="10" y2="14"></line>
            </svg>
        </button>
    </div>

    <!-- Content -->
    <div class="panel-content">
        <div v-if="properties.length === 0" class="empty-state">
            No properties
        </div>
        <div v-else class="prop-list">
            <div v-for="prop in properties" :key="prop.key" class="prop-row">
                <span class="label">{{ prop.key }}</span>
                <span class="value" :title="String(prop.value)">{{ prop.value }}</span>
            </div>
        </div>

        <!-- 叶子分区变体切换器：当且仅当选中叶子分区且该分区有 alt 文件时渲染（组件内部空列表时返回 null） -->
        <VariantSwitcherChips
          v-if="variantContext"
          :leaf-zone-id="variantContext.leafZoneId"
          :leaf-zone-path="variantContext.leafZonePath"
          class="variant-switcher-section"
        />
    </div>
  </aside>
</template>

<style scoped lang="scss">
.property-panel {
  /* Card Mode (Default) */
  position: absolute;
  left: 24px;
  top: 48px;
  width: 280px;
  max-height: 50vh; /* Limit height in card mode */
  
  /* Aurora Glass */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  
  /* Enhanced Border & Glow */
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  border-radius: 20px;
  
  /* Glare & Deep Shadow */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow: 
    0 12px 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */
  
  display: flex;
  flex-direction: column;
  overflow: hidden;
  z-index: 100;
  
  /* Transitions */
  transition: 
    width 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    height 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    top 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    bottom 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    left 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    border-radius 0.5s cubic-bezier(0.19, 1, 0.22, 1),
    opacity 0.3s ease,
    transform 0.3s ease;

  /* Hidden State */
  &.hidden {
    opacity: 0;
    transform: translateY(-20px) scale(0.95);
    pointer-events: none;
  }

  /* Full Height Mode (Now just "Tall Card" mode) */
  &.full-height {
    /* Keep horizontal position */
    left: 24px;
    bottom: 24px;
    
    /* Expand vertically */
    /* Top Bar (32px) + Ribbon (40px) + Gap (24px) = 96px total from screen top */
    /* Container (.properties-area) is already at top: 72px, so we need 24px here to match collapsed state */
    top: 24px; 
    height: auto; /* Let top/bottom define height */
    
    max-height: none; /* Remove limit */
    
    /* Maintain Card Style */
    border-radius: 20px; /* Keep rounded corners */
    border: 1px solid rgba(255, 255, 255, 0.2); /* Keep border */
    border-left: 1px solid rgba(255, 255, 255, 0.2); /* Ensure left border exists */
  }

  .panel-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    border-bottom: 1px solid var(--border-subtle);
    flex-shrink: 0;

    .title {
        font-weight: 600;
        font-size: 0.9rem;
        color: var(--text-primary);
        letter-spacing: 0.5px;
    }

    .icon-btn {
        background: transparent;
        border: none;
        color: var(--text-secondary);
        cursor: pointer;
        padding: 4px;
        border-radius: 50%;
        display: flex;
        align-items: center;
        justify-content: center;
        transition: all 0.2s ease;

        svg {
            width: 18px;
            height: 18px;
        }

        &:hover {
            background: var(--surface-hover);
            color: var(--text-primary);
        }
    }
  }

  .panel-content {
    flex: 1;
    overflow-y: auto;
    padding: 12px 16px;

    /* Custom Scrollbar */
    &::-webkit-scrollbar {
        width: 4px;
    }
    &::-webkit-scrollbar-track {
        background: transparent;
    }
    &::-webkit-scrollbar-thumb {
        background: var(--border-strong);
        border-radius: 2px;
    }

    .prop-list {
        display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .prop-row {
        display: flex;
        justify-content: space-between;
        align-items: baseline;
        gap: 12px;
        font-size: 0.85rem; /* Smaller font as requested */
        line-height: 1.4;

        .label {
            color: var(--text-secondary);
            flex-shrink: 0;
            max-width: 40%;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .value {
            color: var(--text-primary);
            text-align: right;
            flex: 1;
            overflow: hidden;
            /* text-overflow: ellipsis;  <-- Removed to allow wrapping */
            /* white-space: nowrap;      <-- Removed to allow wrapping */
            word-break: break-word;      /* Break long words if needed */
            white-space: pre-wrap;       /* Preserve formatting but wrap */
            font-family: var(--font-mono); /* Monospace for values looks techy */
        }
    }
    
    .empty-state {
        text-align: center;
        color: var(--text-tertiary);
        font-size: 0.85rem;
        padding: 20px 0;
    }

    .variant-switcher-section {
        margin-top: 12px;
        padding-top: 12px;
        border-top: 1px solid var(--border-subtle);
    }
  }
}
</style>
