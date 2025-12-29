<script setup lang="ts">
import { onMounted, ref } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import BlueprintLoader from './components/UI/BlueprintLoader.vue';
import { useCanvasStore } from './stores/canvasStore';
import { themeService } from './services/theme/ThemeService';

import { ViewCalculator } from './services/interaction/ViewCalculator';
import DebugConsole from './components/UI/DebugConsole.vue';
import { useDebugStore } from './stores/debugStore';

const store = useCanvasStore();
const debugStore = useDebugStore();
const isSplashShowing = ref(true);
const loaderProps = ref<{ spacing?: number, offsetX?: number, offsetY?: number, active: boolean }>({ active: true });

const loadingStage = ref(0); // 0: Loader, 1: Grid, 2: Island, 3: Tools, 4: Chrome, 5: Scene

onMounted(async () => {
  // Initialize Theme
  themeService.init();
  debugStore.log('App Mounted. Initializing...');

  // Attach Keyboard Shortcuts IMMEDIATELY so debug console works during loading
  window.addEventListener('keydown', handleKeydown);
  debugStore.log('Debug Mode Initialized. Press Ctrl + ` to toggle.');

  // Force splash screen for at least 2.5s
  const minTimePromise = new Promise(resolve => setTimeout(resolve, 2500));
  
  // 单项目模式：直接从 Server 加载当前项目（无需 URL 参数）
  debugStore.log('Starting project load...');
  const loadPromise = store.loadProject().then(() => {
    debugStore.log('Project data loaded.');
    // Data loaded, calculate target view
    if (store.projectData) {
      debugStore.log('Calculating target view...');
      const target = ViewCalculator.calculateTargetView(
        store.projectData, 
        window.innerWidth, 
        window.innerHeight
      );
      if (target) {
        debugStore.log(`Target view calculated: spacing=${target.spacing.toFixed(2)}`);
        loaderProps.value = {
          ...loaderProps.value,
          spacing: target.spacing,
          offsetX: target.offsetX,
          offsetY: target.offsetY
        };
      } else {
        debugStore.warn('Failed to calculate target view (no valid bounds).');
      }
    } else {
      debugStore.warn('No project data found after load.');
    }
  }).catch(err => {
    debugStore.error(`Project load failed: ${err}`);
    throw err;
  });

  // Timeout Promise (10 seconds max)
  const timeoutPromise = new Promise((_, reject) => 
    setTimeout(() => reject(new Error('Loading timed out')), 10000)
  );

  // 等待两者都完成 (Race against timeout)
  try {
    await Promise.race([
      Promise.all([minTimePromise, loadPromise]),
      timeoutPromise
    ]);
    debugStore.log('Loading sequence completed successfully.');
  } catch (error) {
    console.error('Failed to load project:', error);
    debugStore.error(`Loading sequence failed or timed out: ${error}`);
  } finally {
    debugStore.log('Starting Cinematic Sequence...');
    
    // Stage 1: Grid Ready (Loader Fades Out)
    isSplashShowing.value = false;
    loaderProps.value.active = false;
    loadingStage.value = 1;
    
    // Wait for loader fade out (approx 200ms) - Reduced from 800ms for snappier feel
    await new Promise(resolve => setTimeout(resolve, 200));

    // Stage 2 & 3: Dynamic Island & Layer Manager (Together)
    loadingStage.value = 3;
    await new Promise(resolve => setTimeout(resolve, 300));

    // Stage 4: Chrome (Header, Ribbon, Panels)
    loadingStage.value = 4;
    await new Promise(resolve => setTimeout(resolve, 500));

    // Stage 5: Scene Build
    loadingStage.value = 5;
    debugStore.log('Triggering Progressive Scene Build...');
    window.dispatchEvent(new CustomEvent('bimcanvas:play-build-sequence'));
  }
});

import { onUnmounted } from 'vue';

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});

const handleKeydown = (e: KeyboardEvent) => {
  // Toggle debug console with Ctrl + ` (Backtick)
  if (e.ctrlKey && e.key === '`') {
    debugStore.toggle();
  }

  // Ignore if typing in an input
  if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement).tagName)) return;

  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
    e.preventDefault();
    if (e.shiftKey) {
      // Ctrl + Shift + Z -> Redo
      if (store.canRedo) store.redo();
    } else {
      // Ctrl + Z -> Undo
      if (store.canUndo) store.undo();
    }
  } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
    // Ctrl + Y -> Redo
    e.preventDefault();
    if (store.canRedo) store.redo();
  }
};
</script>

<template>
  <BlueprintLoader 
    :active="loaderProps.active" 
    :target-spacing="loaderProps.spacing"
    :target-offset-x="loaderProps.offsetX"
    :target-offset-y="loaderProps.offsetY"
  />
  <MainLayout :loading-stage="loadingStage">
    <ThreeCanvas />
  </MainLayout>
  <DebugConsole />
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

/* Disable UI interactions while dragging canvas */
body.is-dragging .command-island,
body.is-dragging .floating-layer-manager,
body.is-dragging .property-panel,
body.is-dragging .side-gallery,
body.is-dragging .toolbar-container,
body.is-dragging .toolbar-container * {
  pointer-events: none !important;
}
</style>
