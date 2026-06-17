/**
 * BIMCanvas 坐标转换工具
 * 
 * 坐标系定义（单一事实来源）：
 * - 数据模型 (CanvasDocument): 2D 平面坐标系，右手系，Y 轴向上 (CAD 标准)
 *   - Point2D = [x, y]，单位：毫米 (mm)
 *   - 正方向：X 向右，Y 向上
 *   - 旋转正方向：逆时针 (CCW)
 * 
 * - 渲染视图 (Three.js 俯视图): 3D 坐标系，相机从 +Y 向下看
 *   - Vector3 = (x, height, z)
 *   - 映射关系：Data(x, y) -> View(x, 0, -y)
 *   - 正方向：X 向右，Z 向下（屏幕下方）
 * 
 * 使用规范：
 * - 所有涉及 2D/3D 坐标转换的代码必须使用本文件中的函数
 * - 禁止在业务代码中手动乘 -1 进行坐标转换
 */

import * as THREE from 'three';
import type { FacingData, Point2D, Polygon2D, Line2D } from '../types/canvas';
import { createLogger } from './logger';

const log = createLogger('SYS');

// ============================================================
// 基础坐标转换
// ============================================================

/**
 * 将 2D 数据坐标转换为 3D 渲染坐标
 * @param point 2D 点 [x, y]
 * @param height Y 轴高度（默认为 0，即地面）
 * @returns Three.js Vector3
 */
export function toWorld(point: Point2D, height: number = 0): THREE.Vector3 {
    return new THREE.Vector3(point[0], height, -point[1]);
}

/**
 * 将 3D 渲染坐标转换回 2D 数据坐标
 * @param vector Three.js Vector3
 * @returns 2D 点 [x, y]
 */
export function toModel(vector: THREE.Vector3): Point2D {
    return [vector.x, -vector.z];
}

/**
 * 将 2D 位移增量转换为 3D 位移增量
 * @param delta2D 2D 位移 [dx, dy]
 * @returns Three.js Vector3 位移
 */
export function deltaToWorld(delta2D: Point2D): THREE.Vector3 {
    return new THREE.Vector3(delta2D[0], 0, -delta2D[1]);
}

/**
 * 将 3D 位移增量转换回 2D 位移增量
 * @param delta3D Three.js Vector3 位移
 * @returns 2D 位移 [dx, dy]
 */
export function deltaToModel(delta3D: THREE.Vector3): Point2D {
    return [delta3D.x, -delta3D.z];
}

// ============================================================
// 多边形 / 线段转换
// ============================================================

/**
 * 将 2D 多边形转换为 3D 点数组
 * @param polygon 2D 多边形
 * @param height Y 轴高度
 * @returns Three.js Vector3 数组
 */
export function polygonToWorld(polygon: Polygon2D, height: number = 0): THREE.Vector3[] {
    return polygon.map(p => toWorld(p, height));
}

/**
 * 将 2D 线段转换为 3D 线段
 * @param line 2D 线段 [[x1,y1], [x2,y2]]
 * @param height Y 轴高度
 * @returns [Vector3, Vector3]
 */
export function lineToWorld(line: Line2D, height: number = 0): [THREE.Vector3, THREE.Vector3] {
    return [toWorld(line[0], height), toWorld(line[1], height)];
}

// ============================================================
// 中心点计算
// ============================================================

/**
 * 计算多边形中心点（重心）的 3D 坐标
 * @param polygon 2D 多边形
 * @param height Y 轴高度
 * @returns Three.js Vector3
 */
export function polygonCenterToWorld(polygon: Polygon2D, height: number = 0): THREE.Vector3 {
    if (!polygon || polygon.length === 0) {
        return new THREE.Vector3(0, height, 0);
    }
    let x = 0, y = 0;
    polygon.forEach(p => {
        x += p[0];
        y += p[1];
    });
    const centerX = x / polygon.length;
    const centerY = y / polygon.length;
    return new THREE.Vector3(centerX, height, -centerY);
}

/**
 * 计算线段中心点的 3D 坐标
 * @param line 2D 线段
 * @param height Y 轴高度
 * @returns Three.js Vector3
 */
export function lineCenterToWorld(line: Line2D, height: number = 0): THREE.Vector3 {
    const centerX = (line[0][0] + line[1][0]) / 2;
    const centerY = (line[0][1] + line[1][1]) / 2;
    return new THREE.Vector3(centerX, height, -centerY);
}

/**
 * 计算多边形包围盒中心点的 3D 坐标
 * @param bounds 2D 包围盒多边形（通常是 4 个点）
 * @param height Y 轴高度
 * @returns Three.js Vector3
 */
export function boundsCenterToWorld(bounds: Polygon2D, height: number = 0): THREE.Vector3 {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    bounds.forEach(p => {
        minX = Math.min(minX, p[0]);
        minY = Math.min(minY, p[1]);
        maxX = Math.max(maxX, p[0]);
        maxY = Math.max(maxY, p[1]);
    });
    const centerX = (minX + maxX) / 2;
    const centerY = (minY + maxY) / 2;
    return new THREE.Vector3(centerX, height, -centerY);
}

// ============================================================
// 旋转计算
// ============================================================
//
// ⚠️ 角度语义系统说明（重要）
//
// 本文件中的旋转函数使用【数据模型角】语义：
// - 正方向：CCW（逆时针）为正
// - 零度方向：+Y 轴（北方）
// - 坐标系：2D 平面，Y 轴向上
//
// 与其他角度系统的关系：
// | 系统 | 正方向 | 转换方式 |
// |------|--------|----------|
// | 交互角（atan2(z,x)）| CW+ | 取反后传入 |
// | Three.js rotation.y | CCW+ | 取反后传入 |
// | 用户输入（度数） | CCW+ | 直接使用 |
//
// 详见 BIMCanvas.Web/README.md "角度语义系统" 章节
// ============================================================

