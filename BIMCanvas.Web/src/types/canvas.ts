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
  finishRequirements: FinishRequirement[];
  schemeId?: string;
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

export interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: Polygon2D;
  facing: string | Point2D;
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
