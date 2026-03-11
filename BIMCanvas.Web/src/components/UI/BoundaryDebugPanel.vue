<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick, computed } from 'vue';
import * as THREE from 'three';
import type { ZoneBoundaryData, BoundarySegment, BoundarySegmentType } from '../../types/canvas';

// ==================== 状态 ====================

const visible = ref(false);
const boundaryData = ref<ZoneBoundaryData[]>([]);
const selectedSegment = ref<BoundarySegment | null>(null);
const selectedZoneId = ref<string>('');

// 拖拽浮窗
const panelRef = ref<HTMLDivElement>();
const isDraggingPanel = ref(false);
const panelPos = ref({ x: -1, y: -1 }); // -1 表示使用默认位置
const dragOffset = ref({ x: 0, y: 0 });

// Three.js
const canvasRef = ref<HTMLCanvasElement>();
let scene: THREE.Scene;
let camera: THREE.OrthographicCamera;
let renderer: THREE.WebGLRenderer;
let animationId: number;
const meshMap = new Map<THREE.Mesh, { segment: BoundarySegment; zoneId: string }>();
let selectionOutline: THREE.LineSegments | null = null;

// 视口交互
const isPanning = ref(false);
const panStart = { x: 0, y: 0 };
const cameraStart = { x: 0, z: 0 };

// ==================== 配色 ====================

const SEGMENT_COLORS: Record<BoundarySegmentType, number> = {
  wall: 0x37474F,     // Blue Grey 800
  door: 0x00E676,     // Spring Green
  window: 0x64B5F6,   // Blue 300
  passage: 0xFFB74D,  // Orange 300 (暖橙)
};

const SEGMENT_LABELS: Record<BoundarySegmentType, string> = {
  wall: 'Wall',
  door: 'Door',
  window: 'Window',
  passage: 'Passage',
};

const WALL_THICKNESS = 200; // mm，视觉厚度
const SELECTION_COLOR = 0x3b82f6; // 与主场景选中蓝色一致

// ==================== 属性面板 ====================

const properties = computed(() => {
  if (!selectedSegment.value) return [];
  const seg = selectedSegment.value;
  const dx = seg.end[0] - seg.start[0];
  const dy = seg.end[1] - seg.start[1];
  const length = Math.round(Math.sqrt(dx * dx + dy * dy));

  const props: { key: string; value: string }[] = [
    { key: 'zoneId', value: selectedZoneId.value },
    { key: 'type', value: seg.type },
  ];
  if (seg.id) {
    props.push({ key: 'id', value: seg.id });
  }
  props.push(
    { key: 'start', value: `[${seg.start[0]}, ${seg.start[1]}]` },
    { key: 'end', value: `[${seg.end[0]}, ${seg.end[1]}]` },
    { key: 'length', value: `${length} mm` },
  );
  return props;
});

// ==================== SignalR 事件 ====================

function onBoundaryDebug(event: Event) {
  const data = (event as CustomEvent).detail as ZoneBoundaryData[];
  if (!data || !Array.isArray(data)) return;
  boundaryData.value = data;
  selectedSegment.value = null;
  visible.value = true;
}

onMounted(() => {
  window.addEventListener('bimcanvas:boundary-debug', onBoundaryDebug);
});

onUnmounted(() => {
  window.removeEventListener('bimcanvas:boundary-debug', onBoundaryDebug);
  disposeThree();
});

// ==================== Three.js 初始化 ====================

watch(visible, async (val) => {
  if (val) {
    await nextTick();
    initThree();
    buildScene();
    animate();
  } else {
    disposeThree();
  }
});

watch(boundaryData, () => {
  if (visible.value && renderer) {
    buildScene();
  }
});

function initThree() {
  if (!canvasRef.value) return;
  const canvas = canvasRef.value;
  const rect = canvas.parentElement!.getBoundingClientRect();
  const w = rect.width;
  const h = rect.height;

  scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0a0a0f);

  // 正交相机 Y-up 俯视
  camera = new THREE.OrthographicCamera(-1, 1, 1, -1, 1, 50000);
  camera.position.set(0, 10000, 0);
  camera.up.set(0, 0, -1);

  renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setSize(w, h);
  renderer.setPixelRatio(window.devicePixelRatio);

  // 添加环境光
  scene.add(new THREE.AmbientLight(0xffffff, 1.0));
}

