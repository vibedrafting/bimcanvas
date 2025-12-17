import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';

export class SelectionManager {
    private selectedObject: THREE.Object3D | null = null;
    private selectionBox: THREE.BoxHelper | null = null;
    private scene: THREE.Scene;
    private store = useCanvasStore();

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.store.debugMsg += `\nSelectionManager Created ${Date.now()}`;
    }

    public select(object: THREE.Object3D | null) {
        if (this.selectedObject === object) {
            this.store.debugMsg += ` | Skip Select ${object?.id}`;
            return;
        }

        this.store.debugMsg += ` | Select ${object?.id}`;
        this.clearSelection();

        if (object) {
            this.selectedObject = object;
            // Create selection visual
            this.selectionBox = new THREE.BoxHelper(object, 0x3b82f6); // Blue selection
            this.scene.add(this.selectionBox);

            // Update Store
            this.store.setSelectedObject(object);
        }
    }

    public clearSelection() {
        this.store.debugMsg += ` | Clear`;
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
