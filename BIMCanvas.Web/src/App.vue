<script setup lang="ts">
import { onMounted, ref, watch, onUnmounted } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import BlueprintLoader from './components/UI/BlueprintLoader.vue';
import HomePage from './views/HomePage.vue';
import { useCanvasStore } from './stores/canvasStore';
import { useAppStore } from './stores/appStore';
import { ChangeSource } from './types/history';
import { themeService } from './services/theme/ThemeService';
import { ProjectService } from './services/ProjectService';

import { ViewCalculator } from './services/interaction/ViewCalculator';
import DebugConsole from './components/UI/DebugConsole.vue';
import BranchMergeWizard from './components/UI/merge/BranchMergeWizard.vue';
import AgentNotificationModal from './components/UI/AgentNotificationModal.vue';
import { useDebugStore } from './stores/debugStore';

const store = useCanvasStore();
const appStore = useAppStore();
const debugStore = useDebugStore();
const isSplashShowing = ref(true);
const loaderProps = ref<{ spacing?: number, offsetX?: number, offsetY?: number, active: boolean }>({ active: false });

const loadingStage = ref(0); // 0: Loader, 1: Grid, 2: Island, 3: Tools, 4: Chrome, 5: Scene

/** 执行工作区加载 + cinematic sequence */
const enterWorkspace = async () => {
  // 重置 workspace 状态
  isSplashShowing.value = true;
  loaderProps.value = { active: true };
  loadingStage.value = 0;
  isBuildComplete.value = false;

  // Force splash screen for at least 2.5s
  const minTimePromise = new Promise(resolve => setTimeout(resolve, 2500));

  debugStore.log('Starting project load...');
  const loadPromise = store.loadProject(ChangeSource.SystemInit).then(() => {
    debugStore.log('Project data loaded.');
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

    isSplashShowing.value = false;
    loaderProps.value.active = false;
    loadingStage.value = 1;

    await new Promise(resolve => setTimeout(resolve, 200));

    loadingStage.value = 3;
    await new Promise(resolve => setTimeout(resolve, 300));

    loadingStage.value = 4;
    await new Promise(resolve => setTimeout(resolve, 500));

    loadingStage.value = 5;
    debugStore.log('Triggering Progressive Scene Build...');
    window.dispatchEvent(new CustomEvent('bimcanvas:play-build-sequence'));
  }
};

onMounted(async () => {
  themeService.init();
  debugStore.log('App Mounted. Initializing...');

  window.addEventListener('keydown', handleKeydown);
  debugStore.log('Debug Mode Initialized. Press Ctrl + ` to toggle.');

  // 检查 Server 是否已有加载的项目
  try {
    const status = await ProjectService.getStatus();
    if (status.isLoaded) {
      debugStore.log('Server has loaded project, entering workspace...');
      appStore.goToWorkspace();
      await enterWorkspace();
    } else {
      debugStore.log('No project loaded, showing homepage...');
      appStore.goToHomepage();
    }
  } catch (err) {
    debugStore.warn(`Failed to check project status: ${err}, showing homepage...`);
    appStore.goToHomepage();
  }
});

// 监听视图切换：从 homepage → workspace 时加载项目
watch(() => appStore.currentView, async (newView, oldView) => {
  if (newView === 'workspace' && oldView === 'homepage') {
    await enterWorkspace();
  }
});

const isBuildComplete = ref(false);

onMounted(() => {
  window.addEventListener('bimcanvas:build-complete', () => {
    debugStore.log('Build Complete Event Received.');
    isBuildComplete.value = true;
  });
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});

const handleKeydown = (e: KeyboardEvent) => {
  if (e.ctrlKey && e.key === '`') {
    debugStore.toggle();
  }

  if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement).tagName)) return;

  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'z') {
    e.preventDefault();
    if (e.shiftKey) {
      if (store.canRedo) store.redo();
    } else {
      if (store.canUndo) store.undo();
    }
  } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'y') {
    e.preventDefault();
    if (store.canRedo) store.redo();
  }
};
</script>

<template>
  <!-- 首页 -->
  <HomePage v-if="appStore.currentView === 'homepage'" />

  <!-- 工作区 -->
  <template v-else>
    <BlueprintLoader
      :active="loaderProps.active"
      :target-spacing="loaderProps.spacing"
      :target-offset-x="loaderProps.offsetX"
      :target-offset-y="loaderProps.offsetY"
    />
    <MainLayout :loading-stage="loadingStage" :build-complete="isBuildComplete">
      <ThreeCanvas />
    </MainLayout>
  </template>

  <!-- 全局组件 -->
  <DebugConsole />
  <BranchMergeWizard />
  <AgentNotificationModal />
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
