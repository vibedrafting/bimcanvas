import * as THREE from 'three';
import { SceneBuilder } from '../builders/SceneBuilder';
import { GridBuilder } from '../builders/GridBuilder';
import { OutlineBuilder } from '../builders/OutlineBuilder';
import { LabelBuilder } from '../builders/LabelBuilder';
import { ZoneBuilder } from '../builders/ZoneBuilder';
import { CSS2DRenderer } from 'three-stdlib';
import { useCanvasStore } from '../../stores/canvasStore';
import { watch } from 'vue';
import { LayerManager } from './LayerManager';
import { InteractionService } from '../interaction/InteractionService';
import { ViewportService } from '../interaction/ViewportService';
import { SelectionManager } from '../interaction/SelectionManager';
import { DragManager } from '../interaction/DragManager';
import { GhostManager } from '../interaction/GhostManager';
import { useDebugStore } from '../../stores/debugStore';
import { themeService } from '../theme/ThemeService';

export class ThreeSceneService {
    private container: HTMLElement;
    private scene: THREE.Scene;
    private camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private labelRenderer: CSS2DRenderer;
    private animationId: number | null = null;

    // Bound event handlers (用于正确移除监听器)
    private boundOnResize: () => void;
    private boundAnimate: () => void;
    private boundEventHandlers: Map<string, EventListener> = new Map();

    // Builders
    private sceneBuilder: SceneBuilder;
    private gridBuilder: GridBuilder;
    private outlineBuilder: OutlineBuilder;
    private labelBuilder: LabelBuilder;
    private zoneBuilder: ZoneBuilder;

    private store: ReturnType<typeof useCanvasStore>;

    // Services
    public layerManager: LayerManager;
    private interactionService: InteractionService;
    private viewportService: ViewportService;
    private selectionManager: SelectionManager;
    private dragManager: DragManager;
    private ghostManager: GhostManager;

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
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        container.appendChild(this.renderer.domElement);

        // 3.1 Label Renderer (CSS2D)
        this.labelRenderer = new CSS2DRenderer();
        this.labelRenderer.setSize(container.clientWidth, container.clientHeight);
        this.labelRenderer.domElement.style.position = 'absolute';
        this.labelRenderer.domElement.style.top = '0px';
        this.labelRenderer.domElement.style.pointerEvents = 'none'; // Allow clicks to pass through
        container.appendChild(this.labelRenderer.domElement);

        // 4. Initialize Services
        this.layerManager = new LayerManager(this.camera);
        this.viewportService = new ViewportService(this.camera, this.renderer);

        // Selection & Interaction
        this.selectionManager = new SelectionManager(this.scene);
        this.interactionService = new InteractionService(this.camera, this.renderer.domElement, this.scene, this.selectionManager);
        this.dragManager = new DragManager(this.camera, this.renderer.domElement, this.scene, this.selectionManager);
        this.ghostManager = GhostManager.getInstance(this.scene);

        // 5. Lighting
        this.setupLighting();

        // 6. Initialize Builders
        this.sceneBuilder = new SceneBuilder(this.scene);
        this.gridBuilder = new GridBuilder(this.scene);
        this.outlineBuilder = new OutlineBuilder(this.scene);
        this.labelBuilder = new LabelBuilder(this.scene);
        this.zoneBuilder = new ZoneBuilder(this.scene);

        // Initial Demo Scene
        if (!this.store.document) {
            this.sceneBuilder.buildDemoScene();
            this.gridBuilder.buildGrid();
        }

        // 7. Watch for Store Changes

        // A. Deep watch for content updates (Rebuild Scene)
        watch(() => this.store.document, (newDoc) => {
            if (newDoc) {
                // console.log('Document updated, rebuilding scene...');
                this.sceneBuilder.buildFromDocument(newDoc);
                this.outlineBuilder.buildLines(newDoc);
                this.labelBuilder.buildLabels(newDoc);
                this.zoneBuilder.buildZones(newDoc);
                this.gridBuilder.buildGrid();
            }
        }, { deep: true });

        // B. Shallow watch for document replacement (Fit to Screen)
        // This only triggers when a NEW document is loaded (reference change),
        // not when modules are moved/rotated (mutation).
        watch(() => this.store.document, (newDoc) => {
            if (newDoc) {
                console.log('New document loaded, fitting to screen...');
                setTimeout(() => {
                    this.fitToScreen(newDoc);
                }, 100);
            }
        });

