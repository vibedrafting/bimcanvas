
import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import { themeService } from '../theme/ThemeService';

export class GridBuilder {
    private scene: THREE.Scene;
    private gridHelper: THREE.GridHelper | null = null;
    private labelGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildGrid(size: number = 100000, divisions: number = 100) {
        this.cleanup();

        // 从 ThemeService 获取网格配色
        const colors = themeService.currentTheme.value.grid;
        const color1 = colors.centerLine; // 中心线
        const color2 = colors.gridLine;   // 网格线

        this.gridHelper = new THREE.GridHelper(size, divisions, color1, color2);

        // Assign to GRID Layer
        this.gridHelper.layers.set(LayerManager.LAYER_GRID);
        this.scene.add(this.gridHelper);

        // Build Coordinate Labels
        this.buildCoordinateLabels(size, divisions);
    }

    private cleanup() {
        if (this.gridHelper) {
            this.scene.remove(this.gridHelper);
            this.gridHelper.dispose();
            this.gridHelper = null;
        }

        if (this.labelGroup) {
            // Explicitly cleanup CSS2DObject DOM elements
            this.labelGroup.children.forEach(child => {
                if ((child as any).element && (child as any).element.parentNode) {
                    (child as any).element.parentNode.removeChild((child as any).element);
                }
            });
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }
    }

    private buildCoordinateLabels(size: number, divisions: number) {
        this.labelGroup = new THREE.Group();
        this.labelGroup.layers.set(LayerManager.LAYER_GRID);

        const step = size / divisions; // e.g. 1000mm

        // We want labels to be readable near the origin/axes.
        // Let's label the axes lines directly.

        const range = 30; // 30 lines each side
        const labelColor = themeService.currentTheme.value.label.text;

        for (let i = -range; i <= range; i++) {
            if (i === 0) continue; // Skip origin

            // X Axis Labels (along Z=0)
            // Place them slightly offset from the axis line to not overlap exactly
            // i * 1000 is the X position. Z is 0 (or slightly offset).
            this.createLabel(`${i}m`, new THREE.Vector3(i * 1000, 0, 200), labelColor);

            // Z Axis Labels (along X=0)
            // i * 1000 is the Z position. X is 0 (or slightly offset).
            this.createLabel(`${i}m`, new THREE.Vector3(200, 0, i * 1000), labelColor);
        }

        this.scene.add(this.labelGroup);
    }

    private createLabel(text: string, position: THREE.Vector3, color: string) {
        const div = document.createElement('div');
        div.className = 'grid-label';
        div.textContent = text;
        div.style.color = color;
        div.style.fontSize = '12px'; // Increased from 10px
        div.style.fontWeight = 'bold';
        div.style.fontFamily = 'monospace';
        div.style.opacity = '0.8'; // Increased from 0.6
        div.style.pointerEvents = 'none';
        div.style.textShadow = '0 0 3px rgba(0,0,0,0.5)'; // Add shadow for contrast against floor

        const label = new CSS2DObject(div);
        label.position.copy(position);
        label.layers.set(LayerManager.LAYER_GRID);
        this.labelGroup!.add(label);
    }
}
