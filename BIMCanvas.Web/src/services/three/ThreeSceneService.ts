import * as THREE from 'three';
import { SceneBuilder } from '../builders/SceneBuilder';
import { GridBuilder } from '../builders/GridBuilder';
import { SemanticLineBuilder } from '../builders/SemanticLineBuilder';
import { useCanvasStore } from '../../stores/canvasStore';
import { watch } from 'vue';
import { LayerManager } from './LayerManager';
import { InteractionService } from '../interaction/InteractionService';
import { ViewportService } from '../interaction/ViewportService';
import { SelectionManager } from '../interaction/SelectionManager';
import { DragManager } from '../interaction/DragManager';

export class ThreeSceneService {
    private container: HTMLElement;
    private scene: THREE.Scene;
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private animationId: number | null = null;

    // Builders
    private sceneBuilder: SceneBuilder;
    private gridBuilder: GridBuilder;
    private semanticLineBuilder: SemanticLineBuilder;

    private store: ReturnType<typeof useCanvasStore>;

    // Services
    public layerManager: LayerManager;
    private interactionService: InteractionService;
    private viewportService: ViewportService;
    private selectionManager: SelectionManager;
    private dragManager: DragManager;

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
        // Y-Up Top View: Position at +Y, looking at Origin.
        // Up vector should be -Z (North) to match standard map orientation where North is Up on screen.
        this.camera.position.set(0, 10000, 0);
        this.camera.up.set(0, 0, -1);
        this.camera.lookAt(0, 0, 0);

        // 3. Renderer
        this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: false });
        this.renderer.setSize(container.clientWidth, container.clientHeight);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.renderer.shadowMap.enabled = true;
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        container.appendChild(this.renderer.domElement);

        // 4. Initialize Services
        this.layerManager = new LayerManager(this.camera);
        this.viewportService = new ViewportService(this.camera, this.renderer);

        // Selection & Interaction
        this.selectionManager = new SelectionManager(this.scene);
        this.interactionService = new InteractionService(this.camera, this.renderer.domElement, this.scene);
        this.dragManager = new DragManager(this.camera, this.renderer.domElement, this.scene, this.selectionManager);

        // 5. Lighting
        this.setupLighting();

        // 6. Initialize Builders
        this.sceneBuilder = new SceneBuilder(this.scene);
        this.gridBuilder = new GridBuilder(this.scene);
        this.semanticLineBuilder = new SemanticLineBuilder(this.scene);

        // Initial Demo Scene
        if (!this.store.document) {
            this.sceneBuilder.buildDemoScene();
            this.gridBuilder.buildGrid();
        }

        // 7. Watch for Store Changes
        watch(() => this.store.document, (newDoc) => {
            if (newDoc) {
                console.log('Document changed, rebuilding scene...');
                this.sceneBuilder.buildFromDocument(newDoc);
                this.semanticLineBuilder.buildLines(newDoc);
                this.gridBuilder.buildGrid();
                this.fitToScreen(newDoc);
            }
        });

        // 8. Events
        window.addEventListener('resize', this.onWindowResize.bind(this));
        window.addEventListener('bimcanvas:view-mode-change', ((e: CustomEvent) => {
            this.toggleViewMode(e.detail);
        }) as EventListener);

        window.addEventListener('bimcanvas:action-rotate', () => {
            this.interactionService.rotateSelection();
        });

        window.addEventListener('bimcanvas:action-delete', () => {
            this.interactionService.deleteSelection();
        });
    }


    public toggleViewMode(mode: 'human' | 'ai') {
        this.layerManager.setMode(mode);
    }

    private setupLighting() {
        const ambientLight = new THREE.AmbientLight(this.AMBIENT_LIGHT_COLOR, 0.6);
        this.scene.add(ambientLight);

        const dirLight = new THREE.DirectionalLight(this.DIR_LIGHT_COLOR, 0.5);
        dirLight.position.set(5000, 10000, 5000); // High Y for top-down shadows
        dirLight.castShadow = true;
        dirLight.shadow.mapSize.width = 2048;
        dirLight.shadow.mapSize.height = 2048;
        dirLight.shadow.camera.near = 0.5;
        dirLight.shadow.camera.far = 50000;
        this.scene.add(dirLight);

        const hemiLight = new THREE.HemisphereLight(0xeeeeff, 0x777788, 0.3);
        this.scene.add(hemiLight);
    }

    private fitToScreen(doc: any) {
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

        // Map 2D center (x, y) to 3D center (x, 0, -y) because of -90 X rotation
        const center3D = new THREE.Vector3(centerX, 0, -centerY);

        // Update Camera Position (Keep Y high, move X and Z)
        this.camera.position.set(center3D.x, 10000, center3D.z);
        this.camera.lookAt(center3D.x, 0, center3D.z);

        // Update Controls Target via ViewportService
        this.viewportService.setTarget(center3D.x, 0, center3D.z);

        const aspect = this.container.clientWidth / this.container.clientHeight;
        const padding = 1.5;

        const sizeForHeight = height * padding;
        const sizeForWidth = (width * padding) / aspect;

        const frustumSize = Math.max(sizeForHeight, sizeForWidth);

        this.camera.left = -frustumSize * aspect / 2;
        this.camera.right = frustumSize * aspect / 2;
        this.camera.top = frustumSize / 2;
        this.camera.bottom = -frustumSize / 2;

        this.camera.zoom = 1;
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

        // Update services
        this.viewportService.update();
        this.interactionService.update();

        this.renderer.render(this.scene, this.camera);
    }

    public dispose() {
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
        }
        window.removeEventListener('resize', this.onWindowResize.bind(this));
        this.container.removeChild(this.renderer.domElement);
        this.renderer.dispose();
        this.interactionService.dispose();
        this.viewportService.dispose();
        this.dragManager.dispose();
    }
}
