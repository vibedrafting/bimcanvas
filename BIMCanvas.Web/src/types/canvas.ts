// ==========================================
// BIMCanvas v3.0 Web Type Definitions
// ==========================================

// ========== 基础类型 ==========

export type Point2D = [number, number];
export type Line2D = [Point2D, Point2D];
export type Polygon2D = Point2D[];

// ========== 枚举类型 ==========

export const RoomType = {
  Unknown: 0,
  LivingRoom: 1,
  Bedroom: 2,
  Kitchen: 3,
  Bathroom: 4,
  Balcony: 5,
  Corridor: 6
} as const;
export type RoomType = typeof RoomType[keyof typeof RoomType];

export const ZoneType = {
  Exclusion: 0,
  Room: 1,
  Designable: 2
} as const;
export type ZoneType = typeof ZoneType[keyof typeof ZoneType];

export const FinishType = {
  Normal: 0,
  Special: 1
} as const;
export type FinishType = typeof FinishType[keyof typeof FinishType];

export const FinishSource = {
  RoomDefault: 0,
  ZoneOverride: 1,
  UserOverride: 2
} as const;
export type FinishSource = typeof FinishSource[keyof typeof FinishSource];

export const StrategyApproach = {
  Standard: 0,
  Custom: 1
} as const;
export type StrategyApproach = typeof StrategyApproach[keyof typeof StrategyApproach];

export const StrategyStatus = {
  Valid: 0,
  Dirty: 1,
  Invalid: 2
} as const;
export type StrategyStatus = typeof StrategyStatus[keyof typeof StrategyStatus];

export const ZoneTag = {
  TvMedia: 'TvMedia',
  AudioVideo: 'AudioVideo',
  Sleep: 'Sleep',
  Rest: 'Rest',
  Reading: 'Reading',
  Work: 'Work',
  Study: 'Study',
  WardrobeStorage: 'WardrobeStorage',
  ShoeStorage: 'ShoeStorage',
  GeneralStorage: 'GeneralStorage',
  Dining: 'Dining',
  Cooking: 'Cooking',
  FoodPrep: 'FoodPrep',
  Bar: 'Bar',
  Shower: 'Shower',
  Bathtub: 'Bathtub',
  Toilet: 'Toilet',
  Washing: 'Washing',
  Laundry: 'Laundry',
  Vanity: 'Vanity',
  Entry: 'Entry',
  Passage: 'Passage',
  Display: 'Display',
  Plants: 'Plants'
} as const;
export type ZoneTag = typeof ZoneTag[keyof typeof ZoneTag];

// ========== Project 层 (project.json) ==========

export interface Project {
  id: string;
  name: string;
  version: string;
  createdAt?: string;
  updatedAt?: string;
  coordinateSystem: string;
  activeSchemeId: string;
  schemes: SchemeRef[];
}

export interface SchemeRef {
  id: string;
  path: string;
  name: string;
}

// ========== Baseline 层 (baseline/*.json) ==========

export interface BaselineMetadata {
  placementElevation?: number;
  origin?: [number, number, number];
  rotation?: number;
  baselineHash?: string;
}

export interface Wall {
  id: string;
  elementId?: number;
  polygon: Polygon2D;
  thickness?: number;
}

export interface Column {
  id: string;
  elementId?: number;
  isStructural?: boolean;
  polygon: Polygon2D;
}

export interface Opening {
  id: string;
  type: number;  // 0: Door, 1: Window
  doorOperation?: number;  // 0: Swing(平开), 1: Sliding(推拉)。仅门有效，可选，缺省为 Swing
  line: Line2D;
  facingDirection?: Point2D;
  handDirections?: Point2D[];
}

export interface Room {
  id: string;
  name?: string;
  type: RoomType;
  boundary: {
    shell: Polygon2D;
    holes?: Polygon2D[];
  };
}

export interface LocationLine {
  id: string;
  wallId: string;
  roomId: string;
  side: 'interior' | 'exterior';
  line: Line2D;
  length: number;
}

export interface BaselineData {
  metadata: BaselineMetadata;
  walls: Wall[];
  columns: Column[];
  openings: Opening[];
  rooms: Room[];
  locationLines: LocationLine[];
}

// ========== Scheme 层 (schemes/*.json) v3.2 ==========

export interface Strategy {
  id: string;
  name: string;
  approach: StrategyApproach;
  description?: string;
  createdAt?: string;
  updatedAt?: string;
  lastValidatedBaselineHash: string;
  status: StrategyStatus;
}

