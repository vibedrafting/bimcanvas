import * as THREE from 'three';
import { useCanvasStore } from '@/stores/canvasStore';
import type { GridSystem } from './GridSystem';

export class InteractionService {
    private raycaster: THREE.Raycaster;
    private mouse: THREE.Vector2;
    private camera: THREE.Camera;
    private scene: THREE.Scene;
    private canvas: HTMLElement;
    private gridSystem: GridSystem;
    private store = useCanvasStore();

    private isDragging: boolean = false;
    private draggedElementId: string | null = null;
    private selectedObject: THREE.Object3D | null = null;
    private dragPlane: THREE.Plane;
    private dragOffset: THREE.Vector3 = new THREE.Vector3();
    private intersectionPoint: THREE.Vector3 = new THREE.Vector3();

    constructor(
        camera: THREE.Camera,
        scene: THREE.Scene,
        canvas: HTMLElement,
        gridSystem: GridSystem
    ) {
        this.camera = camera;
        this.scene = scene;
        this.canvas = canvas;
        this.gridSystem = gridSystem;

        this.raycaster = new THREE.Raycaster();
        this.mouse = new THREE.Vector2();
        // Drag plane is XY plane (Z=0)
        this.dragPlane = new THREE.Plane(new THREE.Vector3(0, 0, 1), 0);

        this.setupEvents();
    }

    private setupEvents() {
        this.canvas.addEventListener('pointerdown', this.onPointerDown.bind(this));
        this.canvas.addEventListener('pointermove', this.onPointerMove.bind(this));
        this.canvas.addEventListener('pointerup', this.onPointerUp.bind(this));
    }

    private updateMouse(event: PointerEvent) {
        const rect = this.canvas.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    }

    private onPointerDown(event: PointerEvent) {
        // Only handle left click (button 0) for selection/drag
        if (event.button !== 0) return;

        this.updateMouse(event);
        this.raycaster.setFromCamera(this.mouse, this.camera);

        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        // Find the first object that has an ID (module, wall, zone, etc.)
        const hit = intersects.find(i => i.object.userData && i.object.userData.id);

        if (hit) {
            const { id, type } = hit.object.userData;
            this.selectedObject = hit.object; // Store the mesh or line hit

            // If part of a group (like a module might be), maybe select parent? 
            // For now, SceneBuilder attaches userData to Mesh/Line, so this is fine.

            this.store.select(id);
            console.log(`Selected ${type}: ${id}`);

            // If it's a module, start dragging
            if (type === 'module') {
                this.isDragging = true;
                this.draggedElementId = id;

                // Calculate offset
                if (this.raycaster.ray.intersectPlane(this.dragPlane, this.intersectionPoint)) {
                    // We want to drag the object based on its position. 
                    // Note: hit.object might be a child mesh. We should move the object that represents the module.
                    // In SceneBuilder, we add Mesh and Line separately to Scene. 
                    // This simple logic only moves the clicked Mesh. 
                    // TODO: SceneBuilder should probably group Module parts into a THREE.Group.
                    // For now, let's assume we just move the hit object, which is imperfect but works for visual feedback if they are separate.
                    // BETTER: Let's not move THREE objects directly here, but update store? 
                    // Real-time drag usually requires updating THREE object directly for performance, then commit on up.

                    this.dragOffset.copy(this.intersectionPoint).sub(this.selectedObject.position);
                }

                this.gridSystem.setVisible(true);
            }
        } else {
            this.store.select(null);
            this.selectedObject = null;
        }
    }

    private onPointerMove(event: PointerEvent) {
        if (!this.isDragging || !this.selectedObject) return;

        this.updateMouse(event);
        this.raycaster.setFromCamera(this.mouse, this.camera);

        if (this.raycaster.ray.intersectPlane(this.dragPlane, this.intersectionPoint)) {
            const targetPos = this.intersectionPoint.sub(this.dragOffset);
            const snappedPos = this.gridSystem.snapToGrid(targetPos);

            // Update position of the selected object (visual only)
            // Note: If Module consists of multiple meshes (fill + line), this only moves one.
            // We need to find the "peer" object (line/fill) and move it too, or use Groups.
            // For this iteration, let's just move the selected one.
            this.selectedObject.position.copy(snappedPos);

            // TODO: Find sibling with same ID and move it too?
            const id = this.selectedObject.userData.id;
            this.scene.children.forEach(child => {
                if (child.userData.id === id && child !== this.selectedObject) {
                    child.position.copy(snappedPos);
                }
            });
        }
    }

    private onPointerUp() {
        if (this.isDragging) {
            this.isDragging = false;
            this.gridSystem.setVisible(false);
            this.draggedElementId = null;

            // Commit change
            if (this.selectedObject) {
                console.log('Moved object to', this.selectedObject.position);
                // In a real app, we would update the store/backend here
                // this.store.updateElementPosition(this.selectedObject.userData.id, this.selectedObject.position);
            }
        }
    }

    public dispose() {
        this.canvas.removeEventListener('pointerdown', this.onPointerDown.bind(this));
        this.canvas.removeEventListener('pointermove', this.onPointerMove.bind(this));
        this.canvas.removeEventListener('pointerup', this.onPointerUp.bind(this));
    }
}