function disposeThree() {
  if (animationId) {
    cancelAnimationFrame(animationId);
    animationId = 0;
  }
  meshMap.clear();
  selectionOutline = null;
  if (renderer) {
    renderer.dispose();
  }
}

function animate() {
  if (!renderer || !visible.value) return;
  animationId = requestAnimationFrame(animate);
  renderer.render(scene, camera);
}

// ==================== 场景构建 ====================

function buildScene() {
  if (!scene || !camera || !renderer) return;

  // 清空旧内容
  while (scene.children.length > 0) {
    const child = scene.children[0];
    scene.remove(child);
  }
  meshMap.clear();
  selectionOutline = null;
  scene.add(new THREE.AmbientLight(0xffffff, 1.0));

  if (boundaryData.value.length === 0) return;

  // 计算所有段的 AABB
  let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

  for (const zone of boundaryData.value) {
    for (const seg of zone.segments) {
      minX = Math.min(minX, seg.start[0], seg.end[0]);
      minY = Math.min(minY, seg.start[1], seg.end[1]);
      maxX = Math.max(maxX, seg.start[0], seg.end[0]);
      maxY = Math.max(maxY, seg.start[1], seg.end[1]);
    }
  }

  // 为每个 zone 的段计算外法线方向
  for (const zone of boundaryData.value) {
    const outwardSign = computeWindingSign(zone.segments);

    for (const seg of zone.segments) {
      const mesh = createSegmentMesh(seg, outwardSign);
      mesh.userData = { segment: seg, zoneId: zone.zoneId };
      meshMap.set(mesh, { segment: seg, zoneId: zone.zoneId });
      scene.add(mesh);
    }
  }

  // 自适应相机
  const cx = (minX + maxX) / 2;
  const cy = (minY + maxY) / 2;
  const rangeX = maxX - minX + WALL_THICKNESS * 4;
  const rangeY = maxY - minY + WALL_THICKNESS * 4;

  const canvas = canvasRef.value!;
  const rect = canvas.parentElement!.getBoundingClientRect();
  const aspect = rect.width / rect.height;

  let halfW: number, halfH: number;
  if (rangeX / rangeY > aspect) {
    halfW = rangeX / 2 * 1.1;
    halfH = halfW / aspect;
  } else {
    halfH = rangeY / 2 * 1.1;
    halfW = halfH * aspect;
  }

  camera.left = -halfW;
  camera.right = halfW;
  camera.top = halfH;
  camera.bottom = -halfH;
  camera.position.set(cx, 10000, cy);
  camera.lookAt(cx, 0, cy);
  camera.updateProjectionMatrix();
}

/** 计算段列表闭合多边形的绕向符号（+1=CCW, -1=CW）*/
function computeWindingSign(segments: BoundarySegment[]): number {
  // Shoelace formula
  let area = 0;
  for (const seg of segments) {
    area += (seg.start[0] * seg.end[1] - seg.end[0] * seg.start[1]);
  }
  return area >= 0 ? 1 : -1;
}

