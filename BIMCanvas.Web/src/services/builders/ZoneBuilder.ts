import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { ProjectData, Zone, Point2D } from '../../types/canvas';
import { ZoneType } from '../../types/canvas';
import { canvasStyleService } from '../canvas/CanvasStyleService';

export class ZoneBuilder {
    private scene: THREE.Scene;
    private zoneGroup: THREE.Group | null = null;
    private materials: Map<string, THREE.Material>;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.initMaterials();
    }

    private initMaterials() {
        const colors = canvasStyleService.currentStyle.value.layers.zones;

        // Room zones: light fill
        this.materials.set('room', new THREE.MeshBasicMaterial({
            color: colors.room.color,
            transparent: true,
            opacity: colors.room.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // Designable zones: green fill
        this.materials.set('designable', new THREE.MeshBasicMaterial({
            color: colors.designable.color,
            transparent: true,
            opacity: colors.designable.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // Exclusion zones: red fill
        this.materials.set('exclusion', new THREE.MeshBasicMaterial({
            color: colors.exclusion.color,
            transparent: true,
            opacity: colors.exclusion.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));
    }

    private ensureMaterials() {
        if (this.materials.size === 0) {
            this.initMaterials();
        }
    }

    /**
     * 清理 Zone 资源
     * @param disposeMaterials 是否释放材质（仅在彻底重建/销毁时使用）
     */
    public cleanup(disposeMaterials = false) {
        if (this.zoneGroup) {
            // 释放所有子对象的几何体
            this.zoneGroup.traverse(child => {
                if ((child as THREE.Mesh).geometry) {
                    (child as THREE.Mesh).geometry.dispose();
                }
            });
            this.zoneGroup.clear();
            this.scene.remove(this.zoneGroup);
            this.zoneGroup = null;
        }

        if (disposeMaterials) {
            this.materials.forEach(material => material.dispose());
            this.materials.clear();
        }
    }

    public buildZones(data: ProjectData) {
        this.ensureMaterials();

        if (this.zoneGroup) {
            // 释放旧的几何体资源
            this.zoneGroup.traverse(child => {
                if ((child as THREE.Mesh).geometry) {
                    (child as THREE.Mesh).geometry.dispose();
                }
            });
            this.zoneGroup.clear();
            this.scene.remove(this.zoneGroup);
            this.zoneGroup = null;
        }

        this.zoneGroup = new THREE.Group();
        this.zoneGroup.layers.set(LayerManager.LAYER_ZONES);

        // 1. 渲染 computed.roomZones（房间区域，ZoneType.Room）
        const computedZones = data.computed?.roomZones;
        if (computedZones) {
            computedZones.forEach(zone => {
                this.createZoneMesh(zone);
            });
        }

        // 2. 渲染 activeScheme.zones（设计区域，支持嵌套分区）
        const schemeZones = data.activeScheme?.zones;
        if (schemeZones) {
            schemeZones.forEach(zone => {
                if (zone.subZones && zone.subZones.length > 0) {
                    // 容器 zone：半透明轮廓线（整体边界参考）
                    this.createContainerZoneOutline(zone);
                    // 子 zone：标准 Designable 渲染
                    zone.subZones.forEach(subZone => {
                        this.createZoneMesh(subZone);
                    });
                } else {
                    // 叶子 zone：保持现有渲染
                    this.createZoneMesh(zone);
                }
            });
        }

        this.scene.add(this.zoneGroup);
    }

    private createZoneMesh(zone: Zone) {
        // Use computedBoundary if available, otherwise rawBoundary
        const boundary = zone.computedBoundary ?? zone.rawBoundary;
        if (!boundary || boundary.length === 0) return;

        const shape = this.createShapeFromPolygon(boundary);
        const geometry = new THREE.ShapeGeometry(shape);

        // Select material based on zone type
        let materialKey: string;
        let yPosition: number;

        switch (zone.type) {
            case ZoneType.Exclusion:
                materialKey = 'exclusion';
                yPosition = 10; // Above other zones
                break;
            case ZoneType.Room:
                materialKey = 'room';
                yPosition = 3; // Lowest
                break;
            case ZoneType.Designable:
            default:
                materialKey = 'designable';
                yPosition = 5; // Between room and exclusion
                break;
        }

        let material = this.materials.get(materialKey);
        if (!material) {
            this.ensureMaterials();
            material = this.materials.get(materialKey) ?? this.materials.get('designable');
        }
        if (!material) {
            return;
        }

        const mesh = new THREE.Mesh(geometry, material);
        mesh.rotation.x = -Math.PI / 2;
        mesh.position.y = yPosition;
        mesh.layers.set(LayerManager.LAYER_ZONES);

        // 设置 userData 支持点击识别
        mesh.userData = {
            id: zone.id,
            type: 'zone',
            zoneType: zone.type,
            roomId: zone.roomId,
            data: zone
        };

        this.zoneGroup!.add(mesh);
    }

    /**
     * 容器 zone（有 subZones）：绘制半透明轮廓线作为整体边界参考
     */
    private createContainerZoneOutline(zone: Zone) {
        const boundary = zone.computedBoundary ?? zone.rawBoundary;
        if (!boundary || boundary.length === 0) return;

        // 构建 3D 点序列（CAD 坐标 XY → Three.js XZ 平面）
        const points = boundary.map(p => new THREE.Vector3(p[0], 0, -p[1]));
        // 闭合
        if (points.length > 0) {
            points.push(points[0].clone());
        }

        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const material = new THREE.LineBasicMaterial({
            color: 0x22c55e,
            transparent: true,
            opacity: 0.3
        });

        const line = new THREE.Line(geometry, material);
        line.position.y = 4; // 介于 Room(3) 和 Designable(5) 之间
        line.layers.set(LayerManager.LAYER_ZONES);
        line.userData = {
            id: zone.id,
            type: 'zone',
            zoneType: zone.type,
            roomId: zone.roomId,
            isContainer: true,
            data: zone
        };

        this.zoneGroup!.add(line);
    }

    private createShapeFromPolygon(polygon: Point2D[]): THREE.Shape {
        const shape = new THREE.Shape();
        if (!polygon || polygon.length === 0) return shape;

        const firstPoint = polygon[0];
        if (firstPoint) {
            shape.moveTo(firstPoint[0], firstPoint[1]);
            for (let i = 1; i < polygon.length; i++) {
                const point = polygon[i];
                if (point) {
                    shape.lineTo(point[0], point[1]);
                }
            }
        }
        shape.closePath();
        return shape;
    }
}
