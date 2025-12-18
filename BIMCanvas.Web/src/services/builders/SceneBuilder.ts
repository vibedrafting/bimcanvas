import * as THREE from 'three';
import type { CanvasDocument, Wall, Column, Module, Point2D, Opening } from '../../types/canvas';
import { LayerManager } from '../three/LayerManager';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;
    private boxHelpers: THREE.BoxHelper[] = [];

    // Constants
    private readonly WALL_HEIGHT = 2800;
    private readonly WALL_COLOR = 0xD0D0D0;
    private readonly COLUMN_COLOR = 0x808080;
    private readonly MODULE_COLOR = 0x3b82f6;
    private readonly DOOR_FRAME_COLOR = 0xA0A0A0;
    private readonly DOOR_PANEL_COLOR = 0xC0C0C0;
    private readonly WINDOW_FRAME_COLOR = 0x909090;
    private readonly GLASS_COLOR = 0xAADDFF;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.initMaterials();
    }

    private initMaterials() {
        this.materials.set('wall', new THREE.MeshStandardMaterial({
            color: 0x333333, // Dark Gray
            roughness: 0.8,
            metalness: 0.1
        }));

        this.materials.set('column', new THREE.MeshStandardMaterial({
            color: 0x444444, // Slightly lighter than wall
            roughness: 0.8,
            metalness: 0.1
        }));

        this.materials.set('module', new THREE.MeshStandardMaterial({
            color: this.MODULE_COLOR,
            roughness: 0.6,
            metalness: 0.2,
            side: THREE.DoubleSide // Ensure visibility
        }));

        this.materials.set('floor', new THREE.MeshStandardMaterial({
            color: 0x0a0a0f, // Very dark, matches BG
            roughness: 0.9,
            metalness: 0.1
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
            opacity: 0.3, // More subtle
            transparent: true,
            linewidth: 1
        }));
    }

    public clearScene() {
        console.log('--- clearScene START ---');
        console.log('Total children before clear:', this.scene.children.length);

        // 1. Remove tracked BoxHelpers
        this.boxHelpers.forEach(helper => {
            if (helper.parent) {
                helper.parent.remove(helper);
            }
            if (helper.geometry) helper.geometry.dispose();
            if (helper.material) {
                const mat = helper.material;
                if (Array.isArray(mat)) {
                    mat.forEach(m => m.dispose());
                } else {
                    mat.dispose();
                }
            }
        });
        this.boxHelpers = [];

        // 2. Remove other objects
        const toRemove: THREE.Object3D[] = [];
        this.scene.traverse((child) => {
            // Keep Lights and Camera (if in scene)
            if (child instanceof THREE.Light || child instanceof THREE.Camera) return;

            // Remove Meshes, Lines, LineSegments (BoxHelper), AxesHelper, Group (if not root)
            if (child !== this.scene) {
                if (child instanceof THREE.Mesh ||
                    child instanceof THREE.Line ||
                    child instanceof THREE.LineSegments ||
                    child instanceof THREE.AxesHelper ||
                    child.type === 'BoxHelper') {

                    console.log('Marking for removal:', child.type, child.uuid, (child as any).geometry?.type);
                    toRemove.push(child);
                } else {
                    console.log('Skipping removal of:', child.type, child.uuid);
                }
            }
        });

        toRemove.forEach(child => {
            // Correctly remove from parent (handles nested objects)
            if (child.parent) {
                child.parent.remove(child);
            }

            // Dispose geometry and material if possible
            if ((child as any).geometry) (child as any).geometry.dispose();
            if ((child as any).material) {
                const mat = (child as any).material;
                if (Array.isArray(mat)) {
                    mat.forEach(m => m.dispose());
                } else {
                    mat.dispose();
                }
            }
        });

        console.log('Total children after clear:', this.scene.children.length);
        this.scene.children.forEach(c => console.log('Remaining child:', c.type, c.uuid));
        console.log('--- clearScene END ---');
    }

    public buildFromDocument(doc: CanvasDocument) {
        console.log('SceneBuilder: Building from document', doc);
        this.clearScene();
        // this.buildFloor(); // Removed as per user request

        // Add Axes Helper
        const axesHelper = new THREE.AxesHelper(1000); // 1m length
        axesHelper.layers.set(LayerManager.LAYER_AXES);
        this.scene.add(axesHelper);

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

        // 5. Update all helpers to ensure they match final world positions
        this.updateAllHelpers();
    }

    public buildDemoScene() {
        // this.buildFloor(); // Removed as per user request
        const wallGeo = new THREE.BoxGeometry(5000, 200, 2800);
        const wallMat = this.materials.get('wall');
        const wall = new THREE.Mesh(wallGeo, wallMat);
        wall.position.set(0, 1000, 1400);
        this.enableLayers(wall);
        this.createBoundsHelper(wall);
        this.scene.add(wall);

        const moduleGeo = new THREE.BoxGeometry(800, 800, 750);
        const moduleMat = this.materials.get('module');
        const module = new THREE.Mesh(moduleGeo, moduleMat);
        module.position.set(0, -500, 375);
        this.enableLayers(module);
        this.createBoundsHelper(module);
        this.scene.add(module);

        // Add Axes Helper
        const axesHelper = new THREE.AxesHelper(1000); // 1m length
        axesHelper.layers.set(LayerManager.LAYER_AXES);
        this.scene.add(axesHelper);

        this.updateAllHelpers();
    }

    private enableLayers(object: THREE.Object3D) {
        // Enable Default and Model layers
        object.layers.enable(LayerManager.LAYER_MODEL);
    }

    private createBoundsHelper(object: THREE.Object3D) {
        // Add BoxHelper for Bounds Layer
        const boxHelper = new THREE.BoxHelper(object, 0xffff00); // Yellow box
        boxHelper.layers.set(LayerManager.LAYER_BOUNDS);
        this.scene.add(boxHelper);

        // Track the helper
        this.boxHelpers.push(boxHelper);
    }

    private updateAllHelpers() {
        this.boxHelpers.forEach(helper => {
            helper.update();
        });
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
        this.enableLayers(mesh);
        this.createBoundsHelper(mesh);
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
        this.enableLayers(mesh);
        this.createBoundsHelper(mesh);
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
        this.enableLayers(topFrame);
        frameGroup.add(topFrame);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableLayers(leftFrame);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableLayers(rightFrame);
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
        this.enableLayers(panel);

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
        this.enableLayers(arc);

        panelGroup.add(arc);

        root.add(panelGroup);

        // Create ONE BoxHelper for the entire door group
        this.createBoundsHelper(root);
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
        this.enableLayers(bottom);
        group.add(bottom);

        const top = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        top.position.set(0, 0, sillHeight + height);
        this.enableLayers(top);
        group.add(top);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const left = new THREE.Mesh(sideGeo, frameMat);
        left.position.set(-width / 2 + frameThickness / 2, 0, sillHeight + height / 2);
        this.enableLayers(left);
        group.add(left);

        const right = new THREE.Mesh(sideGeo, frameMat);
        right.position.set(width / 2 - frameThickness / 2, 0, sillHeight + height / 2);
        this.enableLayers(right);
        group.add(right);

        const glassGeo = new THREE.BoxGeometry(width - frameThickness * 2, 20, height - frameThickness * 2);
        const glass = new THREE.Mesh(glassGeo, this.materials.get('glass'));
        glass.position.set(0, 0, sillHeight + height / 2);
        this.enableLayers(glass);
        group.add(glass);

        root.add(group);

        // Create ONE BoxHelper for the entire window group
        this.createBoundsHelper(root);
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

        // Set User Data
        mesh.userData = {
            id: mod.id,
            type: 'module',
            data: mod
        };

        this.enableLayers(mesh);
        this.createBoundsHelper(mesh);
        this.scene.add(mesh);
    }
}
