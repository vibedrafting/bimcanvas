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
        const size = 60; // Cross size
        const geometry = new THREE.BufferGeometry();
        const vertices = new Float32Array([
            -size, 0, 0, size, 0, 0, // X-axis line
            0, 0, -size, 0, 0, size, // Z-axis line
            // Optional: Diagonal for "Star" or just Plus? User said "Cross Star" (十字星). 
            // Often X shape + Plus shape = Star.
            // Let's add diagonals for "Star" look if desired, or stick to clean Cross.
            // CAD uses a specialized glyph. Let's do a tilted cross (X) + Plus (+) ? 
            // Or just a simple 'X' is often used for points.
            // Let's do a Plus (+) shape as base, maybe tilted 45 deg?
            // User: "十字星" -> Cross Star.
            // Let's do a 4-point star.
        ]);
        // Let's stick to a Plus (+) shape which is standard "Crosshair" key point snap.
        // Or 'X' for Intersection. 
        // Let's do an 'X' shape.
        const xVertices = new Float32Array([
            -size, 0, -size, size, 0, size,
            -size, 0, size, size, 0, -size
        ]);

        geometry.setAttribute('position', new THREE.BufferAttribute(xVertices, 3));

        const material = new THREE.LineBasicMaterial({
            color: 0x00ff00, // Green
            depthTest: false,
            transparent: true,
            opacity: 0.8
        });

        // Use LineSegments
        this.mesh = new THREE.LineSegments(geometry, material) as any;
        // this.mesh.rotation.x is not needed if we defined flat in XZ
        this.mesh!.renderOrder = 1000;
        this.mesh!.visible = false;
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
