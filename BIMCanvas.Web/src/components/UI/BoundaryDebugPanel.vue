<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch, nextTick, computed } from 'vue';
import * as THREE from 'three';
import type { ZoneBoundaryData, BoundarySegment, BoundarySegmentType } from '../../types/canvas';
import { useCanvasStore } from '../../stores/canvasStore';

// ==================== 状态 ====================

const store = useCanvasStore();
const visible = ref(false);
const boundaryData = ref<ZoneBoundaryData[]>([]);
const selectedSegment = ref<BoundarySegment | null>(null);
const selectedZoneId = ref<string>('');

// 拖拽浮窗
const panelRef = ref<HTMLDivElement | null>(null);
const isDraggingPanel = ref(false);
const panelPos = ref<{ x: number; y: number } | null>(null);
const dragOffset = ref({ x: 0, y: 0 });

// Three.js
const canvasRef = ref<HTMLCanvasElement | null>(null);
let scene: THREE.Scene | null = null;
let camera: THREE.OrthographicCamera | null = null;
let renderer: THREE.WebGLRenderer | null = null;
let animationId = 0;
const meshMap = new Map<THREE.Mesh, { segment: BoundarySegment; zoneId: string }>();
const zoneMeshMap = new Map<THREE.Mesh, string>();  // zone fill mesh → zoneId
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
  passage: 0x22c55e,  // 与 Zone 填充同色（"可通过 = 空间本身"）
};

const SEGMENT_LABELS: Record<BoundarySegmentType, string> = {
  wall: 'Wall',
  door: 'Door',
  window: 'Window',
  passage: 'Passage',
};

const ZONE_FILL_COLOR = 0x22c55e; // 绿色，与主画布一致
const ZONE_FILL_OPACITY = 0.15;

const WALL_THICKNESS = 200; // mm，视觉厚度
const SELECTION_COLOR = 0x3b82f6; // 与主场景选中蓝色一致

// ==================== 属性面板 ====================

const hasSelection = computed(() => !!selectedSegment.value || !!selectedZoneId.value);

const selectionTitle = computed(() => {
  if (selectedSegment.value) {
    return SEGMENT_LABELS[selectedSegment.value.type as BoundarySegmentType] ?? selectedSegment.value.type;
  }
  if (selectedZoneId.value) {
    return 'Zone';
  }
  return '';
});

