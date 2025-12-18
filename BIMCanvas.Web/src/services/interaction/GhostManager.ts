import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

export class GhostManager {
    private scene: THREE.Scene;
    private ghostGroup: THREE.Group;
    private originalMaterials: Map<string, THREE.Material | THREE.Material[]> = new Map();
    private originalObject: THREE.Object3D | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.ghostGroup = new THREE.Group();
        this.scene.add(this.ghostGroup);
        this.ghostGroup.layers.enable(LayerManager.LAYER_MODEL);
    }

    public createGhost(original: THREE.Object3D) {
        this.clear();

        this.originalObject = original;

        // 1. Create a "Solid Clone" that will move with the mouse
        // This clone should look exactly like the original (solid)
        const solidClone = original.clone();

        // Ensure the clone is marked as ghost to avoid raycasting
        solidClone.userData.isGhost = true;
        solidClone.traverse((child) => {
            child.userData.isGhost = true;
        });

        this.ghostGroup.add(solidClone);

        // Reset position to 0,0,0 relative to group
        // The group will be moved by the tool
        solidClone.position.set(0, 0, 0);
        this.ghostGroup.position.set(0, 0, 0);


        // 2. Turn the "Original Object" into a Ghost (Dashed/Transparent)
        // Store original materials first
        this.originalMaterials.clear();
        original.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                this.originalMaterials.set(child.uuid, child.material);

                // Apply ghost material to original
                // Dashed lines are hard on meshes, so we use transparent material
                const ghostMaterial = new THREE.MeshBasicMaterial({
                    color: 0xaaaaaa,
                    transparent: true,
                    opacity: 0.3,
                    depthTest: true,
                    side: THREE.DoubleSide,
                    wireframe: true // Wireframe gives a "dashed-like" technical look
                });
                child.material = ghostMaterial;
            }
        });
    }

    public removeGhost() {
        this.clear();
    }

    private clear() {
        this.ghostGroup.clear();
        this.ghostGroup.position.set(0, 0, 0);
        this.ghostGroup.rotation.set(0, 0, 0);

        // Restore original object materials
        if (this.originalObject) {
            this.originalObject.traverse((child) => {
                if (child instanceof THREE.Mesh && this.originalMaterials.has(child.uuid)) {
                    child.material = this.originalMaterials.get(child.uuid)!;
                }
            });
            this.originalObject = null;
            this.originalMaterials.clear();
        }
    }

    public updateGhosts(ghosts: any[]) {
        // Placeholder
    }

    public setPositionOffset(offset: THREE.Vector3) {
        this.ghostGroup.position.copy(offset);
    }

    public setRotation(rotation: number) {
        this.ghostGroup.rotation.y = -rotation;
    }
}
