import * as THREE from 'three';
import type { CanvasDocument, Wall, Column, Module, Point2D, Opening } from '../../types/canvas';
import { LayerManager } from '../three/LayerManager';
import { themeService } from '../theme/ThemeService';

export class SceneBuilder {
    private scene: THREE.Scene;
    private materials: Map<string, THREE.Material>;
    private boxHelpers: THREE.BoxHelper[] = [];

    // Constants
    private readonly WALL_HEIGHT = 2800;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.materials = new Map();
        this.initMaterials();
    }

    private initMaterials() {
        // 从 ThemeService 获取当前主题配色
        const colors = themeService.currentTheme.value.scene;

        this.materials.set('wall', new THREE.MeshStandardMaterial({
            color: colors.wall,
            roughness: 0.8,
            metalness: 0.1
        }));

        this.materials.set('column', new THREE.MeshStandardMaterial({
            color: colors.column,
            roughness: 0.8,
            metalness: 0.1
        }));

        this.materials.set('module', new THREE.MeshStandardMaterial({
            color: colors.module,
            roughness: 0.6,
            metalness: 0.2,
            side: THREE.DoubleSide
        }));

        this.materials.set('floor', new THREE.MeshStandardMaterial({
            color: colors.floor,
            roughness: 0.9,
            metalness: 0.1
        }));

        this.materials.set('doorFrame', new THREE.MeshStandardMaterial({
            color: colors.doorFrame,
            roughness: 0.8,
            metalness: 0.3
        }));

        this.materials.set('doorPanel', new THREE.MeshStandardMaterial({
            color: colors.doorPanel,
            roughness: 0.7,
            metalness: 0.1
        }));

        this.materials.set('windowFrame', new THREE.MeshStandardMaterial({
            color: colors.windowFrame,
            roughness: 0.8,
            metalness: 0.3
        }));

        this.materials.set('glass', new THREE.MeshPhysicalMaterial({
            color: colors.glass,
            metalness: 0.1,
            roughness: 0.1,
            transmission: 0.6,
            transparent: true,
            opacity: 0.6
        }));

        this.materials.set('swingArc', new THREE.LineBasicMaterial({
            color: colors.swingArc,
            opacity: 0.3,
            transparent: true,
            linewidth: 1
        }));

        // --- AI Vision Materials (Basic Material for flat shading) ---
        const aiColors = themeService.currentTheme.value.aiVision;

        this.materials.set('ai_wall', new THREE.MeshBasicMaterial({ color: aiColors.wall }));
        this.materials.set('ai_column', new THREE.MeshBasicMaterial({ color: aiColors.column }));
        this.materials.set('ai_module', new THREE.MeshBasicMaterial({ color: aiColors.module }));
        this.materials.set('ai_door', new THREE.MeshBasicMaterial({ color: aiColors.door }));
        this.materials.set('ai_window', new THREE.MeshBasicMaterial({ color: aiColors.window }));
        this.materials.set('ai_slab', new THREE.MeshBasicMaterial({ color: aiColors.slab }));
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

        // (Compass removed)

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

        // (Compass removed)

        this.updateAllHelpers();
    }

    private enableLayers(object: THREE.Object3D) {
        // Enable Default and Model layers
        object.layers.enable(LayerManager.LAYER_MODEL);
    }

    private enableAiLayer(object: THREE.Object3D) {
        object.layers.set(LayerManager.LAYER_AI_VISION);
    }

    private createBoundsHelper(object: THREE.Object3D) {
        // 使用 ThemeService 的包围盒颜色
        const boundsColor = themeService.currentTheme.value.scene.bounds;
        const boxHelper = new THREE.BoxHelper(object, boundsColor);
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

        // Set User Data
        mesh.userData = {
            id: wall.id,
            type: 'wall',
            data: wall
        };

        this.enableLayers(mesh);
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

        this.enableLayers(mesh);
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

        if (op.type === 0) {
            this.createDoor(center, width, height, angle, op);
        } else {
            this.createWindow(center, width, height, angle, op);
        }
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

        // AI Vision Panel
        const aiPanelGroup = new THREE.Group();
        aiPanelGroup.position.set(center.x, center.y, 0);
        aiPanelGroup.rotation.z = angle;

        const aiPivotGroup = new THREE.Group();
        aiPivotGroup.position.set(-width / 2, 0, 0);

        const aiPanel = new THREE.Mesh(panelGeo, aiFrameMat); // Use same material for simplicity or distinct if needed
        aiPanel.position.set(panelWidth / 2, 0, height / 2);
        this.enableAiLayer(aiPanel);

        aiPivotGroup.add(aiPanel);
        aiPivotGroup.rotation.z = swingAngle;
        aiPanelGroup.add(aiPivotGroup);

        // AI Vision Arc (Optional, maybe skip for AI?)
        // Let's add it for completeness but maybe same color
        const aiArc = new THREE.Line(geometry, this.materials.get('swingArc')); // Reuse swing arc material or new one?
        // Let's use swingArc but ensure it's on AI layer
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

        this.enableLayers(mesh);
        this.createBoundsHelper(mesh);

        // 添加朝向箭头
        this.createFacingArrow(mod);

        this.scene.add(mesh);

        // --- AI Vision Mesh ---
        const aiMesh = new THREE.Mesh(geometry, this.materials.get('ai_module'));
        aiMesh.rotation.x = -Math.PI / 2;
        aiMesh.userData = mesh.userData;
        this.enableAiLayer(aiMesh);
        this.scene.add(aiMesh);
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

        // 解析朝向角度
        let angle = 0; // 默认朝北 (Y 正方向)
        if (typeof mod.facing === 'string') {
            // 语义方向转角度 (平面坐标系，Y 正方向为 0°)
            const directionMap: { [key: string]: number } = {
                'north': 0,
                'northeast': 45,
                'east': 90,
                'southeast': 135,
                'south': 180,
                'southwest': 225,
                'west': 270,
                'northwest': 315
            };
            angle = (directionMap[mod.facing.toLowerCase()] || 0) * Math.PI / 180;
        } else if (Array.isArray(mod.facing) && mod.facing.length >= 2) {
            // 向量转角度
            angle = Math.atan2(mod.facing[0], mod.facing[1]);
        }

        // 创建箭头组
        const arrowGroup = new THREE.Group();

        // 样式参数
        const shaftLength = 250;
        const shaftWidth = 40;
        const headLength = 120;
        const headWidth = 100;

        // 1. 箭杆 (矩形)
        const shaftShape = new THREE.Shape();
        shaftShape.moveTo(-shaftWidth / 2, 0);
        shaftShape.lineTo(shaftWidth / 2, 0);
        shaftShape.lineTo(shaftWidth / 2, shaftLength);
        shaftShape.lineTo(-shaftWidth / 2, shaftLength);
        shaftShape.closePath();

        const shaftGeo = new THREE.ShapeGeometry(shaftShape);
        // 使用 ThemeService 的 bounds 颜色（与 Bounds 框线一致）
        const boundsColor = themeService.currentTheme.value.scene.bounds;
        const arrowMat = new THREE.MeshBasicMaterial({
            color: boundsColor,
            side: THREE.DoubleSide,
            depthTest: false,
            transparent: true,
            opacity: 0.9
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
        container.position.y = 800; // 略高于模块顶部

        container.layers.set(LayerManager.LAYER_BOUNDS);

        this.scene.add(container);
    }
}
