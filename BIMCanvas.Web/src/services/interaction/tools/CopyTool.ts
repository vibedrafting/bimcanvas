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

function generateUUID(): string {
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
        var r = Math.random() * 16 | 0, v = c == 'x' ? r : (r & 0x3 | 0x8);
        return v.toString(16);
    });
}

export class CopyTool implements Tool {
    name = 'Copy';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private snapIndicator: SnapIndicator;
    private axisLockHelper: AxisLockHelper;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;
    private shiftHeld: boolean = false;
    private boundHandleKeyUp: (event: KeyboardEvent) => void;

    // State
    private state: 'multi_selection' | 'waiting_base' | 'waiting_dest' = 'multi_selection';
    private basePoint: THREE.Vector3 | null = null;
    private rubberBand: THREE.Line | null = null;

    // Distance Label
    private distanceLabel: CSS2DObject | null = null;
    private lastMousePoint: THREE.Vector3 | null = null;
    private lastMouseScreenPos: { x: number; y: number } = { x: 0, y: 0 };

    // Data
    private selectedObjects: any[] = [];
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
        this.snapIndicator = new SnapIndicator(scene);
        this.axisLockHelper = new AxisLockHelper(scene);
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
        this.boundHandleKeyUp = this.handleKeyUp.bind(this);
    }

    activate() {
        const store = useCanvasStore();
        window.addEventListener('keyup', this.boundHandleKeyUp);

        if (store.selectedIds.length > 0) {
            this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

            if (this.selectedObjects.length === 0) {
                console.log('No copyable modules selected');
                store.setPrompt('只有家具模块可以复制，请重新选择');
                this.state = 'multi_selection';
                this.domElement.style.cursor = 'default';
                return;
            }

            store.currentOperation = 'copying';
            this.findAllOriginalObjects();
            this.startCopyOperation();
        } else {
            this.state = 'multi_selection';
            store.setPrompt('请选择要复制的对象，按空格/回车确认');
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

    private startCopyOperation() {
        const store = useCanvasStore();

        if (this.originalObjects.length > 0) {
            this.ghostManager.createGhosts(this.originalObjects);
        }

        this.basePoint = this.calculateGroupCenter();
        this.createBasePointMarker(this.basePoint);

        this.state = 'waiting_base';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt(`请点击选择复制基点 (已选${this.selectedObjects.length}个对象)`);

        const debug = useDebugStore();
        debug.log(`[Copy] Building snap points`);
        this.snappingEngine.buildSnapPoints(store.projectData, []);
    }

    private calculateGroupCenter(): THREE.Vector3 {
        if (this.selectedObjects.length === 0) {
            return new THREE.Vector3(0, 0, 0);
        }

        let sumX = 0, sumZ = 0;
        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                const xs = obj.bounds.map((p: [number, number]) => p[0]);
                const ys = obj.bounds.map((p: [number, number]) => p[1]);
                const cx = (Math.min(...xs) + Math.max(...xs)) / 2;
                const cy = (Math.min(...ys) + Math.max(...ys)) / 2;
                sumX += cx;
                sumZ += cy;
            }
        }
        return new THREE.Vector3(
            sumX / this.selectedObjects.length,
            0,
            -sumZ / this.selectedObjects.length
        );
    }

    private createBasePointMarker(point: THREE.Vector3) {
        this.removeVisuals();

        const geometry = new THREE.SphereGeometry(100, 16, 16);
        const material = new THREE.MeshBasicMaterial({
            color: 0x0000ff,
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
        window.removeEventListener('keyup', this.boundHandleKeyUp);

        this.ghostManager.removeGhost();
        this.removeRubberBand();
        this.removeDistanceLabel();
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.basePoint = null;
        this.lastMousePoint = null;
        this.state = 'multi_selection';
        this.selectedObjects = [];
        this.originalObjects = [];
        store.setPrompt(null);
        store.currentOperation = null;

        this.snappingEngine.clear();
        this.snapIndicator.dispose();
        this.snapIndicator = new SnapIndicator(this.scene);
        this.axisLockHelper.dispose();
        this.axisLockHelper = new AxisLockHelper(this.scene);
        this.shiftHeld = false;
    }

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
        if (this.state === 'multi_selection') return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        const store = useCanvasStore();
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_base') {
            this.basePoint = finalPoint;
            this.state = 'waiting_dest';
            this.createRubberBand(this.basePoint);
            store.setPrompt('请点击选择复制目标点');

        } else if (this.state === 'waiting_dest') {
            this.executeCopy(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        this.lastMouseScreenPos = { x: event.clientX, y: event.clientY };
        if (this.state === 'multi_selection') return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (snapResult.snapped && (snapResult.type === 'vertex' || snapResult.type === 'midpoint')) {
            this.snapIndicator.show(snapResult.position);
        } else {
            this.snapIndicator.hide();
        }

        if (this.state === 'waiting_dest' && this.basePoint) {
            const actualPoint = this.axisLockHelper.lock(this.basePoint, finalPoint, this.shiftHeld);
            this.lastMousePoint = actualPoint.clone();

            this.updateRubberBand(actualPoint);
            this.updateDistanceLabel(this.basePoint, actualPoint);

            const delta = new THREE.Vector3().subVectors(actualPoint, this.basePoint);
            this.ghostManager.setPositionOffset(delta);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    private handleKeyUp(event: KeyboardEvent): void {
        if (event.key === 'Shift') {
            this.shiftHeld = false;
            this.axisLockHelper.resetLock();
            this.axisLockHelper.hide();
        }
    }

    onKeyDown(event: KeyboardEvent) {
        const numericManager = NumericInputManager.getInstance();

        if (numericManager.isActive.value) {
            numericManager.handleKeyDown(event);
            return;
        }

        if (this.state === 'waiting_dest' && /^[0-9]$/.test(event.key)) {
            numericManager.startInput({
                unit: 'mm',
                placeholder: '距离',
                onConfirm: (distance) => this.applyNumericCopy(distance),
                onCancel: () => { }
            }, this.lastMouseScreenPos);

            numericManager.inputValue.value = event.key;
            return;
        }

        if (event.key === 'Shift') {
            this.shiftHeld = true;
        }

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
                    store.setPrompt('请先选择要复制的对象');
                    return;
                }

                this.selectedObjects = store.selectedObjects.filter((obj: any) => obj.type === 'module');

                if (this.selectedObjects.length === 0) {
                    store.setPrompt('只有家具模块可以复制，请重新选择');
                    return;
                }

                store.currentOperation = 'copying';
                this.findAllOriginalObjects();
                this.startCopyOperation();
            } else if (this.state === 'waiting_base' && this.basePoint) {
                this.createRubberBand(this.basePoint);
                this.ghostManager.setPositionOffset(new THREE.Vector3(0, 0, 0));
                this.state = 'waiting_dest';
                store.setPrompt('请点击选择复制目标点');
            }
        }
    }

    private applyNumericCopy(distance: number): void {
        if (!this.basePoint || !this.lastMousePoint) return;

        const dx = this.lastMousePoint.x - this.basePoint.x;
        const dz = this.lastMousePoint.z - this.basePoint.z;
        const len = Math.sqrt(dx * dx + dz * dz);

        if (len < 1) return;

        const destPoint = new THREE.Vector3(
            this.basePoint.x + (dx / len) * distance,
            0,
            this.basePoint.z + (dz / len) * distance
        );

        this.executeCopy(destPoint);
    }

    private executeCopy(destPoint: THREE.Vector3) {
        if (!this.basePoint || this.selectedObjects.length === 0) return;

        const delta = new THREE.Vector3().subVectors(destPoint, this.basePoint);
        const store = useCanvasStore();
        const delta2D = deltaToModel(delta);

        store.beginBatchUpdate();

        for (const obj of this.selectedObjects) {
            if (obj.bounds) {
                // Create deep copy of the module
                const newModule = JSON.parse(JSON.stringify(obj));

                // Generate new ID
                newModule.id = generateUUID();

                // Update bounds
                newModule.bounds = obj.bounds.map((p: [number, number]) => [
                    p[0] + delta2D[0],
                    p[1] + delta2D[1]
                ]);

                store.addModule(newModule);
            }
        }

        store.endBatchUpdate();
        store.clearSelection();

        console.log(`Copy executed: ${this.selectedObjects.length} objects copied`);
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
        const material = new THREE.LineDashedMaterial({
            color: 0x00ffff,
            dashSize: 150,
            gapSize: 75,
            depthTest: false
        });
        this.rubberBand = new THREE.Line(geometry, material);
        this.rubberBand.computeLineDistances();
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
                this.rubberBand.computeLineDistances();
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

        const mid = new THREE.Vector3().lerpVectors(start, end, 0.5);
        mid.y += 50;
        this.distanceLabel!.position.copy(mid);

        const dx = end.x - start.x;
        const dz = end.z - start.z;
        const distance = Math.sqrt(dx * dx + dz * dz);

        const div = this.distanceLabel!.element as HTMLDivElement;
        div.textContent = `${Math.round(distance)}`;
    }

    private removeDistanceLabel(): void {
        if (this.distanceLabel) {
            if (this.distanceLabel.element.parentNode) {
                this.distanceLabel.element.parentNode.removeChild(this.distanceLabel.element);
            }
            this.scene.remove(this.distanceLabel);
            this.distanceLabel = null;
        }
    }
}
