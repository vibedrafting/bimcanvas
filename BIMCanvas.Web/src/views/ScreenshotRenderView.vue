<script setup lang="ts">
import { onMounted, nextTick } from 'vue';
import ThreeCanvas from '../components/Canvas/ThreeCanvas.vue';
import { useCanvasStore } from '../stores/canvasStore';
import { themeService } from '../services/theme/ThemeService';
import { ScreenshotService } from '../services/ScreenshotService';
import { getThreeSceneService } from '../services/three/ThreeSceneService';
import { LayerManager } from '../services/three/LayerManager';
import type { ProjectData, Polygon2D, Room, Zone } from '../types/canvas';

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
  layerPreset?: string;
  layerEnable?: string[];
  layerDisable?: string[];
  viewport?: ViewportConfig;
  theme?: 'dark' | 'light';
}

declare global {
  interface Window {
    __renderConfig?: RenderConfig;
    __renderReady?: boolean;
    __renderError?: string;
    __render?: (config: RenderConfig) => Promise<void>;
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

const DEFAULT_FULL_PADDING = 1000;
const DEFAULT_VIEW_PADDING = 500;
const PADDING_RATIO = 0.05;

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

const expandBounds = (bounds: Bounds2D, padding: number): Bounds2D => ({
  minX: bounds.minX - padding,
  minY: bounds.minY - padding,
  maxX: bounds.maxX + padding,
  maxY: bounds.maxY + padding
});

const computePadding = (bounds: Bounds2D, mode: ViewportMode): number => {
  const width = bounds.maxX - bounds.minX;
  const height = bounds.maxY - bounds.minY;
  const maxSize = Math.max(width, height);
  const minPadding = mode === 'full' ? DEFAULT_FULL_PADDING : DEFAULT_VIEW_PADDING;
  if (!Number.isFinite(maxSize) || maxSize <= 0) return minPadding;
  return Math.max(minPadding, maxSize * PADDING_RATIO);
};

const computeProjectBounds = (projectData: ProjectData): Bounds2D | null => {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  const addPolygon = (polygon?: Polygon2D | null) => {
    if (!polygon || polygon.length === 0) return;
    polygon.forEach(([x, y]) => {
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    });
  };

  const baseline = projectData.baseline;
  baseline?.walls?.forEach(wall => addPolygon(wall.polygon));
  baseline?.columns?.forEach(column => addPolygon(column.polygon));
  baseline?.rooms?.forEach(room => addPolygon(room.boundary?.shell));

  projectData.activeScheme?.modules?.forEach(mod => addPolygon(mod.bounds));

  if (!Number.isFinite(minX) || !Number.isFinite(minY)) {
    return null;
  }

  return { minX, minY, maxX, maxY };
};

const normalizePreset = (preset?: string, fallback?: ViewMode): ViewMode => {
  const value = (preset ?? fallback ?? 'human').toLowerCase().trim();
  if (value === 'ai' || value === 'agent') return 'ai';
  return 'human';
};

const normalizeLayerName = (name: string) => name.trim().toLowerCase().replace(/[\s_-]+/g, '');

const LAYER_NAME_MAP: Record<string, number> = {
  grid: LayerManager.LAYER_GRID,
  architecture: LayerManager.LAYER_ARCHITECTURE,
  furniture: LayerManager.LAYER_FURNITURE,
  labels: LayerManager.LAYER_LABELS,
  label: LayerManager.LAYER_LABELS,
  bounds: LayerManager.LAYER_BOUNDS,
  outline: LayerManager.LAYER_OUTLINE,
  svg: LayerManager.LAYER_SVG,
  svgpreview: LayerManager.LAYER_SVG,
  zones: LayerManager.LAYER_ZONES,
  zone: LayerManager.LAYER_ZONES,
  semantic: LayerManager.LAYER_SEMANTIC,
  aivision: LayerManager.LAYER_AI_VISION,
  model: LayerManager.LAYER_MODEL
};

const resolveLayerIds = (names?: string[] | null): number[] => {
  if (!names || names.length === 0) return [];
  const ids = new Set<number>();
  names.forEach((name) => {
    const key = normalizeLayerName(name);
    const id = LAYER_NAME_MAP[key];
    if (typeof id === 'number') {
      ids.add(id);
      return;
    }
    console.warn(`[ScreenshotRenderView] Unknown layer name: ${name}`);
  });
  return Array.from(ids);
};

const dispatchPreset = (preset: ViewMode) => {
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: preset }));
};

