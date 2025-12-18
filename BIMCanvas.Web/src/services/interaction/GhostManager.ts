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

        // 1. Create a "Solid Clone"
        const solidClone = original.clone();

        // Ensure the clone is marked as ghost
        solidClone.userData.isGhost = true;
        solidClone.traverse((child) => {
            child.userData.isGhost = true;
        });

        this.ghostGroup.add(solidClone);

        // Initially, we want the clone to be at the same world position as the original.
        // If the group is at (0,0,0), the clone should be at original.position.
        // However, for pivoting, we will move the group to the pivot point.
        // So we need to set the clone's local position relative to the group.
        // For now, just copy position.
        solidClone.position.copy(original.position);
        solidClone.rotation.copy(original.rotation);
        solidClone.scale.copy(original.scale);

        this.ghostGroup.position.set(0, 0, 0);
        this.ghostGroup.rotation.set(0, 0, 0);


        // 2. Turn the "Original Object" into a Ghost
        this.originalMaterials.clear();
        original.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                this.originalMaterials.set(child.uuid, child.material);
                const ghostMaterial = new THREE.MeshBasicMaterial({
                    color: 0xaaaaaa,
                    transparent: true,
                    opacity: 0.3,
                    depthTest: true,
                    side: THREE.DoubleSide,
                    wireframe: true
                });
                child.material = ghostMaterial;
            }
        });
    }

    public setPivot(pivot: THREE.Vector3) {
        // To rotate around a pivot:
        // 1. Move the Group to the Pivot Point.
        // 2. Adjust the Children's positions so they stay visually in the same place.
        //    ChildLocal = ChildWorld - PivotWorld

        this.ghostGroup.position.copy(pivot);

        this.ghostGroup.children.forEach(child => {
            if (this.originalObject) {
                // Calculate original world position
                // We assume originalObject hasn't moved since createGhost
                const originalWorldPos = this.originalObject.position.clone();

                // New local position = OriginalWorld - Pivot
                child.position.subVectors(originalWorldPos, pivot);
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

    public updateGhosts(ghosts: any[]) { }

    public setPositionOffset(offset: THREE.Vector3) {
        // For Move Tool: offset is delta
        // If we use setPivot logic, this might need adjustment.
        // But MoveTool uses setPositionOffset(delta) where delta = dest - base.
        // If we want to move the group:
        // GroupPos = OriginalPos + Delta?
        // Let's keep it simple for MoveTool:
        // MoveTool sets Group to (0,0,0) initially (in createGhost logic above, we set Group to 0,0,0 and Child to OriginalPos).
        // So moving Group by Delta moves Child by Delta.
        this.ghostGroup.position.copy(offset);
    }

    public setRotation(rotation: number) {
        // Rotate around Y axis (up)
        // Since Group is at Pivot, rotating Group rotates Child around Pivot.
        // Positive Y rotation is CCW from Top View.
        // CCW Gesture -> Negative Delta.
        // We want CCW Preview -> Positive Y-Rot.
        // So we must Negate the Negative Delta.
        this.ghostGroup.rotation.y = -rotation;
    }
}
