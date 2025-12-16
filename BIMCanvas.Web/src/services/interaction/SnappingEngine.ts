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

    public snap(position: THREE.Vector3, objects: THREE.Object3D[] = []): SnapResult {
        // 1. Grid Snapping
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

        // 2. Edge Snapping (Simple AABB center alignment for now)
        // Check against other objects
        if (objects.length > 0) {
            const snapThreshold = 200; // 200mm
            for (const obj of objects) {
                if (obj.userData.isGhost) continue;

                // Get object center (simplified)
                const box = new THREE.Box3().setFromObject(obj);
                const center = new THREE.Vector3();
                box.getCenter(center);

                // Snap X
                if (Math.abs(center.x - position.x) < snapThreshold) {
                    snappedPos.x = center.x;
                    snapped = true;
                    type = 'edge'; // Center alignment
                }
                // Snap Z
                if (Math.abs(center.z - position.z) < snapThreshold) {
                    snappedPos.z = center.z;
                    snapped = true;
                    type = 'edge';
                }
            }
        }

        return {
            position: snappedPos,
            snapped: snapped,
            type: type
        };
    }
}
