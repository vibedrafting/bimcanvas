import * as THREE from 'three';

export class LayerManager {
    // Layer Definitions
    public static readonly LAYER_MODEL = 0; // Default layer
    public static readonly LAYER_GRID = 2;
    public static readonly LAYER_LABELS = 3;
    public static readonly LAYER_BOUNDS = 4;
    public static readonly LAYER_SEMANTIC = 5;
    public static readonly LAYER_AXES = 6;

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
            this.camera.layers.disable(LayerManager.LAYER_GRID);
            this.camera.layers.disable(LayerManager.LAYER_LABELS);
            this.camera.layers.disable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.disable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.disable(LayerManager.LAYER_AXES);
        } else if (preset === LayerManager.PRESET_AI) {
            this.camera.layers.enable(LayerManager.LAYER_GRID);
            this.camera.layers.enable(LayerManager.LAYER_LABELS);
            this.camera.layers.enable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.enable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.enable(LayerManager.LAYER_AXES);
        }
    }

    // Deprecated, kept for compatibility during refactor if needed, but better to remove or alias
    public setMode(mode: 'human' | 'ai') {
        this.applyPreset(mode);
    }
}
