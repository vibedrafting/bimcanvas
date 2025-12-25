import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { SnapIndicator } from '../SnapIndicator';
import { AxisLockHelper } from '../AxisLockHelper';
import { useCanvasStore } from '../../../stores/canvasStore';
import { useDebugStore } from '../../../stores/debugStore';
import { deltaToModel } from '../../../utils/coordinates';
import { LayerManager } from '../../three/LayerManager';
import { NumericInputManager } from '../NumericInputManager';

export class MoveTool implements Tool {
    name = 'Move';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private snapIndicator: SnapIndicator;  // Phase 2: 吸附指示器
    private axisLockHelper: AxisLockHelper;  // Phase 3: 轴锁定辅助器
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;
    private shiftHeld: boolean = false;  // Phase 3: Shift 键状态
    private boundHandleKeyUp: (event: KeyboardEvent) => void;  // Phase 3: 绑定的事件处理器

    // 多选支持：新增 multi_selection 状态
    private state: 'multi_selection' | 'waiting_base' | 'waiting_dest' = 'multi_selection';
    private basePoint: THREE.Vector3 | null = null;
    private rubberBand: THREE.Line | null = null;

    // 距离标注
    private distanceLabel: CSS2DObject | null = null;
    // 记录最后鼠标位置（用于方向计算）
    private lastMousePoint: THREE.Vector3 | null = null;
    // 记录最后鼠标屏幕位置（用于浮动输入框定位）
    private lastMouseScreenPos: { x: number; y: number } = { x: 0, y: 0 };

    // 存储多个选中对象数据
    private selectedObjects: any[] = [];
    // 存储多个 3D 对象引用
    private visuals: THREE.Object3D[] = [];
    private originalObjects: THREE.Object3D[] = [];

    constructor(
        scene: THREE.Scene,
        camera: THREE.Camera,
        domElement: HTMLElement,
        ghostManager: GhostManager
    ) {
        this.scene = scene;
        this.camera = camera;
        this.domElement = domElement;
        this.ghostManager = ghostManager;
        this.snappingEngine = new SnappingEngine();
        this.snapIndicator = new SnapIndicator(scene);  // Phase 2: 初始化吸附指示器
        this.axisLockHelper = new AxisLockHelper(scene);  // Phase 3
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
        this.boundHandleKeyUp = this.handleKeyUp.bind(this);  // Phase 3
    }

    activate() {
        const store = useCanvasStore();

        // Phase 3: 绑定 keyup 事件
        window.addEventListener('keyup', this.boundHandleKeyUp);

        // 检查是否已有选中对象
        if (store.selectedIds.length > 0) {
            // 1. 过滤只保留 module 类型
            this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

            if (this.selectedObjects.length === 0) {
                // 有选择但没有有效的家具模块
                console.log('No movable modules selected');
                store.setPrompt('只有家具模块可以移动，请重新选择');
                this.state = 'multi_selection';
                this.domElement.style.cursor = 'default';
                return;
            }

            // 2. 有效选择，设置状态并开始
            store.currentOperation = 'moving';
            this.findAllOriginalObjects();
            this.startMoveOperation();
        } else {
            // 无选择，进入多选阶段
            this.state = 'multi_selection';
            store.setPrompt('请选择要移动的对象，按空格/回车确认');
            this.domElement.style.cursor = 'default';
        }
    }

    private findAllOriginalObjects() {
        this.originalObjects = [];
        for (const obj of this.selectedObjects) {
            const threeObj = this.findObjectById(obj.id);
            if (threeObj) {
                this.originalObjects.push(threeObj);
            }
        }
    }

    private startMoveOperation() {
        const store = useCanvasStore();

        if (this.originalObjects.length > 0) {
            this.ghostManager.createGhosts(this.originalObjects);
        }

        // 计算默认起点：所有选中对象的几何中心
        this.basePoint = this.calculateGroupCenter();
        this.createBasePointMarker(this.basePoint);

        this.state = 'waiting_base';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt(`请点击选择移动基点 (已选${this.selectedObjects.length}个对象)`);

        // Revit-Lite: 构建吸附点索引（包含墙柱门窗 + 包括自己的模块，不排除）
        const debug = useDebugStore();
        debug.log(`[Move] Building snap points, including selected modules for self-snapping`);
        this.snappingEngine.buildSnapPoints(store.projectData, []); // 不排除任何模块的吸附点
    }

