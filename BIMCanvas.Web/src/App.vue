<script setup lang="ts">
import { onMounted } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import { useCanvasStore } from './stores/canvasStore';
import { themeService } from './services/theme/ThemeService';

const store = useCanvasStore();

onMounted(async () => {
  // 初始化主题服务 (设置 CSS 变量)
  themeService.init();

  // 从 URL 参数读取默认数据文件（由 Server 控制）
  const urlParams = new URLSearchParams(window.location.search);
  const file = urlParams.get('file');
  if (file) {
    await store.loadDemoData(`/demos/${file}.json`);
  }

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
