import * as THREE from 'three';

export class GhostManager {
    private scene: THREE.Scene;
    private ghostObject: THREE.Object3D | null = null;
    private ghostMaterial: THREE.Material;

    constructor(scene: THREE.Scene) {
        this.scene = scene;

        // Create a shared transparent material for ghosts
        this.ghostMaterial = new THREE.MeshBasicMaterial({
            color: 0x4080ff, // Light Blue
            transparent: true,
            opacity: 0.5,
            depthTest: false, // Always visible on top
            depthWrite: false
        });
    }

    public createGhost(originalObject: THREE.Object3D) {
        if (this.ghostObject) {
            this.removeGhost();
        }

        // Clone the object at its current position (Start of drag)
        this.ghostObject = originalObject.clone();
        this.ghostObject.position.copy(originalObject.position);
        this.ghostObject.rotation.copy(originalObject.rotation);
        this.ghostObject.scale.copy(originalObject.scale);

        this.ghostObject.userData.isGhost = true; // Mark as ghost

        // Apply ghost material to all meshes in the clone
        this.ghostObject.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                child.material = this.ghostMaterial;
                child.userData.isGhost = true; // Mark children too
            }
        });

        // Add to scene
        this.scene.add(this.ghostObject);
    }

    public updatePosition(position: THREE.Vector3) {
        // Static Ghost: Do not update position. 
        // The ghost represents the original position.
    }

    public removeGhost() {
        if (this.ghostObject) {
            this.scene.remove(this.ghostObject);
            // We don't dispose the geometry as it's shared with the original, 
            // but we should check if we cloned it deeply. 
            // THREE.Object3D.clone() shares geometry by default.
            this.ghostObject = null;
        }
    }

    public get hasGhost(): boolean {
        return this.ghostObject !== null;
    }
}
