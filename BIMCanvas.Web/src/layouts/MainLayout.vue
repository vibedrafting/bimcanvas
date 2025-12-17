<script setup lang="ts">
import CanvasToolbar from '../components/UI/CanvasToolbar.vue';
import SideGallery from '../components/UI/SideGallery.vue';
import PropertyPanel from '../components/UI/PropertyPanel.vue';
</script>

<template>
  <div class="main-layout">
    <header class="header-area">
      <CanvasToolbar />
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
  </div>
</template>

<style scoped lang="scss">
.main-layout {
  display: grid;
  grid-template-areas:
    "header header header"
    "gallery canvas properties";
  grid-template-columns: auto 1fr auto;
  grid-template-rows: auto 1fr;
  width: 100vw;
  height: 100vh;
  overflow: hidden;
  background-color: var(--bg-canvas);
}

.header-area {
  grid-area: header;
  z-index: 100;
}

.gallery-area {
  grid-area: gallery;
  z-index: 90;
  /* Ensure it doesn't take up space when empty/collapsed if needed, 
     but SideGallery handles its own width */
}

.canvas-area {
  grid-area: canvas;
  position: relative;
  width: 100%;
  height: 100%;
  z-index: 0;
  overflow: hidden;
}

.properties-area {
  grid-area: properties;
  z-index: 90;
}
</style>
