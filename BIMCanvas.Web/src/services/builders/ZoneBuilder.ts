import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import type { CanvasDocument, Zone, Point2D } from '../../types/canvas';
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

        this.materials.set('innerBoundary', new THREE.MeshBasicMaterial({
            color: colors.innerBoundary,
            transparent: true,
            opacity: colors.opacity,
            side: THREE.DoubleSide,
            depthWrite: false // Prevent z-fighting with floor
        }));

        this.materials.set('exclusion', new THREE.MeshBasicMaterial({
            color: colors.exclusion,
            transparent: true,
            opacity: colors.opacity * 2, // Make exclusion slightly more visible
            side: THREE.DoubleSide,
            depthWrite: false
        }));

        // Optional: Hatch pattern for exclusion?
        // For now, keep it simple with solid color as per KISS.
    }

    public buildZones(doc: CanvasDocument) {
        if (this.zoneGroup) {
            this.scene.remove(this.zoneGroup);
            // Dispose logic if needed, but Group disposal is simple
            this.zoneGroup = null;
        }

        this.zoneGroup = new THREE.Group();
        this.zoneGroup.layers.set(LayerManager.LAYER_ZONES);

        if (doc.zones) {
            doc.zones.forEach(zone => {
                this.createZoneMesh(zone);
            });
        }

        this.scene.add(this.zoneGroup);
    }

    private createZoneMesh(zone: Zone) {
        // 1. Inner Boundary (Safe Area)
        if (zone.innerBoundary && zone.innerBoundary.length > 0) {
            const shape = this.createShapeFromPolygon(zone.innerBoundary);
            const geometry = new THREE.ShapeGeometry(shape);
            const mesh = new THREE.Mesh(geometry, this.materials.get('innerBoundary'));

            // Slightly above floor (floor is usually at 0 or -thickness)
            // Let's put it at z=5 to be above floor but below furniture
            mesh.position.z = 5;

            // Rotate to match coordinate system (if needed, but ShapeGeometry is XY)
            // Scene is Y-Up, but we view from Top (-Z).
            // Wait, SceneBuilder rotates walls -90 X.
            // "mesh.rotation.x = -Math.PI / 2;"
            // Let's check SceneBuilder.
            // SceneBuilder: mesh.rotation.x = -Math.PI / 2; // Y-Up Rotation
            // So floor is on X-Z plane.

            mesh.rotation.x = -Math.PI / 2;
            mesh.position.y = 5; // Lift slightly above Y=0 (Ground)

            mesh.layers.set(LayerManager.LAYER_ZONES);
            this.zoneGroup!.add(mesh);
        }

        // 2. Exclusion Areas
        if (zone.exclusionAreas) {
            zone.exclusionAreas.forEach(area => {
                if (area?.boundary && area.boundary.length > 0) {
                    const shape = this.createShapeFromPolygon(area.boundary);
                    const geometry = new THREE.ShapeGeometry(shape);
                    const mesh = new THREE.Mesh(geometry, this.materials.get('exclusion'));

                    mesh.rotation.x = -Math.PI / 2;
                    mesh.position.y = 10; // Slightly above inner boundary

                    mesh.layers.set(LayerManager.LAYER_ZONES);
                    this.zoneGroup!.add(mesh);
                }
            });
        }
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
