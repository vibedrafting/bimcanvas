import * as THREE from 'three';

export interface ValidationResult {
    isValid: boolean;
    errors: string[];
}

export class ConstraintService {
    private scene: THREE.Scene;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public validate(object: THREE.Object3D): ValidationResult {
        const errors: string[] = [];

        // 1. Collision Detection
        if (this.checkCollisions(object)) {
            errors.push('Collision detected with another object.');
        }

        return {
            isValid: errors.length === 0,
            errors
        };
    }

    private checkCollisions(object: THREE.Object3D): boolean {
        const objectBox = new THREE.Box3().setFromObject(object);

        // Shrink box slightly to avoid touching contacts
        objectBox.expandByScalar(-1);

        for (const child of this.scene.children) {
            if (child === object) continue;
            if (child.userData.isGhost) continue; // Ignore ghosts
            if (!(child instanceof THREE.Mesh)) continue;

            // Ignore floor or other non-collidable objects if tagged
            // For now, we assume all meshes are collidable except floor (which we removed)

            const childBox = new THREE.Box3().setFromObject(child);
            if (objectBox.intersectsBox(childBox)) {
                return true;
            }
        }
        return false;
    }
}
