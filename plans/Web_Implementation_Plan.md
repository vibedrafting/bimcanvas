# BIMCanvas.Web 实施计划

> **版本**：v1.1
> **更新日期**：2025-12-11
> **状态**：Phase 1 待开发

---

## 一、项目概述

### 1.1 核心定位

**BIMCanvas.Web 是 Vue 3 单页应用（SPA）**

BIMCanvas.Web 负责渲染 CanvasDocument 为 SVG 画布，提供用户交互界面，与 Server 通过 REST/SignalR 通信。

**核心职责**：
- **数据渲染**：将 CanvasDocument 渲染为 SVG 画布
- **状态管理**：Pinia Store 镜像后端数据结构
- **实时通信**：SignalR 接收推送更新
- **坐标转换**：Y-up → 屏幕坐标
- **交互预留**：选中、属性编辑、提交机制

### 1.2 职责边界

#### ✅ BIMCanvas.Web 负责

| 功能类别 | 具体内容 | 输入 | 输出 |
|----------|----------|------|------|
| **SVG 渲染** | outline/zones/wallFinishes → SVG | CanvasDocument | 可视化画布 |
| **坐标转换** | WorldToScreen / ScreenToWorld | Y-up 坐标 | 屏幕坐标 |
| **状态管理** | Pinia Store | API 响应 | 响应式数据 |
| **SignalR 通信** | 接收 broadcast | Hub 消息 | Store 更新 |
| **用户交互** | 选中、属性编辑 | 用户操作 | 状态变更 |

#### ❌ BIMCanvas.Web 不负责

| 功能 | 负责方 | 原因 |
|------|--------|------|
| Zone 计算 | BIMCanvas.Server | 后端业务逻辑 |
| AI 布置 | BIMCanvas.Agent | Agent SDK |
| 数据持久化 | BIMCanvas.Server | 后端职责 |

### 1.3 系统中的位置

```
BIMCanvas 系统架构
├── BIMCanvas.Core (.NET Standard 2.0)
├── BIMCanvas.Revit (.NET FW 4.7.2)
├── BIMCanvas.Server (.NET 6+)           ← 后端 + 托管 SPA
├── BIMCanvas.Agent (Python 3.10+)
└── BIMCanvas.Web (Vue 3 + TS)           ← 本项目：前端 SPA
```

### 1.4 渲染架构（混合方案）

```
┌─────────────────────────────────┐
│ SvgCanvas.vue (z-index: 1)      │ ← Phase 1
│ - outline, zones, wallFinishes  │
│ - 600mm 网格背景                │
├─────────────────────────────────┤
│ KonvaCanvas.vue (z-index: 2)    │ ← Phase 2+
│ - modules (draggable)           │
│ - guides (snappable)            │
└─────────────────────────────────┘
```

---

## 二、技术选型

### 2.1 核心框架

| 组件 | 选型 | 版本 | 决策依据 |
|------|------|------|---------|
| **前端框架** | Vue 3 | ^3.4 | 响应式，与 Pinia 集成 |
| **构建工具** | Vite | ^5.x | 快速 HMR |
| **编程语言** | TypeScript | ^5.x | 类型安全 |
| **状态管理** | Pinia | ^2.x | Vue 3 官方推荐 |

### 2.2 通信库

| 组件 | 选型 | 用途 |
|------|------|------|
| **SignalR** | @microsoft/signalr ^8.x | 实时推送 |
| **HTTP** | axios ^1.x | REST API |

### 2.3 渲染库

| 层级 | 技术 | 阶段 |
|------|------|------|
| 静态层 | 原生 SVG | Phase 1 |
| 交互层 | Konva.js + vue-konva | Phase 2+ |

### 2.4 依赖清单 (package.json)