export interface FinishRequirement {
  wallFinishId: string;
  type: FinishType;
}

export interface Zone {
  id: string;
  name: string;
  roomId: string;
  type: ZoneType;
  reason: string;
  rawBoundary?: Polygon2D;
  computedBoundary?: Polygon2D;
  tags: ZoneTag[];
  optionalTags?: ZoneTag[];
  finishRequirements: FinishRequirement[];
  schemeId?: string;
  visible: boolean;
  subZones?: Zone[];
}

export interface FinishSegment {
  id: string;
  sourceLineId: string;
  range: [number, number];
  finishModuleId: string;
  thickness: number;
  source: FinishSource;
  zoneId?: string;
  reason?: string;
}

export interface ModuleItem {
  familyName: string;
  typeName: string;
  offset: Point2D;
  rotation: number;
}

export type FacingSemantic =
  | 'north'
  | 'south'
  | 'east'
  | 'west'
  | 'northeast'
  | 'northwest'
  | 'southeast'
  | 'southwest';

export interface FacingData {
  value: Point2D | null;
  semantic: FacingSemantic | null;
}

export interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: Polygon2D;
  facing: FacingData;
  /** 所属分区 ID（由 Server 加载时自动填充） */
  zoneId?: string;
  items: ModuleItem[];
  placementReason?: string;
}

export interface SchemeData {
  strategy: Strategy;
  zones: Zone[];
  finishes: FinishSegment[];
  modules: Module[];
  /** Load 质检闸门：加载时发现的分区数据质检错误，存在时说明部分模块已被隔离 */
  zoneErrors?: ZoneLoadError[];
}

/** Load 质检闸门：分区数据加载错误描述符 */
export interface ZoneLoadError {
  zoneId: string;
  /** ParseError: 文件无法解析 | StructureError: 字段结构不合法 */
  errorType: 'ParseError' | 'StructureError';
  message: string;
  failedModuleIds: string[];
}

// ========== Computed 层 (computed/*.json) ==========

export interface ComputedData {
  /** 房间区域（来自 computed/room_zones.json，由 baseline/rooms.json 转换） */
  roomZones: Zone[];
  /** 禁区（来自 computed/exclusions.json） */
  exclusions: Zone[];
}

// ========== 聚合根：ProjectData ==========

export interface ProjectData {
  project: Project;
  baseline: BaselineData;
  activeScheme: SchemeData;
  computed: ComputedData;
}

// ========== 边界段调试数据 ==========

export type BoundarySegmentType = 'wall' | 'passage' | 'door' | 'window';

export interface BoundarySegment {
  id?: string;
  type: BoundarySegmentType;
  start: Point2D;
  end: Point2D;
}

export interface ZoneBoundaryData {
  zoneId: string;
  segments: BoundarySegment[];
}

// ========================================================================
// 组 5 §5.C.1: Scene 数据层类型 (主真理源 v1.1 §3.9 / §3.10)
// ========================================================================
// .bcp 项目多 scene 容器化后,Web 端需要 sceneId 感知:
// - activeSceneId:当前激活的 scene id(从 OpenProject 响应 / LaunchContext 填)
// - referenceScenes:跨 scene 只读叠加层(灰色显示,UI 可切换显隐)
//
// 渲染层(ThreeSceneService / SceneBuilder)消费 SceneLayer.visible / .modules,
// 本组只暴露数据结构,渲染逻辑由组 4 / 后续实现。
// ========================================================================

/** Scene 唯一标识(主真理源 §3.9,pattern: `^[a-z0-9-]+$`) */
export type SceneId = string;

/**
 * 跨 scene 只读叠加层。
 * 用户在 active scene 下工作时,可同时显示其他 scene 的家具作为只读底图。
 *
 * 数据来源:`GET /api/scheme/scenes/{sceneId}/modules`(SceneArtifactsController)。
 */
export interface SceneLayer {
  sceneId: SceneId;
  /** 业务分类(residential / electrical / mep 等) */
  scene: string;
  /** 提供该 scene 的 plugin id */
  pluginId: string;
  /** Plugin 版本 range(从 project.json.scenes[].plugin.versionRange) */
  versionRange: string;
  /** UI 切换显隐(渲染层消费) */
  visible: boolean;
  /** 跨 scene 叠加始终只读(写入会被 Server V12b gate 拦截) */
  readOnly: true;
  /** 从 `GET /api/scheme/scenes/{sceneId}/modules` 拉取的聚合模块列表 */
  modules: Module[];
}
