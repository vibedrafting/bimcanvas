import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument } from '../../types/canvas';

export class SemanticLineBuilder {
    private scene: THREE.Scene;
    private material: THREE.LineBasicMaterial;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.material = new THREE.LineBasicMaterial({
            color: 0x00ff00, // Bright Green for AI visibility
            linewidth: 2
        });
    }

    public buildLines(doc: CanvasDocument) {
        // Clear existing lines? (TODO: Manage lifecycle better)
    }
}
