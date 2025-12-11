<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import type { CanvasDocument, Point2D, Polygon2D } from '../../types/canvas';
import { CoordinateService, calculateBoundingBox } from '../../services/CoordinateService';

const props = defineProps<{
  document: CanvasDocument | null;
}>();

// 画布配置
const padding = 50; // 画布边距
const scale = ref(0.05); // 初始缩放比例 (mm -> px)

// 计算边界框和画布尺寸
const boundingBox = computed(() => {
  if (!props.document) return null;
  return calculateBoundingBox(props.document);
});

const canvasWidth = computed(() => {
  if (!boundingBox.value) return 800;
  return boundingBox.value.width * scale.value + padding * 2;
});

const canvasHeight = computed(() => {
  if (!boundingBox.value) return 600;
  return boundingBox.value.height * scale.value + padding * 2;
});

// 坐标转换服务
const coordService = computed(() => {
  if (!boundingBox.value) return null;
  return new CoordinateService(
    canvasHeight.value,
    scale.value,
    -boundingBox.value.minX + padding / scale.value,
    -boundingBox.value.minY + padding / scale.value
  );
});

// 转换多边形为 SVG path
function polygonToPath(polygon: Point2D[]): string {
  if (!coordService.value || polygon.length === 0) return '';
  return coordService.value.polygonToSvgPath(polygon);
}

// 转换 Polygon2D（带孔洞）为 SVG path
function polygon2DToPath(polygon: Polygon2D): string {
  if (!coordService.value) return '';
  return coordService.value.polygon2DToSvgPath(polygon);
}

// 转换点坐标
function transformPoint(point: Point2D): Point2D {
  if (!coordService.value) return point;
  return coordService.value.worldToScreen(point);
}

// viewBox
const viewBox = computed(() => {
  return `0 0 ${canvasWidth.value} ${canvasHeight.value}`;
});
</script>

<template>
  <div class="canvas-container">
    <svg
      :width="canvasWidth"
      :height="canvasHeight"
      :viewBox="viewBox"
      class="svg-canvas"
    >
      <!-- 背景 -->
      <rect
        x="0"
        y="0"
        :width="canvasWidth"
        :height="canvasHeight"
        fill="#f5f5f5"
      />

      <!-- 墙体层 -->
      <g class="wall-layer">
        <path
          v-for="wall in document?.walls"
          :key="wall.id"
          :d="polygonToPath(wall.polygon)"
          :data-id="wall.id"
          fill="#333"
          stroke="#222"
          stroke-width="1"
        />
      </g>

      <!-- 柱子层 -->
      <g class="column-layer">
        <path
          v-for="column in document?.columns"
          :key="column.id"
          :d="polygonToPath(column.polygon)"
          :data-id="column.id"
          fill="#555"
          stroke="#333"
          stroke-width="1"
        />
      </g>

      <!-- 门窗层 -->
      <g class="opening-layer">
        <template v-for="opening in document?.openings" :key="opening.id">
          <!-- 门：红色线 -->
          <line
            v-if="opening.type === 0"
            :x1="transformPoint(opening.line[0])[0]"
            :y1="transformPoint(opening.line[0])[1]"
            :x2="transformPoint(opening.line[1])[0]"
            :y2="transformPoint(opening.line[1])[1]"
            :data-id="opening.id"
            stroke="#e74c3c"
            stroke-width="3"
          />
          <!-- 窗：蓝色线 -->
          <line
            v-else
            :x1="transformPoint(opening.line[0])[0]"
            :y1="transformPoint(opening.line[0])[1]"
            :x2="transformPoint(opening.line[1])[0]"
            :y2="transformPoint(opening.line[1])[1]"
            :data-id="opening.id"
            stroke="#3498db"
            stroke-width="3"
          />
        </template>
      </g>

      <!-- 完成面定位边界层（可选显示） -->
      <g class="finish-boundary-layer" opacity="0.3">
        <path
          v-for="boundary in document?.finishLocationBoundaries"
          :key="boundary.id"
          :d="polygonToPath(boundary.polygon)"
          :data-id="boundary.id"
          fill="none"
          stroke="#9b59b6"
          stroke-width="2"
          stroke-dasharray="5,5"
        />
      </g>

      <!-- 房间边界层 -->
      <g class="room-layer" opacity="0.2">
        <path
          v-for="room in document?.rooms"
          :key="room.id"
          :d="polygon2DToPath(room.boundary)"
          :data-id="room.id"
          fill="#27ae60"
          stroke="#1e8449"
          stroke-width="1"
        />
      </g>

      <!-- 信息显示 -->
      <text x="10" y="20" font-size="12" fill="#666">
        {{ document ? `Canvas: ${document.id} (v${document.version})` : 'No document loaded' }}
      </text>
    </svg>
  </div>
</template>

<style scoped>
.canvas-container {
  display: flex;
  justify-content: center;
  align-items: center;
  padding: 20px;
  background: #e0e0e0;
  overflow: auto;
}

.svg-canvas {
  background: white;
  box-shadow: 0 2px 10px rgba(0, 0, 0, 0.1);
  border-radius: 4px;
}

/* 交互样式 */
.wall-layer path:hover,
.column-layer path:hover {
  opacity: 0.8;
  cursor: pointer;
}

.opening-layer line:hover {
  stroke-width: 5;
  cursor: pointer;
}
</style>
