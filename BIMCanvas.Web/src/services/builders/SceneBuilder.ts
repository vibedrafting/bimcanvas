import * as THREE from 'three';
import type { CanvasDocument, Wall, Column, Module, Point2D } from '../../types/canvas';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;

    // Constants
    private readonly WALL_HEIGHT = 2800;
    private readonly WALL_COLOR = 0x2a2a30;
    private readonly MODULE_COLOR = 0x3b82f6; // Calm Blue
    private readonly COLUMN_COLOR = 0x3a3a40;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.initMaterials();
    }

    private initMaterials() {
        this.materials.set('wall', new THREE.MeshStandardMaterial({
            color: this.WALL_COLOR,
            roughness: 0.9,
            metalness: 0.1
        }));

        this.materials.set('column', new THREE.MeshStandardMaterial({
            color: this.COLUMN_COLOR,
            roughness: 0.9,
            metalness: 0.1
        }));

        this.materials.set('module', new THREE.MeshStandardMaterial({
            color: this.MODULE_COLOR,
            roughness: 0.5,
            metalness: 0.1
        }));

        this.materials.set('floor', new THREE.MeshStandardMaterial({
            color: 0x1a1a20,
            roughness: 0.8,
            metalness: 0.2
        }));
    }

    public clearScene() {
        const toRemove: THREE.Object3D[] = [];
        this.scene.traverse((child) => {
            if (child instanceof THREE.Mesh) {
                toRemove.push(child);
            }
        });
        toRemove.forEach(child => this.scene.remove(child));
    }

    public buildFromDocument(doc: CanvasDocument) {
        console.log('SceneBuilder: Building from document', doc);
        this.clearScene();
        this.buildFloor();

        // 1. Walls
        if (doc.walls && doc.walls.length > 0) {
            console.log(`SceneBuilder: Creating ${doc.walls.length} walls`);
            doc.walls.forEach(wall => this.createWallMesh(wall));
        } else {
            console.warn('SceneBuilder: No walls found in document');
        }

        // 2. Columns
        if (doc.columns && doc.columns.length > 0) {
            console.log(`SceneBuilder: Creating ${doc.columns.length} columns`);
            doc.columns.forEach(col => this.createColumnMesh(col));
        }

        // 3. Modules
        if (doc.modules && doc.modules.length > 0) {
            console.log(`SceneBuilder: Creating ${doc.modules.length} modules`);
            doc.modules.forEach(mod => this.createModuleMesh(mod));
        }
    }

    public buildDemoScene() {
        this.buildFloor();
        const wallGeo = new THREE.BoxGeometry(5000, 200, 2800);
        const wallMat = this.materials.get('wall');
        const wall = new THREE.Mesh(wallGeo, wallMat);
        wall.position.set(0, 1000, 1400);
        this.scene.add(wall);

        const moduleGeo = new THREE.BoxGeometry(800, 800, 750);
        const moduleMat = this.materials.get('module');
        const module = new THREE.Mesh(moduleGeo, moduleMat);
        module.position.set(0, -500, 375);
        this.scene.add(module);
    }

    private buildFloor() {
        const floorGeometry = new THREE.PlaneGeometry(20000, 20000);
        const floorMaterial = this.materials.get('floor');
        const floor = new THREE.Mesh(floorGeometry, floorMaterial);
        floor.receiveShadow = true;
        floor.position.z = -10;
        this.scene.add(floor);
    }

    private createShapeFromPolygon(polygon: Point2D[]): THREE.Shape {
        const shape = new THREE.Shape();
        if (!polygon || polygon.length === 0) return shape;

        shape.moveTo(polygon[0][0], polygon[0][1]);
        for (let i = 1; i < polygon.length; i++) {
            shape.lineTo(polygon[i][0], polygon[i][1]);
        }
        shape.closePath();
        return shape;
    }

    private createWallMesh(wall: Wall) {
        if (!wall.polygon || wall.polygon.length === 0) {
            console.warn('SceneBuilder: Invalid wall polygon', wall);
            return;
        }
        const shape = this.createShapeFromPolygon(wall.polygon);
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: this.WALL_HEIGHT,
            bevelEnabled: false
        });

        const mesh = new THREE.Mesh(geometry, this.materials.get('wall'));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        this.scene.add(mesh);
    }

    private createColumnMesh(col: Column) {
        if (!col.polygon || col.polygon.length === 0) return;

        const shape = this.createShapeFromPolygon(col.polygon);
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: this.WALL_HEIGHT,
            bevelEnabled: false
        });

        const mesh = new THREE.Mesh(geometry, this.materials.get('column'));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        this.scene.add(mesh);
    }

    private createModuleMesh(mod: Module) {
        if (!mod.bounds || mod.bounds.length === 0) return;

        const shape = this.createShapeFromPolygon(mod.bounds);
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: 750,
            bevelEnabled: false
        });

        const mesh = new THREE.Mesh(geometry, this.materials.get('module'));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        this.scene.add(mesh);
    }
}
