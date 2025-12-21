import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { SnapIndicator } from '../SnapIndicator';
import { useCanvasStore } from '../../../stores/canvasStore';
import { useDebugStore } from '../../../stores/debugStore';
import { boundsCenterToWorld, toModel, rotatePoint2D, rotateFacing2D, semanticToVector } from '../../../utils/coordinates';
import { LayerManager } from '../../three/LayerManager';
import { NumericInputManager } from '../NumericInputManager';

export class RotateTool implements Tool {
    name = 'Rotate';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private snapIndicator: SnapIndicator;  // Phase 2: 吸附指示器
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    // 多选支持：新增 multi_selection 状态
    private state: 'multi_selection' | 'waiting_center' | 'waiting_start' | 'waiting_end' = 'multi_selection';
    private centerPoint: THREE.Vector3 | null = null;
    private startAngle: number | null = null;

    // 存储多个选中对象数据
    private selectedObjects: any[] = [];
    // 存储多个 3D 对象引用
    private originalObjects: THREE.Object3D[] = [];

    // Visuals
    private centerMarker: THREE.Mesh | null = null;
    private startLine: THREE.Line | null = null;
    private endLine: THREE.Line | null = null;

    // Phase 3: Shift 角度锁定
    private shiftHeld: boolean = false;
    private boundHandleKeyUp: (e: KeyboardEvent) => void;
    private snapAngleLine: THREE.Line | null = null;  // 锁定角度辅助线
    private axisLines: THREE.Group | null = null;     // XY 轴参考线
    private scaleLines: THREE.Group | null = null;    // 15° 刻度圈

    // 角度标注
    private angleLabel: CSS2DObject | null = null;

