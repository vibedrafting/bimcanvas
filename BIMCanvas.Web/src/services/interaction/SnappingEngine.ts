import * as THREE from 'three';

export interface SnapResult {
    position: THREE.Vector3;
    snapped: boolean;
    type: 'grid' | 'vertex' | 'edge' | 'none';
    guideLine?: { start: THREE.Vector3; end: THREE.Vector3 };
}

export class SnappingEngine {
    private gridSpacing: number = 100; // 100mm default
    private snapDistance: number = 200; // Snap within 200mm

    constructor() { }

    public snap(position: THREE.Vector3): SnapResult {
        // 1. Grid Snapping (快速计算)
        const snappedPos = position.clone();
        let snapped = false;
        let type: 'grid' | 'vertex' | 'edge' | 'none' = 'none';

        // Simple grid snap
        const gridX = Math.round(position.x / this.gridSpacing) * this.gridSpacing;
        const gridZ = Math.round(position.z / this.gridSpacing) * this.gridSpacing;

        if (Math.abs(gridX - position.x) < this.snapDistance) {
            snappedPos.x = gridX;
            snapped = true;
            type = 'grid';
        }
        if (Math.abs(gridZ - position.z) < this.snapDistance) {
            snappedPos.z = gridZ;
            snapped = true;
            type = 'grid';
        }

        // 2. Edge Snapping - 优化：限制检查对象数量，使用简单位置判断
        // 跳过边缘捕捉以提升性能（可选功能）
        // 如果需要边缘捕捉，可以通过缓存对象边界框来优化

        return {
            position: snappedPos,
            snapped: snapped,
            type: type
        };
    }
}
