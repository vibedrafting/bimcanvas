import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument } from '../../types/canvas';

export class SemanticLineBuilder {
    private scene: THREE.Scene;
    private material: THREE.LineBasicMaterial;
    private lineGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.material = new THREE.LineBasicMaterial({
            color: 0x00ff00, // Bright Green for AI visibility
            linewidth: 2
        });
    }

    public buildLines(doc: CanvasDocument) {
        // Clear existing lines
        if (this.lineGroup) {
            this.scene.remove(this.lineGroup);
            this.lineGroup = null;
        }

        this.lineGroup = new THREE.Group();
        this.lineGroup.layers.set(LayerManager.LAYER_AI);

        // 1. Wall Boundaries
        if (doc.walls) {
            doc.walls.forEach(wall => {
                const points = wall.polygon.map(p => new THREE.Vector3(p[0], p[1], 0));
                // Close the loop
                if (points.length > 0) {
                    points.push(points[0]);
                }

                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);

                // Rotate to match coordinate system (X, Y) -> (X, 0, -Y)
                line.rotation.x = -Math.PI / 2;

                this.lineGroup!.add(line);
            });
        }

        // 2. Column Boundaries
        if (doc.columns) {
            doc.columns.forEach(col => {
                const points = col.polygon.map(p => new THREE.Vector3(p[0], p[1], 0));
                if (points.length > 0) points.push(points[0]);
                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);
                line.rotation.x = -Math.PI / 2;
                this.lineGroup!.add(line);
            });
        }

        this.scene.add(this.lineGroup);
    }
}
