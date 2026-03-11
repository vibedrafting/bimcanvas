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
let zoneCrossLines: THREE.LineSegments | null = null;

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

const ZONE_FILL_COLOR = 0x22c55e; // 绿色，与主画布一致
const ZONE_FILL_OPACITY = 0.15;

const WALL_THICKNESS = 200; // mm，视觉厚度
const SELECTION_COLOR = 0x3b82f6; // 与主场景选中蓝色一致

// Zone 标签（3D→2D 投影）
interface ZoneLabelData {
  zoneId: string;
  worldX: number;
  worldY: number;
  screenX: number;
  screenY: number;
  segCount: number;
}
const zoneLabels = ref<ZoneLabelData[]>([]);
const activeZoneLabelId = ref<string>(''); // 当前展开的 zone 标签

// ==================== 属性面板 ====================

// 选中类型：segment 或 zone
const selectionMode = computed<'segment' | 'zone' | null>(() => {
  if (selectedSegment.value) return 'segment';
  if (activeZoneLabelId.value) return 'zone';
  return null;
});

const selectionTitle = computed(() => {
  if (selectionMode.value === 'segment' && selectedSegment.value) {
    return SEGMENT_LABELS[selectedSegment.value.type as BoundarySegmentType] ?? selectedSegment.value.type;
  }
  if (selectionMode.value === 'zone') {
    return activeZoneLabelId.value;
  }
  return '';
});