/** 创建单个段的 Mesh（带厚度的矩形，向外侧偏移） */
function createSegmentMesh(seg: BoundarySegment, outwardSign: number): THREE.Mesh {
  const sx = seg.start[0], sy = seg.start[1];
  const ex = seg.end[0], ey = seg.end[1];

  // 方向向量
  const dx = ex - sx;
  const dy = ey - sy;
  const len = Math.sqrt(dx * dx + dy * dy);
  if (len < 1e-6) {
    // 零长度段，创建一个点状 mesh
    const geo = new THREE.BoxGeometry(10, 10, 10);
    return new THREE.Mesh(geo, new THREE.MeshBasicMaterial({ color: 0xff0000 }));
  }

  const dirX = dx / len;
  const dirY = dy / len;

  // 外法线：对于 CCW 绕向，左侧是外侧 → (-dirY, dirX)
  // outwardSign 控制方向
  const nx = -dirY * outwardSign;
  const ny = dirX * outwardSign;

  const t = WALL_THICKNESS;

  // 4 个角点：内侧沿线段，外侧偏移 thickness
  // 注意：Three.js 中 X→X, Y→Z（因为 Y-up 俯视）
  const p0 = [sx, sy];           // 内侧起点
  const p1 = [ex, ey];           // 内侧终点
  const p2 = [ex + nx * t, ey + ny * t]; // 外侧终点
  const p3 = [sx + nx * t, sy + ny * t]; // 外侧起点

  // 创建 Shape（2D 平面，之后旋转到 XZ 平面）
  const shape = new THREE.Shape();
  shape.moveTo(p0[0], p0[1]);
  shape.lineTo(p1[0], p1[1]);
  shape.lineTo(p2[0], p2[1]);
  shape.lineTo(p3[0], p3[1]);
  shape.closePath();

  const geometry = new THREE.ShapeGeometry(shape);
  const material = new THREE.MeshBasicMaterial({
    color: SEGMENT_COLORS[seg.type as BoundarySegmentType] ?? 0x888888,
    side: THREE.DoubleSide,
  });

  const mesh = new THREE.Mesh(geometry, material);
  // Shape 在 XY 平面，旋转到 XZ 平面（Y-up 俯视）
  mesh.rotation.x = -Math.PI / 2;
  mesh.position.y = seg.type === 'wall' ? 1 : 2; // door/window/passage 略高于 wall

  return mesh;
}

// ==================== 交互：点击选中 ====================

function onCanvasClick(event: MouseEvent) {
  if (!renderer || !camera || !scene) return;
  if (isPanning.value) return;

  const canvas = canvasRef.value!;
  const rect = canvas.getBoundingClientRect();
  const mouse = new THREE.Vector2(
    ((event.clientX - rect.left) / rect.width) * 2 - 1,
    -((event.clientY - rect.top) / rect.height) * 2 + 1
  );

  const raycaster = new THREE.Raycaster();
  raycaster.setFromCamera(mouse, camera);

  const meshes = Array.from(meshMap.keys());
  const intersects = raycaster.intersectObjects(meshes);

  // 移除旧高亮
  if (selectionOutline) {
    scene.remove(selectionOutline);
    selectionOutline = null;
  }

  if (intersects.length > 0) {
    const hit = intersects[0].object as THREE.Mesh;
    const data = meshMap.get(hit);
    if (data) {
      selectedSegment.value = data.segment;
      selectedZoneId.value = data.zoneId;

      // 添加选中高亮描边
      const edges = new THREE.EdgesGeometry(hit.geometry);
      selectionOutline = new THREE.LineSegments(
        edges,
        new THREE.LineBasicMaterial({ color: SELECTION_COLOR, linewidth: 2, depthTest: false })
      );
      selectionOutline.rotation.copy(hit.rotation);
      selectionOutline.position.copy(hit.position);
      selectionOutline.position.y += 0.5; // 略高于 mesh
      scene.add(selectionOutline);
    }
  } else {
    selectedSegment.value = null;
    selectedZoneId.value = '';
  }
}

// ==================== 交互：Pan (中键/右键拖拽) ====================

function onCanvasMouseDown(event: MouseEvent) {
  if (event.button === 1 || event.button === 2) {
    event.preventDefault();
    isPanning.value = true;
    panStart.x = event.clientX;
    panStart.y = event.clientY;
    cameraStart.x = camera.position.x;
    cameraStart.z = camera.position.z;
    window.addEventListener('mousemove', onCanvasPanMove);
    window.addEventListener('mouseup', onCanvasPanEnd);
  }
}

function onCanvasPanMove(event: MouseEvent) {
  if (!isPanning.value || !camera || !canvasRef.value) return;
  const canvas = canvasRef.value;
  const rect = canvas.parentElement!.getBoundingClientRect();
  const w = rect.width;
  const h = rect.height;

  const viewW = camera.right - camera.left;
  const viewH = camera.top - camera.bottom;

  const dx = (event.clientX - panStart.x) / w * viewW;
  const dy = (event.clientY - panStart.y) / h * viewH;

  camera.position.x = cameraStart.x - dx;
  camera.position.z = cameraStart.z + dy;
  camera.updateProjectionMatrix();
}

function onCanvasPanEnd() {
  isPanning.value = false;
  window.removeEventListener('mousemove', onCanvasPanMove);
  window.removeEventListener('mouseup', onCanvasPanEnd);
}

// ==================== 交互：Zoom (滚轮) ====================

