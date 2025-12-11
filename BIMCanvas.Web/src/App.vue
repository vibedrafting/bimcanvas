<script setup lang="ts">
import ThreeCanvas from '@/components/Canvas/ThreeCanvas.vue';
import Toolbar from '@/components/UI/Toolbar.vue';
import PropertyPanel from '@/components/UI/PropertyPanel.vue';
import StatusBar from '@/components/UI/StatusBar.vue';
</script>

<template>
  <div class="app-container">
    <Toolbar />
    <ThreeCanvas />
    <PropertyPanel />
    <StatusBar />
  </div>
</template>

<style scoped>
.app-container {
  position: relative;
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background-color: #000;
}

/* Ensure ThreeCanvas is the background */
:deep(.three-canvas-container) {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  z-index: 0;
}

/* Ensure UI components are on top and fixed */
:deep(.toolbar),
:deep(.status-bar),
:deep(.property-panel) {
  position: fixed;
  z-index: 100; /* High z-index to stay on top */
}

:deep(.toolbar) {
  top: 0;
  left: 0;
  width: 100%;
}

:deep(.status-bar) {
  bottom: 0;
  left: 0;
  width: 100%;
}

:deep(.property-panel) {
  top: 60px; /* Height of Toolbar */
  right: 0;
  height: calc(100% - 60px - 30px); /* Subtract Toolbar and StatusBar height */
}
</style>
