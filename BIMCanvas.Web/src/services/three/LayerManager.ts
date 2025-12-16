import * as THREE from 'three';

export class LayerManager {
    public static readonly LAYER_DEFAULT = 0;
    public static readonly LAYER_HUMAN = 1;
    public static readonly LAYER_AI = 2;

    private camera: THREE.Camera;

    constructor(camera: THREE.Camera) {
        this.camera = camera;
        this.setMode('human');
    }

    public setMode(mode: 'human' | 'ai') {
        if (mode === 'human') {
            this.camera.layers.enable(LayerManager.LAYER_DEFAULT);
            this.camera.layers.enable(LayerManager.LAYER_HUMAN);
            this.camera.layers.disable(LayerManager.LAYER_AI);
        } else {
            this.camera.layers.enable(LayerManager.LAYER_DEFAULT);
            this.camera.layers.disable(LayerManager.LAYER_HUMAN);
            this.camera.layers.enable(LayerManager.LAYER_AI);
        }
    }
}
