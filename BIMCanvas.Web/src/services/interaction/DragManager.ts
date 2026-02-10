import * as THREE from 'three';
import { SelectionManager } from './SelectionManager';
import { SnappingEngine } from './SnappingEngine';
import { GhostManager } from './GhostManager';
import { ConstraintService } from '../validation/ConstraintService';
import { useCanvasStore } from '../../stores/canvasStore';
import { deltaToModel } from '../../utils/coordinates';

export class DragManager {
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private scene: THREE.Scene;
    private selectionManager: SelectionManager;
    private snappingEngine: SnappingEngine;
    private ghostManager: GhostManager;
    private constraintService: ConstraintService;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;
    private isDragging: boolean = false;
    private dragObject: THREE.Object3D | null = null;
    private offset: THREE.Vector3 = new THREE.Vector3();
    private intersection: THREE.Vector3 = new THREE.Vector3();
    private startPosition: THREE.Vector3 = new THREE.Vector3();

    constructor(camera: THREE.Camera, domElement: HTMLElement, scene: THREE.Scene, selectionManager: SelectionManager) {
        this.camera = camera;
        this.domElement = domElement;
        this.scene = scene;
        this.selectionManager = selectionManager;
        this.snappingEngine = new SnappingEngine();
        this.ghostManager = GhostManager.getInstance(scene);
        this.constraintService = new ConstraintService(scene);
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0); // XZ plane (Y-Up)

        this.setupEvents();
    }

    private onMouseDownBound = this.onMouseDown.bind(this);
    private onMouseMoveBound = this.onMouseMove.bind(this);
    private onMouseUpBound = this.onMouseUp.bind(this);

    private setupEvents() {
        this.domElement.addEventListener('mousedown', this.onMouseDownBound);
        this.domElement.addEventListener('mousemove', this.onMouseMoveBound);
        this.domElement.addEventListener('mouseup', this.onMouseUpBound);
    }

    private onMouseDown(event: MouseEvent) {
        if (event.button !== 0) return; // Only left click

        const mouse = this.getMousePosition(event);
        this.raycaster.setFromCamera(mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        const store = useCanvasStore();
        store.debugMsg += `\nDragStart: ${mouse.x.toFixed(2)},${mouse.y.toFixed(2)} Hits: ${intersects.length}`;

        if (intersects.length > 0) {
            const hit = intersects.find(i => i.object instanceof THREE.Mesh);
            if (hit) {
                // Check if selectable/draggable
                let target = hit.object;
                if (target.parent instanceof THREE.Group) {
                    target = target.parent;
                }

                // Draggability Check
                if (!target.userData.draggable) {
                    return; // Ignore non-draggable objects
                }

                this.isDragging = true;
                this.dragObject = target;

                this.selectionManager.select(this.dragObject);

                if (this.raycaster.ray.intersectPlane(this.plane, this.intersection)) {
                    this.offset.copy(this.intersection).sub(this.dragObject.position);
                    this.startPosition.copy(this.dragObject.position);
                }

                // Create Ghost at original position (Static Ghost)
                this.ghostManager.createGhost(this.dragObject);
            }
        }
    }

    private targetPosition: THREE.Vector3 = new THREE.Vector3();

    public update() {
        if (this.isDragging && this.dragObject) {
            // Smoothly interpolate current position to target position
            // Lerp factor 0.2 gives a nice weight/inertia feel
            this.dragObject.position.lerp(this.targetPosition, 0.2);
        }
    }

    private onMouseMove(event: MouseEvent) {
        if (!this.isDragging || !this.dragObject) return;

        try {
            const mouse = this.getMousePosition(event);
            this.raycaster.setFromCamera(mouse, this.camera);

            if (this.raycaster.ray.intersectPlane(this.plane, this.intersection)) {
                // Calculate new position
                let newPos = this.intersection.sub(this.offset);

                // Apply Snapping
                // Filter out drag object and ghosts
                const snapObjects = this.scene.children.filter(c =>
                    c !== this.dragObject &&
                    !c.userData.isGhost
                );

                const snapResult = this.snappingEngine.snap(newPos, snapObjects);
                if (snapResult.snapped) {
                    newPos = snapResult.position;
                }

                // Update Target Position (instead of direct assignment)
                this.targetPosition.copy(newPos);

                // Ghost stays static (managed by GhostManager)
            }
        } catch (error) {
            console.error('Error in DragManager.onMouseMove:', error);
        }
    }

    private onMouseUp(event: MouseEvent) {
        if (this.isDragging && this.dragObject) {
            try {
                // Finalize position
                const mouse = this.getMousePosition(event);
                this.raycaster.setFromCamera(mouse, this.camera);
                if (this.raycaster.ray.intersectPlane(this.plane, this.intersection)) {
                    let newPos = this.intersection.sub(this.offset);

                    const snapObjects = this.scene.children.filter(c =>
                        c !== this.dragObject &&
                        !c.userData.isGhost
                    );

                    const snapResult = this.snappingEngine.snap(newPos, snapObjects);
                    if (snapResult.snapped) {
                        newPos = snapResult.position;
                    }
                    this.dragObject.position.copy(newPos);
                }

                // Validate
                const validation = this.constraintService.validate(this.dragObject);
                if (!validation.isValid) {
                    console.warn('Constraint Violation:', validation.errors);
                }

                // Calculate Delta
                const delta = this.dragObject.position.clone().sub(this.startPosition);

                // Update Store if moved
                if (delta.lengthSq() > 0.001) {
                    const store = useCanvasStore();
                    const moduleId = this.dragObject.userData.id;
                    const module = store.projectData?.activeScheme?.modules?.find(m => m.uid === moduleId || m.id === moduleId);

                    if (moduleId && module) {
                        // 使用统一坐标转换工具：3D delta -> 2D delta
                        const delta2D = deltaToModel(delta);

                        const updatedBounds = module.bounds.map(p => [p[0] + delta2D[0], p[1] + delta2D[1]] as [number, number]);

                        store.updateModule(moduleId, { bounds: updatedBounds });
                    }
                }

            } catch (error) {
                console.error('Error in DragManager.onMouseUp:', error);
            } finally {
                // Always clean up
                this.isDragging = false;
                this.ghostManager.removeGhost();
                this.dragObject = null;
            }
        }
    }

    private getMousePosition(event: MouseEvent): THREE.Vector2 {
        const rect = this.domElement.getBoundingClientRect();
        return new THREE.Vector2(
            ((event.clientX - rect.left) / rect.width) * 2 - 1,
            -((event.clientY - rect.top) / rect.height) * 2 + 1
        );
    }

    public dispose() {
        this.domElement.removeEventListener('mousedown', this.onMouseDownBound);
        this.domElement.removeEventListener('mousemove', this.onMouseMoveBound);
        this.domElement.removeEventListener('mouseup', this.onMouseUpBound);
        this.ghostManager.removeGhost();
    }
}
