<script setup lang="ts">
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
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
