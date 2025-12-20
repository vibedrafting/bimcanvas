import * as THREE from 'three';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { useCanvasStore } from '../../../stores/canvasStore';
import { deltaToModel } from '../../../utils/coordinates';

export class MoveTool implements Tool {
    name = 'Move';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    // 多选支持：新增 multi_selection 状态
    private state: 'multi_selection' | 'waiting_base' | 'waiting_dest' = 'multi_selection';
    private basePoint: THREE.Vector3 | null = null;
    private rubberBand: THREE.Line | null = null;

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
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate() {
        const store = useCanvasStore();

        // 检查是否已有选中对象
        if (store.selectedIds.length > 0) {
            // 已有选择，直接进入移动操作
            this.selectedObjects = [...store.selectedObjects];
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
        this.ghostManager.removeGhost();
        this.removeRubberBand();
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.basePoint = null;
        this.state = 'multi_selection';
        this.selectedObjects = [];
        this.originalObjects = [];
        store.setPrompt(null);
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
        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_base') {
            this.basePoint = finalPoint;
            this.state = 'waiting_dest';
            this.createRubberBand(this.basePoint);
            this.ghostManager.setPositionOffset(new THREE.Vector3(0, 0, 0));
            store.setPrompt('请点击选择移动终点');

        } else if (this.state === 'waiting_dest') {
            this.executeMove(finalPoint);
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

        if (this.state === 'waiting_dest' && this.basePoint) {
            // 更新橡皮筋
            this.updateRubberBand(finalPoint);
            // 更新 Ghost 位置
            const delta = new THREE.Vector3().subVectors(finalPoint, this.basePoint);
            this.ghostManager.setPositionOffset(delta);
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

    private raycastObject(event: MouseEvent): THREE.Object3D | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((event.clientX - rect.left) / rect.width) * 2 - 1,
            -((event.clientY - rect.top) / rect.height) * 2 + 1
        );

        this.raycaster.setFromCamera(mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        // 只允许选择 module 类型
        const hit = intersects.find(i =>
            i.object instanceof THREE.Mesh &&
            i.object.userData.type === 'module' &&
            !i.object.userData.isGhost
        );
        return hit ? hit.object : null;
    }

    private executeMove(destPoint: THREE.Vector3) {
        if (!this.basePoint || this.selectedObjects.length === 0) return;

        const delta = new THREE.Vector3().subVectors(destPoint, this.basePoint);
        const store = useCanvasStore();

        // 转换 3D 位移到 2D
        const delta2D = deltaToModel(delta);

        // 批量更新所有选中的模块
        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                const newBounds = obj.bounds.map((p: [number, number]) => [
                    p[0] + delta2D[0],
                    p[1] + delta2D[1]
                ]);
                store.updateModule(obj.id, { bounds: newBounds });
            }
        }

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
        const material = new THREE.LineBasicMaterial({ color: 0xffff00, depthTest: false });
        this.rubberBand = new THREE.Line(geometry, material);
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
}