    // 记录最后鼠标屏幕位置（用于浮动输入框定位）
    private lastMouseScreenPos: { x: number; y: number } = { x: 0, y: 0 };

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
        this.snapIndicator = new SnapIndicator(scene);  // Phase 2
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);

        // Phase 3: 绑定 keyup 处理器
        this.boundHandleKeyUp = this.handleKeyUp.bind(this);
    }

    activate() {
        const store = useCanvasStore();

        // Phase 3: 绑定 keyup 事件
        window.addEventListener('keyup', this.boundHandleKeyUp);

        // 检查是否已有选中对象
        if (store.selectedIds.length > 0) {
            // 过滤只保留 module 类型
            this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

            if (this.selectedObjects.length === 0) {
                store.setPrompt('只有家具模块可以旋转，请重新选择');
                this.state = 'multi_selection';
                this.domElement.style.cursor = 'default';
                return;
            }

            store.currentOperation = 'rotating';
            this.findAllOriginalObjects();
            this.startRotateOperation();
        } else {
            // 无选择，进入多选阶段
            this.state = 'multi_selection';
            store.setPrompt('请选择要旋转的对象，按空格/回车确认');
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

    private startRotateOperation() {
        const store = useCanvasStore();

        if (this.originalObjects.length > 0) {
            this.ghostManager.createGhosts(this.originalObjects);
        }

        // 计算默认旋转中心：所有选中对象的几何中心
        this.centerPoint = this.calculateGroupCenter();
        this.createCenterMarker(this.centerPoint);

        // 注意：不在这里调用 setPivot()！
        // Ghost 初始应与原模块重合，只有在进入旋转阶段（waiting_end）时才设置 pivot

        this.state = 'waiting_center';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt(`请点击设置旋转中心 (已选${this.selectedObjects.length}个对象)`);

        // Revit-Lite: 构建吸附点索引（包含墙柱门窗 + 包括自己的模块，不排除）
        const debug = useDebugStore();
        debug.log(`[Rotate] Building snap points, including selected modules for self-snapping`);
        this.snappingEngine.buildSnapPoints(store.document, []); // 不排除任何模块的吸附点
        // Phase 3: XY 轴和刻度圈只在按住 Shift 时显示，不在这里创建
    }

    private calculateGroupCenter(): THREE.Vector3 {
        if (this.selectedObjects.length === 0) {
            return new THREE.Vector3(0, 0, 0);
        }

        // 计算所有模块的几何中心
        let sumX = 0, sumZ = 0;
        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                const center = boundsCenterToWorld(obj.bounds);
                sumX += center.x;
                sumZ += center.z;
            }
        }
        return new THREE.Vector3(
            sumX / this.selectedObjects.length,
            0,
            sumZ / this.selectedObjects.length
        );
    }

    deactivate() {
        const store = useCanvasStore();
        this.ghostManager.removeGhost();
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.state = 'multi_selection';
        this.centerPoint = null;
        this.startAngle = null;
        this.selectedObjects = [];
        this.originalObjects = [];
        store.setPrompt(null);
        store.currentOperation = null;

        // Revit-Lite: 清理吸附点缓存
        this.snappingEngine.clear();

        // Phase 2: 清理吸附指示器
        this.snapIndicator.dispose();
        this.snapIndicator = new SnapIndicator(this.scene);

        // Phase 3: 解绑 keyup 事件
        window.removeEventListener('keyup', this.boundHandleKeyUp);
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

        // Snap
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_center') {
            this.centerPoint = finalPoint;
            this.updateCenterMarker(this.centerPoint);

            // 注意：不在这里调用 setPivot()！
            // Ghost 应保持与原模块重合，直到进入旋转阶段（waiting_end）

            this.state = 'waiting_start';
            store.setPrompt('请点击设置旋转起始角度');

        } else if (this.state === 'waiting_start') {
            if (!this.centerPoint) return;
            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            let angle = Math.atan2(vector.z, vector.x);

            // Phase 3: Shift 角度锁定
            if (this.shiftHeld) {
                const angleStep = Math.PI / 12; // 15°
                angle = Math.round(angle / angleStep) * angleStep;
            }
            this.startAngle = angle;

            // 使用锁定后的角度画起始线
            const distance = vector.length();
            const lockedPoint = new THREE.Vector3(
                this.centerPoint.x + Math.cos(angle) * distance,
                finalPoint.y,
                this.centerPoint.z + Math.sin(angle) * distance
            );
            this.createStartLine(this.centerPoint, lockedPoint);
            this.removeSnapAngleLine(); // 清理辅助线

            // 关键：在进入旋转阶段前设置 pivot
            // 这确保 Ghost 在 waiting_center/waiting_start 阶段保持原位
            this.ghostManager.setPivot(this.centerPoint);

            this.state = 'waiting_end';
            store.setPrompt('请点击设置旋转终止角度');

        } else if (this.state === 'waiting_end') {
            this.executeRotate(finalPoint);
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

        if (this.state === 'waiting_center') {
            // Preview center
        } else if (this.state === 'waiting_start') {
            if (this.centerPoint) {
                // Phase 3: Shift 角度锁定（起始角度也支持）
                let targetPoint = finalPoint.clone();
                if (this.shiftHeld) {
                    // 显示刻度圈和 XY 轴
                    if (!this.scaleLines) {
                        this.createAxisLines(this.centerPoint);
                        this.createScaleLines(this.centerPoint);
                    }
                    const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
                    let angle = Math.atan2(vector.z, vector.x);
                    const angleStep = Math.PI / 12; // 15°
                    angle = Math.round(angle / angleStep) * angleStep;
                    const distance = vector.length();
                    targetPoint = new THREE.Vector3(
                        this.centerPoint.x + Math.cos(angle) * distance,
                        finalPoint.y,
                        this.centerPoint.z + Math.sin(angle) * distance
                    );
                } else {
                    // 隐藏刻度圈和 XY 轴
                    this.removeAxisLines();
                    this.removeScaleLines();
                }
                this.updateStartLine(this.centerPoint, targetPoint);
            }
        } else if (this.state === 'waiting_end' && this.centerPoint && this.startAngle !== null) {
            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            let currentAngle = Math.atan2(vector.z, vector.x);
            const distance = vector.length();

            if (this.shiftHeld) {
                // Shift 强制锁定到 15° 倍数，显示刻度圈
                if (!this.scaleLines) {
                    this.createAxisLines(this.centerPoint);
                    this.createScaleLines(this.centerPoint);
                }
                const angleStep = Math.PI / 12; // 15°
                currentAngle = Math.round(currentAngle / angleStep) * angleStep;
            } else {
                // 软吸附：只吸附 XY 轴（0°、90°、180°、270°），不显示刻度圈
                this.removeAxisLines();
                this.removeScaleLines();

                const axisStep = Math.PI / 2; // 90°
                const softSnapThreshold = Math.PI / 22.5; // 8° 阈值
                const nearestAxis = Math.round(currentAngle / axisStep) * axisStep;
                const deviation = Math.abs(currentAngle - nearestAxis);

                if (deviation <= softSnapThreshold) {
                    currentAngle = nearestAxis;
                }
            }

            // 计算锁定后的终点位置（让线条也跳跃到锁定角度）
            const lockedEndPoint = new THREE.Vector3(
                this.centerPoint.x + Math.cos(currentAngle) * distance,
                finalPoint.y,
                this.centerPoint.z + Math.sin(currentAngle) * distance
            );
            this.updateEndLine(this.centerPoint, lockedEndPoint);

            const deltaRotation = currentAngle - this.startAngle;

            // 更新角度标注
            this.updateAngleLabel(this.centerPoint, currentAngle, deltaRotation);

            // 使用 setRotation（Pivot 已在用户点击确认旋转中心时设置）
            this.ghostManager.setRotation(deltaRotation);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    // Phase 3: 处理 Shift 释放
    private handleKeyUp(event: KeyboardEvent): void {
        if (event.key === 'Shift') {
            this.shiftHeld = false;
            // 隐藏刻度圈和 XY 轴
            this.removeAxisLines();
            this.removeScaleLines();
        }
    }

    onKeyDown(event: KeyboardEvent) {
        const numericManager = NumericInputManager.getInstance();

        // 数值输入激活时，交给它处理
        if (numericManager.isActive.value) {
            numericManager.handleKeyDown(event);
            return;
        }

        // 在 waiting_end 状态检测数字键，启动数值输入
        if (this.state === 'waiting_end' && /^[0-9]$/.test(event.key)) {
            numericManager.startInput({
                unit: 'deg',
                placeholder: '角度',
                onConfirm: (degrees) => this.applyNumericRotate(degrees),
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
                    store.setPrompt('请先选择要旋转的对象');
                    return;
                }

                // 过滤只保留 module 类型
                this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

                if (this.selectedObjects.length === 0) {
                    console.log('No rotatable modules selected');
                    store.setPrompt('只有家具模块可以旋转，请重新选择');
                    return;
                }

                store.currentOperation = 'rotating';
                this.findAllOriginalObjects();
                this.startRotateOperation();
            } else if (this.state === 'waiting_center' && this.centerPoint) {
                // 确认默认旋转中心，进入选择起始角度阶段
                this.state = 'waiting_start';
                store.setPrompt('请点击设置旋转起始角度');
            }
        }
    }



    /**
     * 根据输入的精确角度执行旋转
     * @param degrees 旋转角度（度数，正值逆时针）
     */
    private applyNumericRotate(degrees: number): void {
        if (!this.centerPoint || this.startAngle === null) return;

        // 度数转弧度
        const radians = degrees * Math.PI / 180;

        // 计算终止角度（从起始角度加上用户输入的旋转量）
        const endAngle = this.startAngle + radians;

        // 构造虚拟终点来调用 executeRotate
        const virtualEndPoint = new THREE.Vector3(
            this.centerPoint.x + Math.cos(endAngle) * 1000,
            0,
            this.centerPoint.z + Math.sin(endAngle) * 1000
        );

        this.executeRotate(virtualEndPoint);
    }

    private executeRotate(endPoint: THREE.Vector3) {
        if (!this.centerPoint || this.startAngle === null || this.selectedObjects.length === 0) return;

        const vector = new THREE.Vector3().subVectors(endPoint, this.centerPoint);
        const endAngle = Math.atan2(vector.z, vector.x);
        const deltaRotation = endAngle - this.startAngle; // 与 Ghost 预览保持一致

        const store = useCanvasStore();
        const center2D = toModel(this.centerPoint);

        // 使用批量更新，确保多个模块的旋转只产生一个历史快照
        store.beginBatchUpdate();

        for (const obj of this.selectedObjects) {
            if (!obj.bounds) continue;

            // 旋转几何
            const newBounds = obj.bounds.map((p: [number, number]) =>
                rotatePoint2D(p, center2D, deltaRotation)
            );

            // 旋转朝向
            let facingVector: [number, number];
            if (typeof obj.facing === 'string') {
                facingVector = semanticToVector(obj.facing);
            } else if (Array.isArray(obj.facing)) {
                facingVector = obj.facing as [number, number];
            } else {
                facingVector = [0, 1]; // Default North
            }
            const newFacing = rotateFacing2D(facingVector, deltaRotation);

            store.updateModule(obj.id, {
                bounds: newBounds,
                facing: newFacing
            });
        }

        store.endBatchUpdate();

        // 清除选择
        store.clearSelection();

        console.log(`Rotate executed: ${this.selectedObjects.length} objects rotated`);
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

    // Visual Helpers
    private createCenterMarker(point: THREE.Vector3) {
        const geometry = new THREE.CircleGeometry(100, 32);
        // Revit 风格：洋红色旋转中心点
        const material = new THREE.MeshBasicMaterial({ color: 0xff00ff, depthTest: false });
        this.centerMarker = new THREE.Mesh(geometry, material);
        this.centerMarker.rotation.x = -Math.PI / 2;
        this.centerMarker.position.copy(point);
        this.centerMarker.renderOrder = 999;
        this.scene.add(this.centerMarker);
    }

    private updateCenterMarker(point: THREE.Vector3) {
        if (this.centerMarker) {
            this.centerMarker.position.copy(point);
        }
    }

    private updateStartLine(start: THREE.Vector3, end: THREE.Vector3) {
        if (!this.startLine) {
            const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
            // CAD/Revit 风格：青色虚线（与移动命令一致）
            const material = new THREE.LineDashedMaterial({
                color: 0x00ffff,  // 青色
                dashSize: 150,
                gapSize: 75,
                depthTest: false
            });
            this.startLine = new THREE.Line(geometry, material);
            this.startLine.computeLineDistances();
            this.startLine.renderOrder = 999;
            this.scene.add(this.startLine);
        } else {
            const positionAttribute = this.startLine.geometry.attributes.position;
            if (positionAttribute) {
                const positions = positionAttribute.array as Float32Array;
                positions[0] = start.x; positions[1] = start.y; positions[2] = start.z;
                positions[3] = end.x; positions[4] = end.y; positions[5] = end.z;
                positionAttribute.needsUpdate = true;
                this.startLine.computeLineDistances();
            }
        }
    }

    private createStartLine(start: THREE.Vector3, end: THREE.Vector3) {
        this.updateStartLine(start, end);
    }

    private updateEndLine(start: THREE.Vector3, end: THREE.Vector3) {
        if (!this.endLine) {
            const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
            // 终止线：也使用青色虚线保持一致
            const material = new THREE.LineDashedMaterial({
                color: 0x00ffff,  // 青色
                dashSize: 150,
                gapSize: 75,
                depthTest: false
            });
            this.endLine = new THREE.Line(geometry, material);
            this.endLine.computeLineDistances();
            this.endLine.renderOrder = 999;
            this.scene.add(this.endLine);
        } else {
            const positionAttribute = this.endLine.geometry.attributes.position;
            if (positionAttribute) {
                const positions = positionAttribute.array as Float32Array;
                positions[0] = start.x; positions[1] = start.y; positions[2] = start.z;
                positions[3] = end.x; positions[4] = end.y; positions[5] = end.z;
                positionAttribute.needsUpdate = true;
                this.endLine.computeLineDistances();
            }
        }
    }

    private removeVisuals() {
        if (this.centerMarker) { this.scene.remove(this.centerMarker); this.centerMarker = null; }
        if (this.startLine) { this.scene.remove(this.startLine); this.startLine = null; }
        if (this.endLine) { this.scene.remove(this.endLine); this.endLine = null; }
        this.removeSnapAngleLine();
        this.removeAxisLines();
        this.removeScaleLines();
        this.removeAngleLabel();  // 清理角度标注
    }

    // Phase 3: 创建 XY 轴参考线（红色 X 轴，蓝色 Z 轴）
    private createAxisLines(center: THREE.Vector3): void {
        this.removeAxisLines();

        const lineLength = 5000; // 5m
        this.axisLines = new THREE.Group();

        // X 轴（红色）
        const xStart = new THREE.Vector3(center.x - lineLength, center.y + 1, center.z);
        const xEnd = new THREE.Vector3(center.x + lineLength, center.y + 1, center.z);
        const xGeometry = new THREE.BufferGeometry().setFromPoints([xStart, xEnd]);
        const xMaterial = new THREE.LineDashedMaterial({
            color: 0xff4444, // 红色
            dashSize: 200,
            gapSize: 100,
            depthTest: false,
            transparent: true,
            opacity: 0.5
        });
        const xLine = new THREE.Line(xGeometry, xMaterial);
        xLine.computeLineDistances();
        xLine.renderOrder = 997;
        this.axisLines.add(xLine);

        // Z 轴（蓝色）
        const zStart = new THREE.Vector3(center.x, center.y + 1, center.z - lineLength);
        const zEnd = new THREE.Vector3(center.x, center.y + 1, center.z + lineLength);
        const zGeometry = new THREE.BufferGeometry().setFromPoints([zStart, zEnd]);
        const zMaterial = new THREE.LineDashedMaterial({
            color: 0x4444ff, // 蓝色
            dashSize: 200,
            gapSize: 100,
            depthTest: false,
            transparent: true,
            opacity: 0.5
        });
        const zLine = new THREE.Line(zGeometry, zMaterial);
        zLine.computeLineDistances();
        zLine.renderOrder = 997;
        this.axisLines.add(zLine);

        this.scene.add(this.axisLines);
    }

    // Phase 3: 移除 XY 轴参考线
    private removeAxisLines(): void {
        if (this.axisLines) {
            this.axisLines.children.forEach(child => {
                if (child instanceof THREE.Line) {
                    child.geometry.dispose();
                    (child.material as THREE.Material).dispose();
                }
            });
            this.scene.remove(this.axisLines);
            this.axisLines = null;
        }
    }

    // Phase 3: 创建 15° 刻度圈（24 条辐射线）
    private createScaleLines(center: THREE.Vector3): void {
        this.removeScaleLines();

        const innerRadius = 800;  // 0.8m 内圈
        const outerRadius = 1200; // 1.2m 外圈
        const angleStep = Math.PI / 12; // 15° = π/12

        this.scaleLines = new THREE.Group();

        for (let i = 0; i < 24; i++) {
            const angle = i * angleStep;
            const isMainAxis = i % 6 === 0; // 0°, 90°, 180°, 270° 为主轴

            const start = new THREE.Vector3(
                center.x + Math.cos(angle) * innerRadius,
                center.y + 1,
                center.z + Math.sin(angle) * innerRadius
            );
            const end = new THREE.Vector3(
                center.x + Math.cos(angle) * outerRadius,
                center.y + 1,
                center.z + Math.sin(angle) * outerRadius
            );

            const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
            const material = new THREE.LineBasicMaterial({
                color: isMainAxis ? 0xffffff : 0x888888, // 主轴白色，次轴灰色
                depthTest: false,
                transparent: true,
                opacity: isMainAxis ? 0.8 : 0.4
            });

            const line = new THREE.Line(geometry, material);
            line.renderOrder = 996;
            this.scaleLines.add(line);
        }

        this.scene.add(this.scaleLines);
    }

    // Phase 3: 移除刻度圈
    private removeScaleLines(): void {
        if (this.scaleLines) {
            this.scaleLines.children.forEach(child => {
                if (child instanceof THREE.Line) {
                    child.geometry.dispose();
                    (child.material as THREE.Material).dispose();
                }
            });
            this.scene.remove(this.scaleLines);
            this.scaleLines = null;
        }
    }

    // Phase 3: 更新锁定角度辅助线
    private updateSnapAngleLine(center: THREE.Vector3, angle: number): void {
        const lineLength = 3000; // 3m 辅助线长度
        const end = new THREE.Vector3(
            center.x + Math.cos(angle) * lineLength,
            center.y + 1, // 略高于地面
            center.z + Math.sin(angle) * lineLength
        );
        const start = center.clone();
        start.y += 1;

        if (!this.snapAngleLine) {
            const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
            const material = new THREE.LineDashedMaterial({
                color: 0x00ff00,  // 绿色表示锁定
                dashSize: 100,
                gapSize: 50,
                depthTest: false
            });
            this.snapAngleLine = new THREE.Line(geometry, material);
            this.snapAngleLine.computeLineDistances();
            this.snapAngleLine.renderOrder = 998;
            this.scene.add(this.snapAngleLine);
        } else {
            const positionAttribute = this.snapAngleLine.geometry.attributes.position;
            if (positionAttribute) {
                const positions = positionAttribute.array as Float32Array;
                positions[0] = start.x; positions[1] = start.y; positions[2] = start.z;
                positions[3] = end.x; positions[4] = end.y; positions[5] = end.z;
                positionAttribute.needsUpdate = true;
                this.snapAngleLine.computeLineDistances();
            }
        }
    }

    // Phase 3: 移除锁定角度辅助线
    private removeSnapAngleLine(): void {
        if (this.snapAngleLine) {
            this.scene.remove(this.snapAngleLine);
            this.snapAngleLine.geometry.dispose();
            (this.snapAngleLine.material as THREE.Material).dispose();
            this.snapAngleLine = null;
        }
    }

    // === 角度标注方法 ===

    private createAngleLabel(): void {
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
        this.angleLabel = new CSS2DObject(div);
        this.angleLabel.layers.set(LayerManager.LAYER_LABELS);
        this.scene.add(this.angleLabel);
    }

    private updateAngleLabel(center: THREE.Vector3, currentAngle: number, deltaAngle: number): void {
        if (!this.angleLabel) {
            this.createAngleLabel();
        }

        // 定位在旋转弧线外侧
        const radius = 1400; // 标注半径，比刻度圈稍大
        const labelAngle = currentAngle - Math.PI / 6; // 偏移 30° 避免遮挡终止线
        const pos = new THREE.Vector3(
            center.x + Math.cos(labelAngle) * radius,
            50, // 稍微抬高
            center.z + Math.sin(labelAngle) * radius
        );
        this.angleLabel!.position.copy(pos);

        // 弧度转度数显示
        const degrees = deltaAngle * 180 / Math.PI;
        const div = this.angleLabel!.element as HTMLDivElement;
        div.textContent = `${degrees.toFixed(1)}°`;
    }

    private removeAngleLabel(): void {
        if (this.angleLabel) {
            if (this.angleLabel.element.parentNode) {
                this.angleLabel.element.parentNode.removeChild(this.angleLabel.element);
            }
            this.scene.remove(this.angleLabel);
            this.angleLabel = null;
        }
    }
}

