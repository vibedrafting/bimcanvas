import * as THREE from 'three';
import { SelectionManager } from './SelectionManager';

export class InteractionService {
    private raycaster: THREE.Raycaster;
    private mouse: THREE.Vector2;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private scene: THREE.Scene;
    private selectionManager: SelectionManager;

    constructor(camera: THREE.Camera, domElement: HTMLElement, scene: THREE.Scene) {
        this.camera = camera;
        this.domElement = domElement;
        this.scene = scene;
        this.raycaster = new THREE.Raycaster();
        this.mouse = new THREE.Vector2();
        this.selectionManager = new SelectionManager(scene);

        this.setupEvents();
    }

    private setupEvents() {
        this.domElement.addEventListener('mousemove', this.onMouseMove.bind(this));
        this.domElement.addEventListener('click', this.onClick.bind(this));
    }

    private onMouseMove(event: MouseEvent) {
        const rect = this.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    }

    private onClick(event: MouseEvent) {
        // Only handle left click
        if (event.button !== 0) return;

        this.raycaster.setFromCamera(this.mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        if (intersects.length > 0) {
            // Filter for selectable objects (e.g., modules)
            // For now, select the first hit that is a Mesh
            const hit = intersects.find(i => i.object instanceof THREE.Mesh);
            if (hit) {
                // Traverse up to find the root object (e.g. module group) if needed
                // For now, just select the object
                this.selectionManager.select(hit.object);
            } else {
                this.selectionManager.clearSelection();
            }
        } else {
            this.selectionManager.clearSelection();
        }
    }

    public update() {
        // Hover effects can go here
    }

    public dispose() {
        this.domElement.removeEventListener('mousemove', this.onMouseMove.bind(this));
        this.domElement.removeEventListener('click', this.onClick.bind(this));
    }
}
