
import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../three/LayerManager';
import { canvasStyleService, type LabelStyle } from '../canvas/CanvasStyleService';

export class GridBuilder {
    private scene: THREE.Scene;
    private gridHelper: THREE.GridHelper | null = null;
    private labelGroup: THREE.Group | null = null;
    private gridSpacing: number;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.gridSpacing = canvasStyleService.currentStyle.value.layers.grid.spacing;
    }

    /**
     * 设置网格规格 (600mm 或 1000mm)
     */
    public setGridSpacing(spacing: 600 | 1000): void {
        this.gridSpacing = spacing;
        canvasStyleService.setGridSpacing(spacing);
    }

    public getGridSpacing(): number {
        return this.gridSpacing;
    }

    /**
     * 构建网格和语义标签
     * - 网格铺满整个屏幕（以原点为中心）
     * - 标签只显示在第一象限（屏幕右上区域）
     */
    public buildGrid() {
        this.cleanup();

        const style = canvasStyleService.currentStyle.value.layers.grid;
        this.gridSpacing = style.spacing;
        const color1 = style.centerLine; // 中心线
        const color2 = style.gridLine;   // 网格线

        // 网格铺满整个场景（以原点为中心，100m x 100m）
        const gridSize = style.size; // 100m
        // divisions 必须为偶数才能正确显示通过原点的中心线
        let divisions = Math.round(gridSize / this.gridSpacing);
        if (divisions % 2 !== 0) {
            divisions += 1; // 确保为偶数
        }

        // GridHelper 默认以原点为中心
        this.gridHelper = new THREE.GridHelper(gridSize, divisions, color1, color2);

        this.gridHelper.layers.set(LayerManager.LAYER_GRID);
        this.scene.add(this.gridHelper);

        // 构建语义标签 (Excel 风格) - 仅在第一象限
        this.buildSemanticLabels();
    }

    /**
     * 清理网格和标签资源
     * 在销毁实例或重建前调用
     */
    public cleanup() {
        if (this.gridHelper) {
            this.scene.remove(this.gridHelper);
            this.gridHelper.dispose();
            this.gridHelper = null;
        }

        if (this.labelGroup) {
            // Explicitly cleanup CSS2DObject DOM elements - 清理所有子元素 DOM
            this.labelGroup.traverse(child => {
                if ((child as any).element) {
                    const el = (child as any).element as HTMLElement;
                    if (el.parentNode) {
                        el.parentNode.removeChild(el);
                    }
                }
            });
            this.labelGroup.clear(); // 移除所有子对象
            this.scene.remove(this.labelGroup);
            this.labelGroup = null;
        }
    }

    /**
     * 生成 Excel 风格的语义标签
     * 
     * Three.js 坐标系（俯视图）：
     * - X 轴向右（屏幕右方）
     * - Z 轴向下（屏幕下方，因为相机 up = -Z）
     * 
     * 用户期望的"数学第一象限"（屏幕右上区域）：
     * - X 正方向 = 屏幕右侧 ✓
     * - Y 正方向 = 屏幕上方 = Three.js Z 负方向
     * 
     * 因此：
     * - 列标签 (A, B, C...) 沿 X 正方向排列，边缘标注在 Z 正值位置（屏幕下方）
     * - 行标签 (1, 2, 3...) 沿 Z 负方向排列，边缘标注在 X 负值位置（屏幕左侧）
     */
    private buildSemanticLabels() {
        this.labelGroup = new THREE.Group();
        this.labelGroup.layers.set(LayerManager.LAYER_GRID);

        const labelConfig = style.label;

        // 固定生成 50 个标签
        const maxLabels = 50;

        // 列标签 (A, B, C, ..., Z, AA, AB, ...) 
        // - 沿 X 正方向排列（屏幕右方）
        // - 边缘标注在 Z 正值位置（屏幕下方，作为列头）
        const zOffsetForColumnLabels = this.gridSpacing * 0.6;
        for (let col = 0; col < maxLabels; col++) {
            const label = this.getColumnLabel(col);
            const x = (col + 0.5) * this.gridSpacing;
            this.createSemanticLabel(label, new THREE.Vector3(x, 0, zOffsetForColumnLabels), labelConfig);
        }

        // 行标签 (1, 2, 3, ...) 
        // - 沿 Z 负方向排列（屏幕上方）
        // - 边缘标注在 X 负值位置（屏幕左侧，作为行头）
        const xOffsetForRowLabels = -this.gridSpacing * 0.6;
        for (let row = 0; row < maxLabels; row++) {
            const label = String(row + 1);
            const z = -(row + 0.5) * this.gridSpacing; // Z 负方向 = 屏幕向上
            this.createSemanticLabel(label, new THREE.Vector3(xOffsetForRowLabels, 0, z), labelConfig);
        }

        this.scene.add(this.labelGroup);
    }

    /**
     * 将列索引转换为 Excel 风格的列名
     * 0 -> A, 1 -> B, ..., 25 -> Z, 26 -> AA, 27 -> AB, ...
     */
    private getColumnLabel(index: number): string {
        let label = '';
        let num = index;

        while (num >= 0) {
            label = String.fromCharCode(65 + (num % 26)) + label;
            num = Math.floor(num / 26) - 1;
        }

        return label;
    }

    private createSemanticLabel(
        text: string,
        position: THREE.Vector3,
        config: LabelStyle
    ) {
        const div = document.createElement('div');
        div.className = 'semantic-grid-label';
        div.textContent = text;
        div.style.color = config.text;
        // 使用简洁的阴影（无发光效果）
        div.style.textShadow = config.shadow;

        if (config.background) div.style.background = config.background;
        if (config.padding) div.style.padding = config.padding;
        if (config.borderRadius) div.style.borderRadius = config.borderRadius;

        div.style.fontSize = `${config.fontSize}px`;
        div.style.fontWeight = config.fontWeight;
        if (config.fontFamily) {
            div.style.fontFamily = config.fontFamily;
        }

        const label = new CSS2DObject(div);
        label.position.copy(position);
        label.layers.set(LayerManager.LAYER_GRID);
        this.labelGroup!.add(label);
    }
}
