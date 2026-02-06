import * as THREE from 'three';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnapIndex2D } from '../snap/SnapIndex2D';
import { SnapSolver } from '../snap/SnapSolver';
import { SnapVisual } from '../snap/SnapVisual';
import { useCanvasStore } from '../../../stores/canvasStore';
import { useDebugStore } from '../../../stores/debugStore';
import { semanticToVector } from '../../../utils/coordinates';

export class MirrorTool implements Tool {
    name = 'Mirror';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snapIndex: SnapIndex2D;
    private snapSolver: SnapSolver;
    private snapVisual: SnapVisual;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    // State
    private state: 'multi_selection' | 'waiting_start' | 'waiting_end' = 'multi_selection';
    private startPoint: THREE.Vector3 | null = null;

    // Data
    private selectedObjects: any[] = [];
    private originalObjects: THREE.Object3D[] = [];

    // Visuals
    private mirrorLine: THREE.Line | null = null;
    private startMarker: THREE.Mesh | null = null;

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
        this.snapIndex = new SnapIndex2D();
        this.snapSolver = new SnapSolver(this.snapIndex, this.camera, this.domElement);
        this.snapVisual = new SnapVisual(scene, this.domElement);
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate() {
        const store = useCanvasStore();

        if (store.selectedIds.length > 0) {
            // 1. Filter for modules
            this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

            if (this.selectedObjects.length === 0) {
                store.setPrompt('只有家具模块可以镜像，请重新选择');
                this.state = 'multi_selection';
                this.domElement.style.cursor = 'default';
                return;
            }

            // 2. Start operation
            store.currentOperation = 'mirroring';
            this.findAllOriginalObjects();
            this.startMirrorOperation();
        } else {
            this.state = 'multi_selection';
            store.setPrompt('请选择要镜像的对象，按空格/回车确认');
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

    private findObjectById(id: string): THREE.Object3D | null {
        let found: THREE.Object3D | null = null;
        this.scene.traverse((child) => {
            if (child.userData && child.userData.id === id) {
                found = child;
            }
        });
        return found;
    }

    private startMirrorOperation() {
        const store = useCanvasStore();

        if (this.originalObjects.length > 0) {
            this.ghostManager.createGhosts(this.originalObjects);
        }

        this.state = 'waiting_start';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt(`请点击镜像线起点 (已选${this.selectedObjects.length}个对象)`);

        // CAD Snap: 构建边索引（包含墙柱门窗 + 家具）
        const debug = useDebugStore();
        debug.log(`[Mirror] Building snap edges`);
        this.snapIndex.rebuild(store.projectData);
    }

    deactivate() {
        const store = useCanvasStore();
        this.ghostManager.removeGhost();
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.state = 'multi_selection';
        this.startPoint = null;
        this.selectedObjects = [];
        this.originalObjects = [];
        store.setPrompt(null);
        store.currentOperation = null;

        // CAD Snap: 清理吸附状态与视觉
        this.snapSolver.clear();
        this.snapVisual.dispose();
    }

    isInSelectionPhase(): boolean {
        return this.state === 'multi_selection';
    }

    onMouseDown(event: MouseEvent) {
        if (event.button !== 0) return;
        if (this.state === 'multi_selection') return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        const snapResult = this.snapSolver.snap({ x: event.clientX, y: event.clientY }, point);
        const finalPoint = snapResult ? snapResult.worldPoint : point;

        if (snapResult) {
            this.snapVisual.show(snapResult, { x: event.clientX, y: event.clientY });
        } else {
            this.snapVisual.hide();
        }

        const store = useCanvasStore();

        if (this.state === 'waiting_start') {
            this.startPoint = finalPoint;
            this.createStartMarker(this.startPoint);
            this.createMirrorLine(this.startPoint, this.startPoint); // Init line

            this.state = 'waiting_end';
            store.setPrompt('请点击镜像线终点');

        } else if (this.state === 'waiting_end') {
            this.executeMirror(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        if (this.state === 'multi_selection') return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        // CAD Snap: 屏幕像素捕捉 + 端点/中点/垂足/交点
        const snapResult = this.snapSolver.snap({ x: event.clientX, y: event.clientY }, point);
        const finalPoint = snapResult ? snapResult.worldPoint : point;

        if (snapResult) {
            this.snapVisual.show(snapResult, { x: event.clientX, y: event.clientY });
        } else {
            this.snapVisual.hide();
        }

        if (this.state === 'waiting_end' && this.startPoint) {
            this.updateMirrorLine(this.startPoint, finalPoint);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    onKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
            return;
        }

        if (event.key === ' ' || event.key === 'Enter') {
            event.preventDefault();
            const store = useCanvasStore();

            if (this.state === 'multi_selection') {
                if (store.selectedIds.length === 0) {
                    store.setPrompt('请先选择要镜像的对象');
                    return;
                }

                this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');
                if (this.selectedObjects.length === 0) {
                    store.setPrompt('只有家具模块可以镜像，请重新选择');
                    return;
                }

                store.currentOperation = 'mirroring';
                this.findAllOriginalObjects();
                this.startMirrorOperation();
            }
        }
    }

    private executeMirror(endPoint: THREE.Vector3) {
        if (!this.startPoint || this.selectedObjects.length === 0) return;

        const store = useCanvasStore();

        // 2D Line
        const p1 = { x: this.startPoint.x, y: -this.startPoint.z }; // World to Model 2D (Y is -Z)
        const p2 = { x: endPoint.x, y: -endPoint.z };

        // Line equation: ax + by + c = 0
        const A = p1.y - p2.y;
        const B = p2.x - p1.x;
        const C = -A * p1.x - B * p1.y;
        const len = Math.sqrt(A * A + B * B);
        if (len < 0.001) return;

        // Normalize
        const a = A / len;
        const b = B / len;
        const c = C / len;

        console.log(`Mirror Line: ${a.toFixed(2)}x + ${b.toFixed(2)}y + ${c.toFixed(2)} = 0`);

        // 使用批量更新，确保多个模块的镜像只产生一个历史快照
        store.beginBatchUpdate();

        for (const obj of this.selectedObjects) {
            if (!obj.bounds) continue;

            // 1. Reflect Bounds
            const newBounds = obj.bounds.map((p: [number, number]) => {
                const x = p[0];
                const y = p[1];
                const d = a * x + b * y + c;
                return [
                    x - 2 * a * d,
                    y - 2 * b * d
                ];
            });

            // 2. Reflect Facing
            let facingVector: [number, number];
            if (typeof obj.facing === 'string') {
                facingVector = semanticToVector(obj.facing);
            } else if (Array.isArray(obj.facing)) {
                facingVector = obj.facing as [number, number];
            } else {
                facingVector = [0, 1]; // Default North
            }

            const vx = facingVector[0];
            const vy = facingVector[1];
            // Reflected vector v' = v - 2(v.n)n where n is (a, b)
            const dot = vx * a + vy * b;
            const newFacing = [
                vx - 2 * a * dot,
                vy - 2 * b * dot
            ];

            store.updateModule(obj.id, {
                bounds: newBounds,
                facing: newFacing
            });
        }

        store.endBatchUpdate();

        // Clear Selection
        store.clearSelection();

        console.log(`Mirror executed: ${this.selectedObjects.length} objects`);
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

    // Visuals
    private createStartMarker(point: THREE.Vector3) {
        const geometry = new THREE.SphereGeometry(80, 16, 16);
        const material = new THREE.MeshBasicMaterial({ color: 0xff00ff, depthTest: false });
        this.startMarker = new THREE.Mesh(geometry, material);
        this.startMarker.position.copy(point);
        this.startMarker.renderOrder = 999;
        this.scene.add(this.startMarker);
    }

    private createMirrorLine(start: THREE.Vector3, end: THREE.Vector3) {
        const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
        const material = new THREE.LineDashedMaterial({
            color: 0xff00ff,
            dashSize: 200,
            gapSize: 100,
            depthTest: false
        });
        this.mirrorLine = new THREE.Line(geometry, material);
        this.mirrorLine.computeLineDistances();
        this.mirrorLine.renderOrder = 999;
        this.scene.add(this.mirrorLine);
    }

    private updateMirrorLine(start: THREE.Vector3, end: THREE.Vector3) {
        if (this.mirrorLine) {
            const positions = this.mirrorLine.geometry.attributes.position.array as Float32Array;
            positions[0] = start.x; positions[1] = start.y; positions[2] = start.z;
            positions[3] = end.x; positions[4] = end.y; positions[5] = end.z;
            this.mirrorLine.geometry.attributes.position.needsUpdate = true;
            this.mirrorLine.computeLineDistances();
        }
    }

    private removeVisuals() {
        if (this.startMarker) { this.scene.remove(this.startMarker); this.startMarker = null; }
        if (this.mirrorLine) { this.scene.remove(this.mirrorLine); this.mirrorLine = null; }
    }
}
