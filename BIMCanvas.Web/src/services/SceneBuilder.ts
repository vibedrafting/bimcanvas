import * as THREE from 'three';
import type { CanvasDocument, Wall, Zone, Module, Polygon2D } from '@/types/canvas';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Record<string, THREE.Material>;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = this.initMaterials();
    }

    private initMaterials() {
        return {
            wallLine: new THREE.LineBasicMaterial({ color: 0x00ffff, linewidth: 2 }), // Cyan Neon
            wallFill: new THREE.MeshBasicMaterial({ color: 0x00ffff, transparent: true, opacity: 0.1 }),
            columnLine: new THREE.LineBasicMaterial({ color: 0xffaa00, linewidth: 2 }), // Orange Neon
            columnFill: new THREE.MeshBasicMaterial({ color: 0xffaa00, transparent: true, opacity: 0.2 }),
            openingLine: new THREE.LineBasicMaterial({ color: 0xff0000, linewidth: 2 }), // Red Neon
            zoneFill: new THREE.MeshBasicMaterial({ color: 0x00ff00, transparent: true, opacity: 0.05, side: THREE.DoubleSide }),
            moduleLine: new THREE.LineBasicMaterial({ color: 0xff00ff, linewidth: 1 }), // Pink Neon
            moduleFill: new THREE.MeshBasicMaterial({ color: 0xff00ff, transparent: true, opacity: 0.1 }),
        };
    }

    public build(document: CanvasDocument) {
        this.clearScene();

        // 1. Build Walls
        if (document.walls) document.walls.forEach(wall => this.buildWall(wall));

        // 2. Build Columns
        if (document.columns) document.columns.forEach(column => this.buildColumn(column));

        // 3. Build Openings
        if (document.openings) document.openings.forEach(opening => this.buildOpening(opening));

        // 4. Build Zones
        if (document.zones) document.zones.forEach(zone => this.buildZone(zone));

        // 5. Build Modules
        if (document.modules) document.modules.forEach(module => this.buildModule(module));
    }

    private clearScene() {
        // Remove all children except GridHelper (if any) and Camera/Lights
        const toRemove: THREE.Object3D[] = [];
        this.scene.traverse((child) => {
            if ((child instanceof THREE.Mesh || child instanceof THREE.LineSegments || child instanceof THREE.Line) && child.name !== 'GridHelper') {
                toRemove.push(child);
            }
        });
        toRemove.forEach(child => this.scene.remove(child));
    }

    private createShapeFromPolygon(polygon: Polygon2D): THREE.Shape {
        const shape = new THREE.Shape();
        if (!polygon || polygon.length === 0) return shape;

        shape.moveTo(polygon[0][0], polygon[0][1]);
        for (let i = 1; i < polygon.length; i++) {
            shape.lineTo(polygon[i][0], polygon[i][1]);
        }
        shape.closePath();
        return shape;
    }

    private buildWall(wall: Wall) {
        const shape = this.createShapeFromPolygon(wall.polygon);

        // 3D Extrusion for walls (height 2800mm)
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: 2800,
            bevelEnabled: false
        });

        // Edges for Neon effect
        const edges = new THREE.EdgesGeometry(geometry);
        const line = new THREE.LineSegments(edges, this.materials.wallLine);
        line.userData = { id: wall.id, type: 'wall' };

        // Transparent fill
        const mesh = new THREE.Mesh(geometry, this.materials.wallFill);
        mesh.userData = { id: wall.id, type: 'wall' };

        this.scene.add(line);
        this.scene.add(mesh);
    }

    private buildColumn(column: any) {
        const shape = this.createShapeFromPolygon(column.polygon);

        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: 2800,
            bevelEnabled: false
        });

        const edges = new THREE.EdgesGeometry(geometry);
        const line = new THREE.LineSegments(edges, this.materials.columnLine);
        line.userData = { id: column.id, type: 'column' };

        const mesh = new THREE.Mesh(geometry, this.materials.columnFill);
        mesh.userData = { id: column.id, type: 'column' };

        this.scene.add(line);
        this.scene.add(mesh);
    }

    private buildOpening(opening: any) {
        // Draw a simple line for now, maybe an arc later
        // Opening line is [Point2D, Point2D]
        if (!opening.line || opening.line.length < 2) return;

        const points = [
            new THREE.Vector3(opening.line[0][0], opening.line[0][1], 100),
            new THREE.Vector3(opening.line[1][0], opening.line[1][1], 100)
        ];
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const line = new THREE.Line(geometry, this.materials.openingLine);
        line.userData = { id: opening.id, type: 'opening' };
        this.scene.add(line);
    }

    private buildZone(zone: Zone) {
        const shape = this.createShapeFromPolygon(zone.innerBoundary);
        const geometry = new THREE.ShapeGeometry(shape);

        const mesh = new THREE.Mesh(geometry, this.materials.zoneFill);
        mesh.position.z = 10; // Slightly above floor
        mesh.userData = { id: zone.id, type: 'zone' };

        this.scene.add(mesh);
    }

    private buildModule(module: Module) {
        const shape = this.createShapeFromPolygon(module.bounds);
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: 800, // Default furniture height
            bevelEnabled: false
        });

        const edges = new THREE.EdgesGeometry(geometry);
        const line = new THREE.LineSegments(edges, this.materials.moduleLine);
        line.userData = { id: module.id, type: 'module' };

        const mesh = new THREE.Mesh(geometry, this.materials.moduleFill);
        mesh.userData = { id: module.id, type: 'module' };

        this.scene.add(line);
        this.scene.add(mesh);
    }
}
