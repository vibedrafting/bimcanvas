import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import type { ProjectData, Point2D, Line2D, Polygon2D } from '../../types/canvas';
import { canvasStyleService } from '../canvas/CanvasStyleService';
import { polygonCenterToWorld, lineCenterToWorld } from '../../utils/coordinates';
import { useCanvasStore } from '../../stores/canvasStore';

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
            // 清理 CSS2DObject DOM 元素和事件监听器
            this.labelGroup.children.forEach(child => {
                this.cleanupLabelElement(child);
            });
            this.labelGroup.clear();
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }
    }

    private cleanupLabelElement(child: THREE.Object3D) {
        const el = (child as any).element as HTMLElement | undefined;
        if (!el) return;
        // 移除 zone 标签的 click handler
        if (child.userData.clickHandler) {
            el.removeEventListener('click', child.userData.clickHandler);
            child.userData.clickHandler = null;
        }
        if (el.parentNode) {
            el.parentNode.removeChild(el);
        }
    }

    public buildLabels(data: ProjectData) {
        if (this.labelGroup) {
            // Explicitly cleanup CSS2DObject DOM elements and event listeners
            this.labelGroup.children.forEach(child => {
                this.cleanupLabelElement(child);
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
                if (!exclusion.visible) return;
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

        // 7. Scheme Zones (Designable) - subZones 显示名称标签
        if (data.activeScheme?.zones) {
            data.activeScheme.zones.forEach(zone => {
                if (zone.subZones && zone.subZones.length > 0) {
                    // 容器 zone 有子分区时，为每个子分区创建标签
                    zone.subZones.forEach(subZone => {
                        const boundary = subZone.computedBoundary ?? subZone.rawBoundary;
                        if (boundary && boundary.length > 0) {
                            const center = this.getPolygonCenter(boundary);
                            const orientation = this.getOrientation(boundary);
                            this.createLabel(subZone.id, center, orientation, true);
                        }
                    });
                } else if (zone.type === 2 && zone.id) {
                    // 叶子 Designable zone
                    const boundary = zone.computedBoundary ?? zone.rawBoundary;
                    if (boundary && boundary.length > 0) {
                        const center = this.getPolygonCenter(boundary);
                        const orientation = this.getOrientation(boundary);
                        this.createLabel(zone.id, center, orientation, true);
                    }
                }
            });
        }

        this.scene.add(this.labelGroup);
    }

    private createLabel(id: string, position: THREE.Vector3, orientation: 'horizontal' | 'vertical', isZoneLabel: boolean = false) {

        const config = canvasStyleService.currentStyle.value.layers.labels.component;

        const div = document.createElement('div');
        div.className = 'ai-label';

        if (isZoneLabel) {
            // Zone 标签：统一 #ID 格式
            div.textContent = `#${id}`;
        } else {
            // 组件标签：#前缀 + 截断
            const shortId = id.length > 8 ? id.substring(0, 4) : id;
            div.textContent = `#${shortId}`;
        }

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

        div.style.fontSize = `${config.fontSize}px`;
        div.style.fontWeight = config.fontWeight; // 变细，减少视觉干扰
        if (config.fontFamily) {
            div.style.fontFamily = config.fontFamily; // 等宽字体利于 AI 识别
        }
        if (isZoneLabel) {
            // Zone 标签可点击，用于选中 Zone
            div.style.pointerEvents = 'auto';
            div.style.cursor = 'pointer';
        } else {
            div.style.pointerEvents = 'none'; // 非 Zone 标签穿透点击
        }

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
            label.userData.zoneId = id;

            // Zone 标签 click handler：通过 canvasStore 设置选中状态
            const store = useCanvasStore();
            const clickHandler = (e: MouseEvent) => {
                e.stopPropagation();
                if (e.shiftKey) {
                    store.removeFromSelection(id);
                } else if (e.ctrlKey || e.metaKey) {
                    store.toggleSelection(id);
                } else {
                    store.setSelectedObject(id);
                }
            };
            div.addEventListener('click', clickHandler);
            label.userData.clickHandler = clickHandler;
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
