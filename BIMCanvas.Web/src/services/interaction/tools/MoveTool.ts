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

    private state: 'waiting_selection' | 'waiting_base' | 'waiting_dest' = 'waiting_selection';
    private basePoint: THREE.Vector3 | null = null;
    private rubberBand: THREE.Line | null = null;
    private selectedObject: any = null; // Store module data
    private originalObject: THREE.Object3D | null = null; // The actual 3D object

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
        // Ensure we get the latest selection from the store
        this.selectedObject = store.selectedObject;

        console.log("MoveTool Activate. Selected:", this.selectedObject);

        // Check if we have a valid selection (must be a module)
        // Relaxed check: if it has ID and bounds, we treat it as a module
        const isModule = this.selectedObject && (
            this.selectedObject.type === 'module' ||
            (this.selectedObject.userData && this.selectedObject.userData.type === 'module') ||
            (this.selectedObject.id && this.selectedObject.bounds)
        );

        console.log("MoveTool Activate. isModule:", isModule);

        if (isModule) {
            // Handle case where selectedObject might be the ThreeJS object or the data object
            // If it's the ThreeJS object, get data from userData
            if (this.selectedObject.userData && this.selectedObject.userData.data) {
                this.selectedObject = this.selectedObject.userData.data;
            }
            this.startMoveOperation();
        } else {
            this.state = 'waiting_selection';
            store.setPrompt('Select object to move');
            this.domElement.style.cursor = 'default';
        }
    }

    private startMoveOperation() {
        const store = useCanvasStore();
        this.originalObject = this.findObjectById(this.selectedObject.id);

        if (this.originalObject) {
            this.ghostManager.createGhost(this.originalObject);
        }

        this.state = 'waiting_base';
        this.basePoint = null;
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt('Click to set base point');
    }

    deactivate() {
        const store = useCanvasStore();
        this.ghostManager.removeGhost();
        this.removeRubberBand();
        this.domElement.style.cursor = 'default';
        this.basePoint = null;
        this.state = 'waiting_selection';
        store.setPrompt(null);
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

        const point = this.getRayIntersection(event);
        if (!point) return;

        const store = useCanvasStore();

        if (this.state === 'waiting_selection') {
            // Try to select an object
            const hit = this.raycastObject(event);
            if (hit && hit.userData && hit.userData.type === 'module') {
                // Valid selection
                store.setSelectedObject(hit.userData.data);
                this.selectedObject = hit.userData.data;
                this.startMoveOperation();
            } else {
                // Invalid selection (e.g. wall)
                // Optional: Flash warning or just ignore
                console.log("Invalid selection for Move Tool");
            }
            return;
        }

        // Apply Snapping for Base/Dest points
        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_base') {
            this.basePoint = finalPoint;
            this.state = 'waiting_dest';
            this.createRubberBand(this.basePoint);

            // Ghost should now start following mouse relative to base point
            this.ghostManager.setPositionOffset(new THREE.Vector3(0, 0, 0));

            store.setPrompt('Click to set destination point');

        } else if (this.state === 'waiting_dest') {
            this.executeMove(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        const point = this.getRayIntersection(event);
        if (!point) return;

        if (this.state === 'waiting_selection') {
            // Hover effect?
            const hit = this.raycastObject(event);
            if (hit && hit.userData && hit.userData.type === 'module') {
                this.domElement.style.cursor = 'pointer';
            } else {
                this.domElement.style.cursor = 'default';
            }
            return;
        }

        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_dest' && this.basePoint) {
            // Update Rubber Band
            this.updateRubberBand(finalPoint);

            // Update Ghost Position
            const delta = new THREE.Vector3().subVectors(finalPoint, this.basePoint);
            this.ghostManager.setPositionOffset(delta);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    onKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
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

        // Find first mesh that is not a ghost
        const hit = intersects.find(i => i.object instanceof THREE.Mesh && !i.object.userData.isGhost);
        return hit ? hit.object : null;
    }

    private executeMove(destPoint: THREE.Vector3) {
        if (!this.basePoint || !this.selectedObject) return;

        const delta = new THREE.Vector3().subVectors(destPoint, this.basePoint);

        // Update Store
        const store = useCanvasStore();

        // 使用统一坐标转换工具：3D delta -> 2D delta
        const delta2D = deltaToModel(delta);

        const newBounds = this.selectedObject.bounds.map((p: [number, number]) => [
            p[0] + delta2D[0],
            p[1] + delta2D[1]
        ]);

        store.updateModule(this.selectedObject.id, { bounds: newBounds });

        // Update selection to new state
        const updated = store.document?.modules.find(m => m.id === this.selectedObject.id);
        if (updated) {
            store.setSelectedObject(updated);
        }

        // Clear selection after move
        store.setSelectedObject(null);

        console.log("Move executed");
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
        const material = new THREE.LineBasicMaterial({ color: 0xffff00, depthTest: false }); // Yellow line
        this.rubberBand = new THREE.Line(geometry, material);
        this.rubberBand.renderOrder = 999; // On top
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
