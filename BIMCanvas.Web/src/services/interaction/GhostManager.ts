import * as THREE from 'three';
import { LayerManager } from '../three/LayerManager';

/**
 * GhostManager - 管理拖拽/旋转时的预览幽灵对象
 * 支持多选模式：可以同时创建多个 Ghost
 * 
 * 位置逻辑（简化版）：
 * - ghostGroup 包含 clone（用于变换计算）
 * - BoxHelper 直接添加到 scene（避免双重变换）
 * - 每次变换后调用 boxHelper.update() 刷新位置
 */
export class GhostManager {
    private static instance: GhostManager | null = null;

    private scene: THREE.Scene;
    private ghostGroups: Map<string, THREE.Group> = new Map();
    private boxHelpers: Map<string, THREE.BoxHelper> = new Map(); // BoxHelper 单独存储
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

            // **关键修复**：BoxHelper 直接添加到 scene，不作为 ghostGroup 的子对象
            const boxHelper = new THREE.BoxHelper(clone, ghostColor);
            boxHelper.layers.set(LayerManager.LAYER_MODEL);
            if (boxHelper.material instanceof THREE.LineBasicMaterial) {
                boxHelper.material.depthTest = false;
                boxHelper.material.transparent = true;
                boxHelper.material.opacity = 0.9;
            }
            this.scene.add(boxHelper); // 直接添加到 scene
            this.boxHelpers.set(id, boxHelper); // 单独存储

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

        // 更新所有 BoxHelper
        this.updateAllBoxHelpers();
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

        // 清理 Ghost Groups
        for (const [_id, ghostGroup] of this.ghostGroups) {
            this.scene.remove(ghostGroup);
            ghostGroup.traverse((child) => {
                if (child instanceof THREE.Mesh || child instanceof THREE.Line) {
                    child.geometry?.dispose();
                    if (Array.isArray(child.material)) {
                        child.material.forEach(m => m.dispose());
                    } else {
                        child.material?.dispose();
                    }
                }
            });
        }

        // 清理 BoxHelper（单独存储）
        for (const [_id, boxHelper] of this.boxHelpers) {
            this.scene.remove(boxHelper);
            boxHelper.geometry.dispose();
            (boxHelper.material as THREE.Material).dispose();
        }

        this.ghostGroups.clear();
        this.boxHelpers.clear();
        this.originalObjects.clear();
        this.originalMaterials.clear();
        this.sharedPivot = null;
    }

    /**
     * 设置位置偏移（移动预览）
     */
    public setPositionOffset(offset: THREE.Vector3) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.position.copy(offset);
        }
        // 更新所有 BoxHelper
        this.updateAllBoxHelpers();
    }

    /**
     * 设置旋转角度（旋转预览）
     */
    public setRotation(rotation: number) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            ghostGroup.rotation.y = -rotation;
        }
        // 更新所有 BoxHelper
        this.updateAllBoxHelpers();
    }

    /**
     * 更新所有 BoxHelper（重新计算包围盒）
     */
    private updateAllBoxHelpers() {
        for (const [_id, boxHelper] of this.boxHelpers) {
            boxHelper.update();
        }
    }

    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
