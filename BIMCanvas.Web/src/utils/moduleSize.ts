/**
 * Module size utilities — bounds <-> (width, depth) 双向换算 + limits 解析 + clamp。
 *
 * BIMCanvas 的 Module 几何真理是 4 顶点 OBB；width/depth 都是派生量。
 * 本文件是 PlaceTool / PropertyPanel / PlacementSizeBar 共用的唯一换算入口。
 *
 * 不变量：
 * - bounds[0] -> bounds[1] 永远沿模块本地 +X 轴（= width 方向，旋转后）
 * - bounds[1] -> bounds[2] 永远沿模块本地 +Y 轴（= depth 方向，旋转后）
 * 这与 PlaceTool.calculateBounds 的局部点序 [-hw,-hd], [hw,-hd], [hw,hd], [-hw,hd] 一致。
 */

import type { FacingData, FacingSemantic, Point2D, Polygon2D } from '../types/canvas';

// ========== Module morphology 类型（镜像 Server DTO） ==========

export type DimensionLimit =
  | { kind: 'range'; min: number; max: number }
  | { kind: 'enum'; values: number[] };

export interface ModuleMorphology {
  strategy: 'fixed' | 'parametric' | 'horizontal_fill';
  /** 模数（mm）：尺寸 stepper +/- 步进值；undefined 时 UI 兜底为 DEFAULT_STEP */
  step?: number;
  limits?: {
    width?: DimensionLimit;
    depth?: DimensionLimit;
  };
}

/** UI 兜底模数（mm）。当 module_library.json 中模块未指定 morphology.step 时使用。 */
export const DEFAULT_STEP = 50;

/**
 * Server 下发的原始 morphology 形态（DTO 直接序列化结果）。
 * 转换为 ModuleMorphology 时把 { range: [...] } / { enum: [...] } 归一化到 kind 标签。
 */
export interface RawModuleMorphology {
  strategy: string;
  step?: number;
  limits?: {
    width?: { range?: number[]; enum?: number[] };
    depth?: { range?: number[]; enum?: number[] };
  };
}

export function normalizeMorphology(raw: RawModuleMorphology | undefined | null): ModuleMorphology | undefined {
  if (!raw) return undefined;
  const strategy = (raw.strategy === 'parametric' || raw.strategy === 'horizontal_fill')
    ? raw.strategy
    : 'fixed';
  const result: ModuleMorphology = { strategy };
  if (typeof raw.step === 'number' && Number.isFinite(raw.step) && raw.step > 0) {
    result.step = raw.step;
  }
  if (raw.limits) {
    const width = normalizeLimit(raw.limits.width);
    const depth = normalizeLimit(raw.limits.depth);
    if (width || depth) {
      result.limits = {};
      if (width) result.limits.width = width;
      if (depth) result.limits.depth = depth;
    }
  }
  return result;
}

/** 取模块的有效 step：morphology 自带 → 否则 DEFAULT_STEP */
export function resolveStep(morphology: ModuleMorphology | undefined): number {
  return morphology?.step ?? DEFAULT_STEP;
}

function normalizeLimit(raw: { range?: number[]; enum?: number[] } | undefined): DimensionLimit | undefined {
  if (!raw) return undefined;
  if (Array.isArray(raw.range) && raw.range.length === 2) {
    const min = raw.range[0];
    const max = raw.range[1];
    if (typeof min === 'number' && typeof max === 'number'
        && Number.isFinite(min) && Number.isFinite(max) && min <= max) {
      return { kind: 'range', min, max };
    }
  }
  if (Array.isArray(raw.enum) && raw.enum.length > 0) {
    const values = raw.enum.filter((v): v is number => typeof v === 'number' && Number.isFinite(v));
    if (values.length > 0) {
      return { kind: 'enum', values };
    }
  }
  return undefined;
}

// ========== bounds <-> size 换算 ==========

/**
 * 中心 + 尺寸 + 旋转角（CCW+ 弧度，数据模型角度）→ 4 顶点 OBB。
 * 提取自 PlaceTool.calculateBounds，是 PlaceTool / PropertyPanel resize 共用入口。
 */
export function boundsFromCenter(
  center: Point2D,
  width: number,
  depth: number,
  rotation: number
): Polygon2D {
  const hw = width / 2;
  const hd = depth / 2;

  const localPoints: Polygon2D = [
    [-hw, -hd],
    [hw, -hd],
    [hw, hd],
    [-hw, hd]
  ];

  const cos = Math.cos(rotation);
  const sin = Math.sin(rotation);

  return localPoints.map(([lx, ly]) => {
    const rx = lx * cos - ly * sin;
    const ry = lx * sin + ly * cos;
    return [center[0] + rx, center[1] + ry] as Point2D;
  });
}

/**
 * 把 FacingData 解析为单位方向向量。优先 value，其次 semantic。
 * 仅本文件内消歧 width/depth 时使用；其他坐标计算请用 coordinates.ts 的 getFacingValue。
 */
function facingDirection(facing: FacingData | null | undefined): { x: number; y: number } | null {
  if (!facing) return null;
  if (facing.value && Number.isFinite(facing.value[0]) && Number.isFinite(facing.value[1])) {
    const len = Math.hypot(facing.value[0], facing.value[1]);
    if (len > 1e-6) return { x: facing.value[0] / len, y: facing.value[1] / len };
  }
  const semantic = facing.semantic as FacingSemantic | null;
  if (!semantic) return null;
  switch (semantic) {
    case 'north': return { x: 0, y: 1 };
    case 'south': return { x: 0, y: -1 };
    case 'east': return { x: 1, y: 0 };
    case 'west': return { x: -1, y: 0 };
    case 'northeast': return { x: 0.7071, y: 0.7071 };
    case 'northwest': return { x: -0.7071, y: 0.7071 };
    case 'southeast': return { x: 0.7071, y: -0.7071 };
    case 'southwest': return { x: -0.7071, y: -0.7071 };
    default: return null;
  }
}

