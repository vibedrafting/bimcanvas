export type Point2D = [number, number];
export type Line2D = [Point2D, Point2D];
export type Polygon2D = Point2D[];

// Enums
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
  Exclusion: 0,   // 禁区
  Room: 1,        // 房间（直接由 Revit Room 轮廓转换）
  Designable: 2   // 设计区（AI/用户划分后的功能区）
} as const;

export type ZoneType = typeof ZoneType[keyof typeof ZoneType];

export const FinishType = {
  Normal: 0,   // 普通完成面
  Special: 1   // 特殊完成面（如电视墙）
} as const;

export type FinishType = typeof FinishType[keyof typeof FinishType];

// BIM Elements
export interface Wall {
  id: string;
  elementId?: number;
  polygon: Polygon2D; // Changed from Line2D to Polygon2D for precision
  thickness?: number; // Optional, derived from polygon
}

export interface Column {
  id: string;
  elementId?: number;
  isStructural?: boolean;
  polygon: Polygon2D;
}

export interface Opening {
  id: string;
  type: number; // 0: Door, 1: Window
  line: Line2D;
  facingDirection?: Point2D;
  handDirections?: Point2D[];
}

export interface FinishLocationBoundary {
  id: string;
  elementIds: number[];
  polygon: Polygon2D;
}

export interface Room {
  id: string;
  name?: string;
  type: number;
  boundary: {
    shell: Polygon2D;
    holes?: Polygon2D[];
  };
}

export interface FinishRequirement {
  wallFinishId: string;
  type: FinishType;
}

export interface Zone {
  id: string;
  name: string;
  type: ZoneType;
  reason: string;
  rawBoundary?: Polygon2D;
  computedBoundary?: Polygon2D;
  tags: string[];
  finishRequirements: FinishRequirement[];
  schemeId?: string;
}

export interface Module {
  id: string;
  moduleId: string;
  moduleName?: string;
  bounds: Polygon2D; // 4 points
  facing: string | Point2D; // "north" or vector
  zoneId: string;
  items: any[]; // Placeholder for furniture items
}

// 方案数据（预留，对应 Zone.schemeId）
export interface Scheme {
  id: string;
  name: string;
  description?: string;
}

// 方案布置数据
export interface LayoutData {
  modules: Module[];
  schemes: Scheme[];
}

// Metadata
export interface Metadata {
  placementElevation?: number;
  origin?: [number, number, number];
  rotation?: number;
  method?: string;
}

// WallFinish (计算派生数据)
export interface WallFinish {
  id: string;
  locationLine: Line2D;
  thickness: number;
  exclusionBoundary?: Polygon2D;
}

// Revit 原始数据子结构
export interface RevitData {
  metadata?: Metadata;
  walls: Wall[];
  columns?: Column[];
  openings?: Opening[];
  finishLocationBoundaries?: FinishLocationBoundary[];
  rooms?: Room[];
}

// 计算派生数据子结构
export interface ComputedData {
  zones: Zone[];
  wallFinishes?: WallFinish[];
}

// Document Root
export interface DesignDocument {
  id: string;
  projectName?: string;
  exportDate?: string;
  version: number;
  coordinateSystem: string;
  revit?: RevitData;
  computed?: ComputedData;
  layout?: LayoutData;
}

// 向后兼容别名
export type CanvasDocument = DesignDocument;