    private calculateGroupCenter(): THREE.Vector3 {
        if (this.selectedObjects.length === 0) {
            return new THREE.Vector3(0, 0, 0);
        }

        // 计算所有模块的几何中心
        let sumX = 0, sumZ = 0;
        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                // bounds 是 [[x,y], ...] 格式，计算中心
                const xs = obj.bounds.map((p: [number, number]) => p[0]);
                const ys = obj.bounds.map((p: [number, number]) => p[1]);
                const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
                const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
                sumX += cx;
                sumZ += cy;
            }
        }
        // 返回世界坐标（y 轴为 0，x/z 对应模型的 x/y）
        return new THREE.Vector3(
            sumX / this.selectedObjects.length,
            0,
            -sumZ / this.selectedObjects.length  // y 轴需要取反（模型坐标系 y 向上，世界坐标系 z 向下）
        );
    }

    private createBasePointMarker(point: THREE.Vector3) {
        this.removeVisuals(); // Clear existing

        const geometry = new THREE.SphereGeometry(100, 16, 16); // 100mm radius
        const material = new THREE.MeshBasicMaterial({
            color: 0x0000ff, // Blue
            transparent: true,
            opacity: 0.5,
            depthTest: false,
            depthWrite: false
        });
        const marker = new THREE.Mesh(geometry, material);
        marker.position.copy(point);
        marker.renderOrder = 999;
        this.scene.add(marker);
        this.visuals.push(marker);
    }

    private removeVisuals() {
        this.visuals.forEach(v => {
            if (v.parent) v.parent.remove(v);
            if (v instanceof THREE.Mesh) {
                v.geometry.dispose();
                if (Array.isArray(v.material)) {
                    v.material.forEach(m => m.dispose());
                } else {
                    v.material.dispose();
                }
            }
        });
        this.visuals = [];
    }

    deactivate() {
        const store = useCanvasStore();

        // Phase 3: 解绑 keyup 事件
        window.removeEventListener('keyup', this.boundHandleKeyUp);

        this.ghostManager.removeGhost();
        this.removeRubberBand();
        this.removeDistanceLabel();  // 清理距离标注
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.basePoint = null;
        this.lastMousePoint = null;
        this.state = 'multi_selection';
        this.selectedObjects = [];
        this.originalObjects = [];
        store.setPrompt(null);
        store.currentOperation = null;

        // Revit-Lite: 清理吸附点缓存
        this.snappingEngine.clear();

        // Phase 2: 清理吸附指示器
        this.snapIndicator.dispose();
        this.snapIndicator = new SnapIndicator(this.scene);  // 重新创建以备下次使用

        // Phase 3: 清理轴锁定辅助器
        this.axisLockHelper.dispose();
        this.axisLockHelper = new AxisLockHelper(this.scene);
        this.shiftHeld = false;
    }

    // 实现 Tool 接口的可选方法
    isInSelectionPhase(): boolean {
        return this.state === 'multi_selection';
    }

    private findObjectById(id: string): THREE.Object3D | null {
        let found: THREE.Object3D | null = null;
        this.scene.traverse((child) => {
            if (child.userData && child.userData.id === id) {
                found = child;
            }
        });
        return found;
    }

    onMouseDown(event: MouseEvent) {
        if (event.button !== 0) return;

        // multi_selection 状态下不拦截鼠标事件，让 InteractionService 处理选择
        if (this.state === 'multi_selection') {
            return; // 不处理，交给 InteractionService
        }

        const point = this.getRayIntersection(event);
        if (!point) return;

        const store = useCanvasStore();

        // Apply Snapping for Base/Dest points
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_base') {
            this.basePoint = finalPoint;
            this.state = 'waiting_dest';
            this.createRubberBand(this.basePoint);
            // 移动不需要 setPivot，直接用 offset
            store.setPrompt('请点击选择移动终点');

        } else if (this.state === 'waiting_dest') {
            this.executeMove(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        // 记录屏幕坐标（用于浮动输入框定位）
        this.lastMouseScreenPos = { x: event.clientX, y: event.clientY };

        // multi_selection 状态下不拦截鼠标事件
        if (this.state === 'multi_selection') {
            return; // 不处理，交给 InteractionService
        }

        const point = this.getRayIntersection(event);
        if (!point) return;

        // 优化：只进行网格捕捉，跳过昂贵的对象边缘捕捉
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        // Phase 2: 更新吸附指示器
        if (snapResult.snapped && (snapResult.type === 'vertex' || snapResult.type === 'midpoint')) {
            this.snapIndicator.show(snapResult.position);
        } else {
            this.snapIndicator.hide();
        }

        if (this.state === 'waiting_dest' && this.basePoint) {
            // 应用轴吸附（软吸附 + Shift 强制锁定）
            const actualPoint = this.axisLockHelper.lock(this.basePoint, finalPoint, this.shiftHeld);

            // 记录最后鼠标位置（用于数值输入时的方向计算）
            this.lastMousePoint = actualPoint.clone();

            // 更新橡皮筋
            this.updateRubberBand(actualPoint);
            // 更新距离标注
            this.updateDistanceLabel(this.basePoint, actualPoint);
            // 更新 Ghost 位置
            const delta = new THREE.Vector3().subVectors(actualPoint, this.basePoint);
            this.ghostManager.setPositionOffset(delta);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    // Phase 3: 处理 Shift 释放
    private handleKeyUp(event: KeyboardEvent): void {
        if (event.key === 'Shift') {
            this.shiftHeld = false;
            this.axisLockHelper.resetLock();
            this.axisLockHelper.hide();
        }
    }

    onKeyDown(event: KeyboardEvent) {
        const numericManager = NumericInputManager.getInstance();

        // 数值输入激活时，交给它处理
        if (numericManager.isActive.value) {
            numericManager.handleKeyDown(event);
            return;
        }

        // 在 waiting_dest 状态检测数字键，启动数值输入
        if (this.state === 'waiting_dest' && /^[0-9]$/.test(event.key)) {
            numericManager.startInput({
                unit: 'mm',
                placeholder: '距离',
                onConfirm: (distance) => this.applyNumericMove(distance),
                onCancel: () => { /* 继续鼠标模式 */ }
            }, this.lastMouseScreenPos);

            // 首个字符传入
            numericManager.inputValue.value = event.key;
            return;
        }

        // Phase 3: 跟踪 Shift 键
        if (event.key === 'Shift') {
            this.shiftHeld = true;
        }

        if (event.key === 'Escape') {
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
            return;
        }

        // 空格或回车
        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            const store = useCanvasStore();

            // 根据当前状态处理
            if (this.state === 'multi_selection') {
                // 确认选择
                if (store.selectedIds.length === 0) {
                    console.log('No objects selected');
                    store.setPrompt('请先选择要移动的对象');
                    return;
                }

                // 过滤只保留 module 类型
                this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

                if (this.selectedObjects.length === 0) {
                    console.log('No movable modules selected');
                    store.setPrompt('只有家具模块可以移动，请重新选择');
                    return;
                }

                store.currentOperation = 'moving';
                this.findAllOriginalObjects();
                this.startMoveOperation();
            } else if (this.state === 'waiting_base' && this.basePoint) {
                // 确认默认基点，进入选择终点阶段
                this.createRubberBand(this.basePoint);
                this.ghostManager.setPositionOffset(new THREE.Vector3(0, 0, 0));
                this.state = 'waiting_dest';
                store.setPrompt('请点击选择移动终点');
            }
        }
    }



    /**
     * 根据输入的精确距离执行移动
     * 方向为基点到当前鼠标位置的方向
     */
    private applyNumericMove(distance: number): void {
        if (!this.basePoint || !this.lastMousePoint) return;

        // 计算方向向量（基点到当前鼠标位置）
        const dx = this.lastMousePoint.x - this.basePoint.x;
        const dz = this.lastMousePoint.z - this.basePoint.z;
        const len = Math.sqrt(dx * dx + dz * dz);

        if (len < 1) return; // 方向不明确

        // 归一化并乘以距离
        const destPoint = new THREE.Vector3(
            this.basePoint.x + (dx / len) * distance,
            0,
            this.basePoint.z + (dz / len) * distance
        );

        this.executeMove(destPoint);
    }

    private executeMove(destPoint: THREE.Vector3) {
        if (!this.basePoint || this.selectedObjects.length === 0) return;

        const delta = new THREE.Vector3().subVectors(destPoint, this.basePoint);
        const store = useCanvasStore();

        // 转换 3D 位移到 2D
        const delta2D = deltaToModel(delta);

        // 使用批量更新，确保多个模块的移动只产生一个历史快照
        store.beginBatchUpdate();

        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                const newBounds = obj.bounds.map((p: [number, number]) => [
                    p[0] + delta2D[0],
                    p[1] + delta2D[1]
                ]);
                store.updateModule(obj.id, { bounds: newBounds });
            }
        }

        store.endBatchUpdate();

        // 清除选择
        store.clearSelection();

        console.log(`Move executed: ${this.selectedObjects.length} objects moved`);
        this.deactivate();
        window.dispatchEvent(new CustomEvent('bimcanvas:tool-completed'));
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

    private createRubberBand(start: THREE.Vector3) {
        const geometry = new THREE.BufferGeometry().setFromPoints([start, start]);
        // CAD/Revit 风格：青色虚线
        const material = new THREE.LineDashedMaterial({
            color: 0x00ffff,  // 青色 (Cyan)
            dashSize: 150,    // 虚线段长度 150mm
            gapSize: 75,      // 间隙长度 75mm
            depthTest: false
        });
        this.rubberBand = new THREE.Line(geometry, material);
        this.rubberBand.computeLineDistances(); // 虚线需要计算线段距离
        this.rubberBand.renderOrder = 999;
        this.scene.add(this.rubberBand);
    }

    private updateRubberBand(end: THREE.Vector3) {
        if (this.rubberBand && this.basePoint) {
            const positionAttribute = this.rubberBand.geometry.attributes.position;
            if (positionAttribute) {
                const positions = positionAttribute.array as Float32Array;
                positions[3] = end.x;
                positions[4] = end.y;
                positions[5] = end.z;
                positionAttribute.needsUpdate = true;
                this.rubberBand.computeLineDistances(); // 更新虚线显示
            }
        }
    }

    private removeRubberBand() {
        if (this.rubberBand) {
            this.scene.remove(this.rubberBand);
            this.rubberBand.geometry.dispose();
            this.rubberBand = null;
        }
    }

    // === 距离标注方法 ===

    private createDistanceLabel(): void {
        const div = document.createElement('div');
        div.className = 'measurement-label';
        div.style.cssText = `
            background: var(--glass-bg, rgba(20, 20, 30, 0.8));
            backdrop-filter: blur(8px);
            -webkit-backdrop-filter: blur(8px);
            padding: 4px 8px;
            border-radius: 4px;
            font-family: var(--font-mono, 'JetBrains Mono', monospace);
            font-size: 12px;
            color: var(--text-primary, #fff);
            pointer-events: none;
            white-space: nowrap;
            border: 1px solid var(--border-subtle, rgba(255,255,255,0.1));
        `;
        this.distanceLabel = new CSS2DObject(div);
        this.distanceLabel.layers.set(LayerManager.LAYER_LABELS);
        this.scene.add(this.distanceLabel);
    }

    private updateDistanceLabel(start: THREE.Vector3, end: THREE.Vector3): void {
        if (!this.distanceLabel) {
            this.createDistanceLabel();
        }

        // 计算中点，稍微抬高避免遮挡虚线
        const mid = new THREE.Vector3().lerpVectors(start, end, 0.5);
        mid.y += 50;
        this.distanceLabel!.position.copy(mid);

        // 计算 XZ 平面距离
        const dx = end.x - start.x;
        const dz = end.z - start.z;
        const distance = Math.sqrt(dx * dx + dz * dz);

        // 格式化显示（不显示单位）
        const div = this.distanceLabel!.element as HTMLDivElement;
        div.textContent = `${Math.round(distance)}`;
    }

    private removeDistanceLabel(): void {
        if (this.distanceLabel) {
            // 移除 DOM 元素
            if (this.distanceLabel.element.parentNode) {
                this.distanceLabel.element.parentNode.removeChild(this.distanceLabel.element);
            }
            this.scene.remove(this.distanceLabel);
            this.distanceLabel = null;
        }
    }
}