function onCanvasWheel(event: WheelEvent) {
  event.preventDefault();
  if (!camera) return;

  const factor = event.deltaY > 0 ? 1.1 : 0.9;
  const cx = (camera.left + camera.right) / 2;
  const cy = (camera.top + camera.bottom) / 2;
  const halfW = (camera.right - camera.left) / 2 * factor;
  const halfH = (camera.top - camera.bottom) / 2 * factor;

  camera.left = cx - halfW;
  camera.right = cx + halfW;
  camera.top = cy + halfH;
  camera.bottom = cy - halfH;
  camera.updateProjectionMatrix();
}

function onCanvasContextMenu(event: MouseEvent) {
  event.preventDefault();
}

// ==================== 浮窗拖拽 ====================

function onHeaderMouseDown(event: MouseEvent) {
  if (event.button !== 0) return;
  isDraggingPanel.value = true;
  const panel = panelRef.value!;
  const rect = panel.getBoundingClientRect();
  dragOffset.x = event.clientX - rect.left;
  dragOffset.y = event.clientY - rect.top;
  window.addEventListener('mousemove', onPanelDrag);
  window.addEventListener('mouseup', onPanelDragEnd);
}

function onPanelDrag(event: MouseEvent) {
  if (!isDraggingPanel.value) return;
  panelPos.value = {
    x: event.clientX - dragOffset.x,
    y: event.clientY - dragOffset.y,
  };
}

function onPanelDragEnd() {
  isDraggingPanel.value = false;
  window.removeEventListener('mousemove', onPanelDrag);
  window.removeEventListener('mouseup', onPanelDragEnd);
}

// ==================== resize ====================

function onResize() {
  if (!renderer || !canvasRef.value) return;
  const rect = canvasRef.value.parentElement!.getBoundingClientRect();
  renderer.setSize(rect.width, rect.height);
  // 保持视口比例
  const aspect = rect.width / rect.height;
  const halfH = (camera.top - camera.bottom) / 2;
  const cy = (camera.top + camera.bottom) / 2;
  camera.left = cy - halfH * aspect;
  camera.right = cy + halfH * aspect;
  camera.updateProjectionMatrix();
}

onMounted(() => {
  window.addEventListener('resize', onResize);
});

onUnmounted(() => {
  window.removeEventListener('resize', onResize);
});

// ==================== 面板控制 ====================

function close() {
  visible.value = false;
  selectedSegment.value = null;
}

const panelStyle = computed(() => {
  if (panelPos.value.x >= 0) {
    return {
      left: `${panelPos.value.x}px`,
      top: `${panelPos.value.y}px`,
      right: 'auto',
      bottom: 'auto',
    };
  }
  // 默认位置：右下角
  return {
    right: '24px',
    bottom: '24px',
  };
});

// 段统计
const segmentStats = computed(() => {
  const stats: Record<string, number> = {};
  for (const zone of boundaryData.value) {
    for (const seg of zone.segments) {
      stats[seg.type] = (stats[seg.type] || 0) + 1;
    }
  }
  return stats;
});

const totalSegments = computed(() => {
  return boundaryData.value.reduce((sum, z) => sum + z.segments.length, 0);
});
</script>

