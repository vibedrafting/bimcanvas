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
        this.gridHelper = new THREE.GridHelper(size, divisions, 0x444444, 0x222222);
        this.gridHelper.rotation.x = Math.PI / 2; // Rotate to lie on XY plane (since we use Z-up logic but Three is Y-up, wait... Three is Y-up, our logic is Z-up? No, Architecture doc says Y-up. Let's check SceneBuilder.)

        // SceneBuilder uses:
        // Wall Extrude depth = height (Z)
        // Floor position z = -10
        // Camera lookAt(0,0,0) from (0,0,10000)
        // So Z is Up.

        this.gridHelper.rotation.x = Math.PI / 2; // GridHelper is XZ by default. We need XY.

        // Assign to AI Layer only
        this.gridHelper.layers.set(LayerManager.LAYER_AI);
        this.gridHelper.layers.enable(LayerManager.LAYER_DEFAULT); // Keep it visible in default for debugging? No, strictly AI.
        this.gridHelper.layers.disable(LayerManager.LAYER_DEFAULT);
        this.gridHelper.layers.enable(LayerManager.LAYER_AI);

        this.scene.add(this.gridHelper);
    }
}
