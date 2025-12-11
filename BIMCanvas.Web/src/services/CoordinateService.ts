import type { Point2D, Polygon2D } from '../types/canvas';

/**
 * 坐标转换服务
 * BIMCanvas 使用 CAD 坐标系（Y-up），需要转换为屏幕坐标系（Y-down）
 */
export class CoordinateService {
  private canvasHeight: number;
  private scale: number;
  private offsetX: number;
  private offsetY: number;

  constructor(
    canvasHeight: number,
    scale: number = 1,
    offsetX: number = 0,
    offsetY: number = 0
  ) {
    this.canvasHeight = canvasHeight;
    this.scale = scale;
    this.offsetX = offsetX;
    this.offsetY = offsetY;
  }

  /**
   * 世界坐标 (mm, Y-up) → 屏幕坐标 (px, Y-down)
   */
  worldToScreen(point: Point2D): Point2D {
    return [
      (point[0] + this.offsetX) * this.scale,
      this.canvasHeight - (point[1] + this.offsetY) * this.scale,
    ];
  }

  /**
   * 屏幕坐标 (px, Y-down) → 世界坐标 (mm, Y-up)
   */
  screenToWorld(point: Point2D): Point2D {
    return [
      point[0] / this.scale - this.offsetX,
      (this.canvasHeight - point[1]) / this.scale - this.offsetY,
    ];
  }

  /**
   * 转换多边形顶点数组
   */
  transformPolygon(vertices: Point2D[]): Point2D[] {
    return vertices.map((p) => this.worldToScreen(p));
  }

  /**
   * 转换 Polygon2D（带孔洞）
   */
  transformPolygon2D(polygon: Polygon2D): Polygon2D {
    return {
      shell: this.transformPolygon(polygon.shell),
      holes: polygon.holes?.map((hole) => this.transformPolygon(hole)),
    };
  }

  /**
   * 将多边形顶点转换为 SVG path 的 d 属性
   */
  polygonToSvgPath(vertices: Point2D[]): string {
    if (vertices.length === 0) return '';

    const screenPoints = this.transformPolygon(vertices);
    const [first, ...rest] = screenPoints;

    let path = `M ${first[0]} ${first[1]}`;
    for (const point of rest) {
      path += ` L ${point[0]} ${point[1]}`;
    }
    path += ' Z';

    return path;
  }

  /**
   * 将 Polygon2D（带孔洞）转换为 SVG path
   */
  polygon2DToSvgPath(polygon: Polygon2D): string {
    let path = this.polygonToSvgPath(polygon.shell);

    // 添加孔洞（反向绘制）
    if (polygon.holes) {
      for (const hole of polygon.holes) {
        const holeScreenPoints = this.transformPolygon(hole);
        if (holeScreenPoints.length > 0) {
          const [first, ...rest] = holeScreenPoints;
          path += ` M ${first[0]} ${first[1]}`;
          // 反向绘制孔洞
          for (let i = rest.length - 1; i >= 0; i--) {
            path += ` L ${rest[i][0]} ${rest[i][1]}`;
          }
          path += ' Z';
        }
      }
    }

    return path;
  }

  /**
   * 更新画布高度
   */
  setCanvasHeight(height: number) {
    this.canvasHeight = height;
  }

  /**
   * 更新缩放比例
   */
  setScale(scale: number) {
    this.scale = scale;
  }

  /**
   * 更新偏移
   */
  setOffset(offsetX: number, offsetY: number) {
    this.offsetX = offsetX;
    this.offsetY = offsetY;
  }
}

/**
 * 计算画布边界框
 */
export function calculateBoundingBox(document: {
  walls: { polygon: Point2D[] }[];
  columns: { polygon: Point2D[] }[];
}): { minX: number; minY: number; maxX: number; maxY: number; width: number; height: number } {
  let minX = Infinity;
  let minY = Infinity;
  let maxX = -Infinity;
  let maxY = -Infinity;

  const processPolygon = (polygon: Point2D[]) => {
    for (const [x, y] of polygon) {
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
  };

  for (const wall of document.walls) {
    processPolygon(wall.polygon);
  }
  for (const column of document.columns) {
    processPolygon(column.polygon);
  }

  return {
    minX,
    minY,
    maxX,
    maxY,
    width: maxX - minX,
    height: maxY - minY,
  };
}
