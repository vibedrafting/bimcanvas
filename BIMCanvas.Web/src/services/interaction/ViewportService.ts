import * as THREE from 'three';

export class ViewportService {
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private isDragging: boolean = false;
    private lastMousePosition: { x: number, y: number } = { x: 0, y: 0 };
    private lastMiddleClickTime: number = 0;

    // Bound event handlers (用于正确移除监听器)
    private boundOnMouseDown: (e: MouseEvent) => void;
    private boundOnMouseMove: (e: MouseEvent) => void;
    private boundOnMouseUp: (e: MouseEvent) => void;
    private boundOnWheel: (e: WheelEvent) => void;
    private boundOnContextMenu: (e: Event) => void;

    constructor(camera: THREE.OrthographicCamera, renderer: THREE.WebGLRenderer) {
        this.camera = camera;
        this.renderer = renderer;

        // 绑定事件处理器
        this.boundOnMouseDown = this.onMouseDown.bind(this);
        this.boundOnMouseMove = this.onMouseMove.bind(this);
        this.boundOnMouseUp = this.onMouseUp.bind(this);
        this.boundOnWheel = this.onWheel.bind(this);
        this.boundOnContextMenu = (e) => e.preventDefault();

        this.setupEvents();
    }

    private setupEvents() {
        const element = this.renderer.domElement;

        element.addEventListener('mousedown', this.boundOnMouseDown);
        // mousemove and mouseup are now handled globally during drag
        element.addEventListener('wheel', this.boundOnWheel, { passive: false });
        element.addEventListener('contextmenu', this.boundOnContextMenu);
    }

    private onMouseDown(event: MouseEvent) {
        // Middle mouse double click to reset view
        if (event.button === 1) {
            const now = Date.now();
            if (now - this.lastMiddleClickTime < 300) {
                this.resetView();
                return;
            }
            this.lastMiddleClickTime = now;
        }

        // Middle mouse or Right mouse for panning
        if (event.button === 1 || event.button === 2) {
            this.isDragging = true;
            this.lastMousePosition = { x: event.clientX, y: event.clientY };
            this.renderer.domElement.style.cursor = 'grabbing';
            document.body.classList.add('is-dragging');

            // Attach global listeners for smooth dragging outside canvas
            window.addEventListener('mousemove', this.boundOnMouseMove);
            window.addEventListener('mouseup', this.boundOnMouseUp);
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
            document.body.classList.remove('is-dragging');

            // Remove global listeners
            window.removeEventListener('mousemove', this.boundOnMouseMove);
            window.removeEventListener('mouseup', this.boundOnMouseUp);
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
        window.dispatchEvent(new CustomEvent('bimcanvas:reset-view'));
    }

    public dispose() {
        const element = this.renderer.domElement;
        element.removeEventListener('mousedown', this.boundOnMouseDown);
        element.removeEventListener('wheel', this.boundOnWheel);
        element.removeEventListener('contextmenu', this.boundOnContextMenu);

        // Ensure global listeners are cleaned up
        window.removeEventListener('mousemove', this.boundOnMouseMove);
        window.removeEventListener('mouseup', this.boundOnMouseUp);
    }
}
