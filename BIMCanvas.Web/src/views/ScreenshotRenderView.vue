<script setup lang="ts">
import { onMounted, nextTick } from 'vue';
import ThreeCanvas from '../components/Canvas/ThreeCanvas.vue';
import { useCanvasStore } from '../stores/canvasStore';
import { themeService } from '../services/theme/ThemeService';
import { ScreenshotService } from '../services/ScreenshotService';
import { getThreeSceneService } from '../services/three/ThreeSceneService';
import { LayerManager } from '../services/three/LayerManager';
import type { ProjectData, Polygon2D, Room } from '../types/canvas';

type ViewMode = 'human' | 'ai';
type ViewportMode = 'full' | 'bounds' | 'room';

interface Bounds2D {
  minX: number;
  minY: number;
  maxX: number;
  maxY: number;
}

interface ViewportConfig {
  mode: ViewportMode;
  roomId?: string;
  bounds?: Bounds2D;
}

interface RenderConfig {
  projectData: ProjectData;
  viewMode?: ViewMode;
  layers?: number[];
  viewport?: ViewportConfig;
  theme?: 'dark' | 'light';
}

declare global {
  interface Window {
    __renderConfig?: RenderConfig;
    __renderReady?: boolean;
    __renderError?: string;
    __capture?: () => Promise<string>;
  }
}

const store = useCanvasStore();

const ALL_LAYERS = [
  LayerManager.LAYER_MODEL,
  LayerManager.LAYER_GRID,
  LayerManager.LAYER_LABELS,
  LayerManager.LAYER_BOUNDS,
  LayerManager.LAYER_OUTLINE,
  LayerManager.LAYER_SVG,
  LayerManager.LAYER_ZONES,
  LayerManager.LAYER_SEMANTIC,
  LayerManager.LAYER_AI_VISION,
  LayerManager.LAYER_ARCHITECTURE,
  LayerManager.LAYER_FURNITURE
];

const wait = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

const waitForSceneService = async (timeoutMs = 10000) => {
  const start = Date.now();
  while (Date.now() - start < timeoutMs) {
    const service = getThreeSceneService();
    if (service) return service;
    await wait(50);
  }
  throw new Error('ThreeSceneService not ready');
};

const waitForBuildComplete = (timeoutMs = 20000) => new Promise<void>((resolve, reject) => {
  const onComplete = () => {
    window.clearTimeout(timer);
    resolve();
  };

  const timer = window.setTimeout(() => {
    window.removeEventListener('bimcanvas:build-complete', onComplete);
    reject(new Error('Build complete timeout'));
  }, timeoutMs);

  window.addEventListener('bimcanvas:build-complete', onComplete, { once: true });
});

const waitFrames = (count: number) => new Promise<void>((resolve) => {
  let frames = 0;
  const step = () => {
    frames += 1;
    if (frames >= count) {
      resolve();
      return;
    }
    requestAnimationFrame(step);
  };
  requestAnimationFrame(step);
});

const computeBoundsFromPolygon = (polygon: Polygon2D): Bounds2D => {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  polygon.forEach(([x, y]) => {
    minX = Math.min(minX, x);
    minY = Math.min(minY, y);
    maxX = Math.max(maxX, x);
    maxY = Math.max(maxY, y);
  });

  if (!Number.isFinite(minX) || !Number.isFinite(minY)) {
    throw new Error('Invalid polygon bounds');
  }

  return { minX, minY, maxX, maxY };
};

const computeRoomBounds = (room: Room): Bounds2D => {
  const shell = room.boundary?.shell;
  if (!shell || shell.length === 0) {
    throw new Error(`Room ${room.id} has no boundary`);
  }
  return computeBoundsFromPolygon(shell);
};

const applyLayers = (viewMode: ViewMode, layers?: number[] | null) => {
  if (!layers || layers.length === 0) {
    window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: viewMode }));
    return;
  }

  ALL_LAYERS.forEach((layerId) => {
    window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', {
      detail: { layerId, visible: false }
    }));
  });

  [...new Set(layers)].forEach((layerId) => {
    window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', {
      detail: { layerId, visible: true }
    }));
  });
};

const applyViewport = async (projectData: ProjectData, viewport?: ViewportConfig) => {
  if (!viewport || viewport.mode === 'full') return;

  const sceneService = await waitForSceneService();

  if (viewport.mode === 'bounds') {
    if (!viewport.bounds) {
      throw new Error('Viewport bounds missing');
    }
    sceneService.fitToBounds(viewport.bounds);
    return;
  }

  if (viewport.mode === 'room') {
    const roomId = viewport.roomId;
    if (!roomId) {
      throw new Error('Viewport roomId missing');
    }
    const room = projectData.baseline?.rooms?.find(r => r.id === roomId);
    if (!room) {
      throw new Error(`Room not found: ${roomId}`);
    }
    sceneService.fitToBounds(computeRoomBounds(room));
  }
};

onMounted(async () => {
  window.__renderReady = false;
  window.__renderError = undefined;

  try {
    const config = window.__renderConfig;
    if (!config?.projectData) {
      throw new Error('Render config missing projectData');
    }

    themeService.init();
    if (config.theme) {
      themeService.setTheme(config.theme);
    }

    store.projectData = config.projectData;

    await nextTick();
    await waitForSceneService();

    const buildPromise = waitForBuildComplete();
    window.dispatchEvent(new CustomEvent('bimcanvas:play-build-sequence'));
    await buildPromise;

    applyLayers(config.viewMode ?? 'human', config.layers);
    await applyViewport(config.projectData, config.viewport);

    await waitFrames(3);

    const screenshotService = new ScreenshotService();
    window.__capture = async () => screenshotService.captureCanvas();
    window.__renderReady = true;
  } catch (error: any) {
    const message = error?.message ?? String(error);
    console.error('[ScreenshotRenderView] Failed:', message);
    window.__renderError = message;
  }
});
</script>

<template>
  <div class="screenshot-render-root">
    <ThreeCanvas />
  </div>
</template>

<style scoped>
.screenshot-render-root {
  position: fixed;
  inset: 0;
  width: 100vw;
  height: 100vh;
  background: var(--bg-canvas);
}
</style>
