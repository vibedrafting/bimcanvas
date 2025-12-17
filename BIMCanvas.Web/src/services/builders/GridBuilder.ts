import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

export class GridBuilder {
    private scene: THREE.Scene;
    private gridHelper: THREE.GridHelper | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildGrid(size: number = 20000, divisions: number = 20) {
        if (this.gridHelper) {
            this.scene.remove(this.gridHelper);
        }

        // High contrast grid for AI Vision View
        // Color 1: Center line color, Color 2: Grid line color
        this.gridHelper = new THREE.GridHelper(size, divisions, 0x444444, 0x222222);

        // Rotate to lie on XY plane (since we use Z-up logic)
        this.gridHelper.rotation.x = Math.PI / 2;

        // Assign to AI Layer only
        this.gridHelper.layers.set(LayerManager.LAYER_AI);

        this.scene.add(this.gridHelper);
    }
}
