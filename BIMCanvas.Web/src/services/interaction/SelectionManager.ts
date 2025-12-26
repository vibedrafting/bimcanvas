import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';
import { useDebugStore } from '../../stores/debugStore';
import { watch } from 'vue';
import { storeToRefs } from 'pinia';

export class SelectionManager {
    // 多选支持：使用 Map 存储选中对象和选择框
    private selectedObjects: Map<string, THREE.Object3D> = new Map();
    private selectionBoxes: Map<string, THREE.BoxHelper> = new Map();
    private selectionOutlines: Map<string, THREE.Line> = new Map();
    private scene: THREE.Scene;
    private store = useCanvasStore();
    private debug = useDebugStore();

    // 精确轮廓线材质
    private selectionMaterial = new THREE.LineBasicMaterial({
        color: 0x3b82f6,
        linewidth: 2,
        depthTest: false
    });

    constructor(scene: THREE.Scene) {
        this.scene = scene;
        this.debug.log('[SelectionMgr] Created');

        // 使用 storeToRefs 获取响应式引用
        const { selectedIds } = storeToRefs(this.store);

        // 监听 Store 的 selectedIds 变化，同步视觉
        watch(selectedIds, (newIds, oldIds) => {
            this.debug.log(`[SelectionMgr] IDs changed: [${oldIds?.join(',') || ''}] -> [${newIds.join(',')}]`);
            this.syncWithStore(newIds);
        }, { deep: true, immediate: true });
    }

    /**
     * 同步 Store 的选择状态到视觉
     */
    private syncWithStore(ids: string[]) {
        this.debug.log(`[SelectionMgr] syncWithStore: ${ids.length} items`);
        const currentIds = new Set(ids);

        // 移除不再选中的对象（检查 BoxHelper）
        for (const [id, _box] of this.selectionBoxes) {
            if (!currentIds.has(id)) {
                this.debug.log(`[SelectionMgr] Remove visual: ${id}`);
                this.removeSelectionVisual(id);
            }
        }
        // 移除不再选中的对象（检查轮廓线）
        for (const [id, _line] of this.selectionOutlines) {
            if (!currentIds.has(id)) {
                this.debug.log(`[SelectionMgr] Remove outline: ${id}`);
                this.removeSelectionVisual(id);
            }
        }

        // 添加新选中的对象
        for (const id of ids) {
            if (!this.selectionBoxes.has(id) && !this.selectionOutlines.has(id)) {
                const object = this.findObjectById(id);
                this.debug.log(`[SelectionMgr] Find scene obj: ${id} -> ${object ? 'FOUND' : 'NOT FOUND'}`);
                if (object) {
                    this.addSelectionVisual(object);
                }
            }
        }
    }

    private findObjectById(id: string): THREE.Object3D | null {
        let found: THREE.Object3D | null = null;
        this.scene.traverse((child) => {
            if (child.userData && child.userData.id === id) {
                found = child;
            }
        });
        return found;
    }

    /**
     * 为对象添加选择视觉（精确轮廓线，无轮廓时降级到蓝色框）
     */
    private addSelectionVisual(object: THREE.Object3D) {
        const id = object.userData?.id;
        if (!id) return;

        // 避免重复添加
        if (this.selectionBoxes.has(id) || this.selectionOutlines.has(id)) return;

        const data = object.userData?.data;
        // 尝试获取多边形轮廓数据
        const polygon = data?.bounds || data?.polygon || data?.boundary ||
                        data?.computedBoundary || data?.rawBoundary;

        if (polygon && polygon.length >= 3) {
            // 使用精确轮廓线
            const points = polygon.map((p: [number, number]) => new THREE.Vector3(p[0], p[1], 0));
            points.push(points[0]!); // 闭合轮廓

            const geometry = new THREE.BufferGeometry().setFromPoints(points);
            const line = new THREE.Line(geometry, this.selectionMaterial);
            line.rotation.x = -Math.PI / 2;
            line.position.y = 50; // 抬高避免 Z-fighting

            this.scene.add(line);
            this.selectionOutlines.set(id, line);
            this.debug.log(`[SelectionMgr] Added outline for: ${id}`);
        } else {
            // 降级到 BoxHelper
            const box = new THREE.BoxHelper(object, 0x3b82f6);
            this.scene.add(box);
            this.selectionBoxes.set(id, box);
            this.debug.log(`[SelectionMgr] Added boxHelper for: ${id}`);
        }
        this.selectedObjects.set(id, object);
    }

    /**
     * 移除对象的选择视觉
     */
    private removeSelectionVisual(id: string) {
        // 清理轮廓线
        const line = this.selectionOutlines.get(id);
        if (line) {
            this.scene.remove(line);
            line.geometry?.dispose();
            this.selectionOutlines.delete(id);
        }
        // 清理 BoxHelper
        const box = this.selectionBoxes.get(id);
        if (box) {
            this.scene.remove(box);
            box.geometry?.dispose();
            this.selectionBoxes.delete(id);
        }
        this.selectedObjects.delete(id);
    }

    /**
     * 选择单个对象（兼容旧代码）
     */
    public select(object: THREE.Object3D | null) {
        if (!object) {
            this.clearSelection();
            return;
        }

        const id = object.userData?.id;
        if (!id) return;

        // 通过 Store 设置选择，视觉会通过 watch 自动同步
        this.store.setSelectedObject(object);
    }

    /**
     * 添加对象到选择集（多选）
     */
    public addToSelection(object: THREE.Object3D) {
        const id = object.userData?.id;
        if (!id) return;

        this.store.addToSelection(object);
    }

    /**
     * 切换对象选择状态（多选）
     */
    public toggleSelection(object: THREE.Object3D) {
        const id = object.userData?.id;
        if (!id) return;

        this.store.toggleSelection(object);
    }

    /**
     * 清除所有选择
     */
    public clearSelection() {
        // 通过 Store 清除，视觉会通过 watch 自动同步
        this.store.clearSelection();
    }

    /**
     * 清除本地视觉（内部使用，不影响 Store）
     */
    private clearAllVisuals() {
        // 清理轮廓线
        for (const [_id, line] of this.selectionOutlines) {
            this.scene.remove(line);
            line.geometry?.dispose();
        }
        this.selectionOutlines.clear();
        // 清理 BoxHelper
        for (const [_id, box] of this.selectionBoxes) {
            this.scene.remove(box);
            box.geometry?.dispose();
        }
        this.selectionBoxes.clear();
        this.selectedObjects.clear();
    }

    /**
     * 获取当前选中的第一个对象（兼容旧代码）
     */
    public getSelected(): THREE.Object3D | null {
        const firstId = this.store.selectedIds[0];
        return firstId ? (this.selectedObjects.get(firstId) ?? null) : null;
    }

    /**
     * 获取所有选中的对象
     */
    public getAllSelected(): THREE.Object3D[] {
        return Array.from(this.selectedObjects.values());
    }

    /**
     * 检查对象是否被选中
     */
    public isSelected(object: THREE.Object3D): boolean {
        const id = object.userData?.id;
        return id ? this.selectedObjects.has(id) : false;
    }

    /**
     * 更新所有选择框（在场景变化后调用）
     */
    public update() {
        for (const [_id, box] of this.selectionBoxes) {
            box.update();
        }
    }
}
