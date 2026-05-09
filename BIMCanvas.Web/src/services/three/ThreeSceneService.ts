import * as THREE from 'three';
import { SceneBuilder } from '../builders/SceneBuilder';
import { GridBuilder } from '../builders/GridBuilder';
import { OutlineBuilder } from '../builders/OutlineBuilder';
import { LabelBuilder } from '../builders/LabelBuilder';
import { ZoneBuilder } from '../builders/ZoneBuilder';
import { ExclusionBuilder } from '../builders/ExclusionBuilder';
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
import { canvasStyleService } from '../canvas/CanvasStyleService';
import { moduleLibraryService } from '../ModuleLibraryService';
import { getWebRuntime } from '../../runtime/runtimeRegistry';

// 全局实例引用（供 ScreenshotService 等外部服务访问）
let globalInstance: ThreeSceneService | null = null;

export function getThreeSceneService(): ThreeSceneService | null {
    return globalInstance;
}

export class ThreeSceneService {
    private container: HTMLElement;
    private _scene: THREE.Scene;
    private _camera: THREE.OrthographicCamera;
    private renderer: THREE.WebGLRenderer;
    private labelRenderer: CSS2DRenderer;
    private animationId: number | null = null;

    private isInitialLoad: boolean = true;

    // Public getters for screenshot service
    public get scene(): THREE.Scene {
        return this._scene;
    }

    public get camera(): THREE.OrthographicCamera {
        return this._camera;
    }



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
    private exclusionBuilder: ExclusionBuilder;

    private store: ReturnType<typeof useCanvasStore>;

    // Services
    public layerManager: LayerManager;
    private interactionService: InteractionService;
    private viewportService: ViewportService;
    private selectionManager: SelectionManager;
    private dragManager: DragManager;
    private spatialMarkWasGridVisible: boolean | null = null;

    private ambientLight: THREE.AmbientLight | null = null;
    private directionalLight: THREE.DirectionalLight | null = null;
    private hemisphereLight: THREE.HemisphereLight | null = null;