```json
{
  "name": "bimcanvas-web",
  "version": "0.1.0",
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "vue-tsc && vite build",
    "preview": "vite preview"
  },
  "dependencies": {
    "vue": "^3.4.0",
    "pinia": "^2.1.0",
    "@microsoft/signalr": "^8.0.0",
    "axios": "^1.6.0"
  },
  "devDependencies": {
    "vite": "^5.0.0",
    "typescript": "^5.3.0",
    "@vitejs/plugin-vue": "^4.5.0",
    "vue-tsc": "^1.8.0"
  }
}
```

---

## 三、功能规格

### 3.1 Phase 1：核心渲染

#### 3.1.1 SvgCanvas.vue - 主渲染组件

**位置**：`src/components/Canvas/SvgCanvas.vue`

```vue
<template>
  <svg :viewBox="viewBox" @pointerdown="handlePointerDown">
    <!-- 网格背景 -->
    <GridPattern :size="600" />

    <!-- 墙体轮廓 -->
    <WallLayer :walls="document.walls" />

    <!-- 柱子轮廓 -->
    <ColumnLayer :columns="document.columns" />

    <!-- 门窗 -->
    <OpeningLayer :openings="document.openings" />

    <!-- 完成面定位边界（可选显示） -->
    <FinishLocationBoundaryLayer :boundaries="document.finishLocationBoundaries" />

    <!-- 设计区 -->
    <ZoneLayer :zones="document.zones" :selected="selectedElementId" />

    <!-- 墙面完成面 -->
    <WallFinishLayer :finishes="document.wallFinishes" />

    <!-- 禁区 -->
    <ExclusionLayer :zones="document.zones" />
  </svg>
</template>
```

**要求**：
- 所有 SVG 元素添加 `data-id` 属性
- 使用 CoordinateService 进行坐标转换
- 禁止 CSS `scaleY(-1)`

#### 3.1.2 CoordinateService.ts - 坐标转换

**位置**：`src/services/CoordinateService.ts`

```typescript
export class CoordinateService {
  constructor(
    private canvasHeight: number,
    private scale: number = 1
  ) {}

  // 世界坐标 → 屏幕坐标
  worldToScreen(point: [number, number]): [number, number] {
    return [
      point[0] * this.scale,
      this.canvasHeight - point[1] * this.scale
    ];
  }

  // 屏幕坐标 → 世界坐标
  screenToWorld(point: [number, number]): [number, number] {
    return [
      point[0] / this.scale,
      (this.canvasHeight - point[1]) / this.scale
    ];
  }

  // 多边形转换
  transformPolygon(polygon: Point2D[]): Point2D[] {
    return polygon.map(p => this.worldToScreen(p));
  }
}
```

#### 3.1.3 canvasStore.ts - Pinia Store

**位置**：`src/stores/canvasStore.ts`

```typescript
import { defineStore } from 'pinia';
import type { CanvasDocument, ElementChange } from '@/types/canvas';

export const useCanvasStore = defineStore('canvas', {
  state: () => ({
    document: null as CanvasDocument | null,
    pendingChanges: [] as ElementChange[],  // Phase 1 预留
    selectedElementId: null as string | null,
    connectionStatus: 'disconnected' as 'connected' | 'disconnected' | 'error',
  }),

  getters: {
    hasChanges: (state) => state.pendingChanges.length > 0,
    currentVersion: (state) => state.document?.version ?? 0,
  },

  actions: {
    setDocument(doc: CanvasDocument) {
      this.document = doc;
    },

    select(elementId: string | null) {
      this.selectedElementId = elementId;
    },

    setConnectionStatus(status: 'connected' | 'disconnected' | 'error') {
      this.connectionStatus = status;
    },

    // Phase 2+
    enqueueChange(change: ElementChange) {
      // Phase 1: no-op
    },

    commitChanges() {
      // Phase 1: no-op
    },

    discardChanges() {
      this.pendingChanges = [];
    },
  },
});
```

#### 3.1.4 SignalRService.ts - Hub 连接

**位置**：`src/services/SignalRService.ts`

