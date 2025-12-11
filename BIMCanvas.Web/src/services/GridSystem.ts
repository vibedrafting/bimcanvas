import * as THREE from 'three';

export class GridSystem {
    private gridHelper: THREE.GridHelper;
    private scene: THREE.Scene;
    private gridSize: number;
    private divisions: number;

    constructor(scene: THREE.Scene, size: number = 500000, divisions: number = 5000) {
        this.scene = scene;
        this.gridSize = size;
        this.divisions = divisions;

        // Create GridHelper
        // Color center line: Cyan (0x00ffff)
        // Color grid: Dark Gray (0x222222)
        this.gridHelper = new THREE.GridHelper(size, divisions, 0x00ffff, 0x222222);
        this.gridHelper.name = 'GridHelper'; // IMPORTANT: Prevent removal by SceneBuilder

        // Rotate to X-Y plane (GridHelper is X-Z by default)
        this.gridHelper.rotation.x = Math.PI / 2;

        // Position behind objects (Z-axis is depth in our top-down view)
        this.gridHelper.position.z = -10;
        this.gridHelper.position.y = 0;

        this.gridHelper.visible = false; // Initially hidden
        this.scene.add(this.gridHelper);

        console.log('GridSystem initialized', this.gridHelper);
    }

    public setVisible(visible: boolean) {
        this.gridHelper.visible = visible;
        console.log('Grid visibility set to:', visible);
    }

    public snapToGrid(position: THREE.Vector3, threshold: number = 10): THREE.Vector3 {
        const step = this.gridSize / this.divisions;

        const snapAxis = (val: number, step: number, threshold: number): number => {
            const snapped = Math.round(val / step) * step;
            if (Math.abs(val - snapped) <= threshold) {
                return snapped;
            }
            return val;
        };

        const x = snapAxis(position.x, step, threshold);
        const y = snapAxis(position.y, step, threshold);

        // Keep Z as is
        return new THREE.Vector3(x, y, position.z);
    }
}
