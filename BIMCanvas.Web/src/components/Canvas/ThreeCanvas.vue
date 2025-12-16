<script setup lang="ts">
import { onMounted, onUnmounted, ref } from 'vue';
import { ThreeSceneService } from '../../services/three/ThreeSceneService';

const containerRef = ref<HTMLElement | null>(null);
let sceneService: ThreeSceneService | null = null;

onMounted(() => {
  if (containerRef.value) {
    sceneService = new ThreeSceneService(containerRef.value);
    sceneService.animate();
  }
});

onUnmounted(() => {
  if (sceneService) {
    sceneService.dispose();
  }
});
</script>

<template>
  <div ref="containerRef" class="three-canvas"></div>
</template>

<style scoped>
.three-canvas {
  width: 100%;
  height: 100%;
  display: block;
}
</style>
