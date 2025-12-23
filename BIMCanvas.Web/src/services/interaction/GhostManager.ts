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

    /**
     * 从 bounds 创建 OBB 轮廓线
     * 使用本地坐标，与模块相同的变换链路
     *
     * 关键：不使用 BoxHelper！
     * BoxHelper 使用世界坐标且 matrixAutoUpdate=false，
     * 在 setPivot 时无法正确跟随位置变换
     */
    private createOutlineFromBounds(bounds: [number, number][]): THREE.LineLoop {
        // bounds 是 2D 坐标 [[x,y], ...]
        // 转换为 3D 本地坐标（XY 平面）
        const points: THREE.Vector3[] = bounds.map(([x, y]) =>
            new THREE.Vector3(x, y, 0)
        );

        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const material = new THREE.LineBasicMaterial({
            color: 0x00aaff,  // Ghost 颜色
            depthTest: false,
            transparent: true,
            opacity: 0.9
        });

        const outline = new THREE.LineLoop(geometry, material);
        outline.rotation.x = -Math.PI / 2;  // 与模块相同：XY → XZ 翻转
        outline.renderOrder = 999;
        outline.userData.isOutline = true;
        outline.layers.set(LayerManager.LAYER_MODEL);

        return outline;
    }

    public createGhosts(originals: THREE.Object3D[]) {
        this.removeAllGhosts();

        for (const original of originals) {
            const id = original.userData?.id;
            if (!id) continue;

            // 计算 original 几何体的世界包围盒中心
            // 注意：模块的 mesh.position 始终是 (0,0,0)，真实位置在几何体顶点中
            const bbox = new THREE.Box3().setFromObject(original);
            const geometryCenter = new THREE.Vector3();
            bbox.getCenter(geometryCenter);

            // Ghost Group 保持在原点（关键！）
            // setPositionOffset(delta) 会设置 ghostGroup.position = delta
            // 如果初始位置不在原点，会导致位置计算错误
            const ghostGroup = new THREE.Group();
            ghostGroup.userData.isGhost = true;
            ghostGroup.userData.geometryCenter = geometryCenter.clone();  // 存储供 setPivot 使用
            ghostGroup.layers.set(LayerManager.LAYER_MODEL);
            // 不设置 position，保持 (0,0,0)
            this.scene.add(ghostGroup);

            // 克隆对象，保持原始变换（关键！）
            // 模块的 mesh.position = (0,0,0)，几何体顶点包含世界坐标
            // clone 继承这些属性，无需调整 position
            const clone = original.clone();
            clone.userData.isGhost = true;
            // 不修改 clone.position，保持原样
            ghostGroup.add(clone);

            // 隐藏 Mesh（只显示轮廓）
            clone.traverse((child) => {
                child.layers.set(LayerManager.LAYER_MODEL);
                child.userData.isGhost = true;
                if (child instanceof THREE.Mesh) {
                    child.visible = false;
                }
            });

            // ★ 用 LineLoop 替代 BoxHelper
            // BoxHelper 使用世界坐标且 matrixAutoUpdate=false，无法正确跟随 setPivot 变换
            // LineLoop 从 bounds 生成本地坐标轮廓，与 clone 变换链路一致
            const moduleData = original.userData?.data;
            const bounds = moduleData?.bounds as [number, number][] | undefined;
            if (bounds && bounds.length >= 3) {
                const outline = this.createOutlineFromBounds(bounds);
                ghostGroup.add(outline);
            }

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

            // ★ 关键修改：所有子对象（包括 outline）都设置 -pivot 偏移
            // LineLoop 使用本地坐标，会正确跟随父级变换
            ghostGroup.children.forEach(child => {
                child.position.set(-pivot.x, -pivot.y, -pivot.z);
            });

            // 强制更新矩阵
            ghostGroup.updateMatrixWorld(true);
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
     * ⚠️ 角度语义：交互角（CW+）
     *
     * @param rotation 交互角（弧度），来自 atan2(z, x)，顺时针为正
     *
     * 内部转换：
     * - 输入：CW+ 交互角（用户顺时针拖动 → 正值）
     * - Three.js rotation.y：CCW+（从 +Y 向下看，正值逆时针）
     * - 转换：rotation.y = -rotation（CW+ → CCW-，即顺时针显示）
     *
     * 与 executeRotate() 的一致性：
     * - 两者都对交互角取反，确保预览和结果方向一致
     */
    public setRotation(rotation: number) {
        for (const [_id, ghostGroup] of this.ghostGroups) {
            // CW+ 交互角 → CCW- Three.js 角度（顺时针显示）
            ghostGroup.rotation.y = -rotation;

            // 强制更新矩阵（让 Three.js 渲染时使用新的变换）
            ghostGroup.updateMatrixWorld(true);
        }
    }

    public hasGhosts(): boolean {
        return this.ghostGroups.size > 0;
    }

    public getGhostCount(): number {
        return this.ghostGroups.size;
    }
}
