import * as THREE from 'three';

/**
 * 轴锁定辅助器 - 按住 Shift 时锁定移动到 X 或 Z 轴
 * Phase 3: Shift 轴锁定 + 辅助线
 */
export class AxisLockHelper {
    private scene: THREE.Scene;
    private line: THREE.Line | null = null;
    private isVisible: boolean = false;

    // 当前锁定的轴: 'x' | 'z' | null
    private lockedAxis: 'x' | 'z' | null = null;

    // 样式配置
    private readonly lineLength = 50000;  // 辅助线长度 (50m)
    private readonly colorX = 0xff4444;   // X 轴红色
    private readonly colorZ = 0x4444ff;   // Z 轴蓝色

    // 软吸附配置：接近轴向多少度以内自动吸附
    private readonly softSnapAngle = 8;   // 度数

    constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    /**
     * 锁定点到轴上
     * @param basePoint 基点（移动起点）
     * @param currentPoint 当前鼠标位置
     * @param shiftHeld 是否按住 Shift
     * @returns 锁定后的点（如果吸附）或原点（如果不吸附）
     */
    public lock(basePoint: THREE.Vector3, currentPoint: THREE.Vector3, shiftHeld: boolean): THREE.Vector3 {
        // 计算位移向量
        const delta = new THREE.Vector3().subVectors(currentPoint, basePoint);
        const len = Math.sqrt(delta.x * delta.x + delta.z * delta.z);

        // 位移太小时不吸附
        if (len < 10) {
            this.hide();
            this.lockedAxis = null;
            return currentPoint.clone();
        }

        if (shiftHeld) {
            // Shift 强制锁定：根据主方向锁定
            this.lockedAxis = Math.abs(delta.x) >= Math.abs(delta.z) ? 'x' : 'z';
        } else {
            // 软吸附：计算与 X 轴的夹角（0° = X轴，90° = Z轴）
            const angleToX = Math.atan2(Math.abs(delta.z), Math.abs(delta.x)) * 180 / Math.PI;

            if (angleToX <= this.softSnapAngle) {
                // 接近 X 轴
                this.lockedAxis = 'x';
            } else if (angleToX >= 90 - this.softSnapAngle) {
                // 接近 Z 轴
                this.lockedAxis = 'z';
            } else {
                // 不在吸附范围内
                this.hide();
                this.lockedAxis = null;
                return currentPoint.clone();
            }
        }

        // 创建锁定后的点
        const lockedPoint = basePoint.clone();
        if (this.lockedAxis === 'x') {
            lockedPoint.x = currentPoint.x;
            // Z 保持不变
        } else {
            lockedPoint.z = currentPoint.z;
            // X 保持不变
        }

        // 显示辅助线
        this.showAxisLine(basePoint, this.lockedAxis);

        return lockedPoint;
    }

    /**
     * 重置轴锁定（Shift 松开时调用）
     */
    public resetLock(): void {
        this.lockedAxis = null;
    }

    /**
     * 显示轴辅助线
     */
    private showAxisLine(basePoint: THREE.Vector3, axis: 'x' | 'z'): void {
        // 如果轴变了，重新创建线
        if (this.line) {
            this.scene.remove(this.line);
            this.line.geometry.dispose();
            (this.line.material as THREE.Material).dispose();
        }

        // 计算线的起点和终点
        const start = basePoint.clone();
        const end = basePoint.clone();

        if (axis === 'x') {
            start.x -= this.lineLength / 2;
            end.x += this.lineLength / 2;
        } else {
            start.z -= this.lineLength / 2;
            end.z += this.lineLength / 2;
        }

        const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
        const material = new THREE.LineDashedMaterial({
            color: axis === 'x' ? this.colorX : this.colorZ,
            dashSize: 200,
            gapSize: 100,
            depthTest: false
        });

        this.line = new THREE.Line(geometry, material);
        this.line.computeLineDistances();
        this.line.renderOrder = 998;
        this.line.position.y = 1;  // 略高于地面

        this.scene.add(this.line);
        this.isVisible = true;
    }

    /**
     * 隐藏辅助线
     */
    public hide(): void {
        if (this.line && this.isVisible) {
            this.scene.remove(this.line);
            this.isVisible = false;
        }
    }

    /**
     * 清理资源
     */
    public dispose(): void {
        this.hide();
        if (this.line) {
            this.line.geometry.dispose();
            (this.line.material as THREE.Material).dispose();
            this.line = null;
        }
        this.lockedAxis = null;
    }
}
