import * as THREE from 'three';
import type { CanvasDocument, Wall, Column, Module, Point2D, Opening } from '../../types/canvas';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;

    // Constants
    private readonly WALL_HEIGHT = 2800;
    private readonly WALL_COLOR = 0xD0D0D0; // Light Grey (High Contrast)
    private readonly COLUMN_COLOR = 0x808080; // Mid Grey
    private readonly MODULE_COLOR = 0x3b82f6; // Calm Blue
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

        // 3. Openings (Doors/Windows)
        if (doc.openings && doc.openings.length > 0) {
            console.log(`SceneBuilder: Creating ${doc.openings.length} openings`);
            doc.openings.forEach(op => this.createOpeningMesh(op));
        }

        // 4. Modules
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

    private createOpeningMesh(op: Opening) {
        if (!op.line || op.line.length < 2) return;

        const start = new THREE.Vector2(op.line[0][0], op.line[0][1]);
        const end = new THREE.Vector2(op.line[1][0], op.line[1][1]);
        const width = start.distanceTo(end);
        const height = 2100; // Standard door/window height
        const center = new THREE.Vector2().addVectors(start, end).multiplyScalar(0.5);
        const angle = Math.atan2(end.y - start.y, end.x - start.x);

        // 0 = Door, 1 = Window
        if (op.type === 0) {
            this.createDoor(center, width, height, angle, op);
        } else {
            this.createWindow(center, width, height, angle);
        }
    }

    private createDoor(center: THREE.Vector2, width: number, height: number, angle: number, op: Opening) {
        const frameThickness = 50;
        const frameDepth = 120;

        // 1. Frame
        const frameGroup = new THREE.Group();
        frameGroup.position.set(center.x, center.y, 0);
        frameGroup.rotation.z = angle;

        // Top frame
        const topGeo = new THREE.BoxGeometry(width + frameThickness * 2, frameDepth, frameThickness);
        const frameMat = this.materials.get('doorFrame');
        const topFrame = new THREE.Mesh(topGeo, frameMat);
        topFrame.position.set(0, 0, height);
        frameGroup.add(topFrame);

        // Side frames
        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        frameGroup.add(rightFrame);

        this.scene.add(frameGroup);

        // 2. Panel & Arc
        const panelThickness = 40;
        const panelWidth = width; // Panel width matches opening width

        const panelGroup = new THREE.Group();
        panelGroup.position.set(center.x, center.y, 0);
        panelGroup.rotation.z = angle;

        // Pivot Group (Hinge at left side: -width/2)
        const pivotGroup = new THREE.Group();
        pivotGroup.position.set(-width / 2, 0, 0);

        // Panel Mesh (Offset so its left edge is at pivot 0,0)
        const panelGeo = new THREE.BoxGeometry(panelWidth, panelThickness, height - frameThickness);
        const panelMat = this.materials.get('doorPanel');
        const panel = new THREE.Mesh(panelGeo, panelMat);
        panel.position.set(panelWidth / 2, 0, height / 2); // Center of panel relative to pivot

        pivotGroup.add(panel);

        // Swing Angle
        let swingAngle = Math.PI / 2; // 90 degrees open

        // Apply rotation
        pivotGroup.rotation.z = swingAngle;

        panelGroup.add(pivotGroup);

        // 3. Swing Arc (2D Line on floor)
        // Arc must be drawn relative to the hinge point (-width/2, 0)
        const curve = new THREE.EllipseCurve(
            -width / 2, 0,            // Center x, y (Hinge position)
            width, width,             // xRadius, yRadius (Radius = width)
            0, swingAngle,            // StartAngle, EndAngle
            false,                    // Clockwise
            0                         // Rotation
        );

        const points = curve.getPoints(32);
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const arc = new THREE.Line(geometry, this.materials.get('swingArc'));

        // Position arc slightly above floor
        arc.position.set(0, 0, 20);

        // Add arc to panelGroup (static relative to door frame)
        panelGroup.add(arc);

        this.scene.add(panelGroup);
    }

    private createWindow(center: THREE.Vector2, width: number, height: number, angle: number) {
        const frameThickness = 50;
        const frameDepth = 100;
        const sillHeight = 900;

        const group = new THREE.Group();
        group.position.set(center.x, center.y, 0);
        group.rotation.z = angle;

        // Frame
        const frameMat = this.materials.get('windowFrame');

        // Bottom
        const bottom = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        bottom.position.set(0, 0, sillHeight);
        group.add(bottom);

        // Top
        const top = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        top.position.set(0, 0, sillHeight + height);
        group.add(top);

        // Sides
        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const left = new THREE.Mesh(sideGeo, frameMat);
        left.position.set(-width / 2 + frameThickness / 2, 0, sillHeight + height / 2);
        group.add(left);

        const right = new THREE.Mesh(sideGeo, frameMat);
        right.position.set(width / 2 - frameThickness / 2, 0, sillHeight + height / 2);
        group.add(right);

        // Glass
        const glassGeo = new THREE.BoxGeometry(width - frameThickness * 2, 20, height - frameThickness * 2);
        const glass = new THREE.Mesh(glassGeo, this.materials.get('glass'));
        glass.position.set(0, 0, sillHeight + height / 2);
        group.add(glass);

        this.scene.add(group);
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
