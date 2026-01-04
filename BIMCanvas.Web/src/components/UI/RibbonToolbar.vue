<script setup lang="ts">
import { ref } from 'vue';
import FileGroup from './Ribbon/FileGroup.vue';
import ProjectGroup from './Ribbon/ProjectGroup.vue';
import DesignGroup from './Ribbon/DesignGroup.vue';
import ZoneGroup from './Ribbon/ZoneGroup.vue';
import LibraryGroup from './Ribbon/LibraryGroup.vue';
import EditGroup from './Ribbon/EditGroup.vue';
import ViewGroup from './Ribbon/ViewGroup.vue';

// Tabs definition
const tabs = [
  { id: 'file', label: 'File' },
  { id: 'project', label: 'Project' },
  { id: 'design', label: 'Design' },
  { id: 'zone', label: 'Zone' },
  { id: 'library', label: 'Library' },
  { id: 'edit', label: 'Edit' },
  { id: 'view', label: 'View' },
];

const activeTab = ref<string | null>(null);
const dropdownPos = ref({ top: 0, left: 0 });
let closeTimer: any = null;

const openTab = (id: string, event: MouseEvent) => {
  if (closeTimer) clearTimeout(closeTimer);
  
  // Calculate position based on the tab element
  const target = event.currentTarget as HTMLElement;
  const rect = target.getBoundingClientRect();
  
  dropdownPos.value = {
    top: rect.bottom + 4, // Add slight gap
    left: rect.left
  };
  
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
        @mouseenter="openTab(tab.id, $event)"
        @mouseleave="closeTab"
      >
        <button
          class="tab-btn"
          :class="{ active: activeTab === tab.id }"
        >
          {{ tab.label }}
        </button>

        <!-- Dropdown Panel - Teleported to body to avoid stacking context issues -->
        <Teleport to="body">
          <div 
            class="ribbon-dropdown" 
            v-if="activeTab === tab.id"
            :style="{ top: dropdownPos.top + 'px', left: dropdownPos.left + 'px' }"
            @mouseenter="keepOpen"
            @mouseleave="closeTab"
          >
            <div class="panel-content">
              <FileGroup v-if="tab.id === 'file'" />
              <ProjectGroup v-else-if="tab.id === 'project'" />
              <DesignGroup v-else-if="tab.id === 'design'" />
              <ZoneGroup v-else-if="tab.id === 'zone'" />
              <LibraryGroup v-else-if="tab.id === 'library'" />
              <EditGroup v-else-if="tab.id === 'edit'" />
              <ViewGroup v-else-if="tab.id === 'view'" />
            </div>
          </div>
        </Teleport>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.ribbon-container {
  position: relative; /* Changed from absolute to flow naturally */
  /* top: 32px; Removed */
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
  
  /* Merged Bar Style - Removed (Moved to MainLayout wrapper) */
  background: transparent;
  /* border-bottom: var(--glass-border); Removed */
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
  position: fixed; /* Changed to fixed for Teleport */
  /* top/left set via inline style */
  
  /* Floating Glass Panel */
  background: var(--glass-bg); /* Standard Glass Token */
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: var(--glass-border);
  border-radius: 8px;
  padding: 12px;
  box-shadow: var(--shadow-island), var(--glass-inner-highlight);
  pointer-events: auto;
  min-width: 200px;
  z-index: 1000; /* High z-index for fixed element */
  
  /* Animation */
  animation: dropdownSlideDown 0.2s var(--ease-spring);
  transform-origin: top left;
  
  /* Glare Overlay - Standard Token */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
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
