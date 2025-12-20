import * as THREE from 'three';

/**
 * 吸附指示器 - 在吸附点位置显示绿色圆点
 * Phase 2: 视觉反馈
 */
export class SnapIndicator {
    private scene: THREE.Scene;
    private mesh: THREE.Mesh | null = null;
    private isVisible: boolean = false;

    // 样式配置
    private readonly radius = 80;  // 80mm 半径
    private readonly color = 0x00ff00;  // 绿色
    private readonly opacity = 0.7;

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.createMesh();
    }

    private createMesh() {
        const geometry = new THREE.CircleGeometry(this.radius, 32);
        const material = new THREE.MeshBasicMaterial({
            color: this.color,
            transparent: true,
            opacity: this.opacity,
            depthTest: false,
            side: THREE.DoubleSide
        });

        this.mesh = new THREE.Mesh(geometry, material);
        this.mesh.rotation.x = -Math.PI / 2;  // 平铺在 XZ 平面
        this.mesh.renderOrder = 1000;  // 确保在最上层
        this.mesh.visible = false;
    }

    /**
     * 显示指示器并更新位置
     */
    public show(position: THREE.Vector3): void {
        if (!this.mesh) return;

        this.mesh.position.copy(position);
        this.mesh.position.y = 1;  // 略高于地面，避免 z-fighting

        if (!this.isVisible) {
            this.scene.add(this.mesh);
            this.mesh.visible = true;
            this.isVisible = true;
        }
    }

    /**
     * 隐藏指示器
     */
    public hide(): void {
        if (!this.mesh || !this.isVisible) return;

        this.mesh.visible = false;
        this.scene.remove(this.mesh);
        this.isVisible = false;
    }

    /**
     * 清理资源
     */
    public dispose(): void {
        this.hide();

        if (this.mesh) {
            this.mesh.geometry.dispose();
            if (Array.isArray(this.mesh.material)) {
                this.mesh.material.forEach(m => m.dispose());
            } else {
                this.mesh.material.dispose();
            }
            this.mesh = null;
        }
    }
}
