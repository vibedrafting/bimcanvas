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

            // 计算 original 几何体的世界包围盒中心
            // 注意：模块的 mesh.position 始终是 (0,0,0)，真实位置在几何体顶点中
            const bbox = new THREE.Box3().setFromObject(original);
            const geometryCenter = new THREE.Vector3();
            bbox.getCenter(geometryCenter);

            // Ghost Group 位于几何中心
            const ghostGroup = new THREE.Group();
            ghostGroup.userData.isGhost = true;
            ghostGroup.userData.geometryCenter = geometryCenter.clone();  // 存储供 setPivot 使用
            ghostGroup.layers.set(LayerManager.LAYER_MODEL);
            ghostGroup.position.copy(geometryCenter);
            this.scene.add(ghostGroup);

            // 克隆对象
            const clone = original.clone();
            clone.userData.isGhost = true;

            // 关键：调整 clone 位置，使其相对于 ghostGroup（几何中心）
            // clone 的几何顶点在世界坐标中，需要减去 center 使其居中于 ghostGroup
            clone.position.set(-geometryCenter.x, -geometryCenter.y, -geometryCenter.z);
            ghostGroup.add(clone);

            // 隐藏 Mesh（只显示轮廓）
            clone.traverse((child) => {
                child.layers.set(LayerManager.LAYER_MODEL);
                child.userData.isGhost = true;
                if (child instanceof THREE.Mesh) {
                    child.visible = false;
                }
            });

            // BoxHelper - 在 clone 位置调整后创建
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
     * 坐标系说明：
     * - 模块的 mesh.position = (0,0,0)，几何体顶点直接使用世界坐标
     * - geometryCenter = 几何体包围盒中心（createGhosts 时计算并存储）
     * - clone.position = -pivot 时，几何体世界位置 = pivot + (-pivot) + 顶点 = 顶点 ✓
     */
    public setPivot(pivot: THREE.Vector3) {
        this.sharedPivot = pivot.clone();

        for (const [_id, ghostGroup] of this.ghostGroups) {
            // 使用存储的几何中心（而非 original.position，后者始终是 0,0,0）
            const geometryCenter = ghostGroup.userData.geometryCenter as THREE.Vector3;
            if (!geometryCenter) continue;

            // 重置 ghostGroup 的旋转（防止之前的旋转影响新的 pivot 设置）
            ghostGroup.rotation.set(0, 0, 0);

            // ghostGroup 位置设为 pivot
            ghostGroup.position.copy(pivot);

            // 调整 clone 位置为相对于 pivot 的偏移
            // 因为几何体顶点在世界坐标中，clone.position = -pivot 使几何体保持原位
            ghostGroup.children.forEach(child => {
                if (!(child instanceof THREE.BoxHelper)) {
                    child.position.set(-pivot.x, -pivot.y, -pivot.z);
                }
            });

            // 强制更新矩阵
            ghostGroup.updateMatrixWorld(true);

            // 更新 BoxHelper（仅在 setPivot 时更新，setRotation 时不更新）
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
     *
     * BoxHelper 说明：
     * - 不在此方法中调用 BoxHelper.update()
     * - BoxHelper 作为 ghostGroup 的子对象会自动跟随旋转
     * - 如果调用 update()，AABB 会重新计算导致形状变化（矩形变菱形）
     */
    public setRotation(rotation: number) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.rotation.y = -rotation;

            // 强制更新矩阵（让 Three.js 渲染时使用新的变换）
            ghostGroup.updateMatrixWorld(true);

            // 不调用 BoxHelper.update()！
            // BoxHelper 会跟随 ghostGroup 旋转，保持原始形状
        }
    }

    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
