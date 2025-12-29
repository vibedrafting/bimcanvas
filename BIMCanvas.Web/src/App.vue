<script setup lang="ts">
import { onMounted, ref } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import BlueprintLoader from './components/UI/BlueprintLoader.vue';
import { useCanvasStore } from './stores/canvasStore';
import { themeService } from './services/theme/ThemeService';

const store = useCanvasStore();
const isSplashShowing = ref(true);

onMounted(async () => {
  // 初始化主题服务 (设置 CSS 变量)
  themeService.init();

  // 强制显示加载动画至少 2.5 秒，提升仪式感
  const minTimePromise = new Promise(resolve => setTimeout(resolve, 2500));
  
  // 单项目模式：直接从 Server 加载当前项目（无需 URL 参数）
  const loadPromise = store.loadProject();

  // 等待两者都完成
  await Promise.all([minTimePromise, loadPromise]);
  
  isSplashShowing.value = false;

  // Keyboard Shortcuts
  window.addEventListener('keydown', handleKeydown);
});

import { onUnmounted } from 'vue';

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});

const handleKeydown = (e: KeyboardEvent) => {
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
  <BlueprintLoader :active="isSplashShowing" />
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

/* Disable UI interactions while dragging canvas */
body.is-dragging .command-island,
body.is-dragging .floating-layer-manager,
body.is-dragging .property-panel,
body.is-dragging .side-gallery {
  pointer-events: none !important;
}
</style>
