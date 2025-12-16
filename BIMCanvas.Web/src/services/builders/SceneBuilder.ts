import * as THREE from 'three';
import type { CanvasDocument, Wall, Column, Module, Point2D, Opening } from '../../types/canvas';
import { LayerManager } from '../three/LayerManager';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;

    // Constants
    private readonly WALL_HEIGHT = 2800;
    private readonly WALL_COLOR = 0xD0D0D0;
    private readonly COLUMN_COLOR = 0x808080;
    private readonly MODULE_COLOR = 0x3b82f6;
    private readonly DOOR_FRAME_COLOR = 0x404040;
    private readonly DOOR_PANEL_COLOR = 0x505050;
    private readonly WINDOW_FRAME_COLOR = 0x303030;
    private readonly GLASS_COLOR = 0x88ccff;

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

        this.materials.set('doorFrame', new THREE.MeshStandardMaterial({
            color: this.DOOR_FRAME_COLOR,
            roughness: 0.8,
            metalness: 0.3
        }));

        this.materials.set('doorPanel', new THREE.MeshStandardMaterial({
            color: this.DOOR_PANEL_COLOR,
            roughness: 0.7,
            metalness: 0.1
        }));

        this.materials.set('windowFrame', new THREE.MeshStandardMaterial({
            color: this.WINDOW_FRAME_COLOR,
            roughness: 0.8,
            metalness: 0.3
        }));

        this.materials.set('glass', new THREE.MeshPhysicalMaterial({
            color: this.GLASS_COLOR,
            metalness: 0.1,
            roughness: 0.1,
            transmission: 0.6,
            transparent: true,
            opacity: 0.6
        }));

        this.materials.set('swingArc', new THREE.LineBasicMaterial({
            color: 0xffffff,
            opacity: 0.8,
            transparent: true,
            linewidth: 2
        }));
    }

    public clearScene() {
        const toRemove: THREE.Object3D[] = [];
        this.scene.traverse((child) => {
            if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
                toRemove.push(child);
            }
        });
        toRemove.forEach(child => this.scene.remove(child));
    }

    public buildFromDocument(doc: CanvasDocument) {
        console.log('SceneBuilder: Building from document', doc);
        this.clearScene();
        // this.buildFloor(); // Removed as per user request

        // 1. Walls
        if (doc.walls && doc.walls.length > 0) {
            doc.walls.forEach(wall => this.createWallMesh(wall));
        }

        // 2. Columns
        if (doc.columns && doc.columns.length > 0) {
            doc.columns.forEach(col => this.createColumnMesh(col));
        }

        // 3. Openings (Doors/Windows)
        if (doc.openings && doc.openings.length > 0) {
            doc.openings.forEach(op => this.createOpeningMesh(op));
        }

        // 4. Modules
        if (doc.modules && doc.modules.length > 0) {
            doc.modules.forEach(mod => this.createModuleMesh(mod));
        }
    }

    public buildDemoScene() {
        // this.buildFloor(); // Removed as per user request
        const wallGeo = new THREE.BoxGeometry(5000, 200, 2800);
        const wallMat = this.materials.get('wall');
        const wall = new THREE.Mesh(wallGeo, wallMat);
        wall.position.set(0, 1000, 1400);
        this.setLayers(wall);
        this.scene.add(wall);

        const moduleGeo = new THREE.BoxGeometry(800, 800, 750);
        const moduleMat = this.materials.get('module');
        const module = new THREE.Mesh(moduleGeo, moduleMat);
        module.position.set(0, -500, 375);
        this.setLayers(module);
        this.scene.add(module);
    }

    private setLayers(object: THREE.Object3D) {
        // Enable Default and Human layers
        object.layers.enable(LayerManager.LAYER_DEFAULT);
        object.layers.enable(LayerManager.LAYER_HUMAN);
        // AI layer logic will be handled by SemanticLineBuilder
    }

    // private buildFloor() { ... } // Removed

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

    private createWallMesh(wall: Wall) {
        if (!wall.polygon || wall.polygon.length === 0) return;
        const shape = this.createShapeFromPolygon(wall.polygon);
        const geometry = new THREE.ExtrudeGeometry(shape, {
            depth: this.WALL_HEIGHT,
            bevelEnabled: false
        });

        const mesh = new THREE.Mesh(geometry, this.materials.get('wall'));
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        mesh.rotation.x = -Math.PI / 2; // Y-Up Rotation
        this.setLayers(mesh);
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
        mesh.rotation.x = -Math.PI / 2; // Y-Up Rotation
        this.setLayers(mesh);
        this.scene.add(mesh);
    }

    private createOpeningMesh(op: Opening) {
        if (!op.line || op.line.length < 2) return;

        const start = new THREE.Vector2(op.line[0][0], op.line[0][1]);
        const end = new THREE.Vector2(op.line[1][0], op.line[1][1]);
        const width = start.distanceTo(end);
        const height = 2100;
        const center = new THREE.Vector2().addVectors(start, end).multiplyScalar(0.5);
        const angle = Math.atan2(end.y - start.y, end.x - start.x);

        if (op.type === 0) {
            this.createDoor(center, width, height, angle);
        } else {
            this.createWindow(center, width, height, angle);
        }
    }


    private createDoor(center: THREE.Vector2, width: number, height: number, angle: number) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;
        this.scene.add(root);

        const frameThickness = 50;
        const frameDepth = 120;

        const frameGroup = new THREE.Group();
        frameGroup.position.set(center.x, center.y, 0);
        frameGroup.rotation.z = angle;

        const topGeo = new THREE.BoxGeometry(width + frameThickness * 2, frameDepth, frameThickness);
        const frameMat = this.materials.get('doorFrame');
        const topFrame = new THREE.Mesh(topGeo, frameMat);
        topFrame.position.set(0, 0, height);
        this.setLayers(topFrame);
        frameGroup.add(topFrame);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.setLayers(leftFrame);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.setLayers(rightFrame);
        frameGroup.add(rightFrame);

        root.add(frameGroup);

        const panelThickness = 40;
        const panelWidth = width;

        const panelGroup = new THREE.Group();
        panelGroup.position.set(center.x, center.y, 0);
        panelGroup.rotation.z = angle;

        const pivotGroup = new THREE.Group();
        pivotGroup.position.set(-width / 2, 0, 0);

        const panelGeo = new THREE.BoxGeometry(panelWidth, panelThickness, height - frameThickness);
        const panelMat = this.materials.get('doorPanel');
        const panel = new THREE.Mesh(panelGeo, panelMat);
        panel.position.set(panelWidth / 2, 0, height / 2);
        this.setLayers(panel);

        pivotGroup.add(panel);

        let swingAngle = Math.PI / 2;
        pivotGroup.rotation.z = swingAngle;

        panelGroup.add(pivotGroup);

        const curve = new THREE.EllipseCurve(
            -width / 2, 0,
            width, width,
            0, swingAngle,
            false,
            0
        );

        const points = curve.getPoints(32);
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const arc = new THREE.Line(geometry, this.materials.get('swingArc'));
        arc.position.set(0, 0, 20);
        this.setLayers(arc);

        panelGroup.add(arc);

        root.add(panelGroup);
    }

    private createWindow(center: THREE.Vector2, width: number, height: number, angle: number) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;
        this.scene.add(root);

        const frameThickness = 50;
        const frameDepth = 100;
        const sillHeight = 900;

        const group = new THREE.Group();
        group.position.set(center.x, center.y, 0);
        group.rotation.z = angle;

        const frameMat = this.materials.get('windowFrame');

        const bottom = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        bottom.position.set(0, 0, sillHeight);
        this.setLayers(bottom);
        group.add(bottom);

        const top = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        top.position.set(0, 0, sillHeight + height);
        this.setLayers(top);
        group.add(top);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const left = new THREE.Mesh(sideGeo, frameMat);
        left.position.set(-width / 2 + frameThickness / 2, 0, sillHeight + height / 2);
        this.setLayers(left);
        group.add(left);

        const right = new THREE.Mesh(sideGeo, frameMat);
        right.position.set(width / 2 - frameThickness / 2, 0, sillHeight + height / 2);
        this.setLayers(right);
        group.add(right);

        const glassGeo = new THREE.BoxGeometry(width - frameThickness * 2, 20, height - frameThickness * 2);
        const glass = new THREE.Mesh(glassGeo, this.materials.get('glass'));
        glass.position.set(0, 0, sillHeight + height / 2);
        this.setLayers(glass);
        group.add(glass);

        root.add(group);
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
        mesh.rotation.x = -Math.PI / 2; // Y-Up Rotation
        this.setLayers(mesh);
        this.scene.add(mesh);
    }
}
