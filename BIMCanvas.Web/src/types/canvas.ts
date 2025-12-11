// ============================================
// 基础几何类型
// ============================================
export type Point2D = [number, number];
export type Vec2D = [number, number];
export type Line2D = [Point2D, Point2D];

// 多边形（支持带孔洞）
export interface Polygon2D {
  shell: Point2D[];
  holes?: Point2D[][];
}

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
  placementElevation: number;
  origin: [number, number, number];
  rotation: number;
  method: 'boundingBox' | 'cropBox';
}

// ============================================
// 建筑构件
// ============================================
export interface Wall {
  id: string;
  elementId: number;
  polygon: Point2D[];
}

export interface Column {
  id: string;
  elementId: number;
  isStructural: boolean;
  polygon: Point2D[];
}

export interface Opening {
  id: string;
  type: OpeningType;
  line: Line2D;
  facingDirection?: Vec2D;
  handDirections?: Vec2D[];
}

export type OpeningType = 0 | 1; // 0 = door, 1 = window

export interface FinishLocationBoundary {
  id: string;
  elementIds: number[];
  polygon: Point2D[];
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
// 枚举类型
// ============================================
export type RoomType = number; // 枚举值

export type ZoneTag = string;

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