const dispatchLayerToggle = (layerId: number, visible: boolean) => {
  window.dispatchEvent(new CustomEvent('bimcanvas:layer-toggle', {
    detail: { layerId, visible }
  }));
};

const computeRoomBounds = (room: Room): Bounds2D => {
  const shell = room.boundary?.shell;
  if (!shell || shell.length === 0) {
    throw new Error(`Room ${room.id} has no boundary`);
  }
  return computeBoundsFromPolygon(shell);
};

const computeZoneBounds = (zone: Zone): Bounds2D => {
  const boundary = zone.computedBoundary ?? zone.rawBoundary;
  if (!boundary || boundary.length === 0) {
    throw new Error(`Room zone ${zone.id} has no boundary`);
  }
  return computeBoundsFromPolygon(boundary);
};

const applyLegacyLayers = (viewMode: ViewMode, layers?: number[] | null) => {
  if (!layers || layers.length === 0) {
    dispatchPreset(viewMode);
    return;
  }

  ALL_LAYERS.forEach((layerId) => {
    dispatchLayerToggle(layerId, false);
  });

  [...new Set(layers)].forEach((layerId) => {
    dispatchLayerToggle(layerId, true);
  });
};

const applyLayerConfig = (config: RenderConfig) => {
  const hasNewConfig = Boolean(
    (config.layerPreset && config.layerPreset.trim()) ||
    (config.layerEnable && config.layerEnable.length) ||
    (config.layerDisable && config.layerDisable.length)
  );

  if (!hasNewConfig) {
    applyLegacyLayers(config.viewMode ?? 'human', config.layers);
    return;
  }

  const preset = normalizePreset(config.layerPreset, config.viewMode);
  dispatchPreset(preset);

  const enableIds = resolveLayerIds(config.layerEnable);
  const disableIds = resolveLayerIds(config.layerDisable);
  const disableSet = new Set(disableIds);

  enableIds.forEach(layerId => {
    if (!disableSet.has(layerId)) {
      dispatchLayerToggle(layerId, true);
    }
  });

  disableIds.forEach(layerId => {
    dispatchLayerToggle(layerId, false);
  });
};

const applyViewport = async (projectData: ProjectData, viewport?: ViewportConfig) => {
  const sceneService = await waitForSceneService();
  const mode = viewport?.mode ?? 'full';

  if (mode === 'full') {
    const bounds = computeProjectBounds(projectData);
    if (!bounds) return;
    const padding = computePadding(bounds, mode);
    sceneService.fitToBounds(expandBounds(bounds, padding));
    return;
  }

  if (mode === 'bounds') {
    if (!viewport?.bounds) {
      throw new Error('Viewport bounds missing');
    }
    const padding = computePadding(viewport.bounds, mode);
    sceneService.fitToBounds(expandBounds(viewport.bounds, padding));
    return;
  }

  if (mode === 'room') {
    const roomId = viewport?.roomId;
    if (!roomId) {
      throw new Error('Viewport roomId missing');
    }
    const room = projectData.baseline?.rooms?.find(r => r.id === roomId);
    if (room) {
      const bounds = computeRoomBounds(room);
      const padding = computePadding(bounds, mode);
      sceneService.fitToBounds(expandBounds(bounds, padding));
      return;
    }

    const roomZone = projectData.computed?.roomZones?.find(z => z.id === roomId || z.roomId === roomId);
    if (!roomZone) {
      throw new Error(`Room not found: ${roomId}`);
    }
    const bounds = computeZoneBounds(roomZone);
    const padding = computePadding(bounds, mode);
    sceneService.fitToBounds(expandBounds(bounds, padding));
  }
};

const renderWithConfig = async (config: RenderConfig) => {
  window.__renderReady = false;
  window.__renderError = undefined;

  try {
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
    window.dispatchEvent(new CustomEvent('bimcanvas:play-build-sequence-fast'));
    await buildPromise;

    applyLayerConfig(config);
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
};

onMounted(async () => {
  window.__render = renderWithConfig;

  const config = window.__renderConfig;
  if (config) {
    await renderWithConfig(config);
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
