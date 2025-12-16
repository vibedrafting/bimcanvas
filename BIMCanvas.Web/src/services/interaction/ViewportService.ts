import * as THREE from 'three';

export class ViewportService {
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private isDragging: boolean = false;
    private lastMousePosition: { x: number, y: number } = { x: 0, y: 0 };

    constructor(camera: THREE.OrthographicCamera, renderer: THREE.WebGLRenderer) {
        this.camera = camera;
        this.renderer = renderer;

        this.setupEvents();
    }

    private setupEvents() {
        const element = this.renderer.domElement;

        element.addEventListener('mousedown', this.onMouseDown.bind(this));
        element.addEventListener('mousemove', this.onMouseMove.bind(this));
        element.addEventListener('mouseup', this.onMouseUp.bind(this));
        element.addEventListener('wheel', this.onWheel.bind(this), { passive: false });
        element.addEventListener('contextmenu', (e) => e.preventDefault());
    }

    private onMouseDown(event: MouseEvent) {
        // Middle mouse or Right mouse for panning
        if (event.button === 1 || event.button === 2) {
            this.isDragging = true;
            this.lastMousePosition = { x: event.clientX, y: event.clientY };
            this.renderer.domElement.style.cursor = 'grabbing';
        }
    }

    private onMouseMove(event: MouseEvent) {
        if (!this.isDragging) return;

        const deltaX = event.clientX - this.lastMousePosition.x;
        const deltaY = event.clientY - this.lastMousePosition.y;

        this.lastMousePosition = { x: event.clientX, y: event.clientY };

        this.pan(deltaX, deltaY);
    }

    private onMouseUp(event: MouseEvent) {
        if (event.button === 1 || event.button === 2) {
            this.isDragging = false;
            this.renderer.domElement.style.cursor = 'default';
        }
    }

    private onWheel(event: WheelEvent) {
        event.preventDefault();
        const zoomScale = 0.95;
        if (event.deltaY > 0) {
            this.camera.zoom *= zoomScale;
        } else {
            this.camera.zoom /= zoomScale;
        }
        this.camera.updateProjectionMatrix();
    }

    private pan(deltaX: number, deltaY: number) {
        const element = this.renderer.domElement;

        // Orthographic size
        const frustumHeight = (this.camera.top - this.camera.bottom) / this.camera.zoom;
        const frustumWidth = (this.camera.right - this.camera.left) / this.camera.zoom;

        const moveX = -(deltaX / element.clientWidth) * frustumWidth;
        const moveZ = -(deltaY / element.clientHeight) * frustumHeight;

        this.camera.position.x += moveX;
        this.camera.position.z += moveZ; // Z is the "Y" on screen
    }

    public update() {
        // No-op for now
    }

    public setTarget(x: number, _y: number, z: number) {
        this.camera.position.x = x;
        this.camera.position.z = z;
    }

    public resetView() {
        this.camera.position.set(0, 10000, 0);
        this.camera.lookAt(0, 0, 0);
        this.camera.zoom = 1;
        this.camera.updateProjectionMatrix();
    }

    public dispose() {
        const element = this.renderer.domElement;
        element.removeEventListener('mousedown', this.onMouseDown);
        element.removeEventListener('mousemove', this.onMouseMove);
        element.removeEventListener('mouseup', this.onMouseUp);
        element.removeEventListener('wheel', this.onWheel);
        element.removeEventListener('contextmenu', (e) => e.preventDefault());
    }
}
