import * as THREE from 'three';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { useCanvasStore } from '../../../stores/canvasStore';
import { boundsCenterToWorld, toModel, rotatePoint2D, rotateFacing2D, semanticToVector } from '../../../utils/coordinates';

export class RotateTool implements Tool {
    name = 'Rotate';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
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
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate() {
        const store = useCanvasStore();

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

        // 设置 Ghost 旋转中心
        this.ghostManager.setPivot(this.centerPoint);

        this.state = 'waiting_center';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt(`请点击设置旋转中心 (已选${this.selectedObjects.length}个对象)`);
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
        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_center') {
            this.centerPoint = finalPoint;
            this.updateCenterMarker(this.centerPoint);
            this.ghostManager.setPivot(this.centerPoint);

            this.state = 'waiting_start';
            store.setPrompt('请点击设置旋转起始角度');

        } else if (this.state === 'waiting_start') {
            if (!this.centerPoint) return;
            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            this.startAngle = Math.atan2(vector.z, vector.x);

            this.createStartLine(this.centerPoint, finalPoint);
            this.state = 'waiting_end';
            store.setPrompt('请点击设置旋转终止角度');

        } else if (this.state === 'waiting_end') {
            this.executeRotate(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        // multi_selection 状态下不拦截鼠标事件
        if (this.state === 'multi_selection') {
            return; // 不处理，交给 InteractionService
        }

        const point = this.getRayIntersection(event);
        if (!point) return;

        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_center') {
            // Preview center
        } else if (this.state === 'waiting_start') {
            if (this.centerPoint) {
                this.updateStartLine(this.centerPoint, finalPoint);
            }
        } else if (this.state === 'waiting_end' && this.centerPoint && this.startAngle !== null) {
            this.updateEndLine(this.centerPoint, finalPoint);

            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            const currentAngle = Math.atan2(vector.z, vector.x);
            const deltaRotation = currentAngle - this.startAngle;

            this.ghostManager.setRotation(deltaRotation);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    onKeyDown(event: KeyboardEvent) {
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



    private executeRotate(endPoint: THREE.Vector3) {
        if (!this.centerPoint || this.startAngle === null || this.selectedObjects.length === 0) return;

        const vector = new THREE.Vector3().subVectors(endPoint, this.centerPoint);
        const endAngle = Math.atan2(vector.z, vector.x);
        const deltaRotation = -(endAngle - this.startAngle); // Negate for 2D Math compatibility

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
        const material = new THREE.MeshBasicMaterial({ color: 0x0000ff, depthTest: false });
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
            const material = new THREE.LineDashedMaterial({ color: 0x0000ff, dashSize: 100, gapSize: 50, depthTest: false });
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
            const material = new THREE.LineDashedMaterial({ color: 0x00ff00, dashSize: 100, gapSize: 50, depthTest: false });
            this.endLine = new THREE.Line(geometry, material);
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
    }
}

