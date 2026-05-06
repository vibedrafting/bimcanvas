<script setup lang="ts">
import { onMounted, ref, watch, onUnmounted, nextTick } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import ThreeCanvas from './components/Canvas/ThreeCanvas.vue';
import BlueprintLoader from './components/UI/BlueprintLoader.vue';
import HomePage from './views/HomePage.vue';
import { useCanvasStore } from './stores/canvasStore';
import { useAppStore } from './stores/appStore';
import { ChangeSource } from './types/history';
import { themeService } from './services/theme/ThemeService';
import { getWebRuntime } from './runtime/runtimeRegistry';
import { supports } from './runtime/WebRuntimeProtocol';

import { ViewCalculator } from './services/interaction/ViewCalculator';
import DebugConsole from './components/UI/DebugConsole.vue';
import BranchMergeWizard from './components/UI/merge/BranchMergeWizard.vue';
import AgentNotificationModal from './components/UI/AgentNotificationModal.vue';
import { useDebugStore } from './stores/debugStore';

const store = useCanvasStore();
const appStore = useAppStore();
const debugStore = useDebugStore();
const runtime = getWebRuntime();
const canUseGitBranching = supports(runtime.capabilities.gitBranching);

// 初始化状态：在判断完 Server 状态前，不渲染任何视图
const isInitialized = ref(false);

// 工作区 cinematic 状态
const isSplashShowing = ref(true);
const loaderProps = ref<{ spacing?: number, offsetX?: number, offsetY?: number, active: boolean }>({ active: false });
const loadingStage = ref(0);
const isBuildComplete = ref(false);

// 防止 enterWorkspace 被并发调用
let enterWorkspaceLock = false;
let reuseExistingProjectOnNextEnter = false;

/**
 * 执行工作区加载 + cinematic sequence
 * 唯一的入口点，由 watch 触发
 */
const enterWorkspace = async () => {
  console.log('[OpenPerf] T3 enterWorkspace', Date.now());
  if (enterWorkspaceLock) {
    debugStore.warn('[App] enterWorkspace already running, skipping');
    return;
  }
  enterWorkspaceLock = true;

  try {
    // 重置 workspace UI 状态
    isSplashShowing.value = true;
    loaderProps.value = { active: true };
    console.log('[OpenPerf] T4 loader active', Date.now());
    loadingStage.value = 0;
    isBuildComplete.value = false;

    // 等待下一帧，确保 MainLayout/ThreeCanvas 已挂载
    await nextTick();

    const minTimePromise = new Promise(resolve => setTimeout(resolve, 2500));

    const shouldReuseExistingProject =
      store.projectData &&
      (reuseExistingProjectOnNextEnter || !supports(runtime.capabilities.serverPersistence));

    let loaded = Boolean(store.projectData);
    if (shouldReuseExistingProject) {
      reuseExistingProjectOnNextEnter = false;
      debugStore.log('[App] Reusing projectData already loaded in canvasStore');
    } else {
      // Connected 模式下重新从 Runtime 拉取，确保打开项目/分支切换后的数据是最新的。
      // Standalone 模式只有在没有 projectData 时才会走这里，通常返回 null 并停留首页。
      store.projectData = null;
      loaded = await store.loadInitialProject(ChangeSource.SystemInit);
    }

    if (loaded) {
      appStore.applyPendingProjectWarning();
    } else {
      appStore.clearPendingProjectWarnings();
    }

    // 计算目标视图
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
      }
    } else {
      debugStore.warn('No project data found after load.');
    }

    // 等待最小展示时间
    await minTimePromise;

    // Cinematic Sequence
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

  } catch (error) {
    console.error('Failed to enter workspace:', error);
    debugStore.error(`enterWorkspace failed: ${error}`);
  } finally {
    enterWorkspaceLock = false;
  }
};

onMounted(async () => {
  themeService.init();
  debugStore.log('App Mounted. Initializing...');

  window.addEventListener('keydown', handleKeydown);
  window.addEventListener('bimcanvas:build-complete', () => {
    debugStore.log('Build Complete Event Received.');
    isBuildComplete.value = true;
  });

  // 检查当前 Runtime 是否已有初始项目。Standalone 默认返回 null，不会触发 Server API。
  try {
    const loaded = await store.loadInitialProject(ChangeSource.SystemInit);
    if (loaded) {
      debugStore.log('Runtime has loaded project, entering workspace...');
      reuseExistingProjectOnNextEnter = true;
      appStore.goToWorkspace();
    } else {
      debugStore.log('No project loaded, showing homepage...');
    }
  } catch (err) {
    debugStore.warn(`Failed to load initial project: ${err}, showing homepage...`);
  }

  isInitialized.value = true;
});

// 核心：监听视图切换，homepage → workspace 时加载项目
// 这是进入工作区的唯一触发点
watch(() => appStore.currentView, async (newView, oldView) => {
  if (newView === 'workspace' && oldView === 'homepage') {
    debugStore.log(`[App] View changed: ${oldView} → ${newView}, entering workspace...`);
    await enterWorkspace();
  }
});

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeydown);
});

const handleKeydown = (e: KeyboardEvent) => {
  if (e.ctrlKey && e.key === '`') {
    debugStore.toggle();
  }

  if (['INPUT', 'TEXTAREA'].includes((e.target as HTMLElement).tagName)) return;

  // 只在工作区处理 undo/redo
  if (appStore.currentView !== 'workspace') return;

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
  <!-- 初始化完成前不显示任何内容 -->
  <template v-if="isInitialized">
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
  </template>

  <!-- 全局组件（始终存在） -->
  <DebugConsole />
  <BranchMergeWizard v-if="canUseGitBranching" />
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
