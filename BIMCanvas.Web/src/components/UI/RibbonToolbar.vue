<script setup lang="ts">
import { ref } from 'vue';
import FileGroup from './Ribbon/FileGroup.vue';
import ProjectGroup from './Ribbon/ProjectGroup.vue';
import StrategyGroup from './Ribbon/StrategyGroup.vue';
import VariantGroup from './Ribbon/VariantGroup.vue';
import AIGroup from './Ribbon/AIGroup.vue';
import ZoneGroup from './Ribbon/ZoneGroup.vue';
import LibraryGroup from './Ribbon/LibraryGroup.vue';
import EditGroup from './Ribbon/EditGroup.vue';
import ViewGroup from './Ribbon/ViewGroup.vue';

// Tabs definition
const tabs = [
  { id: 'file', label: 'File' },
  { id: 'project', label: 'Project' },
  { id: 'strategy', label: 'Strategy' },
  { id: 'variant', label: 'Variant' },
  { id: 'ai', label: 'AI' },
  { id: 'zone', label: 'Zone' },
  { id: 'library', label: 'Library' },
  { id: 'edit', label: 'Edit' },
  { id: 'view', label: 'View' },
];

const activeTab = ref<string | null>(null);
let closeTimer: any = null;

const openTab = (id: string) => {
  if (closeTimer) clearTimeout(closeTimer);
  activeTab.value = id;
};

const closeTab = () => {
  closeTimer = setTimeout(() => {
    activeTab.value = null;
  }, 300); // 300ms delay for smooth interaction
};

const keepOpen = () => {
  if (closeTimer) clearTimeout(closeTimer);
};
</script>

<template>
  <div class="ribbon-container">
    <!-- Tabs Row -->
    <div class="ribbon-tabs">
      <div 
        v-for="tab in tabs" 
        :key="tab.id" 
        class="tab-wrapper"
        @mouseenter="openTab(tab.id)"
        @mouseleave="closeTab"
      >
        <button
          class="tab-btn"
          :class="{ active: activeTab === tab.id }"
        >
          {{ tab.label }}
        </button>

        <!-- Dropdown Panel -->
        <div 
          class="ribbon-dropdown" 
          v-if="activeTab === tab.id"
          @mouseenter="keepOpen"
          @mouseleave="closeTab"
        >
          <div class="panel-content">
            <FileGroup v-if="tab.id === 'file'" />
            <ProjectGroup v-else-if="tab.id === 'project'" />
            <StrategyGroup v-else-if="tab.id === 'strategy'" />
            <VariantGroup v-else-if="tab.id === 'variant'" />
            <AIGroup v-else-if="tab.id === 'ai'" />
            <ZoneGroup v-else-if="tab.id === 'zone'" />
            <LibraryGroup v-else-if="tab.id === 'library'" />
            <EditGroup v-else-if="tab.id === 'edit'" />
            <ViewGroup v-else-if="tab.id === 'view'" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.ribbon-container {
  position: absolute;
  top: 32px; /* Immediately below Top Bar */
  left: 0;
  width: 100%;
  height: 40px; /* Fixed height for the tab bar */
  z-index: 90;
  pointer-events: auto; /* The bar itself is interactive */
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  /* 
     Alignment Fix: 
     AppHeader padding is var(--spacing-md) = 16px.
     Tab button padding-left is 12px.
     To align text, Ribbon container padding-left should be 16px - 12px = 4px.
  */
  padding: 0 4px; 
  
  /* Merged Bar Style */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-bottom: var(--glass-border);
}

.ribbon-tabs {
  display: flex;
  gap: 4px;
  height: 100%;
  align-items: center;
}

.tab-wrapper {
  position: relative;
  height: 100%;
  display: flex;
  align-items: center;
}

.tab-btn {
  background: transparent;
  border: none;
  color: var(--text-secondary);
  padding: 4px 12px;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: 4px;
  transition: all 0.2s;
  position: relative;
  white-space: nowrap;

  &:hover {
    color: var(--text-primary);
    background: rgba(255, 255, 255, 0.05);
  }

  &.active {
    color: var(--tab-text-active);
    background: var(--tab-bg-active);
  }
}

.ribbon-dropdown {
  position: absolute;
  top: 100%; /* Directly below the button */
  left: 0;
  margin-top: 4px; /* Slight gap */
  
  /* Floating Glass Panel */
  background-color: var(--glass-bg-solid); /* Explicit color */
  backdrop-filter: none !important; /* Force disable blur */
  -webkit-backdrop-filter: none !important;
  border: var(--glass-border);
  border-radius: 8px;
  padding: 12px;
  box-shadow: var(--shadow-island);
  pointer-events: auto;
  min-width: 200px;
  z-index: 100;
  
  /* Animation */
  animation: dropdownSlideDown 0.2s var(--ease-spring);
  transform-origin: top left;
  
  /* Glare effect - ensure gradient uses solid colors */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg-solid), var(--glass-bg-solid));
  background-origin: border-box;
  background-clip: padding-box, border-box;
}

.panel-content {
  display: flex;
  gap: 12px;
  justify-content: flex-start;
}

@keyframes dropdownSlideDown {
  from { 
    opacity: 0; 
    transform: translateY(-8px) scale(0.98); 
  }
  to { 
    opacity: 1; 
    transform: translateY(0) scale(1); 
  }
}
</style>
