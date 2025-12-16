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
  background-color: #0a0a0f;
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
}
</style>
