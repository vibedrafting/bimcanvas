import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import type { ProjectData, Point2D, Line2D, Polygon2D } from '../../types/canvas';
import { themeService } from '../theme/ThemeService';
import { polygonCenterToWorld, lineCenterToWorld } from '../../utils/coordinates';

export class LabelBuilder {
    private scene: THREE.Scene;
    private labelGroup: THREE.Group | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    /**
     * 清理标签资源（主题切换时调用）
     */
    public cleanup() {
        if (this.labelGroup) {
            // 清理 CSS2DObject DOM 元素
            this.labelGroup.children.forEach(child => {
                if ((child as any).element && (child as any).element.parentNode) {
                    (child as any).element.parentNode.removeChild((child as any).element);
                }
            });
            this.labelGroup.clear();
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }
    }

    public buildLabels(data: ProjectData) {
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

        // 从 baseline 子结构获取建筑构件数据
        const baseline = data.baseline;

        // 1. Walls
        if (baseline?.walls) {
            baseline.walls.forEach(wall => {
                if (wall.id && wall.polygon && wall.polygon.length > 0) {
                    // Calculate center
                    const center = this.getPolygonCenter(wall.polygon);
                    const orientation = this.getOrientation(wall.polygon);
                    this.createLabel(wall.id, center, orientation);
                }
            });
        }

        // 2. Columns
        if (baseline?.columns) {
            baseline.columns.forEach(col => {
                if (col.id && col.polygon && col.polygon.length > 0) {
                    const center = this.getPolygonCenter(col.polygon);
                    const orientation = this.getOrientation(col.polygon);
                    this.createLabel(col.id, center, orientation);
                }
            });
        }

        // 3. Modules
        if (data.activeScheme?.modules) {
            data.activeScheme.modules.forEach(mod => {
                if (mod.id && mod.bounds && mod.bounds.length > 0) {
                    const center = this.getPolygonCenter(mod.bounds);
                    const orientation = this.getOrientation(mod.bounds);
                    this.createLabel(mod.id, center, orientation);
                }
            });
        }

        // 4. Openings (Doors/Windows)
        if (baseline?.openings) {
            baseline.openings.forEach(opening => {
                if (opening.id && opening.line) {
                    const center = this.getLineCenter(opening.line);
                    const orientation = this.getOrientation(opening.line);
                    this.createLabel(opening.id, center, orientation);
                }
            });
        }

        // 5. Room Zones - from computed/room_zones.json
        if (data.computed?.roomZones) {
            data.computed.roomZones.forEach(zone => {
                if (zone.type === 1 && zone.id) {
                    const boundary = zone.computedBoundary ?? zone.rawBoundary;
                    if (boundary && boundary.length > 0) {
                        const center = this.getPolygonCenter(boundary);
                        const orientation = this.getOrientation(boundary);
                        this.createLabel(zone.id, center, orientation, true);
                    }
                }
            });
        }

        // 6. Exclusion Zones - from computed/exclusions.json
        if (data.computed?.exclusions) {
            data.computed.exclusions.forEach(exclusion => {
                if (exclusion.id) {
                    const boundary = exclusion.computedBoundary ?? exclusion.rawBoundary;
                    if (boundary && boundary.length > 0) {
                        const center = this.getPolygonCenter(boundary);
                        const orientation = this.getOrientation(boundary);
                        this.createLabel(exclusion.id, center, orientation, true);
                    }
                }
            });
        }

        this.scene.add(this.labelGroup);
    }

    private createLabel(id: string, position: THREE.Vector3, orientation: 'horizontal' | 'vertical', isZoneLabel: boolean = false) {

        // 从 ThemeService 获取构件标签配色
        const config = themeService.currentTheme.value.componentLabel;

        const div = document.createElement('div');
        div.className = 'ai-label';
        // Simplify: Just show ID, maybe with a small prefix if needed, but user asked for ID emphasis.
        // Let's use "#" + last 4 chars for brevity, or full ID if short.
        // User example: "#m_12".
        // Let's assume ID is meaningful. If it's a UUID, take last 4.
        const shortId = id.length > 8 ? id.substring(0, 4) : id;
        div.textContent = `#${shortId}`;

        // 样式设置
        div.style.color = config.text;
        div.style.textShadow = config.shadow;

        // 只有当背景色有效且不为 transparent 时才应用背景和滤镜
        if (config.background && config.background !== 'transparent') {
            div.style.background = config.background;
            div.style.backdropFilter = 'blur(4px)'; // 仅在有背景色时应用毛玻璃
        } else {
            div.style.background = 'transparent';
            div.style.backdropFilter = 'none';
        }

        if (config.padding) div.style.padding = config.padding;
        if (config.borderRadius) div.style.borderRadius = config.borderRadius;

        div.style.fontSize = '10px';
        div.style.fontWeight = 'normal'; // 变细，减少视觉干扰
        div.style.fontFamily = 'monospace'; // 等宽字体利于 AI 识别
        div.style.pointerEvents = 'none'; // Crucial for clicking through

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

        if (isZoneLabel) {
            label.userData.isZoneLabel = true;
        }

        this.labelGroup!.add(label);
    }

    private getPolygonCenter(polygon: Polygon2D): THREE.Vector3 {
        return polygonCenterToWorld(polygon);
    }

    private getLineCenter(line: Line2D): THREE.Vector3 {
        return lineCenterToWorld(line);
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

    public updateZoneLabelVisibility(labelsVisible: boolean, zonesVisible: boolean) {
        if (!this.labelGroup) return;

        const shouldShowZoneLabels = labelsVisible && zonesVisible;

        this.labelGroup.children.forEach(child => {
            if (child.userData.isZoneLabel) {
                child.visible = shouldShowZoneLabels;
                // Also need to handle the DOM element visibility if CSS2DObject doesn't handle it automatically when parent is visible/hidden
                // CSS2DObject.visible does control the element display.
                // However, the parent labelGroup might be hidden if labelsVisible is false.
                // If labelsVisible is false, the whole group is hidden, so child.visible doesn't matter (it won't render).
                // If labelsVisible is true, group is visible.
                // Then we check zonesVisible. If zonesVisible is false, we hide zone labels.
                // So:
                // If labelsVisible=true, zonesVisible=true -> Group Visible, ZoneLabel Visible -> OK
                // If labelsVisible=true, zonesVisible=false -> Group Visible, ZoneLabel Hidden -> OK
                // If labelsVisible=false, zonesVisible=true -> Group Hidden -> OK
                // If labelsVisible=false, zonesVisible=false -> Group Hidden -> OK
            }
        });
    }
}
