import * as THREE from 'three';
import type { Tool } from './Tool';
import type { ModuleDefinition } from '../../ModuleLibraryService';
import { useCanvasStore } from '../../../stores/canvasStore';
import { useDebugStore } from '../../../stores/debugStore';
import { toModel } from '../../../utils/coordinates';
import type { Module, Point2D } from '../../../types/canvas';
import { LayerManager } from '../../three/LayerManager';

function generateUUID(): string {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

/**
 * PlaceTool - 模块放置工具
 *
 * 从模块库选择家具后，在画布上跟随鼠标放置。
 * 支持连续放置（Revit 风格）：放置后继续跟随，按 Esc 退出。
 * 按 R 旋转 90 度。
 *
 * 预览使用 LineLoop 矩形轮廓 + 朝向箭头，与 GhostManager 视觉风格一致。
 */
export class PlaceTool implements Tool {
    name = 'Place';

    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private moduleDef: ModuleDefinition;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    // 预览对象
    private previewGroup: THREE.Group | null = null;

    // 当前旋转角度（数据模型角度，CCW+，弧度）
    private currentRotation: number = 0;

    // 当前鼠标对应的世界坐标
    private currentWorldPoint: THREE.Vector3 | null = null;

    constructor(
        scene: THREE.Scene,
        camera: THREE.Camera,
        domElement: HTMLElement,
        moduleDef: ModuleDefinition
    ) {
        this.scene = scene;
        this.camera = camera;
        this.domElement = domElement;
        this.moduleDef = moduleDef;
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate(): void {
        const store = useCanvasStore();
        const debug = useDebugStore();

        store.currentOperation = 'placing';
        store.clearSelection();
        store.setPrompt(`放置 "${this.moduleDef.name}" — 点击放置，R 旋转，Esc 退出`);

        this.domElement.style.cursor = 'crosshair';
        this.currentRotation = 0;

        this.createPreview();

        debug.log(`[PlaceTool] Activated: ${this.moduleDef.id} (${this.moduleDef.name})`);
    }

    deactivate(): void {
        const store = useCanvasStore();

        this.removePreview();
        this.domElement.style.cursor = 'default';
        store.setPrompt(null);
        store.currentOperation = null;
        this.currentWorldPoint = null;
    }

    /**
     * 创建 OBB 轮廓线预览
     */
    private createPreview(): void {
        this.removePreview();

        const { width, depth } = this.moduleDef.size;
        const hw = width / 2;
        const hd = depth / 2;

        // 矩形轮廓（Three.js XZ 平面，Y=0.5 防 z-fighting）
        // 数据坐标 north = +Y → Three.js -Z，所以 depth 沿 Z 轴
        const outlinePoints = [
            new THREE.Vector3(-hw, 0.5, hd),
            new THREE.Vector3(hw, 0.5, hd),
            new THREE.Vector3(hw, 0.5, -hd),
            new THREE.Vector3(-hw, 0.5, -hd),
        ];

        const outlineGeometry = new THREE.BufferGeometry().setFromPoints(outlinePoints);
        const outlineMaterial = new THREE.LineBasicMaterial({
            color: 0x00aaff,
            depthTest: false,
            transparent: true,
            opacity: 0.9
        });

        const outline = new THREE.LineLoop(outlineGeometry, outlineMaterial);
        outline.renderOrder = 999;

        // 朝向箭头：从中心指向 -Z（= 数据 north）
        const arrowPoints = [
            new THREE.Vector3(0, 0.5, 0),
            new THREE.Vector3(0, 0.5, -hd * 0.6)
        ];
        const arrowGeometry = new THREE.BufferGeometry().setFromPoints(arrowPoints);
        const arrowMaterial = new THREE.LineBasicMaterial({
            color: 0x00ff88,
            depthTest: false,
            transparent: true,
            opacity: 0.7
        });
        const arrow = new THREE.Line(arrowGeometry, arrowMaterial);
        arrow.renderOrder = 999;

        // 箭头尖端
        const tipSize = Math.min(width, depth) * 0.08;
        const tipPoints = [
            new THREE.Vector3(-tipSize, 0.5, -hd * 0.6 + tipSize),
            new THREE.Vector3(0, 0.5, -hd * 0.6),
            new THREE.Vector3(tipSize, 0.5, -hd * 0.6 + tipSize)
        ];
        const tipGeometry = new THREE.BufferGeometry().setFromPoints(tipPoints);
        const tip = new THREE.Line(tipGeometry, arrowMaterial.clone());
        tip.renderOrder = 999;

        this.previewGroup = new THREE.Group();
        this.previewGroup.add(outline);
        this.previewGroup.add(arrow);
        this.previewGroup.add(tip);
        this.previewGroup.userData.isGhost = true;
        this.previewGroup.traverse((child) => {
            child.layers.set(LayerManager.LAYER_MODEL);
        });

        // 初始隐藏，等鼠标进入画布后显示
        this.previewGroup.visible = false;
        this.scene.add(this.previewGroup);
    }

    private removePreview(): void {
        if (this.previewGroup) {
            this.scene.remove(this.previewGroup);
            this.previewGroup.traverse((child) => {
                if (child instanceof THREE.Line || child instanceof THREE.LineLoop) {
                    child.geometry?.dispose();
                    if (Array.isArray(child.material)) {
                        child.material.forEach(m => m.dispose());
                    } else {
                        child.material?.dispose();
                    }
                }
            });
            this.previewGroup = null;
        }
    }

    private updatePreviewPosition(worldPoint: THREE.Vector3): void {
        if (!this.previewGroup) return;
        this.previewGroup.visible = true;
        this.previewGroup.position.copy(worldPoint);
        // 数据模型 CCW+ → Three.js rotation.y 需取反（y→-z 镜像翻转手性）
        this.previewGroup.rotation.y = -this.currentRotation;
    }

    onMouseDown(event: MouseEvent): void {
        if (event.button !== 0) return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        this.executePlace(point);
    }

    onMouseMove(event: MouseEvent): void {
        const point = this.getRayIntersection(event);
        if (!point) return;

        this.currentWorldPoint = point;
        this.updatePreviewPosition(point);
    }

    onMouseUp(_event: MouseEvent): void { }

    onKeyDown(event: KeyboardEvent): void {
        if (event.key === 'Escape') {
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
            return;
        }

        // R 键旋转 90 度（CCW+）
        if (event.key === 'r' || event.key === 'R') {
            this.currentRotation += Math.PI / 2;
            if (this.currentWorldPoint) {
                this.updatePreviewPosition(this.currentWorldPoint);
            }
            const debug = useDebugStore();
            debug.log(`[PlaceTool] Rotation: ${(this.currentRotation * 180 / Math.PI).toFixed(0)}°`);
        }
    }

    /**
     * 执行放置
     */
    private executePlace(worldPoint: THREE.Vector3): void {
        const store = useCanvasStore();
        const debug = useDebugStore();

        // 1. 世界坐标 → 数据坐标
        const center: Point2D = toModel(worldPoint);

        // 2. 计算 bounds（基于 size + rotation）
        const bounds = this.calculateBounds(center, this.moduleDef.size, this.currentRotation);

        // 3. 旋转角 → 语义 facing
        const facing = this.rotationToFacing(this.currentRotation);

        // 4. 构造 Module 对象
        const newModule: Module = {
            id: generateUUID(),
            _internalId: '',  // Server 自动计算
            moduleId: this.moduleDef.id,
            moduleName: this.moduleDef.name,
            bounds: bounds,
            facing: facing,
            rotation: 0,
            items: []
        };

        // 5. 写入 Store → 持久化
        store.beginBatchUpdate();
        store.addModule(newModule);
        void store.endBatchUpdate();

        debug.log(`[PlaceTool] Placed "${this.moduleDef.name}" at (${center[0].toFixed(0)}, ${center[1].toFixed(0)}), facing: ${facing}`);

        // 6. 继续放置（不退出工具）
        store.setPrompt(`已放置 "${this.moduleDef.name}" — 继续点击放置，R 旋转，Esc 退出`);
    }

    /**
     * 基于中心点、尺寸和旋转角度计算 4 顶点 bounds
     */
    private calculateBounds(center: Point2D, size: { width: number; depth: number }, rotation: number): Point2D[] {
        const hw = size.width / 2;
        const hd = size.depth / 2;

        // 未旋转时的本地顶点
        const localPoints: Point2D[] = [
            [-hw, -hd],
            [hw, -hd],
            [hw, hd],
            [-hw, hd]
        ];

        // 2D 旋转矩阵（数据模型 CCW+）
        const cos = Math.cos(rotation);
        const sin = Math.sin(rotation);

        return localPoints.map(([lx, ly]) => {
            const rx = lx * cos - ly * sin;
            const ry = lx * sin + ly * cos;
            return [center[0] + rx, center[1] + ry] as Point2D;
        });
    }

    /**
     * 旋转角度 → 语义 facing 字符串（量化到 8 方向）
     */
    private rotationToFacing(rotation: number): string {
        // 归一化到 [0, 2PI)
        let angle = rotation % (2 * Math.PI);
        if (angle < 0) angle += 2 * Math.PI;

        const deg = angle * 180 / Math.PI;
        const directions = ['north', 'northeast', 'east', 'southeast', 'south', 'southwest', 'west', 'northwest'];
        const index = Math.round(deg / 45) % 8;
        return directions[index];
    }

    private getRayIntersection(event: MouseEvent): THREE.Vector3 | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((event.clientX - rect.left) / rect.width) * 2 - 1,
            -((event.clientY - rect.top) / rect.height) * 2 + 1
        );

        this.raycaster.setFromCamera(mouse, this.camera);
        const intersection = new THREE.Vector3();
        if (this.raycaster.ray.intersectPlane(this.plane, intersection)) {
            return intersection;
        }
        return null;
    }
}