/**
 * 在 2D 数据坐标系中绕中心点旋转一个点
 *
 * ⚠️ 角度语义：数据模型角（CCW+）
 * - 正值 = 逆时针旋转
 * - 负值 = 顺时针旋转
 *
 * @param point 要旋转的点 [x, y]
 * @param center 旋转中心 [cx, cy]
 * @param angleRad 旋转角度（弧度，逆时针/CCW 为正）
 * @returns 旋转后的点 [x', y']
 */
export function rotatePoint2D(
    point: Point2D,
    center: Point2D,
    angleRad: number
): Point2D {
    const cos = Math.cos(angleRad);
    const sin = Math.sin(angleRad);
    const dx = point[0] - center[0];
    const dy = point[1] - center[1];
    return [
        center[0] + dx * cos - dy * sin,
        center[1] + dx * sin + dy * cos
    ];
}

/**
 * 在 2D 数据坐标系中绕中心点旋转多边形
 * @param polygon 要旋转的多边形
 * @param center 旋转中心 [cx, cy]
 * @param angleRad 旋转角度（弧度，逆时针为正）
 * @returns 旋转后的多边形
 */
export function rotatePolygon2D(
    polygon: Polygon2D,
    center: Point2D,
    angleRad: number
): Polygon2D {
    return polygon.map(p => rotatePoint2D(p, center, angleRad));
}

/**
 * 旋转 2D 方向向量
 *
 * ⚠️ 角度语义：数据模型角（CCW+）
 *
 * @param facing 方向向量 [vx, vy]（单位向量）
 * @param angleRad 旋转角度（弧度，逆时针/CCW 为正）
 * @returns 旋转后的方向向量 [vx', vy']
 */
export function rotateFacing2D(facing: Point2D, angleRad: number): Point2D {
    const cos = Math.cos(angleRad);
    const sin = Math.sin(angleRad);
    return [
        facing[0] * cos - facing[1] * sin,
        facing[0] * sin + facing[1] * cos
    ];
}

// ============================================================
// 朝向 (Facing) 转换
// ============================================================

/**
 * 语义朝向到角度的映射（数据模型坐标系，逆时针为正）
 */
export const FACING_DIRECTIONS: Record<string, number> = {
    north: 0,
    northeast: Math.PI / 4,
    east: Math.PI / 2,
    southeast: (3 * Math.PI) / 4,
    south: Math.PI,
    southwest: (5 * Math.PI) / 4,
    west: (3 * Math.PI) / 2,
    northwest: (7 * Math.PI) / 4
};

export const DEFAULT_FACING_VALUE: Point2D = [0, 1];

/**
 * 语义朝向转换为单位向量
 * @param semantic 语义字符串，如 "north", "east" 等
 * @returns 单位向量 [vx, vy]
 */
export function semanticToVector(semantic: string): Point2D {
    const lower = semantic.toLowerCase();
    switch (lower) {
        case 'north': return [0, 1];
        case 'south': return [0, -1];
        case 'east': return [1, 0];
        case 'west': return [-1, 0];
        case 'northeast': return [Math.SQRT1_2, Math.SQRT1_2];
        case 'northwest': return [-Math.SQRT1_2, Math.SQRT1_2];
        case 'southeast': return [Math.SQRT1_2, -Math.SQRT1_2];
        case 'southwest': return [-Math.SQRT1_2, -Math.SQRT1_2];
        default: return DEFAULT_FACING_VALUE;
    }
}

/**
 * 归一化方向向量
 */
export function normalizeFacingValue(facing: Point2D): Point2D {
    const length = Math.hypot(facing[0], facing[1]);
    if (!Number.isFinite(length) || length < 1e-6) {
        return DEFAULT_FACING_VALUE;
    }

    return [facing[0] / length, facing[1] / length];
}

/**
 * 判断 facing.value 是否有效
 */
export function isValidFacingValue(value: Point2D | null | undefined): value is Point2D {
    if (!value || value.length < 2) return false;
    return Number.isFinite(value[0]) &&
        Number.isFinite(value[1]) &&
        Math.hypot(value[0], value[1]) >= 1e-6;
}

/**
 * 获取可渲染/可编辑的方向向量。
 * 读取阶段只消费 value，semantic 不参与常规逻辑。
 */
export function getFacingValue(facing: FacingData | null | undefined, context?: string): Point2D {
    if (isValidFacingValue(facing?.value)) {
        return normalizeFacingValue(facing.value);
    }

    if (context) {
        log.warn('invalid facing.value, fallback to north', { context });
    }

    return DEFAULT_FACING_VALUE;
}

/**
 * 构造规范 FacingData。
 */
export function createFacingData(value: Point2D | null, semantic: FacingData['semantic'] = null): FacingData {
    return {
        value: value ? normalizeFacingValue(value) : null,
        semantic
    };
}

/**
 * 将 FacingData 转换为弧度角度
 * @returns 弧度角度（以 north=0 为基准，逆时针为正）
 */
export function facingToAngle(facing: FacingData | null | undefined, context?: string): number {
    const value = getFacingValue(facing, context);
    return Math.atan2(value[0], value[1]);
}

/**
 * 将角度转换为单位方向向量
 * @param angleRad 弧度角度（以 north=0 为基准，逆时针为正）
 * @returns 单位向量 [vx, vy]
 */
export function angleToFacing(angleRad: number): Point2D {
    // north=[0,1] at 0°, east=[1,0] at 90°
    return [Math.sin(angleRad), Math.cos(angleRad)];
}
