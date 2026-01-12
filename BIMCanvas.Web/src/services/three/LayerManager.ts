import * as THREE from 'three';

export class LayerManager {
    // Layer Definitions
    public static readonly LAYER_MODEL = 0; // 默认模型层
    public static readonly LAYER_GRID = 2;  // 语义网格
    public static readonly LAYER_LABELS = 3; // ID 标签
    public static readonly LAYER_BOUNDS = 4; // 家具模块 OBB + 朝向箭头
    public static readonly LAYER_OUTLINE = 5; // 边界描边（原 SEMANTIC）
    public static readonly LAYER_SVG = 6;   // SVG 预览层
    public static readonly LAYER_ZONES = 7;  // 设计区 / 禁区（预留）
    public static readonly LAYER_SEMANTIC = 8; // 设计场线（预留）
    public static readonly LAYER_AI_VISION = 9; // AI 视觉层 (高对比度)
    public static readonly LAYER_ARCHITECTURE = 10; // 建筑图层（墙柱门窗）
    public static readonly LAYER_FURNITURE = 11;    // 模块预览图层（家具）

    // Presets
    public static readonly PRESET_HUMAN = 'human';
    public static readonly PRESET_AI = 'ai';

    private camera: THREE.Camera;

    constructor(camera: THREE.Camera) {
        this.camera = camera;
        this.applyPreset(LayerManager.PRESET_HUMAN);
    }

    public toggleLayer(layerId: number, visible: boolean) {
        if (visible) {
            this.camera.layers.enable(layerId);
        } else {
            this.camera.layers.disable(layerId);
        }
    }

    public applyPreset(preset: string) {
        // Always enable model
        this.camera.layers.enable(LayerManager.LAYER_MODEL);

        if (preset === LayerManager.PRESET_HUMAN) {
            this.camera.layers.enable(LayerManager.LAYER_GRID); // Enable Grid by default for Human
            this.camera.layers.enable(LayerManager.LAYER_ARCHITECTURE); // 建筑图层默认启用
            this.camera.layers.enable(LayerManager.LAYER_FURNITURE);    // 模块图层默认启用
            this.camera.layers.disable(LayerManager.LAYER_LABELS);
            this.camera.layers.disable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.disable(LayerManager.LAYER_OUTLINE);
            this.camera.layers.disable(LayerManager.LAYER_SVG);
            this.camera.layers.disable(LayerManager.LAYER_ZONES);
            this.camera.layers.disable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.disable(LayerManager.LAYER_AI_VISION);
        } else if (preset === LayerManager.PRESET_AI) {
            // AI Mode: Enable all auxiliary layers (LAYER_MODEL always stays enabled)
            this.camera.layers.enable(LayerManager.LAYER_GRID);
            this.camera.layers.enable(LayerManager.LAYER_LABELS);
            this.camera.layers.enable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.enable(LayerManager.LAYER_OUTLINE);
            this.camera.layers.enable(LayerManager.LAYER_SVG);
            this.camera.layers.enable(LayerManager.LAYER_ZONES);
            this.camera.layers.enable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.enable(LayerManager.LAYER_AI_VISION);
            this.camera.layers.enable(LayerManager.LAYER_ARCHITECTURE);
            this.camera.layers.enable(LayerManager.LAYER_FURNITURE);
            // Note: LAYER_MODEL is NOT disabled here. AI Vision is an OVERLAY, not a replacement.
        }
    }

    // Deprecated, kept for compatibility during refactor if needed, but better to remove or alias
    public setMode(mode: 'human' | 'ai') {
        this.applyPreset(mode);
    }
}
