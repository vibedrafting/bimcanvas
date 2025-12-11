import * as THREE from 'three';

export class GridSystem {
    private gridHelper: THREE.GridHelper;
    private scene: THREE.Scene;
    private gridSize: number;
    private divisions: number;

    constructor(scene: THREE.Scene, size: number = 20000, divisions: number = 200) {
        this.scene = scene;
        this.gridSize = size;
        this.divisions = divisions;

        // Create GridHelper
        // Color center line: Cyan (0x00ffff)
        // Color grid: Dark Gray (0x444444)
        this.gridHelper = new THREE.GridHelper(size, divisions, 0x00ffff, 0x222222);
        this.gridHelper.position.y = -1; // Slightly below zero to avoid z-fighting if floor is at 0
        this.gridHelper.rotation.x = Math.PI / 2; // Rotate to X-Y plane if using Z-up, but we are using Y-up in Three.js default?
        // Wait, Three.js GridHelper is on X-Z plane by default.
        // Our camera is looking down Z axis (Top view).
        // So our floor is the X-Y plane.
        // We need to rotate the grid to be on X-Y plane.
        this.gridHelper.rotation.x = Math.PI / 2;

        this.gridHelper.visible = false; // Initially hidden
        this.scene.add(this.gridHelper);
    }

    public setVisible(visible: boolean) {
        this.gridHelper.visible = visible;
    }

    public snapToGrid(position: THREE.Vector3): THREE.Vector3 {
        const step = this.gridSize / this.divisions;
        const snappedX = Math.round(position.x / step) * step;
        const snappedY = Math.round(position.y / step) * step;
        // Keep Z as is
        return new THREE.Vector3(snappedX, snappedY, position.z);
    }
}