const properties = computed(() => {
  // Zone 选中（无 segment）：从 store 查找完整 zone 数据
  if (!selectedSegment.value && selectedZoneId.value) {
    const props: { key: string; value: string }[] = [
      { key: 'ID', value: selectedZoneId.value },
    ];
    // 从 projectData 查找 zone 详细信息
    const pd = store.projectData;
    const zones = pd?.activeScheme?.zones ?? [];
    let zone = zones.find(z => z.id === selectedZoneId.value);
    // 搜索子分区
    if (!zone) {
      for (const z of zones) {
        const sub = z.subZones?.find(sz => sz.id === selectedZoneId.value);
        if (sub) { zone = sub; props.push({ key: 'parentZoneId', value: z.id }); break; }
      }
    }
    // 回退到 computed.roomZones
    if (!zone) {
      zone = pd?.computed?.roomZones?.find(z => z.id === selectedZoneId.value);
    }
    if (zone) {
      if (zone.name) props.push({ key: 'name', value: zone.name });
      if (zone.roomId) props.push({ key: 'roomId', value: zone.roomId });
      if (zone.type !== undefined) props.push({ key: 'type', value: String(zone.type) });
      if (zone.reason) props.push({ key: 'reason', value: zone.reason });
    }
    return props;
  }
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

/** 标准化 Point2D：兼容 [x,y] 数组和 {x,y} 对象两种格式 */
function normalizePoint(p: any): [number, number] {
  if (Array.isArray(p)) return [p[0], p[1]];
  if (p && typeof p === 'object') return [p.x, p.y];
  return [0, 0];
}

/** 标准化接收到的数据，确保 start/end 为 [x,y] 格式 */
function normalizeData(raw: any[]): ZoneBoundaryData[] {
  return raw.map(zone => ({
    zoneId: zone.zoneId,
    segments: (zone.segments || []).map((seg: any) => ({
      id: seg.id,
      type: seg.type,
      start: normalizePoint(seg.start),
      end: normalizePoint(seg.end),
    }))
  }));
}

function onBoundaryDebug(event: Event) {
  const raw = (event as CustomEvent).detail;
  if (!raw || !Array.isArray(raw)) return;
  boundaryData.value = normalizeData(raw);
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
  zoneMeshMap.clear();
  selectionOutline = null;
  if (renderer) {
    renderer.dispose();
    renderer = null;
  }
  scene = null;
  camera = null;
}

function animate() {
  if (!renderer || !scene || !camera || !visible.value) return;
  animationId = requestAnimationFrame(animate);
  renderer.render(scene, camera);
}

// ==================== 场景构建 ====================

function buildScene() {
  if (!scene || !camera || !renderer) return;

  // 清空旧内容
  while (scene.children.length > 0) {
    const child = scene.children[0];
    if (!child) break;
    scene.remove(child);
  }
  meshMap.clear();
  zoneMeshMap.clear();
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

  // 为每个 zone 创建填充、虚线边框和边界段
  for (const zone of boundaryData.value) {
    const outwardSign = computeWindingSign(zone.segments);

    // 1. Zone 绿色填充
    const fillMesh = createZoneFillMesh(zone.segments);
    if (fillMesh) {
      scene.add(fillMesh);
      zoneMeshMap.set(fillMesh, zone.zoneId);
    }

    // 2. Zone 加粗虚线边框
    const outlineLine = createZoneOutline(zone.segments);
    if (outlineLine) scene.add(outlineLine);

    // 3. 边界段
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
  camera.position.set(cx, 10000, -cy);
  camera.lookAt(cx, 0, -cy);
  camera.updateProjectionMatrix();
}

/** 计算段列表闭合多边形的绕向符号（+1=CCW, -1=CW）*/
function computeWindingSign(segments: BoundarySegment[]): number {
  // Shoelace formula
  let area = 0;
  for (const seg of segments) {
    area += (seg.start[0] * seg.end[1] - seg.end[0] * seg.start[1]);
  }
  return area >= 0 ? -1 : 1;
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
  const p0: [number, number] = [sx, sy];           // 内侧起点
  const p1: [number, number] = [ex, ey];           // 内侧终点
  const p2: [number, number] = [ex + nx * t, ey + ny * t]; // 外侧终点
  const p3: [number, number] = [sx + nx * t, sy + ny * t]; // 外侧起点

  const color = SEGMENT_COLORS[seg.type as BoundarySegmentType] ?? 0x888888;
  const yPos = seg.type === 'wall' ? 1 : 2;
  const isPassage = seg.type === 'passage';

  // Shape → ShapeGeometry（所有类型统一流程）
  const shape = new THREE.Shape();
  shape.moveTo(p0[0], p0[1]);
  shape.lineTo(p1[0], p1[1]);
  shape.lineTo(p2[0], p2[1]);
  shape.lineTo(p3[0], p3[1]);
  shape.closePath();

  const geometry = new THREE.ShapeGeometry(shape);
  const material = new THREE.MeshBasicMaterial({
    color,
    side: THREE.DoubleSide,
    // Passage: 与 Zone 填充一致的半透明效果
    ...(isPassage && { transparent: true, opacity: ZONE_FILL_OPACITY, depthWrite: false }),
  });

  const mesh = new THREE.Mesh(geometry, material);
  mesh.rotation.x = -Math.PI / 2;
  mesh.position.y = yPos;

  // Passage: 添加子级边框线
  if (isPassage) {
    const edges = new THREE.EdgesGeometry(geometry);
    const outline = new THREE.LineSegments(edges,
      new THREE.LineBasicMaterial({ color }));
    mesh.add(outline);
  }

  return mesh;
}

/** 从闭合边界段创建 Zone 填充 Mesh */
function createZoneFillMesh(segments: BoundarySegment[]): THREE.Mesh | null {
  if (segments.length < 3) return null;
  const firstSegment = segments[0];
  if (!firstSegment) return null;

  const shape = new THREE.Shape();
  shape.moveTo(firstSegment.start[0], firstSegment.start[1]);
  for (let i = 1; i < segments.length; i++) {
    const segment = segments[i];
    if (!segment) continue;
    shape.lineTo(segment.start[0], segment.start[1]);
  }
  shape.closePath();

  const geometry = new THREE.ShapeGeometry(shape);
  const material = new THREE.MeshBasicMaterial({
    color: ZONE_FILL_COLOR,
    transparent: true,
    opacity: ZONE_FILL_OPACITY,
    side: THREE.DoubleSide,
    depthWrite: false,
  });

  const mesh = new THREE.Mesh(geometry, material);
  mesh.rotation.x = -Math.PI / 2;
  mesh.position.y = 3; // 顶层，高于 wall(1) 和 door/window(2)

  return mesh;
}

/** 创建 Zone 加粗虚线边框 */
function createZoneOutline(segments: BoundarySegment[]): THREE.Line | null {
  if (segments.length < 3) return null;

  const points: THREE.Vector3[] = [];
  for (const seg of segments) {
    points.push(new THREE.Vector3(seg.start[0], seg.start[1], 0));
  }
  const firstPoint = points[0];
  if (!firstPoint) return null;
  points.push(firstPoint.clone()); // 闭合

  const geometry = new THREE.BufferGeometry().setFromPoints(points);
  const material = new THREE.LineDashedMaterial({
    color: ZONE_FILL_COLOR,
    linewidth: 2,
    dashSize: 150,
    gapSize: 100,
    depthTest: false,
  });

  const line = new THREE.Line(geometry, material);
  line.computeLineDistances(); // LineDashedMaterial 必须
  line.rotation.x = -Math.PI / 2;
  line.position.y = 4; // 最顶层，高于 fill(3)

  return line;
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

  // 移除旧高亮
  if (selectionOutline) {
    scene.remove(selectionOutline);
    selectionOutline = null;
  }

  // 优先命中 segment，回退到 zone fill
  const segMeshes = Array.from(meshMap.keys());
  const segHits = raycaster.intersectObjects(segMeshes);

  if (segHits.length > 0) {
    const firstHit = segHits[0];
    if (!firstHit) return;
    const hit = firstHit.object as THREE.Mesh;
    const data = meshMap.get(hit);
    if (data) {
      selectedSegment.value = data.segment;
      selectedZoneId.value = data.zoneId;

      const edges = new THREE.EdgesGeometry(hit.geometry);
      selectionOutline = new THREE.LineSegments(
        edges,
        new THREE.LineBasicMaterial({ color: SELECTION_COLOR, linewidth: 2, depthTest: false })
      );
      selectionOutline.rotation.copy(hit.rotation);
      selectionOutline.position.copy(hit.position);
      selectionOutline.position.y += 0.5;
      scene.add(selectionOutline);
      return;
    }
  }

  // 回退：点击 zone 内部空白区域
  const zoneMeshes = Array.from(zoneMeshMap.keys());
  const zoneHits = raycaster.intersectObjects(zoneMeshes);

  if (zoneHits.length > 0) {
    const firstHit = zoneHits[0];
    if (!firstHit) return;
    const hit = firstHit.object as THREE.Mesh;
    const zoneId = zoneMeshMap.get(hit);
    if (zoneId) {
      selectedSegment.value = null;
      selectedZoneId.value = zoneId;
      return;
    }
  }

  // 未命中任何对象
  selectedSegment.value = null;
  selectedZoneId.value = '';
}

// ==================== 交互：Pan (中键/右键拖拽) ====================

function onCanvasMouseDown(event: MouseEvent) {
  if (!camera) return;
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
  camera.position.z = cameraStart.z - dy;
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
  const panel = panelRef.value;
  if (!panel) return;
  isDraggingPanel.value = true;
  const rect = panel.getBoundingClientRect();
  dragOffset.value.x = event.clientX - rect.left;
  dragOffset.value.y = event.clientY - rect.top;
  window.addEventListener('mousemove', onPanelDrag);
  window.addEventListener('mouseup', onPanelDragEnd);
}

function onPanelDrag(event: MouseEvent) {
  if (!isDraggingPanel.value) return;
  panelPos.value = {
    x: event.clientX - dragOffset.value.x,
    y: event.clientY - dragOffset.value.y,
  };
}

function onPanelDragEnd() {
  isDraggingPanel.value = false;
  window.removeEventListener('mousemove', onPanelDrag);
  window.removeEventListener('mouseup', onPanelDragEnd);
}

// ==================== resize ====================

/** 仅更新 renderer 尺寸和相机 aspect，保持视口中心和缩放级别不变 */
function updateRendererSize() {
  if (!renderer || !canvasRef.value || !camera) return;
  const parent = canvasRef.value.parentElement;
  if (!parent) return;
  const rect = parent.getBoundingClientRect();
  renderer.setSize(rect.width, rect.height);

  const newAspect = rect.width / rect.height;
  const cx = (camera.left + camera.right) / 2;
  const halfH = (camera.top - camera.bottom) / 2;
  const newHalfW = halfH * newAspect;

  camera.left = cx - newHalfW;
  camera.right = cx + newHalfW;
  camera.updateProjectionMatrix();
}

onMounted(() => {
  window.addEventListener('resize', updateRendererSize);
});

onUnmounted(() => {
  window.removeEventListener('resize', updateRendererSize);
});

// ==================== 面板控制 ====================

function close() {
  visible.value = false;
  selectedSegment.value = null;
}

function clearSelection() {
  selectedSegment.value = null;
  selectedZoneId.value = '';
  if (selectionOutline && scene) {
    scene.remove(selectionOutline);
    selectionOutline = null;
  }
}

// 4:3 比例：宽度 50vw，高度 = 50vw * 3/4 = 37.5vw
const PANEL_W_RATIO = 0.5;  // 50vw
const PANEL_H_RATIO = PANEL_W_RATIO * 0.75; // 37.5vw (4:3)

const panelStyle = computed(() => {
  const style: Record<string, string> = {};

  if (panelPos.value !== null) {
    style.left = `${panelPos.value.x}px`;
    style.top = `${panelPos.value.y}px`;
  } else {
    // 默认居中
    const w = window.innerWidth * PANEL_W_RATIO;
    const h = window.innerWidth * PANEL_H_RATIO;
    style.left = `${(window.innerWidth - w) / 2}px`;
    style.top = `${(window.innerHeight - h) / 2}px`;
  }

  return style;
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

          <!-- 属性浮窗（模仿主界面 PropertyPanel，悬浮在 canvas 左上角） -->
          <Transition name="props-card">
            <aside v-if="hasSelection" class="props-card">
              <div class="props-header">
                <button class="icon-btn" @click="clearSelection" title="Deselect">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <line x1="19" y1="12" x2="5" y2="12"></line>
                    <polyline points="12 19 5 12 12 5"></polyline>
                  </svg>
                </button>
                <div class="props-title">{{ selectionTitle }}</div>
              </div>
              <div class="props-content">
                <div v-for="prop in properties" :key="prop.key" class="prop-row">
                  <span class="label">{{ prop.key }}</span>
                  <span class="value" :title="prop.value">{{ prop.value }}</span>
                </div>
              </div>
            </aside>
          </Transition>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.boundary-debug-panel {
  position: fixed;
  width: 50vw;
  height: 37.5vw; /* 4:3 比例 */
  min-width: 400px;
  min-height: 300px;
  max-height: 90vh;
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

/* 属性浮窗卡片（模仿主界面 PropertyPanel） */
.props-card {
  position: absolute;
  left: 12px;
  top: 12px;
  width: 240px;
  max-height: calc(100% - 24px);

  /* Aurora Glass */
  background: var(--glass-bg, rgba(15, 15, 25, 0.85));
  backdrop-filter: var(--glass-blur, blur(20px));
  -webkit-backdrop-filter: var(--glass-blur, blur(20px));
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 16px;
  box-shadow:
    0 12px 40px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(255, 255, 255, 0.1) inset;

  display: flex;
  flex-direction: column;
  overflow: hidden;
  z-index: 10;

  .props-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 12px 16px;
    border-bottom: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
    flex-shrink: 0;

    .icon-btn {
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
        width: 18px;
        height: 18px;
      }

      &:hover {
        background: var(--surface-hover, rgba(255, 255, 255, 0.1));
        color: var(--text-primary);
      }
    }

    .props-title {
      font-weight: 600;
      font-size: 0.9rem;
      color: var(--text-primary);
      letter-spacing: 0.5px;
      text-transform: uppercase;
    }
  }

  .props-content {
    flex: 1;
    overflow-y: auto;
    padding: 12px 16px;

    &::-webkit-scrollbar {
      width: 4px;
    }
    &::-webkit-scrollbar-track {
      background: transparent;
    }
    &::-webkit-scrollbar-thumb {
      background: var(--border-strong, rgba(255, 255, 255, 0.2));
      border-radius: 2px;
    }
  }

  .prop-row {
    display: flex;
    justify-content: space-between;
    align-items: baseline;
    gap: 12px;
    font-size: 0.85rem;
    line-height: 1.4;

    & + .prop-row {
      margin-top: 8px;
    }

    .label {
      color: var(--text-secondary);
      flex-shrink: 0;
      max-width: 40%;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .value {
      color: var(--text-primary);
      text-align: right;
      flex: 1;
      overflow: hidden;
      word-break: break-word;
      font-family: var(--font-mono);
    }
  }
}

/* 属性卡片过渡 */
.props-card-enter-active {
  transition: all 0.25s cubic-bezier(0.34, 1.56, 0.64, 1);
}
.props-card-leave-active {
  transition: all 0.15s ease;
}
.props-card-enter-from {
  opacity: 0;
  transform: translateY(-10px) scale(0.95);
}
.props-card-leave-to {
  opacity: 0;
  transform: scale(0.95);
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
