<script setup lang="ts">
import RibbonToolbar from '../components/UI/RibbonToolbar.vue';
import AppHeader from '../components/UI/AppHeader.vue';
import DynamicIsland from '../components/UI/DynamicIsland.vue';
import SideGallery from '../components/UI/SideGallery.vue';
import PropertyPanel from '../components/UI/PropertyPanel.vue';
import DebugConsole from '../components/UI/DebugConsole.vue';
import FloatingLayerManager from '../components/UI/FloatingLayerManager.vue';
import PromptBar from '../components/UI/PromptBar.vue';
import FloatingInput from '../components/UI/FloatingInput.vue';
import { useDebugStore } from '../stores/debugStore';
import { onMounted, onUnmounted } from 'vue';

const debugStore = useDebugStore();

const handleKeydown = (e: KeyboardEvent) => {
  // Toggle debug console with Ctrl + ` (Backtick)
  if (e.ctrlKey && e.key === '`') {
    debugStore.toggle();
  }
};

onMounted(() => {
  window.addEventListener('keydown', handleKeydown);
  debugStore.log('Debug Mode Initialized. Press Ctrl + ` to toggle.');
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});
</script>

<template>
  <div class="main-layout">
    <header class="header-area">
      <div class="toolbar-container">
        <AppHeader />
        <RibbonToolbar />
      </div>
      <DynamicIsland />
    </header>
    
    <aside class="gallery-area">
      <SideGallery />
    </aside>

    <main class="canvas-area">
      <slot></slot>
    </main>
    
    <aside class="properties-area">
      <PropertyPanel />
    </aside>
    
    <FloatingLayerManager />
    <PromptBar />
    <FloatingInput />
    <DebugConsole />
  </div>
</template>

<style scoped lang="scss">
.main-layout {
  position: relative;
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background-color: var(--bg-canvas);
}

.header-area {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  z-index: 200; /* Increased to ensure it's above everything */
}

.canvas-area {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  z-index: 0;
}

/* Gallery and Properties handle their own positioning now */
.gallery-area, .properties-area {
  position: absolute;
  top: 0;
  height: 100%;
  z-index: 90;
  pointer-events: none; /* Let clicks pass through empty areas */
}

.gallery-area {
  right: 0;
}

.properties-area {
  left: 0;
}

/* Enable pointer events for the actual content */
.gallery-area > *, .properties-area > * {
  pointer-events: auto;
}

.toolbar-container {
  position: relative;
  width: 100%;
  z-index: 100;
  pointer-events: auto;
  
  /* Unified Glass Effect */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-bottom: var(--glass-border);
}
</style>
