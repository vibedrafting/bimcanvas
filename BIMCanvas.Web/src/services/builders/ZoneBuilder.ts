import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument, Zone, Point2D } from '../../types/canvas';
import { ZoneType } from '../../types/canvas';
import { themeService } from '../theme/ThemeService';

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
        const colors = themeService.currentTheme.value.zones;

        // Room zones: light fill
        this.materials.set('room', new THREE.MeshBasicMaterial({
            color: colors.innerBoundary,
            transparent: true,
            opacity: colors.opacity * 0.5,
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // Designable zones: green fill
        this.materials.set('designable', new THREE.MeshBasicMaterial({
            color: colors.innerBoundary,
            transparent: true,
            opacity: colors.opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // Exclusion zones: red fill
        this.materials.set('exclusion', new THREE.MeshBasicMaterial({
            color: colors.exclusion,
            transparent: true,
            opacity: colors.opacity * 2,
            side: THREE.DoubleSide,
            depthWrite: false
        }));
    }

    public buildZones(doc: CanvasDocument) {
        if (this.zoneGroup) {
            this.scene.remove(this.zoneGroup);
            // Dispose logic if needed, but Group disposal is simple
            this.zoneGroup = null;
        }

        this.zoneGroup = new THREE.Group();
        this.zoneGroup.layers.set(LayerManager.LAYER_ZONES);

        // 从 computed 子结构获取 zones
        const zones = doc.computed?.zones;
        if (zones) {
            zones.forEach(zone => {
                this.createZoneMesh(zone);
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

        const mesh = new THREE.Mesh(geometry, this.materials.get(materialKey));
        mesh.rotation.x = -Math.PI / 2;
        mesh.position.y = yPosition;
        mesh.layers.set(LayerManager.LAYER_ZONES);
        this.zoneGroup!.add(mesh);
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
