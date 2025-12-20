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
     * @param modules 场景中的所有模块
     * @param excludeIds 要排除的模块 ID（通常是当前选中的模块）
     */
    public buildSnapPoints(modules: any[], excludeIds: string[] = []): void {
        this.snapPoints = [];
        this.currentlySnappedTo = null;

        for (const m of modules) {
            // 排除自身
            if (excludeIds.includes(m.id)) continue;
            if (!m.bounds || m.bounds.length < 2) continue;

            const bounds = m.bounds as [number, number][];

            // 提取顶点
            for (let i = 0; i < bounds.length; i++) {
                const point = bounds[i];
                if (!point) continue;
                const [x, y] = point;
                this.snapPoints.push({
                    position: new THREE.Vector3(x, 0, -y),  // 模型坐标 → 世界坐标
                    type: 'vertex',
                    sourceId: m.id
                });

                // 提取边的中点
                const next = bounds[(i + 1) % bounds.length];
                if (!next) continue;
                this.snapPoints.push({
                    position: new THREE.Vector3(
                        (x + next[0]) / 2,
                        0,
                        -(y + next[1]) / 2
                    ),
                    type: 'midpoint',
                    sourceId: m.id
                });
            }
        }

        const debug = useDebugStore();
        debug.success(`[Snap] Built ${this.snapPoints.length} snap points from ${modules.length - excludeIds.length} modules`);
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