```typescript
import * as signalR from '@microsoft/signalr';
import { useCanvasStore } from '@/stores/canvasStore';

export class SignalRService {
  private connection: signalR.HubConnection;

  constructor(hubUrl: string) {
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    this.setupHandlers();
  }

  private setupHandlers() {
    const store = useCanvasStore();

    this.connection.on('DocumentUpdated', (document: CanvasDocument) => {
      store.setDocument(document);
    });

    this.connection.onreconnecting(() => {
      store.setConnectionStatus('disconnected');
    });

    this.connection.onreconnected(() => {
      store.setConnectionStatus('connected');
    });
  }

  async connect() {
    await this.connection.start();
    useCanvasStore().setConnectionStatus('connected');
  }

  async joinCanvas(canvasId: string) {
    await this.connection.invoke('JoinCanvas', canvasId);
  }
}
```

#### 3.1.5 ApiService.ts - REST 调用

**位置**：`src/services/ApiService.ts`

```typescript
import axios from 'axios';
import type { CanvasDocument } from '@/types/canvas';

const api = axios.create({
  baseURL: '/api',
});

export const ApiService = {
  async getCanvas(id: string): Promise<CanvasDocument> {
    const { data } = await api.get(`/canvas/${id}`);
    return data;
  },

  async createCanvas(document: CanvasDocument): Promise<CanvasDocument> {
    const { data } = await api.post('/canvas', document);
    return data;
  },

  // Phase 2+
  async commitChanges(id: string, changeSet: ChangeSet): Promise<void> {
    await api.post(`/canvas/${id}/commit`, changeSet);
  },
};
```

### 3.2 Phase 1：UI 组件

#### 3.2.1 Toolbar.vue

```vue
<template>
  <div class="toolbar">
    <button :disabled="true">Sync to AI</button>
    <button :disabled="true">Discard</button>
    <select v-model="gridSize">
      <option :value="600">600mm</option>
      <option :value="300">300mm</option>
      <option :value="100">100mm</option>
    </select>
  </div>
</template>
```

#### 3.2.2 PropertyPanel.vue (占位)

```vue
<template>
  <aside class="property-panel">
    <p v-if="!selectedElementId">Select an element</p>
    <!-- Phase 2: 属性编辑表单 -->
  </aside>
</template>
```

#### 3.2.3 StatusBar.vue

```vue
<template>
  <footer class="status-bar">
    <span :class="connectionClass">● {{ connectionStatus }}</span>
    <span>Version: v{{ version }}</span>
    <span v-if="selectedElementId">Selected: {{ selectedElementId }}</span>
  </footer>
</template>
```

---

## 四、项目结构

```
BIMCanvas.Web/
├── src/
│   ├── components/
│   │   ├── Canvas/
│   │   │   ├── CanvasContainer.vue              容器组件
│   │   │   ├── SvgCanvas.vue                    SVG 渲染
│   │   │   ├── KonvaCanvas.vue                  交互层占位
│   │   │   ├── GridPattern.vue                  网格背景
│   │   │   ├── WallLayer.vue                    墙体轮廓（新增）
│   │   │   ├── ColumnLayer.vue                  柱子轮廓（新增）
│   │   │   ├── OpeningLayer.vue                 门窗
│   │   │   ├── FinishLocationBoundaryLayer.vue  完成面定位边界（新增）
│   │   │   ├── ZoneLayer.vue                    设计区
│   │   │   ├── WallFinishLayer.vue              墙面完成面
│   │   │   └── ExclusionLayer.vue               禁区
│   │   ├── Toolbar.vue
│   │   ├── PropertyPanel.vue
│   │   └── StatusBar.vue
│   │
│   ├── stores/
│   │   └── canvasStore.ts
│   │
│   ├── services/
│   │   ├── CoordinateService.ts
│   │   ├── SelectionService.ts
│   │   ├── SignalRService.ts
│   │   └── ApiService.ts
│   │
│   ├── types/
│   │   └── canvas.d.ts
│   │
│   ├── App.vue
│   └── main.ts
│
├── public/
├── index.html
├── vite.config.ts
├── tsconfig.json
└── package.json
```

