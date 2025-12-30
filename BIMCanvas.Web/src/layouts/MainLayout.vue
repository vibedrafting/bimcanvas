<script setup lang="ts">
import RibbonToolbar from '../components/UI/RibbonToolbar.vue';
import AppHeader from '../components/UI/AppHeader.vue';
import DynamicIsland from '../components/UI/DynamicIsland.vue';
import AICommandCenter from '../components/UI/AICommandCenter.vue';
import PropertyPanel from '../components/UI/PropertyPanel.vue';
import FloatingLayerManager from '../components/UI/FloatingLayerManager.vue';
import FloatingInput from '../components/UI/FloatingInput.vue';
import PromptBar from '../components/UI/PromptBar.vue';
import { useDebugStore } from '../stores/debugStore';
import { onMounted } from 'vue';

const props = defineProps<{
  loadingStage?: number;
  buildComplete?: boolean;
}>();

const debugStore = useDebugStore();

onMounted(() => {
  debugStore.log('MainLayout Mounted.');
});
</script>

<template>
  <div class="main-layout">
    <header class="header-area">
      <div class="toolbar-container" :class="{ 'visible': (props.loadingStage ?? 5) >= 4 }">
        <AppHeader />
        <RibbonToolbar />
      </div>
      <div class="island-container" :class="{ 'visible': (props.loadingStage ?? 5) >= 3, 'hint-pulse': props.buildComplete }">
        <DynamicIsland />
      </div>
    </header>
    
    <aside class="gallery-area" :class="{ 'visible': (props.loadingStage ?? 5) >= 4 }">
      <AICommandCenter />
    </aside>

    <main class="canvas-area">
      <slot></slot>
    </main>
    
    <aside class="properties-area" :class="{ 'visible': (props.loadingStage ?? 5) >= 4 }">
      <PropertyPanel />
    </aside>
    
    <div class="floating-tools" :class="{ 'visible': (props.loadingStage ?? 5) >= 3 }">
      <FloatingLayerManager />
    </div>
    
    <PromptBar />
    <FloatingInput />
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
  top: 72px; /* Adjusted to avoid header overlap (32px Header + 40px Ribbon) */
  height: calc(100% - 72px);
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

  /* Transition */
  opacity: 0;
  transform: translateY(-20px);
  transition: opacity 0.6s ease-out, transform 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.toolbar-container.visible {
  opacity: 1;
  transform: translateY(0);
}

.island-container {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 200;
  
  /* Transition */
  opacity: 0;
  transform: scale(0.9) translateY(-10px);
  transition: opacity 0.5s ease-out, transform 0.5s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.island-container.visible {
  opacity: 1;
  transform: scale(1) translateY(0);
}

/* Post-Load Pulse: Subtle expansion hint */
.island-container.hint-pulse :deep(.command-island) {
  animation: island-pulse 1.0s cubic-bezier(0.2, 0.8, 0.2, 1);
}

@keyframes island-pulse {
  0% { width: 180px; height: 36px; transform: translateX(-50%); }
  40% { width: 260px; height: 42px; transform: translateX(-50%); } /* Hint at expansion (Width + Height) */
  100% { width: 180px; height: 36px; transform: translateX(-50%); }
}

.gallery-area {
  transform: translateX(20px);
}
.properties-area {
  transform: translateX(-20px);
}

.gallery-area.visible, .properties-area.visible {
  opacity: 1;
  transform: translateX(0);
}

.floating-tools {
  position: fixed;
  inset: 0;
  pointer-events: none;
  z-index: 80; /* Lower than properties-area (90) so it gets covered */

  opacity: 0;
  transform: translateY(20px);
  transition: opacity 0.5s ease-out, transform 0.5s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.island-container > *, .floating-tools > * {
  pointer-events: auto;
}

.floating-tools.visible {
  opacity: 1;
  transform: translateY(0);
}
</style>
