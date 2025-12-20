import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

/**
 * GhostManager - 管理拖拽/旋转时的预览幽灵对象
 * 支持多选模式：可以同时创建多个 Ghost
 */
export class GhostManager {
    private static instance: GhostManager | null = null;

    private scene: THREE.Scene;
    // 多选支持：使用 Map 存储多个 Ghost Group
    private ghostGroups: Map<string, THREE.Group> = new Map();
    private originalMaterials: Map<string, THREE.Material | THREE.Material[]> = new Map();
    private originalObjects: Map<string, THREE.Object3D> = new Map();

    // 共享的旋转中心（多选旋转时使用）
    private sharedPivot: THREE.Vector3 | null = null;

    private constructor(scene: THREE.Scene) {
        this.scene = scene;
    }

    /**
     * 获取 GhostManager 单例实例
     */
    public static getInstance(scene?: THREE.Scene): GhostManager {
        if (!GhostManager.instance) {
            if (!scene) {
                throw new Error('GhostManager: 首次调用 getInstance 必须传入 Scene');
            }
            GhostManager.instance = new GhostManager(scene);
        }
        return GhostManager.instance;
    }

    /**
     * 重置单例（仅用于测试或场景销毁时）
     */
    public static resetInstance(): void {
        if (GhostManager.instance) {
            GhostManager.instance.removeAllGhosts();
            GhostManager.instance = null;
        }
    }

    /**
     * 创建单个 Ghost（兼容旧代码）
     */
    public createGhost(original: THREE.Object3D) {
        this.createGhosts([original]);
    }

    /**
     * 批量创建多个 Ghost（多选模式）
     */
    public createGhosts(originals: THREE.Object3D[]) {
        this.removeAllGhosts();

        console.log('[GhostManager] Creating ghosts for', originals.length, 'objects');

        for (const original of originals) {
            const id = original.userData?.id;
            if (!id) {
                console.warn('[GhostManager] Object has no ID, skipping');
                continue;
            }

            console.log('[GhostManager] Processing object:', id, 'type:', original.type);

            // 创建 Ghost Group
            const ghostGroup = new THREE.Group();
            ghostGroup.userData.isGhost = true;
            // 确保 Ghost Group 在默认层可见
            ghostGroup.layers.set(LayerManager.LAYER_MODEL);
            this.scene.add(ghostGroup);

            // 克隆对象
            const solidClone = original.clone();
            solidClone.userData.isGhost = true;

            // 强制设置所有子对象的层为 LAYER_MODEL，确保可见
            solidClone.traverse((child) => {
                child.userData.isGhost = true;
                child.layers.set(LayerManager.LAYER_MODEL);

                // 为 Ghost 设置半透明的蓝色材质，作为目标位置预览
                if (child instanceof THREE.Mesh) {
                    child.material = new THREE.MeshBasicMaterial({
                        color: 0x4488ff, // 蓝色
                        transparent: true,
                        opacity: 0.6,
                        depthTest: true,
                        side: THREE.DoubleSide
                    });
                }
            });

            ghostGroup.add(solidClone);
            solidClone.position.copy(original.position);
            solidClone.rotation.copy(original.rotation);
            solidClone.scale.copy(original.scale);

            // 存储
            this.ghostGroups.set(id, ghostGroup);
            this.originalObjects.set(id, original);

            console.log('[GhostManager] Ghost created at position:', original.position.toArray());

            // 将原对象变为半透明 wireframe（显示原始位置）
            original.traverse((child) => {
                if (child instanceof THREE.Mesh) {
                    this.originalMaterials.set(child.uuid, child.material);
                    const ghostMaterial = new THREE.MeshBasicMaterial({
                        color: 0xaaaaaa,
                        transparent: true,
                        opacity: 0.3,
                        depthTest: true,
                        side: THREE.DoubleSide,
                        wireframe: true
                    });
                    child.material = ghostMaterial;
                }
            });
        }

        console.log('[GhostManager] Total ghosts created:', this.ghostGroups.size);
    }

    /**
     * 设置旋转中心（多选旋转时所有对象围绕同一中心）
     */
    public setPivot(pivot: THREE.Vector3) {
        this.sharedPivot = pivot.clone();

        for (const [id, ghostGroup] of this.ghostGroups) {
            const original = this.originalObjects.get(id);
            if (!original) continue;

            ghostGroup.position.copy(pivot);

            ghostGroup.children.forEach(child => {
                const originalWorldPos = original.position.clone();
                child.position.subVectors(originalWorldPos, pivot);
            });
        }
    }

    /**
     * 移除单个 Ghost（兼容旧代码，实际清除所有）
     */
    public removeGhost() {
        this.removeAllGhosts();
    }

    /**
     * 移除所有 Ghost
     */
    public removeAllGhosts() {
        // 恢复原对象材质
        for (const [_id, original] of this.originalObjects) {
            original.traverse((child) => {
                if (child instanceof THREE.Mesh && this.originalMaterials.has(child.uuid)) {
                    child.material = this.originalMaterials.get(child.uuid)!;
                }
            });
        }

        // 清理 Ghost Groups
        for (const [_id, ghostGroup] of this.ghostGroups) {
            this.scene.remove(ghostGroup);
            ghostGroup.traverse((child) => {
                if (child instanceof THREE.Mesh) {
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
     * 设置位置偏移（移动预览）- 作用于所有 Ghost
     * 偏移是相对于原始对象位置的增量
     */
    public setPositionOffset(offset: THREE.Vector3) {
        for (const [id, ghostGroup] of this.ghostGroups) {
            const original = this.originalObjects.get(id);
            if (!original) continue;

            // Ghost Group 的子对象已经有了原始位置，直接设置 Group 的位置为偏移量即可
            // 因为在 createGhosts 中，solidClone.position.copy(original.position)
            ghostGroup.position.copy(offset);
        }
    }

    /**
     * 设置旋转角度（旋转预览）- 作用于所有 Ghost
     */
    public setRotation(rotation: number) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.rotation.y = -rotation;
        }
    }

    /**
     * 获取是否有 Ghost
     */
    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    /**
     * 获取 Ghost 数量
     */
    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
