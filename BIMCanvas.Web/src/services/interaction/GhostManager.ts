import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

/**
 * GhostManager - 管理拖拽/旋转时的预览幽灵对象
 * 
 * 简化架构（回退到工作版本）：
 * - ghostGroup 初始在原点 (0,0,0)
 * - clone 保留 original 的完整变换
 * - 移动：ghostGroup.position = offset
 * - 旋转：分开处理位置和角度
 */
export class GhostManager {
    private static instance: GhostManager | null = null;

    private scene: THREE.Scene;
    private ghostGroups: Map<string, THREE.Group> = new Map();
    private originalMaterials: Map<string, THREE.Material | THREE.Material[]> = new Map();
    private originalObjects: Map<string, THREE.Object3D> = new Map();

    // 旋转专用：存储每个对象相对于 pivot 的初始偏移
    private rotationOffsets: Map<string, THREE.Vector3> = new Map();
    private currentPivot: THREE.Vector3 | null = null;

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
            this.scene.add(ghostGroup);

            // 克隆对象，保留完整的变换
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

            // BoxHelper 添加到 ghostGroup（跟随变换）
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
     * 设置位置偏移（用于移动预览）
     * offset = targetPoint - basePoint
     */
    public setPositionOffset(offset: THREE.Vector3) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.position.copy(offset);
        }
    }

    /**
     * 设置旋转中心（用于旋转预览准备）
     * 计算并存储每个对象相对于 pivot 的偏移
     */
    public setPivot(pivot: THREE.Vector3) {
        this.currentPivot = pivot.clone();
        this.rotationOffsets.clear();

        for (const [id, original] of this.originalObjects) {
            const worldPos = new THREE.Vector3();
            original.getWorldPosition(worldPos);
            const offset = new THREE.Vector3().subVectors(worldPos, pivot);
            this.rotationOffsets.set(id, offset);
        }
    }

    /**
     * 设置旋转角度（用于旋转预览）
     * 必须先调用 setPivot
     */
    public setRotation(rotation: number) {
        if (!this.currentPivot) return;

        const cos = Math.cos(rotation);
        const sin = Math.sin(rotation);

        for (const [id, ghostGroup] of this.ghostGroups) {
            const initialOffset = this.rotationOffsets.get(id);
            if (!initialOffset) continue;

            // 旋转偏移向量（绕 Y 轴，使用标准 Y-up 旋转矩阵）
            const rotatedOffset = new THREE.Vector3(
                initialOffset.x * cos - initialOffset.z * sin,
                initialOffset.y,
                initialOffset.x * sin + initialOffset.z * cos
            );

            // ghostGroup.position = 位移偏移 = 旋转后偏移 - 初始偏移
            // 这样 clone 世界位置 = original世界位置 + (rotatedOffset - initialOffset)
            //                    = pivot + initialOffset + (rotatedOffset - initialOffset)
            //                    = pivot + rotatedOffset ✓
            ghostGroup.position.subVectors(rotatedOffset, initialOffset);

            // 设置 ghostGroup 的旋转（让模块本身也旋转）
            ghostGroup.rotation.y = rotation;
        }
    }

    /**
     * 兼容旧 API
     */
    public setTransform(position: THREE.Vector3, rotationY: number = 0) {
        if (rotationY === 0) {
            // 纯移动
            this.setPositionOffset(position);
        } else if (this.currentPivot) {
            // 纯旋转（position 应该是 pivot）
            this.setRotation(rotationY);
        }
    }

    public removeGhost() {
        this.removeAllGhosts();
    }

    public removeAllGhosts() {
        // 恢复原对象材质
        for (const [_id, original] of this.originalObjects) {
            original.traverse((child) => {
                if (child instanceof THREE.Mesh && this.originalMaterials.has(child.uuid)) {
                    child.material = this.originalMaterials.get(child.uuid)!;
                }
            });
        }

        // 清理资源
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
        this.rotationOffsets.clear();
        this.currentPivot = null;
    }

    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
