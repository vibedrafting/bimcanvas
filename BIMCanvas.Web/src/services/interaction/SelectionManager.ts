import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';
import { watch } from 'vue';

export class SelectionManager {
    private selectedObject: THREE.Object3D | null = null;
    private selectionBox: THREE.BoxHelper | null = null;
    private scene: THREE.Scene;
    private store = useCanvasStore();

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.store.debugMsg += `\nSelectionManager Created ${Date.now()}`;

        // Sync with store
        watch(() => this.store.selectedObject, (newVal) => {
            if (newVal === null) {
                if (this.selectedObject !== null) {
                    this.clearSelection();
                }
            } else {
                // If store has an object, but we don't (or different one), we should select it visually.
                // However, store.selectedObject is data (JSON), not Mesh.
                // We need to find the Mesh by ID.
                if (!this.selectedObject || this.selectedObject.userData.id !== newVal.id) {
                    const object = this.findObjectById(newVal.id);
                    if (object) {
                        this.select(object);
                    }
                }
            }
        });
    }

    private findObjectById(id: string): THREE.Object3D | null {
        let found: THREE.Object3D | null = null;
        this.scene.traverse((child) => {
            if (child.userData && child.userData.id === id) {
                found = child;
            }
        });
        return found;
    }

    public select(object: THREE.Object3D | null) {
        if (this.selectedObject === object) {
            this.store.debugMsg += ` | Skip Select ${object?.id}`;
            return;
        }

        this.store.debugMsg += ` | Select ${object?.id}`;
        this.clearSelection(); // Clear previous visual

        if (object) {
            this.selectedObject = object;
            // Create selection visual
            this.selectionBox = new THREE.BoxHelper(object, 0x3b82f6); // Blue selection
            this.scene.add(this.selectionBox);

            // Update Store (only if different)
            // Note: This might trigger watcher, but watcher has check.
            // However, store object is data, we have mesh.
            // We should check if store already has this ID.
            if (this.store.selectedObject?.id !== object.userData.id) {
                this.store.setSelectedObject(object.userData.data || { id: object.userData.id, type: object.userData.type });
            }
        }
    }

    public clearSelection() {
        this.store.debugMsg += ` | Clear`;
        if (this.selectionBox) {
            this.scene.remove(this.selectionBox);
            this.selectionBox = null;
        }
        this.selectedObject = null;

        // Update Store
        if (this.store.selectedObject !== null) {
            this.store.setSelectedObject(null);
        }
    }

    public getSelected(): THREE.Object3D | null {
        return this.selectedObject;
    }
}
