import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';

export class SelectionManager {
    private selectedObject: THREE.Object3D | null = null;
    private selectionBox: THREE.BoxHelper | null = null;
    private scene: THREE.Scene;
    private store = useCanvasStore();

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public select(object: THREE.Object3D | null) {
        if (this.selectedObject === object) return;

        this.clearSelection();

        if (object) {
            this.selectedObject = object;
            // Create selection visual
            this.selectionBox = new THREE.BoxHelper(object, 0x3b82f6); // Blue selection
            this.scene.add(this.selectionBox);

            // Update Store
            this.store.setSelectedObject(object.userData);
        }
    }

    public clearSelection() {
        if (this.selectionBox) {
            this.scene.remove(this.selectionBox);
            this.selectionBox = null;
        }
        this.selectedObject = null;
        this.store.setSelectedObject(null);
    }

    public getSelected(): THREE.Object3D | null {
        return this.selectedObject;
    }
}
