import * as THREE from 'three';
import type { ProjectData, Wall, Column, Module, Point2D, Opening } from '../../types/canvas';
import { LayerManager } from '../three/LayerManager';
import { canvasStyleService } from '../canvas/CanvasStyleService';
import { SVGModuleRenderer } from './SVGModuleRenderer';
import { facingToAngle } from '../../utils/coordinates';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;
    private boxHelpers: THREE.BoxHelper[] = [];
    private svgRenderer: SVGModuleRenderer;
    private buildOptions = {
        includeBounds: true,
        includeSvg: true
    };

    // Constants
    private readonly WALL_HEIGHT = 2800;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.svgRenderer = new SVGModuleRenderer(scene);
        this.initMaterials();
    }

    public setBuildOptions(options: { includeBounds?: boolean; includeSvg?: boolean }) {
        if (typeof options.includeBounds === 'boolean') {
            this.buildOptions.includeBounds = options.includeBounds;
        }
        if (typeof options.includeSvg === 'boolean') {
            this.buildOptions.includeSvg = options.includeSvg;
        }
    }

    private initMaterials() {
        const styles = canvasStyleService.currentStyle.value.layers;
        const architecture = styles.architecture;
        const furniture = styles.furniture;

        this.materials.set('wall', new THREE.MeshStandardMaterial({
            color: architecture.wall.color,
            roughness: architecture.wall.roughness,
            metalness: architecture.wall.metalness
        }));

        this.materials.set('column', new THREE.MeshStandardMaterial({
            color: architecture.column.color,
            roughness: architecture.column.roughness,
            metalness: architecture.column.metalness
        }));

        this.materials.set('module', new THREE.MeshStandardMaterial({
            color: furniture.module.color,
            roughness: furniture.module.roughness,
            metalness: furniture.module.metalness,
            side: furniture.module.doubleSided ? THREE.DoubleSide : undefined
        }));

        this.materials.set('floor', new THREE.MeshStandardMaterial({
            color: architecture.floor.color,
            roughness: architecture.floor.roughness,
            metalness: architecture.floor.metalness
        }));

        this.materials.set('doorFrame', new THREE.MeshStandardMaterial({
            color: architecture.doorFrame.color,
            roughness: architecture.doorFrame.roughness,
            metalness: architecture.doorFrame.metalness
        }));

        this.materials.set('doorPanel', new THREE.MeshStandardMaterial({
            color: architecture.doorPanel.color,
            roughness: architecture.doorPanel.roughness,
            metalness: architecture.doorPanel.metalness
        }));

        this.materials.set('windowFrame', new THREE.MeshStandardMaterial({
            color: architecture.windowFrame.color,
            roughness: architecture.windowFrame.roughness,
            metalness: architecture.windowFrame.metalness
        }));

        this.materials.set('glass', new THREE.MeshPhysicalMaterial({
            color: architecture.glass.color,
            metalness: architecture.glass.metalness,
            roughness: architecture.glass.roughness,
            transmission: architecture.glass.transmission,
            transparent: architecture.glass.transparent,
            opacity: architecture.glass.opacity
        }));

        const swingArc = architecture.swingArc;
        this.materials.set('swingArc', new THREE.LineBasicMaterial({
            color: swingArc.color,
            opacity: swingArc.opacity,
            transparent: swingArc.transparent,
            linewidth: swingArc.lineWidth
        }));

        // --- AI Vision Materials (Basic Material for flat shading) ---
        const aiColors = styles.aiVision;

        this.materials.set('ai_wall', new THREE.MeshBasicMaterial({ color: aiColors.wall.color }));
        this.materials.set('ai_column', new THREE.MeshBasicMaterial({ color: aiColors.column.color }));
        this.materials.set('ai_module', new THREE.MeshBasicMaterial({ color: aiColors.module.color }));
        this.materials.set('ai_door', new THREE.MeshBasicMaterial({ color: aiColors.door.color }));
        this.materials.set('ai_window', new THREE.MeshBasicMaterial({ color: aiColors.window.color }));
        this.materials.set('ai_slab', new THREE.MeshBasicMaterial({ color: aiColors.slab.color }));
    }

    public clearScene() {
        // 0. Clear SVG renderers
        this.svgRenderer.clear();

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

            // Keep Ghost objects (e.g. PlaceTool preview)
            if (this.isGhostObject(child)) return;

            // Remove Meshes, Lines, LineSegments (BoxHelper), AxesHelper, Group (if not root)
            if (child !== this.scene) {
                if (child instanceof THREE.Mesh ||
                    child instanceof THREE.Line ||
                    child instanceof THREE.LineSegments ||
                    child instanceof THREE.AxesHelper ||
                    child.type === 'BoxHelper') {
                    toRemove.push(child);
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
    }

    /**
     * Check if an object or any of its ancestors is marked as ghost.
     */
    private isGhostObject(obj: THREE.Object3D): boolean {
        let current: THREE.Object3D | null = obj;
        while (current) {
            if (current.userData?.isGhost) return true;
            current = current.parent;
        }
        return false;
    }

    public buildFromDocument(data: ProjectData) {
        this.clearScene();

        const baseline = data.baseline;
        const activeScheme = data.activeScheme;

        // 1. Walls
        if (baseline?.walls && baseline.walls.length > 0) {
            baseline.walls.forEach(wall => this.createWallMesh(wall));
        }

        // 2. Columns
        if (baseline?.columns && baseline.columns.length > 0) {
            baseline.columns.forEach(col => this.createColumnMesh(col));
        }

        // 3. Openings (Doors/Windows)
        if (baseline?.openings && baseline.openings.length > 0) {
            baseline.openings.forEach(op => this.createOpeningMesh(op));
        }

        // 4. Modules
        if (activeScheme?.modules && activeScheme.modules.length > 0) {
            activeScheme.modules.forEach(mod => this.createModuleMesh(mod));
        }

        // 5. Update all helpers to ensure they match final world positions
        this.updateAllHelpers();
    }

    public async buildProgressively(data: ProjectData) {
        // Note: Scene should already be cleared or contain only Grid.
        // We assume clearScene() was called before or we are building on top of empty scene.
        // Actually, ThreeSceneService will handle the clear.

        const baseline = data.baseline;
        const activeScheme = data.activeScheme;

        const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));

        // 1. Columns (Structure first) - Batch
        if (baseline?.columns && baseline.columns.length > 0) {
            baseline.columns.forEach(col => this.createColumnMesh(col));
            await delay(100); // Wait after batch
        }

        // 2. Walls - Batch
        if (baseline?.walls && baseline.walls.length > 0) {
            baseline.walls.forEach(wall => this.createWallMesh(wall));
            await delay(100); // Wait after batch
        }

        // 3. Openings - Batch
        if (baseline?.openings && baseline.openings.length > 0) {
            baseline.openings.forEach(op => this.createOpeningMesh(op));
            await delay(100); // Wait after batch
        }

        // 4. Modules (Furniture last) - One by one for effect
        if (activeScheme?.modules && activeScheme.modules.length > 0) {
            for (const mod of activeScheme.modules) {
                this.createModuleMesh(mod);
                await delay(30); // Fast individual pop
            }
        }

        this.updateAllHelpers();
        window.dispatchEvent(new CustomEvent('bimcanvas:build-complete'));
    }

    public buildDemoScene() {
        // this.buildFloor(); // Removed as per user request
        const wallGeo = new THREE.BoxGeometry(5000, 200, 2800);
        const wallMat = this.materials.get('wall');
        const wall = new THREE.Mesh(wallGeo, wallMat);
        wall.position.set(0, 1000, 1400);
        this.enableArchitectureLayer(wall);
        this.createBoundsHelper(wall);
        this.scene.add(wall);

        const moduleGeo = new THREE.BoxGeometry(800, 800, 750);
        const moduleMat = this.materials.get('module');
        const module = new THREE.Mesh(moduleGeo, moduleMat);
        module.position.set(0, -500, 375);
        this.enableFurnitureLayer(module);
        this.createBoundsHelper(module);
        this.scene.add(module);

        // (Compass removed)

        this.updateAllHelpers();
    }

    private enableArchitectureLayer(object: THREE.Object3D) {
        // 设置建筑图层（墙柱门窗）
        object.layers.set(LayerManager.LAYER_ARCHITECTURE);
    }

    private enableFurnitureLayer(object: THREE.Object3D) {
        // 设置模块预览图层（家具）
        object.layers.set(LayerManager.LAYER_FURNITURE);
    }

    private enableAiLayer(object: THREE.Object3D) {
        object.layers.set(LayerManager.LAYER_AI_VISION);
    }

    private createBoundsHelper(object: THREE.Object3D) {
        const boundsColor = canvasStyleService.currentStyle.value.layers.bounds.color;
        const boxHelper = new THREE.BoxHelper(object, boundsColor);
        boxHelper.layers.set(LayerManager.LAYER_BOUNDS);
        this.scene.add(boxHelper);

        // Track the helper
        this.boxHelpers.push(boxHelper);
    }

    private createModuleBoundsLine(mod: Module) {
        if (!mod.bounds || mod.bounds.length < 3) return;

        const boundsColor = canvasStyleService.currentStyle.value.layers.bounds.color;
        const points = mod.bounds.map(([x, y]) => new THREE.Vector3(x, y, 0));
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const material = new THREE.LineBasicMaterial({
            color: boundsColor,
            depthTest: false
        });

        const line = new THREE.LineLoop(geometry, material);
        line.rotation.x = -Math.PI / 2;
        line.renderOrder = 998;
        line.layers.set(LayerManager.LAYER_BOUNDS);
        line.userData = {
            id: mod.id,
            type: 'module-bounds',
            data: mod
        };

        this.scene.add(line);
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

        // Set User Data
        mesh.userData = {
            id: wall.id,
            type: 'wall',
            data: wall
        };

        this.enableArchitectureLayer(mesh);
        // Bounds 仅用于家具模块，建筑构件使用 Outline 描边
        this.scene.add(mesh);

        // --- AI Vision Mesh ---
        const aiMesh = new THREE.Mesh(geometry, this.materials.get('ai_wall'));
        aiMesh.rotation.x = -Math.PI / 2;
        aiMesh.userData = mesh.userData;
        this.enableAiLayer(aiMesh);
        this.scene.add(aiMesh);
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

        // Set User Data
        mesh.userData = {
            id: col.id,
            type: 'column',
            data: col
        };

        this.enableArchitectureLayer(mesh);
        // Bounds 仅用于家具模块，建筑构件使用 Outline 描边
        this.scene.add(mesh);

        // --- AI Vision Mesh ---
        const aiMesh = new THREE.Mesh(geometry, this.materials.get('ai_column'));
        aiMesh.rotation.x = -Math.PI / 2;
        aiMesh.userData = mesh.userData;
        this.enableAiLayer(aiMesh);
        this.scene.add(aiMesh);
    }

    private createOpeningMesh(op: Opening) {
        if (!op.line || op.line.length < 2) return;

        const start = new THREE.Vector2(op.line[0][0], op.line[0][1]);
        const end = new THREE.Vector2(op.line[1][0], op.line[1][1]);
        const width = start.distanceTo(end);
        const height = 2100;
        const center = new THREE.Vector2().addVectors(start, end).multiplyScalar(0.5);
        const angle = Math.atan2(end.y - start.y, end.x - start.x);
        const renderOp = this.normalizeOpeningForRender(op, start, end);

        if (op.type === 0) {
            if (op.doorOperation === 1) {
                this.createSlidingDoor(center, width, height, angle, renderOp);
            } else if (renderOp.handDirections && renderOp.handDirections.length >= 2) {
                this.createDoubleDoor(center, width, height, angle, renderOp);
            } else {
                this.createDoor(center, width, height, angle, renderOp);
            }
        } else {
            this.createWindow(center, width, height, angle, renderOp);
        }
    }

    private normalizeOpeningForRender(op: Opening, start: THREE.Vector2, end: THREE.Vector2): Opening {
        const lineVec = new THREE.Vector2(end.x - start.x, end.y - start.y);
        if (lineVec.lengthSq() === 0) return op;
        lineVec.normalize();

        const defaultHand: Point2D = [lineVec.x, lineVec.y];
        const defaultFacing: Point2D = [-lineVec.y, lineVec.x];

        const normalizeVec2 = (value: unknown): Point2D | null => {
            if (Array.isArray(value) && value.length >= 2) {
                const x = Number(value[0]);
                const y = Number(value[1]);
                if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
                const len = Math.hypot(x, y);
                if (len < 1e-6) return null;
                return [x / len, y / len];
            }
            if (value && typeof value === 'object') {
                const raw = value as { x?: number; y?: number; X?: number; Y?: number };
                const x = Number(raw.x ?? raw.X);
                const y = Number(raw.y ?? raw.Y);
                if (!Number.isFinite(x) || !Number.isFinite(y)) return null;
                const len = Math.hypot(x, y);
                if (len < 1e-6) return null;
                return [x / len, y / len];
            }
            return null;
        };

        const rawFacing = normalizeVec2((op as any).facingDirection);
        const facing = rawFacing ?? defaultFacing;

        const handDirections: Point2D[] = [];
        const rawHands = (op as any).handDirections;
        if (Array.isArray(rawHands)) {
            rawHands.forEach((hand) => {
                const vec = normalizeVec2(hand);
                if (vec) handDirections.push(vec);
            });
        } else {
            const vec = normalizeVec2(rawHands);
            if (vec) handDirections.push(vec);
        }

        const result: Opening = {
            ...op,
            facingDirection: facing,
            handDirections: handDirections.length > 0 ? handDirections : [defaultHand]
        };
        return result;
    }

    private createDoubleDoor(center: THREE.Vector2, width: number, height: number, angle: number, originalOp?: Opening) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;

        if (originalOp) {
            root.userData = {
                id: originalOp.id,
                type: 'door',
                subType: 'double',
                data: originalOp
            };
        }
        this.scene.add(root);

        // --- AI Vision Group ---
        const aiRoot = new THREE.Group();
        aiRoot.rotation.x = -Math.PI / 2;
        if (originalOp) {
            aiRoot.userData = { id: originalOp.id, type: 'door', subType: 'double', data: originalOp };
        }
        this.scene.add(aiRoot);

        const frameThickness = 50;
        const frameDepth = 120;
        const panelThickness = 40;
        const leafWidth = width / 2;
        const normalVec = new THREE.Vector2(-Math.sin(angle), Math.cos(angle));

        // === Frame (same as single door) ===
        const frameGroup = new THREE.Group();
        frameGroup.position.set(center.x, center.y, 0);
        frameGroup.rotation.z = angle;

        const topGeo = new THREE.BoxGeometry(width + frameThickness * 2, frameDepth, frameThickness);
        const frameMat = this.materials.get('doorFrame');
        const topFrame = new THREE.Mesh(topGeo, frameMat);
        topFrame.position.set(0, 0, height);
        this.enableArchitectureLayer(topFrame);
        frameGroup.add(topFrame);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(leftFrame);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(rightFrame);
        frameGroup.add(rightFrame);

        root.add(frameGroup);

        // AI Frame
        const aiFrameGroup = new THREE.Group();
        aiFrameGroup.position.set(center.x, center.y, 0);
        aiFrameGroup.rotation.z = angle;
        const aiFrameMat = this.materials.get('ai_door');

        const aiTopFrame = new THREE.Mesh(topGeo, aiFrameMat);
        aiTopFrame.position.set(0, 0, height);
        this.enableAiLayer(aiTopFrame);
        aiFrameGroup.add(aiTopFrame);

        const aiLeftFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiLeftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiLeftFrame);
        aiFrameGroup.add(aiLeftFrame);

        const aiRightFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiRightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiRightFrame);
        aiFrameGroup.add(aiRightFrame);

        aiRoot.add(aiFrameGroup);

        // === Determine open direction (shared by both leaves) ===
        let openTowardsUp = true;
        if (originalOp?.facingDirection) {
            const faceVec = new THREE.Vector2(originalOp.facingDirection[0], originalOp.facingDirection[1]);
            if (faceVec.dot(normalVec) < 0) {
                openTowardsUp = false;
            }
        }

        const panelGeo = new THREE.BoxGeometry(leafWidth, panelThickness, height - frameThickness);
        const panelMat = this.materials.get('doorPanel');

        // Helper: create one leaf (architecture + AI) and return arc geometry
        const createLeaf = (isLeftLeaf: boolean) => {
            const panelGroup = new THREE.Group();
            panelGroup.position.set(center.x, center.y, 0);
            panelGroup.rotation.z = angle;

            // Left leaf: hinge at line start (-width/2), Right leaf: hinge at line end (+width/2)
            const hingeX = isLeftLeaf ? -width / 2 : width / 2;

            const pivotGroup = new THREE.Group();
            pivotGroup.position.set(hingeX, 0, 0);

            const panel = new THREE.Mesh(panelGeo, panelMat);
            // Left leaf extends rightward from hinge, right leaf extends leftward
            panel.position.set(isLeftLeaf ? leafWidth / 2 : -leafWidth / 2, 0, height / 2);
            this.enableArchitectureLayer(panel);
            pivotGroup.add(panel);

            // Swing angle: left hinge opens CCW when up, right hinge opens CW when up
            let swingAngle = 0;
            let isCCW = true;

            if (isLeftLeaf) {
                swingAngle = openTowardsUp ? Math.PI / 2 : -Math.PI / 2;
                isCCW = openTowardsUp;
            } else {
                swingAngle = openTowardsUp ? -Math.PI / 2 : Math.PI / 2;
                isCCW = !openTowardsUp;
            }

            pivotGroup.rotation.z = swingAngle;
            panelGroup.add(pivotGroup);

            // Arc
            const startAngle = isLeftLeaf ? 0 : Math.PI;
            const curve = new THREE.EllipseCurve(
                hingeX, 0,
                leafWidth, leafWidth,
                startAngle, startAngle + swingAngle,
                !isCCW,
                0
            );
            const arcPoints = curve.getPoints(32);
            const arcGeo = new THREE.BufferGeometry().setFromPoints(arcPoints);
            const arc = new THREE.Line(arcGeo, this.materials.get('swingArc'));
            arc.position.set(0, 0, 20);
            this.enableArchitectureLayer(arc);
            panelGroup.add(arc);

            root.add(panelGroup);

            // AI Vision leaf
            const aiPanelGroup = new THREE.Group();
            aiPanelGroup.position.set(center.x, center.y, 0);
            aiPanelGroup.rotation.z = angle;

            const aiPivotGroup = new THREE.Group();
            aiPivotGroup.position.set(hingeX, 0, 0);

            const aiPanel = new THREE.Mesh(panelGeo, aiFrameMat);
            aiPanel.position.set(isLeftLeaf ? leafWidth / 2 : -leafWidth / 2, 0, height / 2);
            this.enableAiLayer(aiPanel);
            aiPivotGroup.add(aiPanel);
            aiPivotGroup.rotation.z = swingAngle;
            aiPanelGroup.add(aiPivotGroup);

            const aiArc = new THREE.Line(arcGeo, this.materials.get('swingArc'));
            aiArc.position.set(0, 0, 20);
            this.enableAiLayer(aiArc);
            aiPanelGroup.add(aiArc);

            aiRoot.add(aiPanelGroup);
        };

        // Create both leaves
        createLeaf(true);
        createLeaf(false);
    }

    private createSlidingDoor(center: THREE.Vector2, width: number, height: number, angle: number, originalOp?: Opening) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;

        if (originalOp) {
            root.userData = {
                id: originalOp.id,
                type: 'door',
                subType: 'sliding',
                data: originalOp
            };
        }

        this.scene.add(root);

        // --- AI Vision Group ---
        const aiRoot = new THREE.Group();
        aiRoot.rotation.x = -Math.PI / 2;
        if (originalOp) {
            aiRoot.userData = { id: originalOp.id, type: 'door', subType: 'sliding', data: originalOp };
        }
        this.scene.add(aiRoot);

        const frameThickness = 50;
        const frameDepth = 120;

        // === Architecture Layer: Frame ===
        const frameGroup = new THREE.Group();
        frameGroup.position.set(center.x, center.y, 0);
        frameGroup.rotation.z = angle;

        const topGeo = new THREE.BoxGeometry(width + frameThickness * 2, frameDepth, frameThickness);
        const frameMat = this.materials.get('doorFrame');
        const topFrame = new THREE.Mesh(topGeo, frameMat);
        topFrame.position.set(0, 0, height);
        this.enableArchitectureLayer(topFrame);
        frameGroup.add(topFrame);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(leftFrame);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(rightFrame);
        frameGroup.add(rightFrame);

        root.add(frameGroup);

        // === Architecture Layer: Two sliding panels ===
        const panelGroup = new THREE.Group();
        panelGroup.position.set(center.x, center.y, 0);
        panelGroup.rotation.z = angle;

        const panelThickness = 40;
        const halfWidth = width / 2;
        const panelMat = this.materials.get('doorPanel');

        const leftPanelGeo = new THREE.BoxGeometry(halfWidth, panelThickness, height - frameThickness);
        const leftPanel = new THREE.Mesh(leftPanelGeo, panelMat);
        leftPanel.position.set(-halfWidth / 2, -panelThickness / 2, height / 2);
        this.enableArchitectureLayer(leftPanel);
        panelGroup.add(leftPanel);

        const rightPanelGeo = new THREE.BoxGeometry(halfWidth, panelThickness, height - frameThickness);
        const rightPanel = new THREE.Mesh(rightPanelGeo, panelMat);
        rightPanel.position.set(halfWidth / 2, panelThickness / 2, height / 2);
        this.enableArchitectureLayer(rightPanel);
        panelGroup.add(rightPanel);

        // No swing arc for sliding doors

        root.add(panelGroup);

        // === AI Vision Layer: Frame ===
        const aiFrameGroup = new THREE.Group();
        aiFrameGroup.position.set(center.x, center.y, 0);
        aiFrameGroup.rotation.z = angle;

        const aiFrameMat = this.materials.get('ai_door');

        const aiTopFrame = new THREE.Mesh(topGeo, aiFrameMat);
        aiTopFrame.position.set(0, 0, height);
        this.enableAiLayer(aiTopFrame);
        aiFrameGroup.add(aiTopFrame);

        const aiLeftFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiLeftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiLeftFrame);
        aiFrameGroup.add(aiLeftFrame);

        const aiRightFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiRightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiRightFrame);
        aiFrameGroup.add(aiRightFrame);

        aiRoot.add(aiFrameGroup);

        // === AI Vision Layer: Two sliding panels ===
        const aiPanelGroup = new THREE.Group();
        aiPanelGroup.position.set(center.x, center.y, 0);
        aiPanelGroup.rotation.z = angle;

        const aiLeftPanel = new THREE.Mesh(leftPanelGeo, aiFrameMat);
        aiLeftPanel.position.set(-halfWidth / 2, -panelThickness / 2, height / 2);
        this.enableAiLayer(aiLeftPanel);
        aiPanelGroup.add(aiLeftPanel);

        const aiRightPanel = new THREE.Mesh(rightPanelGeo, aiFrameMat);
        aiRightPanel.position.set(halfWidth / 2, panelThickness / 2, height / 2);
        this.enableAiLayer(aiRightPanel);
        aiPanelGroup.add(aiRightPanel);

        aiRoot.add(aiPanelGroup);
    }

    private createDoor(center: THREE.Vector2, width: number, height: number, angle: number, originalOp?: Opening) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;

        if (originalOp) {
            root.userData = {
                id: originalOp.id,
                type: 'door',
                data: originalOp
            };
        }

        this.scene.add(root);

        // --- AI Vision Group ---
        const aiRoot = new THREE.Group();
        aiRoot.rotation.x = -Math.PI / 2;
        if (originalOp) {
            aiRoot.userData = { id: originalOp.id, type: 'door', data: originalOp };
        }
        this.enableAiLayer(aiRoot); // Set layer for group (might need recursive set for children if not inherited, but layers are not inherited by default in Three.js, need to set on meshes)
        // Actually, layers are not inherited. We need to set layers on meshes.
        this.scene.add(aiRoot);

        const frameThickness = 50;
        const frameDepth = 120;

        const frameGroup = new THREE.Group();
        frameGroup.position.set(center.x, center.y, 0);
        frameGroup.rotation.z = angle;

        const topGeo = new THREE.BoxGeometry(width + frameThickness * 2, frameDepth, frameThickness);
        const frameMat = this.materials.get('doorFrame');
        const topFrame = new THREE.Mesh(topGeo, frameMat);
        topFrame.position.set(0, 0, height);
        this.enableArchitectureLayer(topFrame);
        frameGroup.add(topFrame);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const leftFrame = new THREE.Mesh(sideGeo, frameMat);
        leftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(leftFrame);
        frameGroup.add(leftFrame);

        const rightFrame = new THREE.Mesh(sideGeo, frameMat);
        rightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableArchitectureLayer(rightFrame);
        frameGroup.add(rightFrame);

        root.add(frameGroup);

        // AI Vision Frame
        const aiFrameGroup = new THREE.Group();
        aiFrameGroup.position.set(center.x, center.y, 0);
        aiFrameGroup.rotation.z = angle;

        const aiFrameMat = this.materials.get('ai_door');

        const aiTopFrame = new THREE.Mesh(topGeo, aiFrameMat);
        aiTopFrame.position.set(0, 0, height);
        this.enableAiLayer(aiTopFrame);
        aiFrameGroup.add(aiTopFrame);

        const aiLeftFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiLeftFrame.position.set(-width / 2 - frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiLeftFrame);
        aiFrameGroup.add(aiLeftFrame);

        const aiRightFrame = new THREE.Mesh(sideGeo, aiFrameMat);
        aiRightFrame.position.set(width / 2 + frameThickness / 2, 0, height / 2);
        this.enableAiLayer(aiRightFrame);
        aiFrameGroup.add(aiRightFrame);

        aiRoot.add(aiFrameGroup);

        const panelThickness = 40;
        const panelWidth = width;

        const panelGroup = new THREE.Group();
        panelGroup.position.set(center.x, center.y, 0);
        panelGroup.rotation.z = angle;

        // Dynamic Door Logic
        // 1. Determine Hinge Side
        // Assumption: handDirections points to the HANDLE. So Hinge is on the opposite side.
        // Line Vector (Start -> End)
        // If handDirections aligns with Line, Handle is at End -> Hinge at Start (Left).
        // If handDirections opposes Line, Handle is at Start -> Hinge at End (Right).

        let isHingeAtStart = true; // Default Left Pivot
        if (originalOp && originalOp.handDirections && originalOp.handDirections.length > 0) {
            const firstHandDirection = originalOp.handDirections[0];
            if (!firstHandDirection) {
                return;
            }
            const handVec = new THREE.Vector2(firstHandDirection[0], firstHandDirection[1]);
            // Line vector in local coords is (1, 0) relative to rotation? 
            // No, we need to check alignment in world coords or pre-rotation.
            // But here we are inside createDoor which is already rotated.
            // Actually, simpler: createDoor is called with 'angle'.
            // The 'line' passed to createDoor is not available directly, but we have 'width'.
            // We can infer the line vector from the rotation 'angle'.
            // Line Vector = (cos(angle), sin(angle)).

            const lineVec = new THREE.Vector2(Math.cos(angle), Math.sin(angle));
            if (handVec.dot(lineVec) > 0) {
                // User Feedback: Previous logic was reversed.
                // If Hand aligns with Line, Hinge should be at End (Right).
                isHingeAtStart = false;
            } else {
                // If Hand opposes Line, Hinge should be at Start (Left).
                isHingeAtStart = true;
            }
        }

        const pivotGroup = new THREE.Group();
        if (isHingeAtStart) {
            pivotGroup.position.set(-width / 2, 0, 0); // Left Pivot
        } else {
            pivotGroup.position.set(width / 2, 0, 0); // Right Pivot
        }

        const panelGeo = new THREE.BoxGeometry(panelWidth, panelThickness, height - frameThickness);
        const panelMat = this.materials.get('doorPanel');
        const panel = new THREE.Mesh(panelGeo, panelMat);

        // Panel position relative to pivot
        if (isHingeAtStart) {
            // Pivot Left. Panel extends Right. Center at +panelWidth/2
            panel.position.set(panelWidth / 2, 0, height / 2);
        } else {
            // Pivot Right. Panel extends Left. Center at -panelWidth/2
            panel.position.set(-panelWidth / 2, 0, height / 2);
        }

        this.enableArchitectureLayer(panel);
        pivotGroup.add(panel);

        // 2. Determine Swing Direction
        // Assumption: Open TOWARDS facingDirection.
        // We need to check if facingDirection is "Up" (Local +Y) or "Down" (Local -Y) relative to the door line.

        let openTowardsUp = true; // Default Open Up (Local +Y)
        if (originalOp && originalOp.facingDirection) {
            const faceVec = new THREE.Vector2(originalOp.facingDirection[0], originalOp.facingDirection[1]);
            // Normal Vector (Local +Y) in World Coords
            // Normal = (-sin(angle), cos(angle))
            const normalVec = new THREE.Vector2(-Math.sin(angle), Math.cos(angle));

            if (faceVec.dot(normalVec) < 0) {
                openTowardsUp = false; // Facing Down
            }
        }

        // Calculate Swing Angle
        // If Hinge Left:
        //   Open Up -> CCW (+90)
        //   Open Down -> CW (-90)
        // If Hinge Right:
        //   Open Up -> CW (-90)
        //   Open Down -> CCW (+90)

        let swingAngle = 0;
        let isCCW = true;

        if (isHingeAtStart) { // Left Pivot
            if (openTowardsUp) {
                swingAngle = Math.PI / 2;
                isCCW = true;
            } else {
                swingAngle = -Math.PI / 2;
                isCCW = false;
            }
        } else { // Right Pivot
            if (openTowardsUp) {
                swingAngle = -Math.PI / 2;
                isCCW = false;
            } else {
                swingAngle = Math.PI / 2;
                isCCW = true;
            }
        }


        pivotGroup.rotation.z = swingAngle;
        panelGroup.add(pivotGroup);

        // Draw Arc
        // Center: Pivot X
        // Start Angle: 
        //   Left Pivot (Start): 0
        //   Right Pivot (End): PI
        // End Angle: Start + Swing

        const centerX = isHingeAtStart ? -width / 2 : width / 2;
        const startAngle = isHingeAtStart ? 0 : Math.PI;

        const curve = new THREE.EllipseCurve(
            centerX, 0,
            width, width,
            startAngle, startAngle + swingAngle,
            !isCCW, // EllipseCurve takes aClockwise (true=CW? No, usually false=CCW in Three.js? Wait.)
            // Three.js EllipseCurve(..., aClockwise)
            // aClockwise – Whether the ellipse is clockwise. Default is false (CCW).
            // So if isCCW is true, aClockwise should be false.
            0
        );

        const points = curve.getPoints(32);
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const arc = new THREE.Line(geometry, this.materials.get('swingArc'));
        arc.position.set(0, 0, 20);
        this.enableArchitectureLayer(arc);

        panelGroup.add(arc);

        root.add(panelGroup);

        // AI Vision Panel
        const aiPanelGroup = new THREE.Group();
        aiPanelGroup.position.set(center.x, center.y, 0);
        aiPanelGroup.rotation.z = angle;

        const aiPivotGroup = new THREE.Group();
        if (isHingeAtStart) {
            aiPivotGroup.position.set(-width / 2, 0, 0);
        } else {
            aiPivotGroup.position.set(width / 2, 0, 0);
        }

        const aiPanel = new THREE.Mesh(panelGeo, aiFrameMat);
        if (isHingeAtStart) {
            aiPanel.position.set(panelWidth / 2, 0, height / 2);
        } else {
            aiPanel.position.set(-panelWidth / 2, 0, height / 2);
        }
        this.enableAiLayer(aiPanel);

        aiPivotGroup.add(aiPanel);
        aiPivotGroup.rotation.z = swingAngle;
        aiPanelGroup.add(aiPivotGroup);

        // AI Vision Arc
        const aiArc = new THREE.Line(geometry, this.materials.get('swingArc'));
        const aiArcClone = aiArc.clone();
        aiArcClone.position.set(0, 0, 20);
        this.enableAiLayer(aiArcClone);
        aiPanelGroup.add(aiArcClone);

        aiRoot.add(aiPanelGroup);

        // Bounds 仅用于家具模块，门窗使用 Outline 描边
    }

    private createWindow(center: THREE.Vector2, width: number, height: number, angle: number, originalOp?: Opening) {
        const root = new THREE.Group();
        root.rotation.x = -Math.PI / 2;

        if (originalOp) {
            root.userData = {
                id: originalOp.id,
                type: 'window',
                data: originalOp
            };
        }

        this.scene.add(root);

        // --- AI Vision Group ---
        const aiRoot = new THREE.Group();
        aiRoot.rotation.x = -Math.PI / 2;
        if (originalOp) {
            aiRoot.userData = { id: originalOp.id, type: 'window', data: originalOp };
        }
        this.scene.add(aiRoot);

        const frameThickness = 50;
        const frameDepth = 100;
        const sillHeight = 900;

        const group = new THREE.Group();
        group.position.set(center.x, center.y, 0);
        group.rotation.z = angle;

        const frameMat = this.materials.get('windowFrame');

        const bottom = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        bottom.position.set(0, 0, sillHeight);
        this.enableArchitectureLayer(bottom);
        group.add(bottom);

        const top = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), frameMat);
        top.position.set(0, 0, sillHeight + height);
        this.enableArchitectureLayer(top);
        group.add(top);

        const sideGeo = new THREE.BoxGeometry(frameThickness, frameDepth, height);
        const left = new THREE.Mesh(sideGeo, frameMat);
        left.position.set(-width / 2 + frameThickness / 2, 0, sillHeight + height / 2);
        this.enableArchitectureLayer(left);
        group.add(left);

        const right = new THREE.Mesh(sideGeo, frameMat);
        right.position.set(width / 2 - frameThickness / 2, 0, sillHeight + height / 2);
        this.enableArchitectureLayer(right);
        group.add(right);

        const glassGeo = new THREE.BoxGeometry(width - frameThickness * 2, 20, height - frameThickness * 2);
        const glass = new THREE.Mesh(glassGeo, this.materials.get('glass'));
        glass.position.set(0, 0, sillHeight + height / 2);
        this.enableArchitectureLayer(glass);
        group.add(glass);

        root.add(group);

        // AI Vision Window
        const aiGroup = new THREE.Group();
        aiGroup.position.set(center.x, center.y, 0);
        aiGroup.rotation.z = angle;

        const aiFrameMat = this.materials.get('ai_window');

        const aiBottom = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), aiFrameMat);
        aiBottom.position.set(0, 0, sillHeight);
        this.enableAiLayer(aiBottom);
        aiGroup.add(aiBottom);

        const aiTop = new THREE.Mesh(new THREE.BoxGeometry(width, frameDepth, frameThickness), aiFrameMat);
        aiTop.position.set(0, 0, sillHeight + height);
        this.enableAiLayer(aiTop);
        aiGroup.add(aiTop);

        const aiLeft = new THREE.Mesh(sideGeo, aiFrameMat);
        aiLeft.position.set(-width / 2 + frameThickness / 2, 0, sillHeight + height / 2);
        this.enableAiLayer(aiLeft);
        aiGroup.add(aiLeft);

        const aiRight = new THREE.Mesh(sideGeo, aiFrameMat);
        aiRight.position.set(width / 2 - frameThickness / 2, 0, sillHeight + height / 2);
        this.enableAiLayer(aiRight);
        aiGroup.add(aiRight);

        // const aiGlass = new THREE.Mesh(glassGeo, this.materials.get('glass')); // Reuse glass or opaque?
        // AI usually prefers opaque segmentation. Let's use ai_window color but maybe darker?
        // Or just same ai_window material.
        const aiGlassMesh = new THREE.Mesh(glassGeo, aiFrameMat);
        aiGlassMesh.position.set(0, 0, sillHeight + height / 2);
        this.enableAiLayer(aiGlassMesh);
        aiGroup.add(aiGlassMesh);

        aiRoot.add(aiGroup);

        // Bounds 仅用于家具模块，门窗使用 Outline 描边
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

        this.enableFurnitureLayer(mesh);
        if (this.buildOptions.includeBounds) {
            this.createModuleBoundsLine(mod);
            // 添加朝向箭头
            this.createFacingArrow(mod);
        }

        this.scene.add(mesh);

        // --- AI Vision Mesh ---
        const aiMesh = new THREE.Mesh(geometry, this.materials.get('ai_module'));
        aiMesh.rotation.x = -Math.PI / 2;
        aiMesh.userData = mesh.userData;
        this.enableAiLayer(aiMesh);
        this.scene.add(aiMesh);

        // --- SVG 模块渲染 ---
        // 异步渲染SVG（不阻塞模块创建）
        if (this.buildOptions.includeSvg) {
            this.svgRenderer.renderModuleSVG(mod).catch(error => {
                console.error(`[SceneBuilder] Failed to render SVG for module ${mod.id}:`, error);
            });
        }
    }

    /**
     * 为家具模块创建朝向箭头（简约风格）
     */
    private createFacingArrow(mod: Module) {
        // 计算模块中心
        let cx = 0, cy = 0;
        mod.bounds.forEach(p => {
            cx += p[0];
            cy += p[1];
        });
        cx /= mod.bounds.length;
        cy /= mod.bounds.length;

        // 解析朝向角度（读取期只认 facing.value）
        const angle = facingToAngle(mod.facing, `SceneBuilder:${mod.id}`);

        // 创建箭头组
        const arrowGroup = new THREE.Group();

        // 样式参数
        const boundsStyle = canvasStyleService.currentStyle.value.layers.bounds;
        const arrowStyle = boundsStyle.arrow;
        const shaftLength = arrowStyle.shaftLength;
        const shaftWidth = arrowStyle.shaftWidth;
        const headLength = arrowStyle.headLength;
        const headWidth = arrowStyle.headWidth;

        // 1. 箭杆 (矩形)
        const shaftShape = new THREE.Shape();
        shaftShape.moveTo(-shaftWidth / 2, 0);
        shaftShape.lineTo(shaftWidth / 2, 0);
        shaftShape.lineTo(shaftWidth / 2, shaftLength);
        shaftShape.lineTo(-shaftWidth / 2, shaftLength);
        shaftShape.closePath();

        const shaftGeo = new THREE.ShapeGeometry(shaftShape);
        // Use bounds layer style (matches bounds outline color)
        const boundsColor = boundsStyle.color;
        const arrowMat = new THREE.MeshBasicMaterial({
            color: boundsColor,
            side: THREE.DoubleSide,
            depthTest: false,
            transparent: true,
            opacity: boundsStyle.opacity
        });
        const shaft = new THREE.Mesh(shaftGeo, arrowMat);
        shaft.layers.set(LayerManager.LAYER_BOUNDS);  // 关键：设置图层
        arrowGroup.add(shaft);

        // 2. 箭头 (三角形)
        const headShape = new THREE.Shape();
        headShape.moveTo(0, shaftLength + headLength);           // 尖端
        headShape.lineTo(-headWidth / 2, shaftLength);           // 左下
        headShape.lineTo(headWidth / 2, shaftLength);            // 右下
        headShape.closePath();

        const headGeo = new THREE.ShapeGeometry(headShape);
        const head = new THREE.Mesh(headGeo, arrowMat);
        head.layers.set(LayerManager.LAYER_BOUNDS);  // 关键：设置图层
        arrowGroup.add(head);

        // 3. 定位和旋转
        arrowGroup.position.set(cx, cy, 0);
        arrowGroup.rotation.z = -angle; // 旋转到正确朝向

        // 整体容器
        const container = new THREE.Group();
        container.add(arrowGroup);
        container.rotation.x = -Math.PI / 2;
        container.position.y = boundsStyle.arrow.height; // Slightly above module top

        container.layers.set(LayerManager.LAYER_BOUNDS);

        this.scene.add(container);
    }
}
