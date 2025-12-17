import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

export class GhostManager {
    private scene: THREE.Scene;

    public setPositionOffset(offset: THREE.Vector3) {
        this.ghostGroup.position.copy(offset);
    }
}

