import * as THREE from 'three';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { useCanvasStore } from '../../../stores/canvasStore';

export class MoveTool implements Tool {
    name = 'Move';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    private state: 'waiting_base' | 'waiting_dest' = 'waiting_base';
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
        this.selectedObject = store.selectedObject;

        if (!this.selectedObject) {
            console.warn("MoveTool activated without selection");
            this.deactivate();
            return;
        }

        // Find the 3D object in the scene
        // We need a way to find the object by ID. 
        // For now, let's search the scene. Ideally InteractionService passes it.
        this.originalObject = this.findObjectById(this.selectedObject.id);

        if (this.originalObject) {
            this.ghostManager.createGhost(this.originalObject);
            // Hide original? No, usually we keep it or dim it. Ghost is enough indication.
        }

        this.state = 'waiting_base';
        this.basePoint = null;
        this.domElement.style.cursor = 'crosshair';
        console.log("MoveTool Activated: Click base point");
    }

    deactivate() {
        this.ghostManager.removeGhost();
        this.removeRubberBand();
        this.domElement.style.cursor = 'default';
        this.basePoint = null;
        this.state = 'waiting_base';
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

        // Apply Snapping
        // We should snap to everything EXCEPT the ghost
        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_base') {
            this.basePoint = finalPoint;
            this.state = 'waiting_dest';
            this.createRubberBand(this.basePoint);
            console.log("Base point set:", this.basePoint);
        } else if (this.state === 'waiting_dest') {
            this.executeMove(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        const point = this.getRayIntersection(event);
        if (!point) return;

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

    onMouseUp(_event: MouseEvent) {
        // No-op for click-click workflow
    }

    onKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            this.deactivate();
            // Notify InteractionService to clear tool? 
            // We'll handle that via event or callback if needed.
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
        }
    }

    private executeMove(destPoint: THREE.Vector3) {
        if (!this.basePoint || !this.selectedObject) return;

        const delta = new THREE.Vector3().subVectors(destPoint, this.basePoint);

        // Update Store
        const store = useCanvasStore();

        // Calculate new bounds
        // 3D X = 2D X
        // 3D Z = -2D Y
        const delta2D_X = delta.x;
        const delta2D_Y = -delta.z;

        const newBounds = this.selectedObject.bounds.map((p: [number, number]) => [
            p[0] + delta2D_X,
            p[1] + delta2D_Y
        ]);

        store.updateModule(this.selectedObject.id, { bounds: newBounds });

        // Update selection to new state
        const updated = store.document?.modules.find(m => m.id === this.selectedObject.id);
        if (updated) {
            store.setSelectedObject(updated);
        }

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
            const positions = this.rubberBand.geometry.attributes.position.array as Float32Array;
            if (positions) {
                positions[3] = end.x;
                positions[4] = end.y;
                positions[5] = end.z;
                this.rubberBand.geometry.attributes.position.needsUpdate = true;
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