---

## 五、类型定义

### 5.1 canvas.d.ts

```typescript
// ============================================
// 基础几何类型
// ============================================
export type Point2D = [number, number];
export type Vec2D = [number, number];
export type Line2D = [Point2D, Point2D];
export type Polygon2D = Point2D[];
export type AABB = [number, number, number, number];

// ============================================
// 主文档结构（v2.6 扁平化）
// ============================================
export interface CanvasDocument {
  id: string;
  version: number;
  coordinateSystem: 'cartesian_mm_yUp';
  metadata: Metadata;

  // 建筑构件（顶层）
  walls: Wall[];
  columns: Column[];
  openings: Opening[];
  finishLocationBoundaries: FinishLocationBoundary[];

  // 空间数据
  rooms: Room[];
  zones: Zone[];
  wallFinishes: WallFinish[];
  modules: Module[];
}

// ============================================
// 元数据
// ============================================
export interface Metadata {
  placementElevation: number;  // 布置高度（mm）
  origin: [number, number, number];  // 坐标原点 [x, y, z]（mm）
  rotation: number;  // 视图旋转角度（弧度）
  method: 'boundingBox' | 'cropBox';  // 原点计算方法
}

// ============================================
// 建筑构件
// ============================================
export interface Wall {
  id: string;
  elementId: number;
  polygon: Polygon2D;
}

export interface Column {
  id: string;
  elementId: number;
  isStructural: boolean;
  polygon: Polygon2D;
}

export interface Opening {
  id: string;
  type: OpeningType;
  line: Line2D;
  facingDirection?: Vec2D;
  handDirections?: Vec2D[];
}

export type OpeningType = 'door' | 'window';

export interface FinishLocationBoundary {
  id: string;
  elementIds: number[];
  polygon: Polygon2D;
}

// ============================================
// 空间数据
// ============================================
export interface Room {
  id: string;
  name: string;
  type: RoomType;
  boundary: Polygon2D;
}

export interface Zone {
  id: string;
  name: string;
  roomId: string;
  tags: ZoneTag[];
  rawBoundary: Polygon2D;
  innerBoundary: Polygon2D;
  exclusionAreas: ExclusionArea[];
  openings: string[];
}

export interface WallFinish {
  id: string;
  locationLine: Line2D;
  thickness: number;
  finishModuleId?: string;
  exclusionBoundary: Polygon2D;
  wallId: string;
  roomId: string;
  source: FinishSource;
}

export interface ExclusionArea {
  id: string;
  type: ExclusionType;
  boundary: Polygon2D;
}

export interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: Polygon2D;
  facing: Facing;
  zoneId: string;
  items?: ModuleItem[];
}

export interface ModuleItem {
  familyId: string;
  offset: Vec2D;
  role?: string;
}

// ============================================
// 枚举类型（JSON 格式：snake_case）
// ============================================
export type RoomType =
  | 'living_room'
  | 'dining_room'
  | 'master_bedroom'
  | 'bedroom'
  | 'study'
  | 'kitchen'
  | 'bathroom'
  | 'entrance'
  | 'balcony'
  | 'corridor'
  | 'storage';

export type ZoneTag =
  | 'tv_media'
  | 'audio_video'
  | 'sleep'
  | 'rest'
  | 'reading'
  | 'work'
  | 'study'
  | 'wardrobe_storage'
  | 'shoe_storage'
  | 'general_storage'
  | 'dining'
  | 'cooking'
  | 'food_prep'
  | 'bar'
  | 'shower'
  | 'bathtub'
  | 'toilet'
  | 'washing'
  | 'laundry'
  | 'vanity'
  | 'entry'
  | 'passage'
  | 'display'
  | 'plants';

export type ExclusionType = 'door_swing' | 'passage' | 'other';

export type FinishSource = 'room_default' | 'zone_override' | 'user_override';

export type FacingDirection =
  | 'north'
  | 'south'
  | 'east'
  | 'west'
  | 'northeast'
  | 'northwest'
  | 'southeast'
  | 'southwest';

export type Facing = FacingDirection | Vec2D;

// ============================================
// 变更记录（Phase 2+）
// ============================================
export interface ElementChange {
  id: string;
  elementType: 'zone' | 'wallFinish' | 'module';
  elementId: string;
  changeType: 'create' | 'update' | 'delete';
  before?: Record<string, unknown>;
  after?: Record<string, unknown>;
  timestamp: number;
}
```

