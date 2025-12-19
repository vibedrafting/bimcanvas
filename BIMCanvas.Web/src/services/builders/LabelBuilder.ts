import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument, Point2D, Line2D, Polygon2D } from '../../types/canvas';
import { themeService } from '../theme/ThemeService';

export class LabelBuilder {
    private scene: THREE.Scene;
    private labelGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildLabels(doc: CanvasDocument) {
        if (this.labelGroup) {
            // Explicitly cleanup CSS2DObject DOM elements to prevent ghosts
            this.labelGroup.children.forEach(child => {
                // Check if it's a CSS2DObject (has element property)
                if ((child as any).element && (child as any).element.parentNode) {
                    (child as any).element.parentNode.removeChild((child as any).element);
                }
            });
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }

        this.labelGroup = new THREE.Group();
        // Important: Set the group to LABELS layer
        this.labelGroup.layers.set(LayerManager.LAYER_LABELS);

        // 1. Walls
        if (doc.walls) {
            doc.walls.forEach(wall => {
                if (wall.id && wall.polygon && wall.polygon.length > 0) {
                    // Calculate center
                    const center = this.getPolygonCenter(wall.polygon);
                    const orientation = this.getOrientation(wall.polygon);
                    this.createLabel(wall.id, center, orientation);
                }
            });
        }

        // 2. Columns
        if (doc.columns) {
            doc.columns.forEach(col => {
                if (col.id && col.polygon && col.polygon.length > 0) {
                    const center = this.getPolygonCenter(col.polygon);
                    const orientation = this.getOrientation(col.polygon);
                    this.createLabel(col.id, center, orientation);
                }
            });
        }

        // 3. Modules
        if (doc.modules) {
            doc.modules.forEach(mod => {
                if (mod.id && mod.bounds && mod.bounds.length > 0) {
                    const center = this.getPolygonCenter(mod.bounds);
                    const orientation = this.getOrientation(mod.bounds);
                    this.createLabel(mod.id, center, orientation);
                }
            });
        }

        // 4. Openings (Doors/Windows)
        if (doc.openings) {
            doc.openings.forEach(opening => {
                if (opening.id && opening.line) {
                    const center = this.getLineCenter(opening.line);
                    const orientation = this.getOrientation(opening.line);
                    this.createLabel(opening.id, center, orientation);
                }
            });
        }

        this.scene.add(this.labelGroup);
    }

    private createLabel(id: string, position: THREE.Vector3, orientation: 'horizontal' | 'vertical') {
        // 从 ThemeService 获取标签配色
        const colors = themeService.currentTheme.value.label;

        const div = document.createElement('div');
        div.className = 'ai-label';
        // Simplify: Just show ID, maybe with a small prefix if needed, but user asked for ID emphasis.
        // Let's use "#" + last 4 chars for brevity, or full ID if short.
        // User example: "#m_12".
        // Let's assume ID is meaningful. If it's a UUID, take last 4.
        const shortId = id.length > 8 ? id.substring(0, 4) : id;
        div.textContent = `#${shortId}`;

        // 不使用背景填充和边框，只用纯文字+阴影（明亮/暗黑模式通用）
        div.style.color = colors.text;
        div.style.fontSize = '10px';
        div.style.fontWeight = 'bold';
        div.style.fontFamily = 'monospace';
        div.style.pointerEvents = 'none'; // Crucial for clicking through
        // 使用文字阴影增强可读性
        div.style.textShadow = '0 1px 2px rgba(0,0,0,0.3), 0 0 4px rgba(255,255,255,0.2)';

        // Apply orientation
        if (orientation === 'vertical') {
            // Use writing-mode for vertical text (bottom-to-top)
            div.style.writingMode = 'vertical-rl';
            div.style.textOrientation = 'mixed';
            div.style.transform = 'rotate(180deg)'; // Flip to read bottom-to-top
        }

        const label = new CSS2DObject(div);
        label.position.copy(position);

        // Assign to LABELS Layer
        label.layers.set(LayerManager.LAYER_LABELS);

        this.labelGroup!.add(label);
    }

    private getPolygonCenter(polygon: Polygon2D): THREE.Vector3 {
        let x = 0, y = 0;
        polygon.forEach(p => {
            x += p[0];
            y += p[1];
        });
        const centerX = x / polygon.length;
        const centerY = y / polygon.length;

        // Convert to 3D coordinates (X, 0, -Y)
        return new THREE.Vector3(centerX, 0, -centerY);
    }

    private getLineCenter(line: Line2D): THREE.Vector3 {
        const p1 = line[0];
        const p2 = line[1];
        const centerX = (p1[0] + p2[0]) / 2;
        const centerY = (p1[1] + p2[1]) / 2;
        return new THREE.Vector3(centerX, 0, -centerY);
    }

    private getOrientation(points: Point2D[] | Line2D): 'horizontal' | 'vertical' {
        let minX = Infinity, maxX = -Infinity;
        let minY = Infinity, maxY = -Infinity;

        points.forEach(p => {
            if (p[0] < minX) minX = p[0];
            if (p[0] > maxX) maxX = p[0];
            if (p[1] < minY) minY = p[1];
            if (p[1] > maxY) maxY = p[1];
        });

        const width = maxX - minX;
        const height = maxY - minY;

        const orientation = width >= height ? 'horizontal' : 'vertical';

        // Debug log to verify orientation calculation
        console.log(`[LabelBuilder] AABB: w=${width.toFixed(0)}, h=${height.toFixed(0)} => ${orientation}`);

        return orientation;
    }
}