    constructor(container: HTMLElement) {
        this.container = container;
        this.store = useCanvasStore();

        // 1. Scene
        this._scene = new THREE.Scene();
        this.applySceneStyle();

        // 注册全局实例
        globalInstance = this;

        // 2. Camera (Orthographic)
        const aspect = container.clientWidth / container.clientHeight;
        const frustumSize = 20000;
        this._camera = new THREE.OrthographicCamera(
            frustumSize * aspect / -2,
            frustumSize * aspect / 2,
            frustumSize / 2,
            frustumSize / -2,
            1,
            50000
        );
        // Y-Up Top View: Position at +Y, looking at Origin.
        // Up vector should be -Z (North) to match standard map orientation where North is Up on screen.
        this._camera.position.set(0, 10000, 0);
        this._camera.up.set(0, 0, -1);
        this._camera.lookAt(0, 0, 0);

        // Standalone 模式空白项目下原点居中很别扭(标签全在右上),
        // 把相机 target 偏移让世界原点落到屏幕左下 ~10% 处。Connected 不动。
        if (this.isStandaloneMode()) {
            this.applyStandaloneDefaultView();
        }

        // 3. Renderer
        this.renderer = new THREE.WebGLRenderer({
            antialias: true,
            alpha: false,
            preserveDrawingBuffer: true  // 允许截图
        });
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
        this.selectionManager = new SelectionManager(this._scene);
        this.interactionService = new InteractionService(this.camera, this.renderer.domElement, this._scene, this.selectionManager);
        this.dragManager = new DragManager(this.camera, this.renderer.domElement, this._scene, this.selectionManager);
        GhostManager.getInstance(this._scene);

        // 5. Lighting
        this.setupLighting();

        // 6. Initialize Builders
        this.sceneBuilder = new SceneBuilder(this._scene);
        this.gridBuilder = new GridBuilder(this._scene);
        this.outlineBuilder = new OutlineBuilder(this._scene);
        this.labelBuilder = new LabelBuilder(this._scene);
        this.zoneBuilder = new ZoneBuilder(this._scene);
        this.exclusionBuilder = new ExclusionBuilder(this._scene);

        // Initial Demo Scene - REMOVED to prevent flash
        // if (!this.store.document) {
        //     this.sceneBuilder.buildDemoScene();
        //     this.gridBuilder.buildGrid();
        // }
        // Always build grid initially
        this.gridBuilder.buildGrid();
        // 8. Load Module Library
        moduleLibraryService.load().catch(error => {
            console.error('[ThreeSceneService] Failed to load module library:', error);
        });

        // 7. Watch for Store Changes



        // ...

        // 7. Watch for Store Changes

        // A. Deep watch for content updates (Rebuild Scene)
        // Standalone import may set projectData before ThreeCanvas mounts, so run once immediately.
        watch(() => this.store.projectData, (newData) => {
            if (newData) {
                if (this.store.suppressAutoBuild) {
                    this.isInitialLoad = false;
                    return;
                }
                if (this.isInitialLoad) {
                    console.log('Initial load detected. Building Grid ONLY and fitting screen.');
                    // Only build Grid (already built in init, but ensure it's there)
                    this.gridBuilder.buildGrid();

                    // Fit to screen immediately so camera is ready for the show
                    if (!this.store.preserveViewOnLoad) {
                        this.fitToScreen(newData);
                    }

                    // Do NOT build scene yet. Wait for event.
                    this.isInitialLoad = false;
                } else {
                    // Normal update (e.g. undo/redo, manual load) -> Build immediately
                    // console.log('Project data updated, rebuilding scene...');
                    this.sceneBuilder.buildFromDocument(newData);
                    this.outlineBuilder.buildLines(newData);
                    this.labelBuilder.buildLabels(newData);

                    // Update Zone Label Visibility based on current layer state
                    const labelsOn = this._camera.layers.isEnabled(LayerManager.LAYER_LABELS);
                    const zonesOn = this._camera.layers.isEnabled(LayerManager.LAYER_ZONES);
                    this.labelBuilder.updateZoneLabelVisibility(labelsOn, zonesOn);

                    this.zoneBuilder.buildZones(newData);
                    this.exclusionBuilder.buildExclusions(newData);
                    this.gridBuilder.buildGrid();
                    this.interactionService.refreshSpatialMarkingOverlay();
                }
            }
        }, { deep: true, immediate: true });

        // B. Shallow watch for document replacement (Fit to Screen)
        // This only triggers when a NEW document is loaded (reference change),
        // not when modules are moved/rotated (mutation).
        watch(() => this.store.projectData, (newData) => {
            if (this.store.suppressAutoBuild) {
                return;
            }
            if (newData && !this.isInitialLoad && !this.store.preserveViewOnLoad) {
                // Only fit to screen on subsequent loads, initial load handled above
                // Skip fitToScreen when:
                // - preserveViewOnLoad=true (branch switch, undo/redo)
                console.log('New project loaded (subsequent), fitting to screen...');
                setTimeout(() => {
                    this.fitToScreen(newData);
                }, 100);
            }
        });

        // ...

        // Listen for Cinematic Build
        const playBuildSequenceHandler = (() => {
            if (this.store.projectData) {
                this.sceneBuilder.buildProgressively(this.store.projectData);
                // Also build other helpers that might be needed
                this.outlineBuilder.buildLines(this.store.projectData);
                this.labelBuilder.buildLabels(this.store.projectData);
                this.zoneBuilder.buildZones(this.store.projectData);
                this.exclusionBuilder.buildExclusions(this.store.projectData);
                this.interactionService.refreshSpatialMarkingOverlay();
            }
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:play-build-sequence', playBuildSequenceHandler);
        window.addEventListener('bimcanvas:play-build-sequence', playBuildSequenceHandler);

        const playBuildSequenceFastHandler = ((event: Event) => {
            if (this.store.projectData) {
                const detail = (event as CustomEvent).detail ?? {};
                const build = detail.build ?? {};
                const buildLabels = build.labels !== false;
                const buildOutline = build.outline !== false;
                const buildZones = build.zones !== false;
                const buildExclusions = build.exclusions !== false;
                const buildGrid = build.grid !== false;
                const buildBounds = build.bounds !== false;
                const buildSvg = build.svg !== false;

                this.sceneBuilder.setBuildOptions({
                    includeBounds: buildBounds,
                    includeSvg: buildSvg
                });

                this.sceneBuilder.buildFromDocument(this.store.projectData);

                if (buildOutline) {
                    this.outlineBuilder.buildLines(this.store.projectData);
                } else {
                    this.outlineBuilder.clearLines();
                }

                if (buildLabels) {
                    this.labelBuilder.buildLabels(this.store.projectData);

                    const labelsOn = this._camera.layers.isEnabled(LayerManager.LAYER_LABELS);
                    const zonesOn = this._camera.layers.isEnabled(LayerManager.LAYER_ZONES);
                    this.labelBuilder.updateZoneLabelVisibility(labelsOn, zonesOn);
                } else {
                    this.labelBuilder.cleanup();
                }

                if (buildZones) {
                    this.zoneBuilder.buildZones(this.store.projectData);
                } else {
                    this.zoneBuilder.cleanup();
                }

                if (buildExclusions) {
                    this.exclusionBuilder.buildExclusions(this.store.projectData);
                } else {
                    this.exclusionBuilder.cleanup();
                }

                if (buildGrid) {
                    this.gridBuilder.buildGrid();
                } else {
                    this.gridBuilder.cleanup();
                }

                this.interactionService.refreshSpatialMarkingOverlay();
                window.dispatchEvent(new CustomEvent('bimcanvas:build-complete'));
            }
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:play-build-sequence-fast', playBuildSequenceFastHandler);
        window.addEventListener('bimcanvas:play-build-sequence-fast', playBuildSequenceFastHandler);

        // 8. Events - 使用保存的引用以便正确移除
        this.boundOnResize = this.onWindowResize.bind(this);
        this.boundAnimate = this.animate.bind(this);
        window.addEventListener('resize', this.boundOnResize);

        // Listen for reset view
        const onResetView = () => {
            if (this.store.projectData && !this.store.suppressAutoBuild) {
                this.fitToScreen(this.store.projectData);
            }
        };
        window.addEventListener('bimcanvas:reset-view', onResetView);
        this.boundEventHandlers.set('bimcanvas:reset-view', onResetView);

        // 全局业务事件 - 保存引用到 Map
        const viewModeHandler = ((e: CustomEvent) => {
            this.toggleViewMode(e.detail);
            this.applySpatialMarkGridSuppression();

            // Update Zone Label Visibility after preset application
            const labelsOn = this._camera.layers.isEnabled(LayerManager.LAYER_LABELS);
            const zonesOn = this._camera.layers.isEnabled(LayerManager.LAYER_ZONES);
            this.labelBuilder.updateZoneLabelVisibility(labelsOn, zonesOn);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:view-mode-change', viewModeHandler);
        window.addEventListener('bimcanvas:view-mode-change', viewModeHandler);

        const layerToggleHandler = ((e: CustomEvent) => {
            this.toggleLayer(e.detail.layerId, e.detail.visible);
            this.applySpatialMarkGridSuppression();

            // Update Zone Label Visibility (Requires both LABELS and ZONES layers)
            const labelsOn = this._camera.layers.isEnabled(LayerManager.LAYER_LABELS);
            const zonesOn = this._camera.layers.isEnabled(LayerManager.LAYER_ZONES);
            this.labelBuilder.updateZoneLabelVisibility(labelsOn, zonesOn);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:layer-toggle', layerToggleHandler);
        window.addEventListener('bimcanvas:layer-toggle', layerToggleHandler);

        const spatialMarkModeHandler = ((e: CustomEvent) => {
            const active = !!e.detail?.active;
            if (active) {
                if (this.spatialMarkWasGridVisible === null) {
                    this.spatialMarkWasGridVisible = this._camera.layers.isEnabled(LayerManager.LAYER_GRID);
                }
                this.applySpatialMarkGridSuppression();
                return;
            }

            if (this.spatialMarkWasGridVisible === true) {
                this.layerManager.toggleLayer(LayerManager.LAYER_GRID, true);
                this.dispatchGridLayerState(true);
            }
            this.spatialMarkWasGridVisible = null;
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:spatial-mark-mode-change', spatialMarkModeHandler);
        window.addEventListener('bimcanvas:spatial-mark-mode-change', spatialMarkModeHandler);

        const rotateHandler = () => this.interactionService.rotateSelection();
        this.boundEventHandlers.set('bimcanvas:action-rotate', rotateHandler);
        window.addEventListener('bimcanvas:action-rotate', rotateHandler);

        const moveHandler = () => this.interactionService.activateMoveTool();
        this.boundEventHandlers.set('bimcanvas:action-move', moveHandler);
        window.addEventListener('bimcanvas:action-move', moveHandler);

        const deleteHandler = () => this.interactionService.deleteSelection();
        this.boundEventHandlers.set('bimcanvas:action-delete', deleteHandler);
        window.addEventListener('bimcanvas:action-delete', deleteHandler);

        const mirrorHandler = () => this.interactionService.activateMirrorTool();
        this.boundEventHandlers.set('bimcanvas:action-mirror', mirrorHandler);
        window.addEventListener('bimcanvas:action-mirror', mirrorHandler);

        const copyHandler = () => this.interactionService.activateCopyTool();
        this.boundEventHandlers.set('bimcanvas:action-copy', copyHandler);
        window.addEventListener('bimcanvas:action-copy', copyHandler);

        const measureHandler = () => this.interactionService.activateMeasurementTool();
        this.boundEventHandlers.set('bimcanvas:action-measure', measureHandler);
        window.addEventListener('bimcanvas:action-measure', measureHandler);

        const placeHandler = ((e: CustomEvent) => {
            this.interactionService.activatePlaceTool(e.detail.moduleDef);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:activate-place-tool', placeHandler);
        window.addEventListener('bimcanvas:activate-place-tool', placeHandler);

        // const ghostPatchHandler = ((e: CustomEvent) => {
        //     this.ghostManager.updateGhosts(e.detail);
        // }) as EventListener;
        // this.boundEventHandlers.set('bimcanvas:ghost-patch', ghostPatchHandler);
        // window.addEventListener('bimcanvas:ghost-patch', ghostPatchHandler);

        // Canvas style change - rebuild Builders and scene
        const canvasStyleChangeHandler = (() => {
            console.log('Canvas style changed, rebuilding scene...');
            this.rebuildWithCanvasStyle();
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:canvas-style-change', canvasStyleChangeHandler);
        window.addEventListener('bimcanvas:canvas-style-change', canvasStyleChangeHandler);

        // 网格规格切换事件监听
        const gridSpacingHandler = ((e: CustomEvent) => {
            const spacing = e.detail.spacing as 600 | 1000;
            console.log(`Grid spacing changed to ${spacing}mm`);
            this.gridBuilder.setGridSpacing(spacing);
            this.gridBuilder.buildGrid();
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:grid-spacing-change', gridSpacingHandler);
        window.addEventListener('bimcanvas:grid-spacing-change', gridSpacingHandler);

        // 图层预设配置加载事件监听
        const layerPresetsHandler = ((e: CustomEvent) => {
            const presets = e.detail;
            console.log('[ThreeSceneService] 收到图层预设配置:', presets);
            this.layerManager.setPresetConfig(presets);
            // 重新应用当前预设以使配置生效
            this.layerManager.applyPreset(LayerManager.PRESET_HUMAN);
        }) as EventListener;
        this.boundEventHandlers.set('bimcanvas:layer-presets-loaded', layerPresetsHandler);
        window.addEventListener('bimcanvas:layer-presets-loaded', layerPresetsHandler);

        // 6. Start Animation Loop
        if (!this.store.isScreenshotRender) {
            this.animate();
        }
    }

    public toggleViewMode(mode: 'User' | 'Agent' | 'human' | 'ai') {
        // 统一转换为新格式
        const preset = (mode === 'human') ? 'User' : (mode === 'ai') ? 'Agent' : mode;
        this.layerManager.applyPreset(preset);
    }

    public toggleLayer(layerId: number, visible: boolean) {
        this.layerManager.toggleLayer(layerId, visible);
    }

    private applySpatialMarkGridSuppression(): void {
        if (this.spatialMarkWasGridVisible === null) {
            return;
        }

        if (this._camera.layers.isEnabled(LayerManager.LAYER_GRID)) {
            this.layerManager.toggleLayer(LayerManager.LAYER_GRID, false);
            this.dispatchGridLayerState(false);
        }
    }

    private dispatchGridLayerState(visible: boolean): void {
        window.dispatchEvent(new CustomEvent('bimcanvas:layer-state-change', {
            detail: {
                preset: 'User',
                layerStates: {
                    [LayerManager.LAYER_GRID]: visible
                }
            }
        }));
    }
    /**
     * 重建网格（规格变更后调用）
     */
    public rebuildGrid(): void {
        this.gridBuilder.buildGrid();
    }

    public fitToBounds(bounds: { minX: number; minY: number; maxX: number; maxY: number }): void {
        const width = bounds.maxX - bounds.minX;
        const height = bounds.maxY - bounds.minY;
        if (width <= 0 || height <= 0) return;

        const centerX = (bounds.minX + bounds.maxX) / 2;
        const centerY = (bounds.minY + bounds.maxY) / 2;

        const aspect = this.container.clientWidth / this.container.clientHeight;
        let frustumWidth = width;
        let frustumHeight = height;

        if (width / height > aspect) {
            frustumHeight = width / aspect;
        } else {
            frustumWidth = height * aspect;
        }

        const center3D = new THREE.Vector3(centerX, 0, -centerY);
        this._camera.position.set(center3D.x, 10000, center3D.z);
        this._camera.lookAt(center3D.x, 0, center3D.z);
        this.viewportService.setTarget(center3D.x, 0, center3D.z);

        this._camera.left = -frustumWidth / 2;
        this._camera.right = frustumWidth / 2;
        this._camera.top = frustumHeight / 2;
        this._camera.bottom = -frustumHeight / 2;
        this._camera.zoom = 1;
        this._camera.updateProjectionMatrix();
    }

    /**
     * 获取画布截图
     * @returns Base64 编码的 PNG 图片
     */
    public captureScreenshot(): string {
        // 强制渲染一帧确保内容最新
        this.renderer.render(this._scene, this._camera);
        return this.renderer.domElement.toDataURL('image/png');
    }

    /**
     * 强制渲染一帧（不生成图片）
     */
    public renderOnce(renderLabels: boolean = true): void {
        this.renderer.render(this._scene, this._camera);
        if (renderLabels) {
            this.labelRenderer.render(this._scene, this._camera);
        }
    }

    /**
     * 获取 WebGL canvas 元素
     */
    public getCanvasElement(): HTMLCanvasElement {
        return this.renderer.domElement;
    }

    private applySceneStyle(): void {
        const style = canvasStyleService.currentStyle.value.scene;
        this._scene.background = new THREE.Color(style.background);

        if (style.fog) {
            if (this._scene.fog instanceof THREE.FogExp2) {
                this._scene.fog.color.setHex(style.fog.color);
                this._scene.fog.density = style.fog.density;
            } else {
                this._scene.fog = new THREE.FogExp2(style.fog.color, style.fog.density);
            }
        } else {
            this._scene.fog = null;
        }
    }

    /**
     * Rebuild scene after canvas style change.
     */
    private rebuildWithCanvasStyle() {
        this.applySceneStyle();
        this.setupLighting();

        // 清理旧的 Builder 资源（防止残留）
        this.gridBuilder.cleanup();
        this.labelBuilder.cleanup();
        this.zoneBuilder.cleanup(true);
        this.exclusionBuilder.cleanup(true);

        // Recreate Builders to apply updated canvas styles.
        this.sceneBuilder = new SceneBuilder(this._scene);
        this.gridBuilder = new GridBuilder(this._scene);
        this.outlineBuilder = new OutlineBuilder(this._scene);
        this.labelBuilder = new LabelBuilder(this._scene);
        this.zoneBuilder = new ZoneBuilder(this._scene);
        this.exclusionBuilder = new ExclusionBuilder(this._scene);

        // 如果有当前项目数据，重建场景
        const data = this.store.projectData;
        if (data) {
            this.sceneBuilder.buildFromDocument(data);
            this.outlineBuilder.buildLines(data);
            this.labelBuilder.buildLabels(data);

            // Update Zone Label Visibility based on current layer state
            const labelsOn = this._camera.layers.isEnabled(LayerManager.LAYER_LABELS);
            const zonesOn = this._camera.layers.isEnabled(LayerManager.LAYER_ZONES);
            this.labelBuilder.updateZoneLabelVisibility(labelsOn, zonesOn);

            this.zoneBuilder.buildZones(data);
            this.exclusionBuilder.buildExclusions(data);
            this.gridBuilder.buildGrid();
            this.interactionService.refreshSpatialMarkingOverlay();
        } else {
            this.gridBuilder.buildGrid();
        }
    }

    private setupLighting() {
        const style = canvasStyleService.currentStyle.value.lighting;

        if (!this.ambientLight) {
            this.ambientLight = new THREE.AmbientLight(style.ambient.color, style.ambient.intensity);
            this._scene.add(this.ambientLight);
        } else {
            this.ambientLight.color.setHex(style.ambient.color);
            this.ambientLight.intensity = style.ambient.intensity;
        }

        if (!this.directionalLight) {
            this.directionalLight = new THREE.DirectionalLight(style.directional.color, style.directional.intensity);
            this.directionalLight.castShadow = true;
            this._scene.add(this.directionalLight);
        } else {
            this.directionalLight.color.setHex(style.directional.color);
            this.directionalLight.intensity = style.directional.intensity;
        }

        this.directionalLight.position.set(
            style.directional.position[0],
            style.directional.position[1],
            style.directional.position[2]
        );
        this.directionalLight.shadow.mapSize.width = style.directional.shadow.mapSize;
        this.directionalLight.shadow.mapSize.height = style.directional.shadow.mapSize;
        this.directionalLight.shadow.camera.near = style.directional.shadow.near;
        this.directionalLight.shadow.camera.far = style.directional.shadow.far;
        this.directionalLight.shadow.bias = style.directional.shadow.bias;

        if (!this.hemisphereLight) {
            this.hemisphereLight = new THREE.HemisphereLight(
                style.hemisphere.skyColor,
                style.hemisphere.groundColor,
                style.hemisphere.intensity
            );
            this._scene.add(this.hemisphereLight);
        } else {
            this.hemisphereLight.color.setHex(style.hemisphere.skyColor);
            this.hemisphereLight.groundColor.setHex(style.hemisphere.groundColor);
            this.hemisphereLight.intensity = style.hemisphere.intensity;
        }
    }

    /**
     * Standalone 空白项目的默认视角:把相机 target 移到第一象限,
     * 使世界原点落在屏幕左下约 10% 处,让 Excel 风格列/行标签自然展开。
     */
    private applyStandaloneDefaultView(): void {
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const frustumSize = 20000;
        const originInsetRatio = 0.4; // 0.5=正贴角,0=居中,0.4≈10% 边距
        const targetX = frustumSize * aspect * originInsetRatio;
        const targetZ = -frustumSize * originInsetRatio;
        this._camera.position.set(targetX, 10000, targetZ);
        this._camera.lookAt(targetX, 0, targetZ);
    }

    private isStandaloneMode(): boolean {
        try {
            return getWebRuntime().mode === 'standalone';
        } catch {
            return false;
        }
    }

    private fitToScreen(data: any) {
        // 从 baseline 获取墙体数据
        const walls = data.baseline?.walls;
        if (!walls || walls.length === 0) {
            // Standalone 空白项目走默认左下视角;Connected 维持原有"不动"语义。
            if (this.isStandaloneMode()) {
                this.applyStandaloneDefaultView();
            }
            return;
        }

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

        if (walls) {
            walls.forEach((wall: any) => processPolygon(wall.polygon));
        }

        if (data.activeScheme?.modules) {
            data.activeScheme.modules.forEach((mod: any) => processPolygon(mod.bounds));
        }

        if (minX === Infinity) return;

        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        const width = maxX - minX;
        const height = maxY - minY;

        const debugStore = useDebugStore();
        debugStore.log(`FitToScreen: Bounds [${minX.toFixed(0)},${minY.toFixed(0)}] to [${maxX.toFixed(0)},${maxY.toFixed(0)}] W:${width.toFixed(0)} H:${height.toFixed(0)}`);
        debugStore.log(`FitToScreen: Center [${centerX.toFixed(0)},${centerY.toFixed(0)}]`);

        // 计算视锥体尺寸
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const padding = 1.5;
        const sizeForHeight = height * padding;
        const sizeForWidth = (width * padding) / aspect;
        const frustumSize = Math.max(sizeForHeight, sizeForWidth);

        // 灵动岛遮挡补偿：顶部 UI 占用约 120px，偏移一半让内容在可视区域居中
        const topUIHeight = 120; // 灵动岛底部位置 (px)
        const offsetPixels = topUIHeight / 2;
        const offsetWorld = offsetPixels * (frustumSize / this.container.clientHeight);

        // Map 2D center (x, y) to 3D center (x, 0, -y) because of -90 X rotation
        // Z 轴减小偏移量（相机目标向北），让内容在屏幕上向下显示
        const center3D = new THREE.Vector3(centerX, 0, -centerY - offsetWorld);
        debugStore.log(`FitToScreen: Center3D [${center3D.x.toFixed(0)},${center3D.y.toFixed(0)},${center3D.z.toFixed(0)}] (offset: ${offsetWorld.toFixed(0)})`);

        // Update Camera Position (Keep Y high, move X and Z)
        this._camera.position.set(center3D.x, 10000, center3D.z);
        this._camera.lookAt(center3D.x, 0, center3D.z);
        debugStore.log(`FitToScreen: Camera Pos [${this._camera.position.x.toFixed(0)},${this._camera.position.y.toFixed(0)},${this._camera.position.z.toFixed(0)}]`);

        // Update Controls Target via ViewportService
        this.viewportService.setTarget(center3D.x, 0, center3D.z);

        this._camera.left = -frustumSize * aspect / 2;
        this._camera.right = frustumSize * aspect / 2;
        this._camera.top = frustumSize / 2;
        this._camera.bottom = -frustumSize / 2;

        this._camera.zoom = 1;
        this._camera.updateProjectionMatrix();


    }

    private onWindowResize() {
        const aspect = this.container.clientWidth / this.container.clientHeight;
        const frustumSize = 20000;

        this._camera.left = -frustumSize * aspect / 2;
        this._camera.right = frustumSize * aspect / 2;
        this._camera.top = frustumSize / 2;
        this._camera.bottom = -frustumSize / 2;

        this._camera.updateProjectionMatrix();
        this.renderer.setSize(this.container.clientWidth, this.container.clientHeight);
        this.labelRenderer.setSize(this.container.clientWidth, this.container.clientHeight);
    }

    public animate() {
        if (this.store.isScreenshotRender) {
            return;
        }
        this.animationId = requestAnimationFrame(this.boundAnimate);

        // Update services
        this.viewportService.update();
        this.interactionService.update();
        this.dragManager.update();

        this.renderer.render(this._scene, this._camera);
        this.labelRenderer.render(this._scene, this._camera);
    }


    public dispose() {
        // 清除全局实例引用
        globalInstance = null;

        this.zoneBuilder.cleanup(true);
        this.exclusionBuilder.cleanup(true);

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

        // 重置 GhostManager 单例
        GhostManager.resetInstance();
    }
}