---

## 六、实施计划

### 6.1 开发阶段

#### Phase 1A：项目初始化

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.1 | Vite + Vue 3 + TS 初始化 | 项目结构 |
| 1.2 | Pinia Store 实现 | canvasStore.ts |
| 1.3 | 类型定义 | canvas.d.ts |
| 1.4 | CoordinateService 实现 | 坐标转换 |

#### Phase 1B：通信层

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.5 | ApiService 实现 | REST 调用 |
| 1.6 | SignalRService 实现 | Hub 连接 |

#### Phase 1C：渲染层

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.7 | SvgCanvas 主组件 | 画布容器 |
| 1.8 | BoundaryLayer | 边界渲染 |
| 1.9 | OpeningLayer | 门窗渲染 |
| 1.10 | ZoneLayer | 设计区渲染 |
| 1.11 | WallFinishLayer | 完成面渲染 |
| 1.12 | ExclusionLayer | 禁区渲染 |
| 1.13 | GridPattern | 网格背景 |

#### Phase 1D：UI 组件

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.14 | Toolbar 实现 | 工具栏 |
| 1.15 | PropertyPanel 占位 | 属性面板 |
| 1.16 | StatusBar 实现 | 状态栏 |
| 1.17 | SelectionService | 选中功能 |

#### Phase 1E：集成测试

| 步骤 | 任务 | 产出 |
|------|------|------|
| 1.18 | Server 集成 | 端到端验证 |
| 1.19 | 构建产物 | dist/ 输出 |

### 6.2 验收标准

#### Phase 1 验收

| 检查项 | 标准 |
|--------|------|
| 构建 | `npm run build` 通过 |
| SignalR | 连接成功，收到 DocumentUpdated |
| REST | GET /api/canvas/{id} 数据加载 |
| 渲染 | outline/zones/wallFinishes 正确显示 |
| 坐标 | Y 轴翻转正确 |
| 选中 | 点击元素高亮 |
| 网格 | 600mm 网格显示 |
| 状态栏 | 连接状态 + 版本号显示 |

---

## 七、附录

### 7.1 相关文档

| 文档 | 路径 |
|------|------|
| 架构文档 | `docs/Architecture.md` |
| 评审文档 | `reviews/ServerWeb_Implementation_Review.md` |
| Server 计划 | `plans/Server_Implementation_Plan.md` |

### 7.2 进度追踪

| 阶段 | 状态 | 更新时间 |
|------|------|----------|
| Phase 1A: 项目初始化 | ⬜ 待开始 | - |
| Phase 1B: 通信层 | ⬜ 待开始 | - |
| Phase 1C: 渲染层 | ⬜ 待开始 | - |
| Phase 1D: UI 组件 | ⬜ 待开始 | - |
| Phase 1E: 集成测试 | ⬜ 待开始 | - |

### 7.3 变更日志

| 日期 | 版本 | 变更内容 |
|------|------|----------|
| 2025-12-11 | v1.1 | **数据结构同步 v2.6**：更新 SvgCanvas.vue 模板（WallLayer/ColumnLayer 替代 BoundaryLayer）；重写 canvas.d.ts 类型定义（扁平化结构、新增 Wall/Column/FinishLocationBoundary）；更新项目结构 |
| 2025-12-10 | v1.0 | 计划创建，基于共识文档 |
