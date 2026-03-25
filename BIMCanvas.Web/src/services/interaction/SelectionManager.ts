import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';
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
     * Zone 选中时的 X 对角线（从质心向 4 个对角方向延伸到多边形边界）
     * 保证 X 中心 = 标签位置（顶点平均中心）
     */
    private addSelectionCrossLines(id: string, polygon: [number, number][]) {
        if (polygon.length < 3) return;

        // 1. 计算顶点平均中心（与 polygonCenterToWorld 一致）
        let cx = 0, cy = 0;
        for (const p of polygon) { cx += p[0]; cy += p[1]; }
        cx /= polygon.length;
        cy /= polygon.length;
        const centroid: [number, number] = [cx, cy];

        // 2. 从质心向 4 个对角方向发射射线，求与多边形边界的交点
        const NE = this.rayPolygonIntersection(centroid, [1, 1], polygon);
        const SW = this.rayPolygonIntersection(centroid, [-1, -1], polygon);
        const NW = this.rayPolygonIntersection(centroid, [-1, 1], polygon);
        const SE = this.rayPolygonIntersection(centroid, [1, -1], polygon);

        // 3. 连线形成 X
        const segs: [[number, number], [number, number]][] = [];
        if (NE && SW) segs.push([NE, SW]);
        if (NW && SE) segs.push([NW, SE]);
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
     * 从 origin 沿 direction 发射射线，返回与多边形边界的最近交点
     */
    private rayPolygonIntersection(
        origin: [number, number],
        direction: [number, number],
        polygon: [number, number][]
    ): [number, number] | null {
        // 用足够远的点模拟射线
        const farPoint: [number, number] = [
            origin[0] + direction[0] * 1e8,
            origin[1] + direction[1] * 1e8
        ];
        let minT = Infinity;
        for (let i = 0; i < polygon.length; i++) {
            const a = polygon[i];
            const b = polygon[(i + 1) % polygon.length];
            if (!a || !b) continue;
            const t = this.lineLineIntersectT(origin, farPoint, a, b);
            if (t !== null && t > 1e-9 && t < minT) {
                minT = t;
            }
        }
        if (minT === Infinity) return null;
        return [
            origin[0] + (farPoint[0] - origin[0]) * minT,
            origin[1] + (farPoint[1] - origin[1]) * minT
        ];
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

}
