
import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';
import { themeService } from '../theme/ThemeService';

export class GridBuilder {
    private scene: THREE.Scene;
    private gridHelper: THREE.GridHelper | null = null;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public buildGrid(size: number = 100000, divisions: number = 100) {
        if (this.gridHelper) {
            this.scene.remove(this.gridHelper);
            this.gridHelper.dispose();
            this.gridHelper = null;
        }

        // 从 ThemeService 获取网格配色
        const colors = themeService.currentTheme.value.grid;
        const color1 = colors.centerLine; // 中心线
        const color2 = colors.gridLine;   // 网格线

        this.gridHelper = new THREE.GridHelper(size, divisions, color1, color2);

        // Assign to GRID Layer
        this.gridHelper.layers.set(LayerManager.LAYER_GRID);

        this.scene.add(this.gridHelper);
    }
}