/**
 * 从 OBB 4 顶点反推语义意义上的 (width, depth)。
 *
 * width = 模块"宽度"边长（垂直于 facing 方向）
 * depth = 模块"深度"边长（沿 facing 方向）
 *
 * 不同 writer 写出的 polygon 顶点排列约定不同：
 *  - PlaceTool：edge0 = bounds[1]-bounds[0] 沿模块本地 +X 轴（= width 方向）
 *  - Agent create_module_bounds：N/S facing 同上；E/W facing 时 polygon 是世界轴对齐的，
 *    edge0 沿世界 +X = depth 方向（半宽半深已交换）
 *
 * 用 facing 与 edge0 的点积消歧：
 *  - |edge0 · facing| 接近 1 → edge0 沿 facing 方向 → edge0 是 depth、edge1 是 width
 *  - 接近 0 → edge0 垂直于 facing → edge0 是 width、edge1 是 depth
 *
 * 缺 facing 时退回 PlaceTool 约定（edge0 = width）。
 */
export function obbSizeFromBounds(
  bounds: Polygon2D,
  facing?: FacingData | null
): { width: number; depth: number } {
  if (!bounds || bounds.length < 4) {
    return { width: 0, depth: 0 };
  }
  const p0 = bounds[0];
  const p1 = bounds[1];
  const p2 = bounds[2];
  if (!p0 || !p1 || !p2) {
    return { width: 0, depth: 0 };
  }
  const wdx = p1[0] - p0[0];
  const wdy = p1[1] - p0[1];
  const ddx = p2[0] - p1[0];
  const ddy = p2[1] - p1[1];
  const len0 = Math.hypot(wdx, wdy);
  const len1 = Math.hypot(ddx, ddy);

  const facingVec = facingDirection(facing);
  if (facingVec && len0 > 1e-6) {
    const edge0Dot = Math.abs((wdx / len0) * facingVec.x + (wdy / len0) * facingVec.y);
    if (edge0Dot > 0.5) {
      // edge0 沿 facing 方向 → edge0 是 depth
      return { width: len1, depth: len0 };
    }
  }
  return { width: len0, depth: len1 };
}

/** OBB 中心：4 顶点的算术平均。 */
export function obbCenter(bounds: Polygon2D): Point2D {
  if (!bounds || bounds.length === 0) return [0, 0];
  let cx = 0, cy = 0;
  for (const p of bounds) {
    cx += p[0];
    cy += p[1];
  }
  return [cx / bounds.length, cy / bounds.length];
}

/**
 * OBB 旋转角（PlaceTool 的 currentRotation 约定，bearing 风格：北=0，东=π/2 ...）。
 *
 * 优先用 facing 推导（= facingToAngle）以避免 polygon 顶点约定差异：
 *   bearing = atan2(facing.x, facing.y)
 * 缺 facing 时退回从 polygon edge0 角度估算（PlaceTool 顶点约定）。
 *
 * 该值可直接喂给 boundsFromCenter 重建多边形。
 */
export function obbRotation(bounds: Polygon2D, facing?: FacingData | null): number {
  const facingVec = facingDirection(facing);
  if (facingVec) {
    return Math.atan2(facingVec.x, facingVec.y);
  }
  if (!bounds || bounds.length < 2) return 0;
  const p0 = bounds[0];
  const p1 = bounds[1];
  if (!p0 || !p1) return 0;
  const dx = p1[0] - p0[0];
  const dy = p1[1] - p0[1];
  return Math.atan2(dy, dx);
}

// ========== Limits → UI 推荐文本 ==========

/**
 * 尺寸 hint，区分 kind 让 UI 决定视觉权重。
 *  - limit：morphology 的硬性可调范围（range / enum），需要设计师认真对待 → 视觉正常
 *  - default：仅是目录默认尺寸（无 morphology 约束），轻参考 → 视觉淡化（斜体 + 更暗）
 *  - none：无任何 hint
 */
export type SizeHint = { text: string; kind: 'limit' | 'default' | 'none' };

/**
 * 把 morphology 在指定轴上的限制 + 默认尺寸格式化为简短 hint 文本（无前缀、无单位）。
 *
 * 设计原则：
 * - 不强制 clamp，仅给参考。
 * - 整个面板都是 mm，去单位。
 * - 去前缀，靠 kind + 视觉差异化区分硬约束 vs 轻参考。
 *
 * 输出：
 * - range limit → "600–1200" (kind=limit)
 * - enum limit  → "400 / 600" (kind=limit)
 * - 无 limit    → "550"       (kind=default)
 * - 全部缺失     → ""          (kind=none)
 */
export function formatSizeHint(
  morphology: ModuleMorphology | undefined,
  axis: 'width' | 'depth',
  defaultValue: number
): SizeHint {
  const limit = morphology?.limits?.[axis];
  if (limit?.kind === 'range') {
    return { text: `${limit.min}–${limit.max}`, kind: 'limit' };
  }
  if (limit?.kind === 'enum') {
    return { text: limit.values.join(' / '), kind: 'limit' };
  }
  if (Number.isFinite(defaultValue) && defaultValue > 0) {
    return { text: `${Math.round(defaultValue)}`, kind: 'default' };
  }
  return { text: '', kind: 'none' };
}

/**
 * 输入合法性检查：拒绝 NaN / 非有限数 / ≤ 0。
 * 不做范围 clamp—用户可自由超出推荐范围。
 */
export function isValidDimension(value: number): boolean {
  return Number.isFinite(value) && value > 0;
}
