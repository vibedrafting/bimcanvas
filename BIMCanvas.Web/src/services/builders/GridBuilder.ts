
import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

export class GridBuilder {
    private scene: THREE.Scene;
    private gridHelper: THREE.GridHelper | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildGrid(size: number = 100000, divisions: number = 100) {
        if (this.gridHelper) {
            this.scene.remove(this.gridHelper);
            this.gridHelper.dispose();
            this.gridHelper = null;
        }

        // High contrast for AI Vision
        const color1 = 0x888888; // Center lines (Bright Gray)
        const color2 = 0x333333; // Grid lines (Dark Gray but visible)

        this.gridHelper = new THREE.GridHelper(size, divisions, color1, color2);

        // Rotate to match Y-up coordinate system (Grid is XZ plane)
        // But our camera looks down Y, so GridHelper in XZ is actually correct for "floor"
        // However, we need to check if we rotated everything else.
        // SceneBuilder rotates walls -Math.PI/2 on X. So walls are in XZ plane.
        // GridHelper is in XZ plane by default. So NO rotation needed?
        // Wait, SceneBuilder: mesh.rotation.x = -Math.PI / 2;
        // This puts the wall (extruded in Z) onto the XZ plane.
        // So GridHelper (XZ) is correct.

        // Assign to GRID Layer
        this.gridHelper.layers.set(LayerManager.LAYER_GRID);

        this.scene.add(this.gridHelper);
    }
}
