import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument } from '../../types/canvas';
import { themeService } from '../theme/ThemeService';

export class SemanticLineBuilder {
    private scene: THREE.Scene;
    private material: THREE.LineBasicMaterial;
    private lineGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        // 从 ThemeService 获取语义线配色
        const colors = themeService.currentTheme.value.semantic;
        this.material = new THREE.LineBasicMaterial({
            color: colors.line,
            linewidth: 2,
            depthTest: false // Ensure lines are always visible on top
        });
    }

    public buildLines(doc: CanvasDocument) {
        // Clear existing lines
        if (this.lineGroup) {
            this.scene.remove(this.lineGroup);
            this.lineGroup = null;
        }

        this.lineGroup = new THREE.Group();
        this.lineGroup.layers.set(LayerManager.LAYER_SEMANTIC);

        // 1. Wall Boundaries
        if (doc.walls) {
            doc.walls.forEach(wall => {
                const points = wall.polygon.map(p => new THREE.Vector3(p[0], p[1], 0));
                // Close the loop
                if (points.length > 0) {
                    points.push(points[0]!);
                }

                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);

                // Rotate to match coordinate system (X, Y) -> (X, 0, -Y)
                line.rotation.x = -Math.PI / 2;

                // Fix: Layers are not inherited by children in Three.js, must set explicitly
                line.layers.set(LayerManager.LAYER_SEMANTIC);

                this.lineGroup!.add(line);
            });
        }

        // 2. Column Boundaries
        if (doc.columns) {
            doc.columns.forEach(col => {
                const points = col.polygon.map(p => new THREE.Vector3(p[0], p[1], 0));
                if (points.length > 0) points.push(points[0]!);
                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);
                line.rotation.x = -Math.PI / 2;

                // Fix: Set layer explicitly
                line.layers.set(LayerManager.LAYER_SEMANTIC);

                this.lineGroup!.add(line);
            });
        }

        // 3. Module Boundaries
        if (doc.modules) {
            doc.modules.forEach(mod => {
                const points = mod.bounds.map(p => new THREE.Vector3(p[0], p[1], 0));
                if (points.length > 0) points.push(points[0]!);
                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);
                line.rotation.x = -Math.PI / 2;

                // Fix: Set layer explicitly
                line.layers.set(LayerManager.LAYER_SEMANTIC);

                this.lineGroup!.add(line);
            });
        }

        this.scene.add(this.lineGroup);
    }
}

