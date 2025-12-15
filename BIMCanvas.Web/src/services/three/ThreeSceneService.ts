import * as THREE from 'three';
import { SceneBuilder } from '../builders/SceneBuilder';
import { useCanvasStore } from '../../stores/canvasStore';
import { watch } from 'vue';

export class ThreeSceneService {
    private container: HTMLElement;
    private scene: THREE.Scene;
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private animationId: number | null = null;
    private sceneBuilder: SceneBuilder;
    private store: ReturnType<typeof useCanvasStore>;

    // Calm Tech Colors
    private readonly BG_COLOR = 0x0a0a0f;
    private readonly AMBIENT_LIGHT_COLOR = 0xffffff;
    private readonly DIR_LIGHT_COLOR = 0xffffff;

    constructor(container: HTMLElement) {
        this.container = container;
        this.store = useCanvasStore();

        // 1. Scene
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(this.BG_COLOR);
        // Adjusted fog density for mm scale (visibility ~20m)
        this.scene.fog = new THREE.FogExp2(this.BG_COLOR, 0.00005);

        // 2. Camera (Orthographic)
        const aspect = container.clientWidth / container.clientHeight;
        const frustumSize = 20000;
        this.camera = new THREE.OrthographicCamera(
            frustumSize * aspect / -2,
            frustumSize * aspect / 2,
            frustumSize / 2,
            frustumSize / -2,
            1,
            50000
        );
        // Move camera high enough to see walls (height 2800)
        this.camera.position.set(0, 0, 10000);
        this.camera.lookAt(0, 0, 0);

        // 3. Renderer
        this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
        this.renderer.setSize(container.clientWidth, container.clientHeight);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.renderer.shadowMap.enabled = true;
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        container.appendChild(this.renderer.domElement);

        // 4. Lighting
        this.setupLighting();

        // 5. Scene Builder
        this.sceneBuilder = new SceneBuilder(this.scene);

        // Initial Demo Scene (if no document loaded)
        if (!this.store.document) {
            this.sceneBuilder.buildDemoScene();
        }

        // 6. Watch for Store Changes
        watch(() => this.store.document, (newDoc) => {
            if (newDoc) {
                console.log('Document changed, rebuilding scene...');
                this.sceneBuilder.buildFromDocument(newDoc);
                this.fitToScreen(newDoc);
            }
        });

        // 7. Events
        window.addEventListener('resize', this.onWindowResize.bind(this));
    }

    private setupLighting() {
        // Soft Ambient Light
        const ambientLight = new THREE.AmbientLight(this.AMBIENT_LIGHT_COLOR, 0.6);
        this.scene.add(ambientLight);

        // Directional Light for subtle shadows
        const dirLight = new THREE.DirectionalLight(this.DIR_LIGHT_COLOR, 0.5);
        dirLight.position.set(5000, 5000, 10000); // Top-right-front
        dirLight.castShadow = true;
        dirLight.shadow.mapSize.width = 2048;
        dirLight.shadow.mapSize.height = 2048;
        dirLight.shadow.camera.near = 0.5;
        dirLight.shadow.camera.far = 50000;
        this.scene.add(dirLight);

        // Hemisphere Light for natural gradient
        const hemiLight = new THREE.HemisphereLight(0xeeeeff, 0x777788, 0.3);
        this.scene.add(hemiLight);
    }

    private fitToScreen(doc: any) {
        // Calculate bounding box of all walls
        if (!doc.walls || doc.walls.length === 0) return;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

        doc.walls.forEach((wall: any) => {
            if (wall.polygon) {
                wall.polygon.forEach((p: any) => {
                    minX = Math.min(minX, p[0]);
                    minY = Math.min(minY, p[1]);
                    maxX = Math.max(maxX, p[0]);
                    maxY = Math.max(maxY, p[1]);
                });
            }
        });

        if (minX === Infinity) return;

        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        const width = maxX - minX;
        const height = maxY - minY;

        // Center camera
        this.camera.position.x = centerX;
        this.camera.position.y = centerY;
        this.camera.lookAt(centerX, centerY, 0);

        // Adjust zoom/frustum to fit
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const padding = 1.2; // 20% padding

        const sizeForHeight = height * padding;
        const sizeForWidth = (width * padding) / aspect;

        const frustumSize = Math.max(sizeForHeight, sizeForWidth);

        this.camera.left = -frustumSize * aspect / 2;
        this.camera.right = frustumSize * aspect / 2;
        this.camera.top = frustumSize / 2;
        this.camera.bottom = -frustumSize / 2;

        this.camera.updateProjectionMatrix();
    }

    private onWindowResize() {
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const frustumSize = 20000;

        this.camera.left = -frustumSize * aspect / 2;
        this.camera.right = frustumSize * aspect / 2;
        this.camera.top = frustumSize / 2;
        this.camera.bottom = -frustumSize / 2;

        this.camera.updateProjectionMatrix();
        this.renderer.setSize(this.container.clientWidth, this.container.clientHeight);
    }

    public animate() {
        this.animationId = requestAnimationFrame(this.animate.bind(this));
        this.renderer.render(this.scene, this.camera);
    }

    public dispose() {
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
        }
        window.removeEventListener('resize', this.onWindowResize.bind(this));
        this.container.removeChild(this.renderer.domElement);
        this.renderer.dispose();
    }
}
