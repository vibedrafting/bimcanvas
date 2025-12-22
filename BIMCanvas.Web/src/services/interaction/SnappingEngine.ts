import * as THREE from 'three';
import { useDebugStore } from '../../stores/debugStore';

export interface SnapResult {
    position: THREE.Vector3;
    snapped: boolean;
    type: 'grid' | 'vertex' | 'midpoint' | 'edge' | 'none';
    guideLine?: { start: THREE.Vector3; end: THREE.Vector3 };
}

// 吸附点数据结构
interface SnapPoint {
    position: THREE.Vector3;
    type: 'vertex' | 'midpoint';
    sourceId: string;
}

export class SnappingEngine {
    private gridSpacing: number = 100; // 100mm default
    private snapDistance: number = 200; // Snap within 200mm (网格吸附)

    // Revit-Lite: 构件吸附参数
    private snapInThreshold: number = 300;   // 吸入阈值 300mm
    private snapOutThreshold: number = 400;  // 吸出阈值 400mm（阻尼效果）
    private currentlySnappedTo: THREE.Vector3 | null = null;  // 当前吸附点
    private snapPoints: SnapPoint[] = [];  // 缓存的吸附点

    constructor() { }

    /**
     * 构建吸附点索引（在工具 activate 时调用一次）
     * @param document 场景文档对象
     * @param excludeIds 要排除的 ID（通常是当前选中的模块）
     */
    public buildSnapPoints(document: any, excludeIds: string[] = []): void {
        this.snapPoints = [];
        this.currentlySnappedTo = null;

        // 提取多边形的顶点和边中点
        const extractFromPolygon = (polygon: [number, number][], sourceId: string) => {
            if (!polygon || polygon.length < 2) return;

            for (let i = 0; i < polygon.length; i++) {
                const point = polygon[i];
                if (!point) continue;
                const [x, y] = point;

                // 顶点
                this.snapPoints.push({
                    position: new THREE.Vector3(x, 0, -y),  // 模型坐标 → 世界坐标
                    type: 'vertex',
                    sourceId
                });

                // 边的中点
                const next = polygon[(i + 1) % polygon.length];
                if (!next) continue;
                this.snapPoints.push({
                    position: new THREE.Vector3(
                        (x + next[0]) / 2,
                        0,
                        -(y + next[1]) / 2
                    ),
                    type: 'midpoint',
                    sourceId
                });
            }
        };

        // 提取线段的端点和中点
        const extractFromLine = (line: [number, number][], sourceId: string) => {
            if (!line || line.length < 2) return;
            const [p1, p2] = line;
            if (!p1 || !p2) return;

            // 两个端点
            this.snapPoints.push({
                position: new THREE.Vector3(p1[0], 0, -p1[1]),
                type: 'vertex',
                sourceId
            });
            this.snapPoints.push({
                position: new THREE.Vector3(p2[0], 0, -p2[1]),
                type: 'vertex',
                sourceId
            });

            // 中点
            this.snapPoints.push({
                position: new THREE.Vector3(
                    (p1[0] + p2[0]) / 2,
                    0,
                    -(p1[1] + p2[1]) / 2
                ),
                type: 'midpoint',
                sourceId
            });
        };

        let moduleCount = 0;
        let wallCount = 0;
        let columnCount = 0;
        let openingCount = 0;

        // 1. 家具模块 Modules（排除自身）
        if (document?.modules) {
            for (const m of document.modules) {
                if (excludeIds.includes(m.id)) continue;
                if (!m.bounds) continue;
                extractFromPolygon(m.bounds, m.id);
                moduleCount++;
            }
        }

        // 2. 墙体 Walls（从 revit 子结构获取）
        if (document?.revit?.walls) {
            for (const wall of document.revit.walls) {
                if (!wall.polygon) continue;
                extractFromPolygon(wall.polygon, wall.id);
                wallCount++;
            }
        }

        // 3. 柱子 Columns（从 revit 子结构获取）
        if (document?.revit?.columns) {
            for (const col of document.revit.columns) {
                if (!col.polygon) continue;
                extractFromPolygon(col.polygon, col.id);
                columnCount++;
            }
        }

        // 4. 门窗 Openings（从 revit 子结构获取）
        if (document?.revit?.openings) {
            for (const opening of document.revit.openings) {
                if (!opening.line) continue;
                extractFromLine(opening.line, opening.id);
                openingCount++;
            }
        }

        const debug = useDebugStore();
        debug.success(`[Snap] Built ${this.snapPoints.length} snap points from: ${moduleCount} modules, ${wallCount} walls, ${columnCount} columns, ${openingCount} openings`);
    }

    /**
     * 清理吸附点缓存（在工具 deactivate 时调用）
     */
    public clear(): void {
        this.snapPoints = [];
        this.currentlySnappedTo = null;
    }

    public snap(position: THREE.Vector3): SnapResult {
        const result: SnapResult = {
            position: position.clone(),
            snapped: false,
            type: 'none'
        };

        // ===== 1. 构件吸附（优先级最高）=====
        if (this.snapPoints.length > 0) {
            let bestDist = Infinity;
            let bestPoint: SnapPoint | null = null;

            for (const sp of this.snapPoints) {
                const dist = position.distanceTo(sp.position);
                if (dist < bestDist) {
                    bestDist = dist;
                    bestPoint = sp;
                }
            }

            // 阻尼逻辑：如果已吸附到某点，使用更大的吸出阈值
            const isCurrentlySnapped = this.currentlySnappedTo !== null;
            const threshold = isCurrentlySnapped ? this.snapOutThreshold : this.snapInThreshold;

            if (bestPoint && bestDist < threshold) {
                const debug = useDebugStore();
                debug.log(`[Snap] → ${bestPoint.type} at (${bestPoint.position.x.toFixed(0)}, ${bestPoint.position.z.toFixed(0)}), dist=${bestDist.toFixed(0)}mm`);
                result.position.copy(bestPoint.position);
                result.snapped = true;
                result.type = bestPoint.type;
                this.currentlySnappedTo = bestPoint.position.clone();
                return result;
            }

            // 如果脱离了吸附范围，清除状态
            this.currentlySnappedTo = null;
        }

        // ===== 2. 网格吸附（后备）=====
        const gridX = Math.round(position.x / this.gridSpacing) * this.gridSpacing;
        const gridZ = Math.round(position.z / this.gridSpacing) * this.gridSpacing;

        if (Math.abs(gridX - position.x) < this.snapDistance) {
            result.position.x = gridX;
            result.snapped = true;
            result.type = 'grid';
        }
        if (Math.abs(gridZ - position.z) < this.snapDistance) {
            result.position.z = gridZ;
            result.snapped = true;
            result.type = 'grid';
        }

        return result;
    }
}
