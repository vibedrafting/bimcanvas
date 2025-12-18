import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

export class GhostManager {
    private scene: THREE.Scene;

    private ghostGroup: THREE.Group;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.ghostGroup = new THREE.Group();
        this.scene.add(this.ghostGroup);
        // Ensure ghosts are in default layer or specific layer
        this.ghostGroup.layers.enable(LayerManager.LAYER_MODEL);
    }

    public createGhost(original: THREE.Object3D) {
        this.ghostGroup.clear();

        const clone = original.clone();

        // Traverse and update materials to be transparent/ghostly
        clone.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                // Create a ghost material
                const ghostMaterial = new THREE.MeshBasicMaterial({
                    color: 0x4a9eff, // Blueish
                    transparent: true,
                    opacity: 0.5,
                    depthTest: false, // Always visible
                    side: THREE.DoubleSide
                });

                if (Array.isArray(child.material)) {
                    child.material = child.material.map(() => ghostMaterial);
                } else {
                    child.material = ghostMaterial;
                }

                // Mark as ghost to avoid raycasting/snapping
                child.userData.isGhost = true;
            }
        });

        // Ensure the clone itself is marked
        clone.userData.isGhost = true;

        this.ghostGroup.add(clone);
        // Reset position to 0,0,0 relative to group, as group handles offset
        clone.position.set(0, 0, 0);

        // But wait, the original object has a world position. 
        // If we add it to ghostGroup (which is at 0,0,0 initially), the clone keeps its local position.
        // If original was child of Scene, local = world.
        // We want the ghost to start at the original's position.

        // Actually, MoveTool sets position offset.
        // Let's ensure ghostGroup starts at 0 offset.
        this.ghostGroup.position.set(0, 0, 0);

        // If the original object is at (X, Y, Z), the clone will be at (X, Y, Z).
        // When we move ghostGroup by delta, the clone moves by delta.
        // This is correct.
    }

    public removeGhost() {
        this.ghostGroup.clear();
        this.ghostGroup.position.set(0, 0, 0);
    }

    public updateGhosts(ghosts: any[]) {
        // Placeholder for ghost update logic
        // Clear existing ghosts
        this.ghostGroup.clear();

        // Recreate ghosts based on input
        // For now, just log or do nothing if ghosts structure is not defined
    }

    public setPositionOffset(offset: THREE.Vector3) {
        this.ghostGroup.position.copy(offset);
    }

    public setRotation(rotation: number) {
        // Rotate around Y axis (up)
        this.ghostGroup.rotation.y = -rotation; // Invert for coordinate system match if needed
    }
}