const properties = computed(() => {
  if (selectionMode.value === 'segment' && selectedSegment.value) {
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
  }

  if (selectionMode.value === 'zone') {
    const zone = boundaryData.value.find(z => z.zoneId === activeZoneLabelId.value);
    if (!zone) return [];
    const typeStats: Record<string, number> = {};
    for (const seg of zone.segments) {
      typeStats[seg.type] = (typeStats[seg.type] || 0) + 1;
    }
    const props: { key: string; value: string }[] = [
      { key: 'zoneId', value: zone.zoneId },
      { key: 'segments', value: `${zone.segments.length}` },
    ];
    for (const [type, count] of Object.entries(typeStats)) {
      props.push({ key: type, value: `${count}` });
    }
    return props;
  }

  return [];
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
  selectionOutline = null;
  zoneCrossLines = null;
  if (renderer) {
    renderer.dispose();
  }
}

function animate() {
  if (!renderer || !visible.value) return;
  animationId = requestAnimationFrame(animate);
  renderer.render(scene, camera);
  updateLabelPositions();
}

/** 将 zone 标签的 3D 世界坐标投影到屏幕坐标 */
function updateLabelPositions() {
  if (!camera || !canvasRef.value || zoneLabels.value.length === 0) return;
  const canvas = canvasRef.value;
  const rect = canvas.getBoundingClientRect();
  const vec = new THREE.Vector3();

  for (const label of zoneLabels.value) {
    // 世界坐标：X 不变，Y=0（地面），Z=-worldY（Shape 旋转后 Z 取反）
    vec.set(label.worldX, 0, -label.worldY);
    vec.project(camera);
    label.screenX = (vec.x * 0.5 + 0.5) * rect.width;
    label.screenY = (-vec.y * 0.5 + 0.5) * rect.height;
  }
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

  // 为每个 zone 创建填充和边界段
  const labels: ZoneLabelData[] = [];

  for (const zone of boundaryData.value) {
    const outwardSign = computeWindingSign(zone.segments);

    // 1. Zone 绿色填充
    const fillMesh = createZoneFillMesh(zone.segments);
    if (fillMesh) scene.add(fillMesh);

    // 2. 边界段
    for (const seg of zone.segments) {
      const mesh = createSegmentMesh(seg, outwardSign);
      mesh.userData = { segment: seg, zoneId: zone.zoneId };
      meshMap.set(mesh, { segment: seg, zoneId: zone.zoneId });
      scene.add(mesh);
    }

    // 3. 计算 zone 中心（用于标签）
    let cx = 0, cy = 0;
    for (const seg of zone.segments) {
      cx += seg.start[0];
      cy += seg.start[1];
    }
    cx /= zone.segments.length;
    cy /= zone.segments.length;
    labels.push({ zoneId: zone.zoneId, worldX: cx, worldY: cy, screenX: 0, screenY: 0, segCount: zone.segments.length });
  }

  zoneLabels.value = labels;

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

/** 从闭合边界段创建 Zone 填充 Mesh */
function createZoneFillMesh(segments: BoundarySegment[]): THREE.Mesh | null {
  if (segments.length < 3) return null;

  const shape = new THREE.Shape();
  shape.moveTo(segments[0].start[0], segments[0].start[1]);
  for (let i = 1; i < segments.length; i++) {
    shape.lineTo(segments[i].start[0], segments[i].start[1]);
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
  mesh.position.y = 0; // 最底层，低于 wall(1) 和 door/window(2)

  return mesh;
}

// ==================== Zone X 对角线 ====================

const ZONE_CROSS_COLOR = 0x22c55e; // 绿色，与 zone fill 一致

/** 两线段参数化求交，返回第一条线段上的参数 t */
function lineLineIntersectT(
  p1: [number, number], p2: [number, number],
  p3: [number, number], p4: [number, number]
): number | null {
  const d1x = p2[0] - p1[0], d1y = p2[1] - p1[1];
  const d2x = p4[0] - p3[0], d2y = p4[1] - p3[1];
  const denom = d1x * d2y - d1y * d2x;
  if (Math.abs(denom) < 1e-12) return null;
  const t = ((p3[0] - p1[0]) * d2y - (p3[1] - p1[1]) * d2x) / denom;
  const u = ((p3[0] - p1[0]) * d1y - (p3[1] - p1[1]) * d1x) / denom;
  if (t < 0 || u < 0 || u > 1) return null;
  return t;
}

/** 从 origin 沿 direction 发射射线，返回与多边形边界的最近交点 */
function rayPolygonIntersection(
  origin: [number, number],
  direction: [number, number],
  polygon: [number, number][]
): [number, number] | null {
  const farPoint: [number, number] = [
    origin[0] + direction[0] * 1e8,
    origin[1] + direction[1] * 1e8,
  ];
  let minT = Infinity;
  for (let i = 0; i < polygon.length; i++) {
    const a = polygon[i];
    const b = polygon[(i + 1) % polygon.length];
    const t = lineLineIntersectT(origin, farPoint, a, b);
    if (t !== null && t > 1e-9 && t < minT) minT = t;
  }
  if (minT === Infinity) return null;
  return [
    origin[0] + (farPoint[0] - origin[0]) * minT,
    origin[1] + (farPoint[1] - origin[1]) * minT,
  ];
}

/** 为选中的 Zone 添加 X 对角线（从顶点平均中心向 NE/SW/NW/SE 发射到边界） */
function addZoneCrossLines(zoneId: string) {
  removeZoneCrossLines();

  const zone = boundaryData.value.find(z => z.zoneId === zoneId);
  if (!zone || zone.segments.length < 3) return;

  const polygon: [number, number][] = zone.segments.map(s => s.start as [number, number]);

  // 顶点平均中心（与标签位置一致）
  let cx = 0, cy = 0;
  for (const p of polygon) { cx += p[0]; cy += p[1]; }
  cx /= polygon.length;
  cy /= polygon.length;
  const centroid: [number, number] = [cx, cy];

  const NE = rayPolygonIntersection(centroid, [1, 1], polygon);
  const SW = rayPolygonIntersection(centroid, [-1, -1], polygon);
  const NW = rayPolygonIntersection(centroid, [-1, 1], polygon);
  const SE = rayPolygonIntersection(centroid, [1, -1], polygon);

  const segs: [[number, number], [number, number]][] = [];
  if (NE && SW) segs.push([NE, SW]);
  if (NW && SE) segs.push([NW, SE]);
  if (segs.length === 0) return;

  const coords: number[] = [];
  for (const s of segs) {
    coords.push(s[0][0], s[0][1], 0, s[1][0], s[1][1], 0);
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(coords), 3));

  zoneCrossLines = new THREE.LineSegments(
    geometry,
    new THREE.LineBasicMaterial({ color: ZONE_CROSS_COLOR, linewidth: 2, depthTest: false })
  );
  zoneCrossLines.rotation.x = -Math.PI / 2;
  zoneCrossLines.position.y = 3; // 高于 segment mesh (y=1,2)
  scene.add(zoneCrossLines);
}

function removeZoneCrossLines() {
  if (zoneCrossLines && scene) {
    scene.remove(zoneCrossLines);
    zoneCrossLines.geometry.dispose();
    (zoneCrossLines.material as THREE.Material).dispose();
    zoneCrossLines = null;
  }
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
      activeZoneLabelId.value = ''; // 清除 zone 标签展开
      removeZoneCrossLines(); // 清除 zone X 对角线

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

/** 仅更新 renderer 尺寸和相机 aspect，保持视口中心和缩放级别不变 */
function updateRendererSize() {
  if (!renderer || !canvasRef.value) return;
  const rect = canvasRef.value.parentElement!.getBoundingClientRect();
  renderer.setSize(rect.width, rect.height);

  const newAspect = rect.width / rect.height;
  const cx = (camera.left + camera.right) / 2;
  const cy = (camera.top + camera.bottom) / 2;
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
  activeZoneLabelId.value = '';
  if (selectionOutline && scene) {
    scene.remove(selectionOutline);
    selectionOutline = null;
  }
  removeZoneCrossLines();
}

function onZoneLabelClick(zoneId: string) {
  // 清除 segment 选中
  selectedSegment.value = null;
  selectedZoneId.value = '';
  if (selectionOutline && scene) {
    scene.remove(selectionOutline);
    selectionOutline = null;
  }
  // 切换 zone 展开
  if (activeZoneLabelId.value === zoneId) {
    activeZoneLabelId.value = '';
    removeZoneCrossLines();
  } else {
    activeZoneLabelId.value = zoneId;
    addZoneCrossLines(zoneId);
  }
}

function closeZoneLabel() {
  activeZoneLabelId.value = '';
  removeZoneCrossLines();
}

// 4:3 比例：宽度 50vw，高度 = 50vw * 3/4 = 37.5vw
const PANEL_W_RATIO = 0.5;  // 50vw
const PANEL_H_RATIO = PANEL_W_RATIO * 0.75; // 37.5vw (4:3)

const panelStyle = computed(() => {
  const style: Record<string, string> = {};

  if (panelPos.value.x >= 0) {
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

          <!-- Zone 标签（3D→2D 投影定位） -->
          <div
            v-for="zl in zoneLabels"
            :key="zl.zoneId"
            class="zone-label"
            :class="{ active: activeZoneLabelId === zl.zoneId }"
            :style="{ left: zl.screenX + 'px', top: zl.screenY + 'px' }"
            @click.stop="onZoneLabelClick(zl.zoneId)"
          >
            <span class="zone-label-text">#{{ zl.zoneId }}</span>
            <button v-if="activeZoneLabelId === zl.zoneId" class="zone-label-close" @click.stop="closeZoneLabel">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <!-- 属性浮窗（模仿主界面 PropertyPanel，悬浮在 canvas 左上角） -->
          <Transition name="props-card">
            <aside v-if="selectionMode" class="props-card">
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

/* Zone 标签 */
.zone-label {
  position: absolute;
  transform: translate(-50%, -50%);
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  border-radius: 8px;
  background: rgba(34, 197, 94, 0.15);
  border: 1px solid rgba(34, 197, 94, 0.3);
  cursor: pointer;
  user-select: none;
  transition: all 0.2s ease;
  z-index: 5;
  white-space: nowrap;

  &:hover, &.active {
    background: rgba(34, 197, 94, 0.3);
    border-color: rgba(34, 197, 94, 0.6);
  }

  .zone-label-text {
    font-size: 0.72rem;
    font-family: var(--font-mono, monospace);
    color: #22c55e;
    font-weight: 600;
    letter-spacing: 0.5px;
  }

  .zone-label-close {
    background: transparent;
    border: none;
    color: #22c55e;
    cursor: pointer;
    padding: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: color 0.15s;

    svg {
      width: 12px;
      height: 12px;
    }

    &:hover {
      color: #ef4444;
    }
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
