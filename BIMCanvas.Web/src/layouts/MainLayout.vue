<script setup lang="ts">
import CanvasToolbar from '../components/UI/CanvasToolbar.vue';
import SideGallery from '../components/UI/SideGallery.vue';
import PropertyPanel from '../components/UI/PropertyPanel.vue';
</script>

<template>
  <div class="main-layout">
    <div class="canvas-area">
      <slot></slot>
    </div>
    
    <div class="ui-layer">
      <CanvasToolbar />
      <SideGallery />
      <PropertyPanel />
    </div>
  </div>
</template>

<style scoped lang="scss">
.main-layout {
  position: relative;
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background-color: var(--bg-canvas); // Calm Tech background
}

.canvas-area {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  z-index: 0;
}

.ui-layer {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  pointer-events: none; // Let clicks pass through to canvas
  z-index: 10;
  
  > * {
    pointer-events: auto; // Re-enable clicks for UI components
  }
}
</style>
