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

    private isDragging: boolean = false;
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
        this.dragPlane = new THREE.Plane(new THREE.Vector3(0, 0, 1), 0); // Floor plane at Z=0? No, our walls are extruded up Z. Floor is XY plane.

        this.setupEvents();
    }

    private setupEvents() {
        this.canvas.addEventListener('pointerdown', this.onPointerDown.bind(this));
        this.canvas.addEventListener('pointermove', this.onPointerMove.bind(this));
        this.canvas.addEventListener('pointerup', this.onPointerUp.bind(this));
    }

    private updateMouse(event: PointerEvent) {
        const rect = this.canvas.getBoundingClientRect();
    }
}

    private onPointerMove(event: PointerEvent) {
    if (!this.isDragging || !this.selectedObject) return;

    this.updateMouse(event);
    this.raycaster.setFromCamera(this.mouse, this.camera);

    if (this.raycaster.ray.intersectPlane(this.dragPlane, this.intersectionPoint)) {
        const targetPos = this.intersectionPoint.sub(this.dragOffset);
        const snappedPos = this.gridSystem.snapToGrid(targetPos);

        this.selectedObject.position.copy(snappedPos);
    }
}

    private onPointerUp() {
    if (this.isDragging) {
        this.isDragging = false;
        this.gridSystem.setVisible(false);

        // Commit change (TODO: Send to store/server)
        if (this.selectedObject) {
            console.log('Moved object to', this.selectedObject.position);
            // useCanvasStore().updateElementPosition(...)
        }
    }
}

    public dispose() {
    this.canvas.removeEventListener('pointerdown', this.onPointerDown.bind(this));
    this.canvas.removeEventListener('pointermove', this.onPointerMove.bind(this));
    this.canvas.removeEventListener('pointerup', this.onPointerUp.bind(this));
}
}
