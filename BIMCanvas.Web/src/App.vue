<script setup lang="ts">
import { onMounted } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import { useCanvasStore } from './stores/canvasStore';

const store = useCanvasStore();

onMounted(async () => {
  // Auto-load demo data on startup
  await store.loadDemoData('/demos/demo1.json');
});
</script>

<template>
  <MainLayout>
    <ThreeCanvas />
  </MainLayout>
</template>

<style>
/* Global Reset */
body {
  margin: 0;
  padding: 0;
  overflow: hidden;
  background-color: var(--bg-canvas);
  font-family: var(--font-sans);
}
</style>
