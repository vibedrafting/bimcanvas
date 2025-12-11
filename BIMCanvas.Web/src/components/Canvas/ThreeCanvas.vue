<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue';
import { ThreeSceneService } from '@/services/ThreeSceneService';
import { SceneBuilder } from '@/services/SceneBuilder';
import { GridSystem } from '@/services/GridSystem';
import { InteractionService } from '@/services/InteractionService';
import { useCanvasStore } from '@/stores/canvasStore';

const canvasContainer = ref<HTMLElement | null>(null);
let sceneService: ThreeSceneService | null = null;
let sceneBuilder: SceneBuilder | null = null;
let gridSystem: GridSystem | null = null;
let interactionService: InteractionService | null = null;

const store = useCanvasStore();

onMounted(() => {
  if (canvasContainer.value) {
    // 1. Initialize Scene
    sceneService = new ThreeSceneService(canvasContainer.value);
    
    // 2. Initialize Grid
    gridSystem = new GridSystem(sceneService.getScene());
    
    // 3. Initialize Builder
    sceneBuilder = new SceneBuilder(sceneService.getScene());
    
    // 4. Initialize Interaction
    interactionService = new InteractionService(
      sceneService.getCamera(),
      sceneService.getScene(),
      canvasContainer.value,
      gridSystem
});
</script>

<template>
  <div ref="canvasContainer" class="three-canvas-container"></div>
</template>

<style scoped>
.three-canvas-container {
  width: 100%;
  height: 100vh;
  overflow: hidden;
  background-color: #050510; /* Deep Space Black fallback */
}
</style>
