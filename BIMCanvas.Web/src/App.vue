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
</style>
