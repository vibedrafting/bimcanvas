export type Point2D = [number, number];
export type Line2D = [Point2D, Point2D];
export type Polygon2D = Point2D[];

// Enums
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

export interface ExclusionArea {
  id: string;
  type: number;  // 0: DoorSwing, 1: Passage, 2: Other
  boundary: Polygon2D;
}

export interface Zone {
  roomId: string;
  innerBoundary: Polygon2D;
  exclusionAreas: ExclusionArea[];
}

export interface Module {
  id: string;
  bounds: Polygon2D; // 4 points
  facing: string | Point2D; // "north" or vector
  items: any[]; // Placeholder for furniture items
}

// Document Root
export interface CanvasDocument {
  id: string;
  version: number;
  coordinateSystem: string;
  metadata?: any;
  walls: Wall[];
  columns?: Column[];
  openings?: Opening[];
  finishLocationBoundaries?: FinishLocationBoundary[];
  rooms?: Room[];
  zones: Zone[];
  modules: Module[];
}