<template>
  <Teleport to="body">
    <Transition name="debug-panel">
      <div
        v-if="visible"
        ref="panelRef"
        class="boundary-debug-panel"
        :style="panelStyle"
      >
        <!-- 标题栏（可拖动） -->
        <div class="panel-header" @mousedown="onHeaderMouseDown">
          <div class="header-left">
            <span class="title">Boundary Debug</span>
            <span class="badge">{{ boundaryData.length }} zone{{ boundaryData.length > 1 ? 's' : '' }} / {{ totalSegments }} segs</span>
          </div>
          <button class="close-btn" @click="close" title="Close">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <line x1="18" y1="6" x2="6" y2="18"></line>
              <line x1="6" y1="6" x2="18" y2="18"></line>
            </svg>
          </button>
        </div>

        <!-- 图例 -->
        <div class="legend-bar">
          <div v-for="(count, type) in segmentStats" :key="type" class="legend-item">
            <span class="legend-color" :style="{ backgroundColor: '#' + (SEGMENT_COLORS[type as BoundarySegmentType] ?? 0x888888).toString(16).padStart(6, '0') }"></span>
            <span class="legend-label">{{ SEGMENT_LABELS[type as BoundarySegmentType] ?? type }} ({{ count }})</span>
          </div>
        </div>

        <!-- Three.js Canvas -->
        <div class="canvas-container">
          <canvas
            ref="canvasRef"
            @click="onCanvasClick"
            @mousedown="onCanvasMouseDown"
            @wheel="onCanvasWheel"
            @contextmenu="onCanvasContextMenu"
          ></canvas>
        </div>

        <!-- 属性面板 -->
        <div v-if="selectedSegment" class="props-section">
          <div class="props-header">
            <span class="props-type-badge" :style="{ backgroundColor: '#' + (SEGMENT_COLORS[selectedSegment.type as BoundarySegmentType] ?? 0x888888).toString(16).padStart(6, '0') }">
              {{ SEGMENT_LABELS[selectedSegment.type as BoundarySegmentType] ?? selectedSegment.type }}
            </span>
          </div>
          <div class="prop-list">
            <div v-for="prop in properties" :key="prop.key" class="prop-row">
              <span class="label">{{ prop.key }}</span>
              <span class="value">{{ prop.value }}</span>
            </div>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.boundary-debug-panel {
  position: fixed;
  width: 50vw;
  height: 50vh;
  min-width: 400px;
  min-height: 320px;
  z-index: 500;

  display: flex;
  flex-direction: column;
  overflow: hidden;

  /* Aurora Glass */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 16px;
  box-shadow:
    0 12px 40px rgba(0, 0, 0, 0.5),
    0 0 0 1px rgba(255, 255, 255, 0.1) inset;
}

.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  cursor: grab;
  user-select: none;
  flex-shrink: 0;

  &:active {
    cursor: grabbing;
  }

  .header-left {
    display: flex;
    align-items: center;
    gap: 10px;
  }

  .title {
    font-weight: 600;
    font-size: 0.85rem;
    color: var(--text-primary);
    letter-spacing: 0.5px;
  }

  .badge {
    font-size: 0.72rem;
    color: var(--text-tertiary);
    background: rgba(255, 255, 255, 0.08);
    padding: 2px 8px;
    border-radius: 10px;
  }

  .close-btn {
    background: transparent;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 4px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.2s ease;

    svg {
      width: 16px;
      height: 16px;
    }

    &:hover {
      background: rgba(239, 68, 68, 0.2);
      color: #ef4444;
    }
  }
}

.legend-bar {
  display: flex;
  gap: 12px;
  padding: 6px 16px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.06);
  flex-shrink: 0;

  .legend-item {
    display: flex;
    align-items: center;
    gap: 5px;
  }

  .legend-color {
    width: 10px;
    height: 10px;
    border-radius: 2px;
    flex-shrink: 0;
  }

  .legend-label {
    font-size: 0.72rem;
    color: var(--text-secondary);
  }
}

.canvas-container {
  flex: 1;
  position: relative;
  overflow: hidden;

  canvas {
    width: 100% !important;
    height: 100% !important;
    display: block;
  }
}

.props-section {
  border-top: 1px solid rgba(255, 255, 255, 0.1);
  padding: 8px 16px 10px;
  flex-shrink: 0;
  max-height: 140px;
  overflow-y: auto;

  .props-header {
    margin-bottom: 6px;
  }

  .props-type-badge {
    display: inline-block;
    font-size: 0.72rem;
    font-weight: 600;
    color: #fff;
    padding: 2px 10px;
    border-radius: 8px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .prop-list {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }

  .prop-row {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: 12px;
    font-size: 0.78rem;
    line-height: 1.4;

    .label {
      color: var(--text-secondary);
      flex-shrink: 0;
    }

    .value {
      color: var(--text-primary);
      text-align: right;
      font-family: var(--font-mono);
      word-break: break-word;
    }
  }
}

/* 过渡动画 */
.debug-panel-enter-active {
  transition: all 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
}
.debug-panel-leave-active {
  transition: all 0.2s ease;
}
.debug-panel-enter-from {
  opacity: 0;
  transform: scale(0.9) translateY(20px);
}
.debug-panel-leave-to {
  opacity: 0;
  transform: scale(0.95);
}
</style>
