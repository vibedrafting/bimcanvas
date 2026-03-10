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
    private selectionCrossLines: Map<string, THREE.LineSegments> = new Map();
    private scene: THREE.Scene;
    private store = useCanvasStore();
    private debug = useDebugStore();

    // 精确轮廓线材质（depthTest: false 确保不被遮挡）
    private selectionMaterial = new THREE.LineBasicMaterial({
        color: 0x3b82f6,
        linewidth: 3,
        depthTest: false,
        depthWrite: false
    });

    constructor(scene: THREE.Scene) {
        this.scene = scene;

        // 使用 storeToRefs 获取响应式引用
        const { selectedIds } = storeToRefs(this.store);

        // 监听 Store 的 selectedIds 变化，同步视觉
        watch(selectedIds, (newIds, _oldIds) => {
            this.syncWithStore(newIds);
        }, { deep: true, immediate: true });
    }

    /**
     * 同步 Store 的选择状态到视觉
     */
    private syncWithStore(ids: string[]) {
        const currentIds = new Set(ids);

        // 移除不再选中的对象（检查 BoxHelper）
        for (const [id, _box] of this.selectionBoxes) {
            if (!currentIds.has(id)) {
                this.removeSelectionVisual(id);
            }
        }
        // 移除不再选中的对象（检查轮廓线）
        for (const [id, _line] of this.selectionOutlines) {
            if (!currentIds.has(id)) {
                this.removeSelectionVisual(id);
            }
        }
        // 移除不再选中的对象（检查 X 对角线）
        for (const [id, _cross] of this.selectionCrossLines) {
            if (!currentIds.has(id)) {
                this.removeSelectionVisual(id);
            }
        }

        // 添加新选中的对象
        for (const id of ids) {
            if (!this.selectionBoxes.has(id) && !this.selectionOutlines.has(id)) {
                const object = this.findObjectById(id);
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
            line.position.y = 1000; // 抬高到最上层
            line.renderOrder = 999; // 确保最后渲染

            this.scene.add(line);
            this.selectionOutlines.set(id, line);

            // Zone 类型额外添加 X 对角线
            if (object.userData?.type === 'zone') {
                this.addSelectionCrossLines(id, polygon);
            }
        } else {
            // 降级到 BoxHelper
            const box = new THREE.BoxHelper(object, 0x3b82f6);
            box.renderOrder = 999; // 确保最后渲染
            this.scene.add(box);
            this.selectionBoxes.set(id, box);
        }
        this.selectedObjects.set(id, object);
    }

    /**
     * Zone 选中时的 X 对角线（AABB 对角线裁剪到多边形边界内）
     */
    private addSelectionCrossLines(id: string, polygon: [number, number][]) {
        if (polygon.length < 3) return;

        // 1. 计算 AABB
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        for (const p of polygon) {
            if (p[0] < minX) minX = p[0];
            if (p[0] > maxX) maxX = p[0];
            if (p[1] < minY) minY = p[1];
            if (p[1] > maxY) maxY = p[1];
        }

        // 2. 两条 AABB 对角线，裁剪到多边形内
        const segs = [
            ...this.clipLineToPolygon([minX, minY], [maxX, maxY], polygon),
            ...this.clipLineToPolygon([minX, maxY], [maxX, minY], polygon),
        ];
        if (segs.length === 0) return;

        const coords: number[] = [];
        for (const s of segs) {
            coords.push(s[0][0], s[0][1], 0, s[1][0], s[1][1], 0);
        }

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(coords), 3));

        const crossLines = new THREE.LineSegments(geometry, this.selectionMaterial);
        crossLines.rotation.x = -Math.PI / 2;
        crossLines.position.y = 1000;
        crossLines.renderOrder = 999;

        this.scene.add(crossLines);
        this.selectionCrossLines.set(id, crossLines);
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
        // 清理 X 对角线
        const cross = this.selectionCrossLines.get(id);
        if (cross) {
            this.scene.remove(cross);
            cross.geometry?.dispose();
            this.selectionCrossLines.delete(id);
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
        // 清理 X 对角线
        for (const [_id, cross] of this.selectionCrossLines) {
            this.scene.remove(cross);
            cross.geometry?.dispose();
        }
        this.selectionCrossLines.clear();
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

    // ── 线段-多边形裁剪算法 ──

    /**
     * 将线段 p1→p2 裁剪到多边形内部，返回在多边形内的线段片段
     */
    private clipLineToPolygon(
        p1: [number, number], p2: [number, number],
        polygon: [number, number][]
    ): [[number, number], [number, number]][] {
        // 收集线段与多边形各边的交点参数 t (0~1)
        const tValues: number[] = [];

        if (this.pointInPolygon(p1, polygon)) tValues.push(0);
        if (this.pointInPolygon(p2, polygon)) tValues.push(1);

        for (let i = 0; i < polygon.length; i++) {
            const a = polygon[i];
            const b = polygon[(i + 1) % polygon.length];
            const t = this.lineLineIntersectT(p1, p2, a, b);
            if (t !== null && t > 1e-9 && t < 1 - 1e-9) {
                tValues.push(t);
            }
        }

        tValues.sort((a, b) => a - b);

        // 去重
        const unique: number[] = [];
        for (const t of tValues) {
            if (unique.length === 0 || t - unique[unique.length - 1] > 1e-9) {
                unique.push(t);
            }
        }

        // 每对相邻 t 值的中点如在多边形内，则该段保留
        const result: [[number, number], [number, number]][] = [];
        const dx = p2[0] - p1[0], dy = p2[1] - p1[1];

        for (let i = 0; i < unique.length - 1; i++) {
            const t1 = unique[i], t2 = unique[i + 1];
            const mid: [number, number] = [
                p1[0] + (t1 + t2) / 2 * dx,
                p1[1] + (t1 + t2) / 2 * dy
            ];
            if (this.pointInPolygon(mid, polygon)) {
                result.push([
                    [p1[0] + t1 * dx, p1[1] + t1 * dy],
                    [p1[0] + t2 * dx, p1[1] + t2 * dy]
                ]);
            }
        }
        return result;
    }

    /**
     * 求线段 p1→p2 上与线段 p3→p4 的交点参数 t（仅当 p3→p4 的 u 在 [0,1] 内时返回）
     */
    private lineLineIntersectT(
        p1: [number, number], p2: [number, number],
        p3: [number, number], p4: [number, number]
    ): number | null {
        const dx1 = p2[0] - p1[0], dy1 = p2[1] - p1[1];
        const dx2 = p4[0] - p3[0], dy2 = p4[1] - p3[1];
        const denom = dx1 * dy2 - dy1 * dx2;
        if (Math.abs(denom) < 1e-10) return null;

        const t = ((p3[0] - p1[0]) * dy2 - (p3[1] - p1[1]) * dx2) / denom;
        const u = ((p3[0] - p1[0]) * dy1 - (p3[1] - p1[1]) * dx1) / denom;

        return (u >= 0 && u <= 1) ? t : null;
    }

    /** 射线法点在多边形内测试 */
    private pointInPolygon(pt: [number, number], poly: [number, number][]): boolean {
        let inside = false;
        const x = pt[0], y = pt[1];
        for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
            const xi = poly[i][0], yi = poly[i][1];
            const xj = poly[j][0], yj = poly[j][1];
            if (((yi > y) !== (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi)) {
                inside = !inside;
            }
        }
        return inside;
    }
}
