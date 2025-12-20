import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

/**
 * GhostManager - 管理拖拽/旋转时的预览幽灵对象
 * 支持多选模式：可以同时创建多个 Ghost
 *
 * 位置逻辑：
 * - ghostGroup 初始位置在 (0,0,0)
 * - clone 保留 original 的全部变换（position/rotation/scale）
 * - setPositionOffset(offset) 设置 ghostGroup.position = offset
 * - setPivot(pivot) 设置旋转中心，调整 clone 为相对位置
 * - setRotation(rotation) 旋转 ghostGroup
 */
export class GhostManager {
    private static instance: GhostManager | null = null;

    private scene: THREE.Scene;
    private ghostGroups: Map<string, THREE.Group> = new Map();
    private originalMaterials: Map<string, THREE.Material | THREE.Material[]> = new Map();
    private originalObjects: Map<string, THREE.Object3D> = new Map();
    private sharedPivot: THREE.Vector3 | null = null;

    private constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    public static getInstance(scene?: THREE.Scene): GhostManager {
        if (!GhostManager.instance) {
            if (!scene) {
                throw new Error('GhostManager: 首次调用 getInstance 必须传入 Scene');
            }
            GhostManager.instance = new GhostManager(scene);
        }
        return GhostManager.instance;
    }

    public static resetInstance(): void {
        if (GhostManager.instance) {
            GhostManager.instance.removeAllGhosts();
            GhostManager.instance = null;
        }
    }

    public createGhost(original: THREE.Object3D) {
        this.createGhosts([original]);
    }

    public createGhosts(originals: THREE.Object3D[]) {
        this.removeAllGhosts();

        const ghostColor = 0x00aaff;

        for (const original of originals) {
            const id = original.userData?.id;
            if (!id) continue;

            // Ghost Group 在原点
            const ghostGroup = new THREE.Group();
            ghostGroup.userData.isGhost = true;
            ghostGroup.layers.set(LayerManager.LAYER_MODEL);
            // 不设置 position/rotation/scale，保持默认值 (0,0,0)
            this.scene.add(ghostGroup);

            // 克隆对象，保留完整的变换（position/rotation/scale）
            const clone = original.clone();
            clone.userData.isGhost = true;
            ghostGroup.add(clone);

            // 隐藏 Mesh（只显示轮廓）
            clone.traverse((child) => {
                child.layers.set(LayerManager.LAYER_MODEL);
                child.userData.isGhost = true;
                if (child instanceof THREE.Mesh) {
                    child.visible = false;
                }
            });

            // BoxHelper
            const boxHelper = new THREE.BoxHelper(clone, ghostColor);
            boxHelper.layers.set(LayerManager.LAYER_MODEL);
            if (boxHelper.material instanceof THREE.LineBasicMaterial) {
                boxHelper.material.depthTest = false;
                boxHelper.material.transparent = true;
                boxHelper.material.opacity = 0.9;
            }
            ghostGroup.add(boxHelper);

            // 存储
            this.ghostGroups.set(id, ghostGroup);
            this.originalObjects.set(id, original);

            // Phantom 效果
            original.traverse((child) => {
                if (child instanceof THREE.Mesh) {
                    this.originalMaterials.set(child.uuid, child.material);
                    child.material = new THREE.MeshBasicMaterial({
                        color: 0xdddddd,
                        transparent: true,
                        opacity: 0.3,
                        depthTest: true,
                        side: THREE.DoubleSide
                    });
                }
            });
        }
    }

    /**
     * 设置旋转中心
     * 将 ghostGroup.position 设为 pivot，clone.position 调整为相对于 pivot 的偏移
     *
     * 调用后 clone 的世界位置：
     * worldPos = ghostGroup.position + clone.position
     *          = pivot + (original.position - pivot)
     *          = original.position ✓
     */
    public setPivot(pivot: THREE.Vector3) {
        this.sharedPivot = pivot.clone();

        for (const [id, ghostGroup] of this.ghostGroups) {
            const original = this.originalObjects.get(id);
            if (!original) continue;

            // 重置 ghostGroup 的旋转（防止之前的旋转影响新的 pivot 设置）
            ghostGroup.rotation.set(0, 0, 0);

            // ghostGroup 位置设为 pivot
            ghostGroup.position.copy(pivot);

            // 调整 clone 的位置为相对于 pivot 的偏移
            ghostGroup.children.forEach(child => {
                if (!(child instanceof THREE.BoxHelper)) {
                    const originalWorldPos = original.position.clone();
                    child.position.subVectors(originalWorldPos, pivot);
                }
            });

            // 强制更新矩阵
            ghostGroup.updateMatrixWorld(true);

            // 更新 BoxHelper
            ghostGroup.children.forEach(child => {
                if (child instanceof THREE.BoxHelper) {
                    child.update();
                }
            });
        }
    }

    public removeGhost() {
        this.removeAllGhosts();
    }

    public removeAllGhosts() {
        for (const [_id, original] of this.originalObjects) {
            original.traverse((child) => {
                if (child instanceof THREE.Mesh && this.originalMaterials.has(child.uuid)) {
                    child.material = this.originalMaterials.get(child.uuid)!;
                }
            });
        }

        for (const [_id, ghostGroup] of this.ghostGroups) {
            this.scene.remove(ghostGroup);
            ghostGroup.traverse((child) => {
                if (child instanceof THREE.Mesh || child instanceof THREE.Line || child instanceof THREE.LineSegments) {
                    child.geometry?.dispose();
                    if (Array.isArray(child.material)) {
                        child.material.forEach(m => m.dispose());
                    } else {
                        child.material?.dispose();
                    }
                }
            });
        }

        this.ghostGroups.clear();
        this.originalObjects.clear();
        this.originalMaterials.clear();
        this.sharedPivot = null;
    }

    /**
     * 设置位置偏移（用于移动预览）
     * offset 来自 MoveTool：delta = actualPoint - basePoint
     * clone 世界位置 = ghostGroup.position + clone.position = offset + original.position
     */
    public setPositionOffset(offset: THREE.Vector3) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.position.copy(offset);
        }
    }

    /**
     * 设置旋转角度（用于旋转预览）
     * 必须先调用 setPivot
     *
     * 坐标系说明：
     * - Three.js 俯视图：Z+ 向下（屏幕下方）
     * - 用户顺时针拖动：角度增加（从 X+ 向 Z+）
     * - rotation.y 正值：逆时针旋转（从 +Y 向下看）
     * - 需要取反以匹配用户拖动方向
     */
    public setRotation(rotation: number) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.rotation.y = -rotation;

            // 强制更新矩阵，确保 BoxHelper 能读取正确的世界变换
            ghostGroup.updateMatrixWorld(true);

            // 更新 BoxHelper
            ghostGroup.children.forEach(child => {
                if (child instanceof THREE.BoxHelper) {
                    child.update();
                }
            });
        }
    }

    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