        // 8. Events - 使用保存的引用以便正确移除
        this.boundOnResize = this.onWindowResize.bind(this);
        this.boundAnimate = this.animate.bind(this);
        window.addEventListener('resize', this.boundOnResize);

        // 全局业务事件 - 保存引用到 Map
        const viewModeHandler = ((e: CustomEvent) => {
            this.toggleViewMode(e.detail);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:view-mode-change', viewModeHandler);
        window.addEventListener('bimcanvas:view-mode-change', viewModeHandler);

        const layerToggleHandler = ((e: CustomEvent) => {
            this.toggleLayer(e.detail.layerId, e.detail.visible);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:layer-toggle', layerToggleHandler);
        window.addEventListener('bimcanvas:layer-toggle', layerToggleHandler);

        const rotateHandler = () => this.interactionService.rotateSelection();
        this.boundEventHandlers.set('bimcanvas:action-rotate', rotateHandler);
        window.addEventListener('bimcanvas:action-rotate', rotateHandler);

        const moveHandler = () => this.interactionService.activateMoveTool();
        this.boundEventHandlers.set('bimcanvas:action-move', moveHandler);
        window.addEventListener('bimcanvas:action-move', moveHandler);

        const deleteHandler = () => this.interactionService.deleteSelection();
        this.boundEventHandlers.set('bimcanvas:action-delete', deleteHandler);
        window.addEventListener('bimcanvas:action-delete', deleteHandler);

        const ghostPatchHandler = ((e: CustomEvent) => {
            this.ghostManager.updateGhosts(e.detail);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:ghost-patch', ghostPatchHandler);
        window.addEventListener('bimcanvas:ghost-patch', ghostPatchHandler);

        // 主题切换事件监听 - 重建 Builders 和场景
        const themeChangeHandler = (() => {
            console.log('Theme changed, rebuilding scene with new colors...');
            this.rebuildWithNewTheme();
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:theme-change', themeChangeHandler);
        window.addEventListener('bimcanvas:theme-change', themeChangeHandler);

        // 网格规格切换事件监听
        const gridSpacingHandler = ((e: CustomEvent) => {
            const spacing = e.detail.spacing as 600 | 1000;
            console.log(`Grid spacing changed to ${spacing}mm`);
            this.gridBuilder.setGridSpacing(spacing);
            this.gridBuilder.buildGrid();
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:grid-spacing-change', gridSpacingHandler);
        window.addEventListener('bimcanvas:grid-spacing-change', gridSpacingHandler);
    }

    public toggleViewMode(mode: 'human' | 'ai') {
        this.layerManager.applyPreset(mode);
    }

    public toggleLayer(layerId: number, visible: boolean) {
        this.layerManager.toggleLayer(layerId, visible);
    }

    public applyPreset(preset: string) {
        this.layerManager.applyPreset(preset);
    }

    /**
     * 获取 GridBuilder 实例（供外部配置网格规格）
     */
    public getGridBuilder(): GridBuilder {
        return this.gridBuilder;
    }

    /**
     * 重建网格（规格变更后调用）
     */
    public rebuildGrid(): void {
        this.gridBuilder.buildGrid();
    }

    /**
     * 主题切换时重建场景
     * 重新创建 Builders 以应用新的配色，然后重建当前文档
     */
    private rebuildWithNewTheme() {
        // 更新场景背景色
        const bgColor = themeService.currentTheme.value.background;
        this.scene.background = new THREE.Color(bgColor);
        if (this.scene.fog instanceof THREE.FogExp2) {
            this.scene.fog.color.setHex(bgColor);
        }

        // 清理旧的 GridBuilder 资源（防止标签残留）
        this.gridBuilder.cleanup();
        // 清理旧的 LabelBuilder 资源（防止构件标签残留）
        this.labelBuilder.cleanup();

        // 重新创建所有 Builders（它们在构造时读取 ThemeService 配色）
        this.sceneBuilder = new SceneBuilder(this.scene);
        this.gridBuilder = new GridBuilder(this.scene);
        this.outlineBuilder = new OutlineBuilder(this.scene);
        this.labelBuilder = new LabelBuilder(this.scene);
        this.zoneBuilder = new ZoneBuilder(this.scene);

        // 如果有当前文档，重建场景
        const doc = this.store.document;
        if (doc) {
            this.sceneBuilder.buildFromDocument(doc);
            this.outlineBuilder.buildLines(doc);
            this.labelBuilder.buildLabels(doc);
            this.zoneBuilder.buildZones(doc);
            this.gridBuilder.buildGrid();
        } else {
            this.sceneBuilder.buildDemoScene();
            this.gridBuilder.buildGrid();
        }
    }

    private setupLighting() {
        const ambientLight = new THREE.AmbientLight(this.AMBIENT_LIGHT_COLOR, 0.4); // Reduced intensity
        this.scene.add(ambientLight);

        const dirLight = new THREE.DirectionalLight(this.DIR_LIGHT_COLOR, 0.8); // Increased intensity slightly
        dirLight.position.set(-5000, 10000, 5000); // Angled for better shadows
        dirLight.castShadow = true;
        dirLight.shadow.mapSize.width = 2048;
        dirLight.shadow.mapSize.height = 2048;
        dirLight.shadow.camera.near = 0.5;
        dirLight.shadow.camera.far = 50000;
        dirLight.shadow.bias = -0.0001; // Reduce shadow acne
        this.scene.add(dirLight);

        const hemiLight = new THREE.HemisphereLight(0xeeeeff, 0x777788, 0.2); // Reduced intensity
        this.scene.add(hemiLight);
    }

    private fitToScreen(doc: any) {
        if (!doc.walls || doc.walls.length === 0) return;

        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;

        const processPolygon = (polygon: any[]) => {
            if (!polygon) return;
            polygon.forEach((p: any) => {
                minX = Math.min(minX, p[0]);
                minY = Math.min(minY, p[1]);
                maxX = Math.max(maxX, p[0]);
                maxY = Math.max(maxY, p[1]);
            });
        };

        if (doc.walls) {
            doc.walls.forEach((wall: any) => processPolygon(wall.polygon));
        }

        if (doc.modules) {
            doc.modules.forEach((mod: any) => processPolygon(mod.bounds));
        }

        if (minX === Infinity) return;

        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        const width = maxX - minX;
        const height = maxY - minY;

        const debugStore = useDebugStore();
        debugStore.log(`FitToScreen: Bounds [${minX.toFixed(0)},${minY.toFixed(0)}] to [${maxX.toFixed(0)},${maxY.toFixed(0)}] W:${width.toFixed(0)} H:${height.toFixed(0)}`);
        debugStore.log(`FitToScreen: Center [${centerX.toFixed(0)},${centerY.toFixed(0)}]`);

        // Map 2D center (x, y) to 3D center (x, 0, -y) because of -90 X rotation
        const center3D = new THREE.Vector3(centerX, 0, -centerY);
        debugStore.log(`FitToScreen: Center3D [${center3D.x.toFixed(0)},${center3D.y.toFixed(0)},${center3D.z.toFixed(0)}]`);

        // Update Camera Position (Keep Y high, move X and Z)
        this.camera.position.set(center3D.x, 10000, center3D.z);
        this.camera.lookAt(center3D.x, 0, center3D.z);
        debugStore.log(`FitToScreen: Camera Pos [${this.camera.position.x.toFixed(0)},${this.camera.position.y.toFixed(0)},${this.camera.position.z.toFixed(0)}]`);

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
        this.labelRenderer.setSize(this.container.clientWidth, this.container.clientHeight);
    }

    public animate() {
        this.animationId = requestAnimationFrame(this.boundAnimate);

        // Update services
        this.viewportService.update();
        this.interactionService.update();
        this.dragManager.update();

        this.renderer.render(this.scene, this.camera);
        this.labelRenderer.render(this.scene, this.camera);
    }

    public dispose() {
        if (this.animationId) {
            cancelAnimationFrame(this.animationId);
        }
        // 移除 resize 监听器
        window.removeEventListener('resize', this.boundOnResize);

        // 移除所有全局业务事件监听器
        this.boundEventHandlers.forEach((handler, eventName) => {
            window.removeEventListener(eventName, handler);
        });
        this.boundEventHandlers.clear();

        this.container.removeChild(this.renderer.domElement);
        this.container.removeChild(this.labelRenderer.domElement);
        this.renderer.dispose();
        this.interactionService.dispose();
        this.viewportService.dispose();
        this.dragManager.dispose();
    }
}

