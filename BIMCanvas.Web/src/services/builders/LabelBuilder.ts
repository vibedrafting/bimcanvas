import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument } from '../../types/canvas';

export class LabelBuilder {
    private scene: THREE.Scene;
    private labelGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildLabels(doc: CanvasDocument) {
        if (this.labelGroup) {
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }

        this.labelGroup = new THREE.Group();
        // Important: Set the group to AI layer so it's only visible in AI mode
        // Note: CSS2DObjects don't strictly respect layers in the same way as meshes in some versions,
        // but we can manage their visibility or the renderer's DOM element visibility.
        // However, standard practice is to toggle the CSS2DObject's layers or visibility.
        // Let's try setting layers on the group first.
        this.labelGroup.layers.set(LayerManager.LAYER_AI);

        // 1. Walls
        if (doc.walls) {
            doc.walls.forEach(wall => {
                if (wall.id && wall.polygon && wall.polygon.length > 0) {
                    // Calculate center
                    const center = this.getPolygonCenter(wall.polygon);
                    this.createLabel(wall.id, center, 'Wall');
                }
            });
        }

        // 2. Columns
        if (doc.columns) {
            doc.columns.forEach(col => {
                if (col.id && col.polygon && col.polygon.length > 0) {
                    const center = this.getPolygonCenter(col.polygon);
                    this.createLabel(col.id, center, 'Col');
                }
            });
        }

        // 3. Modules
        if (doc.modules) {
            doc.modules.forEach(mod => {
                if (mod.id && mod.bounds && mod.bounds.length > 0) {
                    const center = this.getPolygonCenter(mod.bounds);
                    this.createLabel(mod.id, center, 'Mod');
                }
            });
        }

        this.scene.add(this.labelGroup);
    }

    private createLabel(id: string, position: THREE.Vector3, prefix: string) {
        const div = document.createElement('div');
        div.className = 'ai-label';
        div.textContent = `${prefix}:${id.substring(0, 4)}`;
        div.style.backgroundColor = 'rgba(0, 0, 0, 0.8)';
        div.style.color = '#00ff00';
        div.style.padding = '2px 4px';
        div.style.borderRadius = '4px';
        div.style.fontSize = '10px';
        div.style.fontFamily = 'monospace';
        div.style.pointerEvents = 'none'; // Crucial for clicking through
        div.style.border = '1px solid #00ff00';

        const label = new CSS2DObject(div);
        label.position.copy(position);

        // CSS2DObject doesn't inherit layers from parent group automatically in all cases,
        // but we will manage visibility via the group or renderer.
        // Actually, CSS2DRenderer renders everything in the scene that is a CSS2DObject.
        // We need to manually hide them if not in AI mode.
        // We'll handle this by putting them in a group and toggling the group's visibility or layers.
        // CSS2DObject.layers DOES work if the renderer camera checks layers.
        label.layers.set(LayerManager.LAYER_AI);

        this.labelGroup!.add(label);
    }

    private getPolygonCenter(polygon: number[][]): THREE.Vector3 {
        let x = 0, y = 0;
        polygon.forEach(p => {
            x += p[0];
            y += p[1];
        });
        const centerX = x / polygon.length;
        const centerY = y / polygon.length;

        // Convert to 3D coordinates (X, 0, -Y)
        return new THREE.Vector3(centerX, 0, -centerY); // Height 0? Or maybe higher?
        // Let's put it slightly above the floor/objects.
        // Walls are 2800 high. Let's put it at 3000.
        // return new THREE.Vector3(centerX, 3000, -centerY);
    }
}
