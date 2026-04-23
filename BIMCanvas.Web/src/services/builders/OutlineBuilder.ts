import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { ProjectData } from '../../types/canvas';
import { canvasStyleService } from '../canvas/CanvasStyleService';

/**
 * OutlineBuilder - 边界描边构建器
 * 为墙体、柱子、家具模块绘制 2D 轮廓线
 */
export class OutlineBuilder {
    private scene: THREE.Scene;
    private material: THREE.LineBasicMaterial;
    private lineGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        const style = canvasStyleService.currentStyle.value.layers.outline;
        this.material = new THREE.LineBasicMaterial({
            color: style.color,
            linewidth: style.lineWidth,
            depthTest: style.depthTest // Ensure lines are always visible on top
        });
    }

    public clearLines() {
        if (this.lineGroup) {
            this.scene.remove(this.lineGroup);
            this.lineGroup = null;
        }
    }

    public buildLines(data: ProjectData) {
        // Clear existing lines
        if (this.lineGroup) {
            this.scene.remove(this.lineGroup);
            this.lineGroup = null;
        }

        this.lineGroup = new THREE.Group();
        this.lineGroup.layers.set(LayerManager.LAYER_OUTLINE);

        // 从 baseline 子结构获取建筑构件数据
        const baseline = data.baseline;

        // 1. Wall Boundaries
        if (baseline?.walls) {
            baseline.walls.forEach(wall => {
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
                line.layers.set(LayerManager.LAYER_OUTLINE);

                this.lineGroup!.add(line);
            });
        }

        // 2. Column Boundaries
        if (baseline?.columns) {
            baseline.columns.forEach(col => {
                const points = col.polygon.map(p => new THREE.Vector3(p[0], p[1], 0));
                if (points.length > 0) points.push(points[0]!);
                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);
                line.rotation.x = -Math.PI / 2;

                // Fix: Set layer explicitly
                line.layers.set(LayerManager.LAYER_OUTLINE);

                this.lineGroup!.add(line);
            });
        }

        // 3. Module Boundaries
        if (data.activeScheme?.modules) {
            data.activeScheme.modules.forEach(mod => {
                if (!mod.bounds) return  // 兜底：Load 质检应已隔离，此处防止意外崩溃
                const points = mod.bounds.map(p => new THREE.Vector3(p[0], p[1], 0));
                if (points.length > 0) points.push(points[0]!);
                const geometry = new THREE.BufferGeometry().setFromPoints(points);
                const line = new THREE.Line(geometry, this.material);
                line.rotation.x = -Math.PI / 2;

                // Fix: Set layer explicitly
                line.layers.set(LayerManager.LAYER_OUTLINE);

                this.lineGroup!.add(line);
            });
        }

        this.scene.add(this.lineGroup);
    }
}
