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

import type { Point2D, Polygon2D } from '../types/canvas';

// ========== Module morphology 类型（镜像 Server DTO） ==========

export type DimensionLimit =
  | { kind: 'range'; min: number; max: number }
  | { kind: 'enum'; values: number[] };

export interface ModuleMorphology {
  strategy: 'fixed' | 'parametric' | 'horizontal_fill';
  limits?: {
    width?: DimensionLimit;
    depth?: DimensionLimit;
  };
}

/**
 * Server 下发的原始 morphology 形态（DTO 直接序列化结果）。
 * 转换为 ModuleMorphology 时把 { range: [...] } / { enum: [...] } 归一化到 kind 标签。
 */
export interface RawModuleMorphology {
  strategy: string;
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
 * 从 OBB 4 顶点反推 (width, depth)。
 * width = |bounds[1] - bounds[0]|, depth = |bounds[2] - bounds[1]|.
 */
export function obbSizeFromBounds(bounds: Polygon2D): { width: number; depth: number } {
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
  return {
    width: Math.hypot(wdx, wdy),
    depth: Math.hypot(ddx, ddy)
  };
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
 * OBB 旋转角（CCW+ 弧度，数据模型角度）：
 * 由 bounds[0] -> bounds[1] 的方向决定（= 模块本地 +X 轴在世界系的方位）。
 */
export function obbRotation(bounds: Polygon2D): number {
  if (!bounds || bounds.length < 2) return 0;
  const p0 = bounds[0];
  const p1 = bounds[1];
  if (!p0 || !p1) return 0;
  const dx = p1[0] - p0[0];
  const dy = p1[1] - p0[1];
  return Math.atan2(dy, dx);
}

// ========== Limits → UI 模式 ==========

export type DimensionMode =
  | { mode: 'readonly'; value: number }
  | { mode: 'range'; min: number; max: number }
  | { mode: 'enum'; values: number[] };

/**
 * 根据 morphology 决定单维度的 UI 输入控件形态。
 * - fixed strategy / 缺 morphology / 该维度无 limit → readonly（用 defaultValue 显示）
 * - range / enum → 对应可编辑形态
 *
 * defaultValue 仅在 readonly 时使用（来自 moduleDef.size）。
 */
export function resolveDimensionMode(
  morphology: ModuleMorphology | undefined,
  axis: 'width' | 'depth',
  defaultValue: number
): DimensionMode {
  if (!morphology || morphology.strategy === 'fixed') {
    return { mode: 'readonly', value: defaultValue };
  }
  const limit = morphology.limits?.[axis];
  if (!limit) {
    return { mode: 'readonly', value: defaultValue };
  }
  if (limit.kind === 'range') {
    return { mode: 'range', min: limit.min, max: limit.max };
  }
  return { mode: 'enum', values: limit.values };
}

/**
 * 把输入值约束到 mode 允许范围内。UI 软防线，确保不发出非法尺寸到 store。
 * - readonly → 永远返回 mode.value
 * - range → clamp 到 [min, max]
 * - enum → 取最近的候选值
 */
export function clampDimension(value: number, mode: DimensionMode): number {
  if (mode.mode === 'readonly') return mode.value;
  if (!Number.isFinite(value)) {
    return mode.mode === 'range' ? mode.min : mode.values[0]!;
  }
  if (mode.mode === 'range') {
    return Math.max(mode.min, Math.min(mode.max, value));
  }
  let nearest = mode.values[0]!;
  let bestDist = Math.abs(value - nearest);
  for (const candidate of mode.values) {
    const d = Math.abs(value - candidate);
    if (d < bestDist) {
      bestDist = d;
      nearest = candidate;
    }
  }
  return nearest;
}

/** 用于 UI 提示语：把 limit 描述成简短文本 */
export function describeLimit(limit: DimensionLimit | undefined): string {
  if (!limit) return 'fixed';
  if (limit.kind === 'range') return `${limit.min}–${limit.max} mm`;
  return `${limit.values.join(' / ')} mm`;
}
