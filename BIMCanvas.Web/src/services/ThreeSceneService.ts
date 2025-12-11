import * as THREE from 'three';
import { EffectComposer } from 'three/examples/jsm/postprocessing/EffectComposer.js';
import { RenderPass } from 'three/examples/jsm/postprocessing/RenderPass.js';
import { UnrealBloomPass } from 'three/examples/jsm/postprocessing/UnrealBloomPass.js';
import { MapControls } from 'three/examples/jsm/controls/MapControls.js';

export class ThreeSceneService {
    private container: HTMLElement;
    private scene: THREE.Scene;
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private composer: EffectComposer;
    private controls: MapControls;
    private animationId: number | null = null;

    constructor(container: HTMLElement) {
        this.container = container;

        // 1. Scene Setup (Deep Space Black)
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x050510); // Deep Space Black

        // 2. Camera Setup (Top-down Orthographic)
        const aspect = container.clientWidth / container.clientHeight;
        const frustumSize = 20000; // 20 meters view
        this.camera = new THREE.OrthographicCamera(
            frustumSize * aspect / -2,
            frustumSize * aspect / 2,
            frustumSize / 2,
            frustumSize / -2,
            1,
            50000
        );
        this.camera.position.set(0, 0, 10000); // Looking down Z
        this.camera.lookAt(0, 0, 0);

        // 3. Renderer Setup
        this.renderer = new THREE.WebGLRenderer({ antialias: true });
        this.renderer.setSize(container.clientWidth, container.clientHeight);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        container.appendChild(this.renderer.domElement);

        // 4. Controls Setup (MapControls for CAD-like feel)
        this.controls = new MapControls(this.camera, this.renderer.domElement);
        this.controls.enableDamping = true; // Smooth motion
        this.controls.dampingFactor = 0.05;
        this.controls.screenSpacePanning = true; // Pan parallel to screen
        this.controls.minZoom = 0.1;
        this.controls.maxZoom = 20;
        this.controls.enableRotate = false; // Disable rotation for 2D view

        // Mouse buttons: Left (Selection - handled by InteractionService), Middle (Pan), Right (Pan)
        this.controls.mouseButtons = {
            LEFT: THREE.MOUSE.ROTATE, // We disable rotate, so this effectively does nothing for controls, allowing InteractionService to pick
            MIDDLE: THREE.MOUSE.PAN,
            RIGHT: THREE.MOUSE.PAN
        };

        // 5. Post-processing (Neon Bloom)
        const renderScene = new RenderPass(this.scene, this.camera);

        const bloomPass = new UnrealBloomPass(
            new THREE.Vector2(container.clientWidth, container.clientHeight),
            1.5, // strength
            0.4, // radius
            0.85 // threshold
        );

        this.composer = new EffectComposer(this.renderer);
        this.composer.addPass(renderScene);
        this.composer.addPass(bloomPass);

        // 6. Handle Resize
        window.addEventListener('resize', this.onWindowResize.bind(this));
    }

    private onWindowResize() {
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const frustumSize = 20000; // Base frustum size, zoom is handled by camera.zoom

        // Update camera frustum based on aspect ratio, maintaining current zoom
        this.camera.left = -frustumSize * aspect / 2;
        this.camera.right = frustumSize * aspect / 2;
        this.camera.top = frustumSize / 2;
        this.camera.bottom = -frustumSize / 2;

        this.camera.updateProjectionMatrix();
        this.renderer.setSize(this.container.clientWidth, this.container.clientHeight);
        this.composer.setSize(this.container.clientWidth, this.container.clientHeight);
    }

    public zoomExtents(objects: THREE.Object3D[]) {
        if (objects.length === 0) return;

        const box = new THREE.Box3();
        objects.forEach(obj => box.expandByObject(obj));

        if (box.isEmpty()) return;

        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());

        // Fit to view
        const maxDim = Math.max(size.x, size.y);
        const aspect = this.container.clientWidth / this.container.clientHeight;

        // Calculate required zoom
        // Frustum height = 20000 (base)
        // We want visible height to cover maxDim * 1.2 (margin)
        // camera.zoom = frustumHeight / visibleHeight

        const padding = 1.2;
        const requiredHeight = maxDim * padding;
        const frustumHeight = 20000; // Matches constructor

        // Adjust for aspect ratio if width is the limiting factor
        let zoom = frustumHeight / requiredHeight;
        if (size.x / aspect > size.y) {
            zoom = (frustumHeight * aspect) / (size.x * padding);
        }

        this.camera.zoom = zoom;
        this.camera.position.set(center.x, center.y, 10000);
        this.camera.updateProjectionMatrix();

        this.controls.target.copy(center);
        this.controls.update();
    }

    public start() {
        const animate = () => {
            this.animationId = requestAnimationFrame(animate);
            this.controls.update(); // Required for damping
            this.composer.render();
        };
        animate();
    }

    public stop() {
        if (this.animationId !== null) {
            cancelAnimationFrame(this.animationId);
            this.animationId = null;
        }
    }

    public dispose() {
        this.stop();
        window.removeEventListener('resize', this.onWindowResize.bind(this));
        this.renderer.dispose();
        this.container.removeChild(this.renderer.domElement);
    }

    public getScene(): THREE.Scene {
        return this.scene;
    }

    public getCamera(): THREE.Camera {
        return this.camera;
    }
}
