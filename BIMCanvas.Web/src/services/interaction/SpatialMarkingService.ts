import * as THREE from 'three';
import { useCanvasStore } from '../../stores/canvasStore';
import type { GridSelectionCell } from '../../types/aiCommandCenter';
import type { Point2D, Polygon2D, ProjectData } from '../../types/canvas';

interface SpatialMarkModeDetail {
    active: boolean;
    cellSize?: number;
    selectedCells?: GridSelectionCell[];
}

interface SpatialState {
    active: boolean;
    cellSize: number;
    selectedCells: GridSelectionCell[];
}

interface Aabb {
    minX: number;
    minY: number;
    maxX: number;
    maxY: number;
}

export class SpatialMarkingService {
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private scene: THREE.Scene;
    private store: ReturnType<typeof useCanvasStore>;

    private state: SpatialState | null = null;
    private overlayGroup: THREE.Group | null = null;
    private hoverCell: GridSelectionCell | null = null;
    private mouseDownPoint: THREE.Vector2 | null = null;
    private mouseDownCell: GridSelectionCell | null = null;
    private suppressNextClick = false;

    private boundModeChange: (event: Event) => void;

    constructor(camera: THREE.Camera, domElement: HTMLElement, scene: THREE.Scene) {
        this.camera = camera;
        this.domElement = domElement;
        this.scene = scene;
        this.store = useCanvasStore();
        this.boundModeChange = this.handleModeChange.bind(this);
        window.addEventListener('bimcanvas:spatial-mark-mode-change', this.boundModeChange);
    }

    public get isActive(): boolean {
        return this.state?.active === true;
    }

    public refreshOverlay(): void {
        this.renderOverlay();
    }

    public handleMouseDown(event: MouseEvent): boolean {
        if (!this.isActive || event.button !== 0) return false;

        this.mouseDownPoint = new THREE.Vector2(event.clientX, event.clientY);
        this.mouseDownCell = this.getCellFromEvent(event);
        return true;
    }

    public handleMouseMove(event: MouseEvent): boolean {
        if (!this.isActive) return false;

        const nextHover = this.getCellFromEvent(event);
        if (!this.areSameCell(this.hoverCell, nextHover)) {
            this.hoverCell = nextHover;
            this.renderOverlay();
        }

        return true;
    }

    public handleMouseUp(event: MouseEvent): boolean {
        if (!this.isActive) return false;

        const startPoint = this.mouseDownPoint;
        const state = this.state;
        if (!startPoint || !state) return true;

        const dragDistanceSq =
            Math.pow(event.clientX - startPoint.x, 2) +
            Math.pow(event.clientY - startPoint.y, 2);

        const removeMode = event.shiftKey;
        const nextCells = new Map(state.selectedCells.map(cell => [this.cellKey(cell), cell]));

        if (dragDistanceSq > 25) {
            const cells = this.getCellsFromScreenBox(startPoint.x, startPoint.y, event.clientX, event.clientY);
            for (const cell of cells) {
                if (removeMode) {
                    nextCells.delete(this.cellKey(cell));
                } else {
                    nextCells.set(this.cellKey(cell), cell);
                }
            }
            this.suppressNextClick = true;
        } else if (this.mouseDownCell) {
            const key = this.cellKey(this.mouseDownCell);
            if (removeMode) {
                nextCells.delete(key);
            } else {
                nextCells.set(key, this.mouseDownCell);
            }
        }

        state.selectedCells = Array.from(nextCells.values())
            .sort((a, b) => a.row === b.row ? a.col - b.col : a.row - b.row);
        this.mouseDownPoint = null;
        this.mouseDownCell = null;
        this.emitSelectionChange();
        this.renderOverlay();
        return true;
    }

    public handleClick(): boolean {
        if (!this.isActive) return false;
        if (this.suppressNextClick) {
            this.suppressNextClick = false;
        }
        return true;
    }

    public dispose(): void {
        window.removeEventListener('bimcanvas:spatial-mark-mode-change', this.boundModeChange);
        this.clearOverlay();
    }

    private handleModeChange(event: Event): void {
        const detail = ((event as CustomEvent).detail || {}) as SpatialMarkModeDetail;
        if (!detail.active || !detail.cellSize) {
            this.state = null;
            this.hoverCell = null;
            this.clearOverlay();
            return;
        }

        this.state = {
            active: true,
            cellSize: detail.cellSize,
            selectedCells: detail.selectedCells || []
        };
        this.renderOverlay();
    }

    private emitSelectionChange(): void {
        if (!this.state) return;
        window.dispatchEvent(new CustomEvent('bimcanvas:spatial-mark-selection-change', {
            detail: {
                selectedCells: this.state.selectedCells.map(cell => ({ col: cell.col, row: cell.row }))
            }
        }));
    }

    private renderOverlay(): void {
        this.clearOverlay();
        const state = this.state;
        const data = this.store.projectData;
        if (!state?.active || !data) return;

        const selectableShells = this.getSelectableZoneShells(data);
        if (selectableShells.length === 0) return;

        const aabb = this.computeProjectAabb(data, selectableShells);
        if (!this.isFiniteAabb(aabb)) return;

        const minCol = Math.floor(aabb.minX / state.cellSize);
        const maxCol = Math.ceil(aabb.maxX / state.cellSize);
        const minRow = Math.floor(aabb.minY / state.cellSize);
        const maxRow = Math.ceil(aabb.maxY / state.cellSize);
        const cols = maxCol - minCol;
        const rows = maxRow - minRow;
        if (cols <= 0 || rows <= 0 || cols * rows > 30000) return;

        const group = new THREE.Group();
        group.userData.type = 'spatial-mark-grid';
        group.userData.isSpatialMarkGrid = true;

        for (const shell of selectableShells) {
            this.addZoneOutline(group, shell);
        }

        this.addGridLines(group, minCol, maxCol, minRow, maxRow, state.cellSize);
        this.addSelectedCells(group, state.cellSize, state.selectedCells, 0x0a84ff, 0.28);

        if (this.hoverCell && this.isCellSelectable(this.hoverCell, state.cellSize, selectableShells)) {
            this.addSelectedCells(group, state.cellSize, [this.hoverCell], 0xffffff, 0.16);
        }

        this.overlayGroup = group;
        this.scene.add(group);
    }

    private addZoneOutline(group: THREE.Group, shell: Point2D[]): void {
        const points = shell.map(([x, y]) => new THREE.Vector3(x, 10, -y));
        const geometry = new THREE.BufferGeometry().setFromPoints(points);
        const material = new THREE.LineBasicMaterial({
            color: 0x0a84ff,
            transparent: true,
            opacity: 0.9
        });
        const line = new THREE.LineLoop(geometry, material);
        group.add(line);
    }

    private addGridLines(
        group: THREE.Group,
        minCol: number,
        maxCol: number,
        minRow: number,
        maxRow: number,
        cellSize: number
    ): void {
        const positions: number[] = [];
        const minX = minCol * cellSize;
        const maxX = maxCol * cellSize;
        const minY = minRow * cellSize;
        const maxY = maxRow * cellSize;

        for (let col = minCol; col <= maxCol; col += 1) {
            const x = col * cellSize;
            positions.push(x, 8, -minY, x, 8, -maxY);
        }

        for (let row = minRow; row <= maxRow; row += 1) {
            const y = row * cellSize;
            positions.push(minX, 8, -y, maxX, 8, -y);
        }

        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
        const material = new THREE.LineBasicMaterial({
            color: 0x7dd3fc,
            transparent: true,
            opacity: 0.24
        });
        group.add(new THREE.LineSegments(geometry, material));
    }

    private addSelectedCells(
        group: THREE.Group,
        cellSize: number,
        cells: GridSelectionCell[],
        color: number,
        opacity: number
    ): void {
        if (cells.length === 0) return;

        const geometry = new THREE.PlaneGeometry(cellSize, cellSize);
        geometry.rotateX(-Math.PI / 2);
        const material = new THREE.MeshBasicMaterial({
            color,
            transparent: true,
            opacity,
            side: THREE.DoubleSide,
            depthWrite: false
        });

        for (const cell of cells) {
            const centerX = (cell.col + 0.5) * cellSize;
            const centerY = (cell.row + 0.5) * cellSize;
            const mesh = new THREE.Mesh(geometry, material);
            mesh.position.set(centerX, 9, -centerY);
            mesh.userData.type = 'spatial-mark-grid';
            group.add(mesh);
        }
    }

    private clearOverlay(): void {
        const group = this.overlayGroup;
        this.overlayGroup = null;
        if (!group) return;

        if (group.parent) {
            group.parent.remove(group);
        }

        group.traverse(child => {
            const geometry = (child as THREE.Mesh | THREE.Line | THREE.LineSegments).geometry;
            if (geometry) geometry.dispose();

            const material = (child as THREE.Mesh | THREE.Line | THREE.LineSegments).material;
            if (Array.isArray(material)) {
                material.forEach(item => item.dispose());
            } else if (material) {
                material.dispose();
            }
        });
    }

    private getCellFromEvent(event: MouseEvent): GridSelectionCell | null {
        const point = this.getModelPointFromEvent(event);
        const state = this.state;
        const data = this.store.projectData;
        if (!point || !state || !data) return null;

        const selectableShells = this.getSelectableZoneShells(data);
        if (selectableShells.length === 0) return null;

        const col = Math.floor(point[0] / state.cellSize);
        const row = Math.floor(point[1] / state.cellSize);
        const cell = { col, row };

        if (!this.isCellSelectable(cell, state.cellSize, selectableShells)) return null;
        return cell;
    }

    private getCellsFromScreenBox(startX: number, startY: number, endX: number, endY: number): GridSelectionCell[] {
        const startPoint = this.getModelPointFromScreen(startX, startY);
        const endPoint = this.getModelPointFromScreen(endX, endY);
        const state = this.state;
        const data = this.store.projectData;
        if (!startPoint || !endPoint || !state || !data) return [];

        const selectableShells = this.getSelectableZoneShells(data);
        if (selectableShells.length === 0) return [];

        const minX = Math.min(startPoint[0], endPoint[0]);
        const maxX = Math.max(startPoint[0], endPoint[0]);
        const minY = Math.min(startPoint[1], endPoint[1]);
        const maxY = Math.max(startPoint[1], endPoint[1]);

        const minCol = Math.floor(minX / state.cellSize);
        const maxCol = Math.floor(maxX / state.cellSize);
        const minRow = Math.floor(minY / state.cellSize);
        const maxRow = Math.floor(maxY / state.cellSize);

        const cells: GridSelectionCell[] = [];
        for (let row = minRow; row <= maxRow; row += 1) {
            for (let col = minCol; col <= maxCol; col += 1) {
                const cell = { col, row };
                if (this.isCellSelectable(cell, state.cellSize, selectableShells)) {
                    cells.push(cell);
                }
            }
        }

        return cells;
    }

    private getModelPointFromEvent(event: MouseEvent): Point2D | null {
        return this.getModelPointFromScreen(event.clientX, event.clientY);
    }

    private getModelPointFromScreen(clientX: number, clientY: number): Point2D | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((clientX - rect.left) / rect.width) * 2 - 1,
            -((clientY - rect.top) / rect.height) * 2 + 1
        );
        const raycaster = new THREE.Raycaster();
        raycaster.setFromCamera(mouse, this.camera);
        const plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
        const hit = new THREE.Vector3();
        if (!raycaster.ray.intersectPlane(plane, hit)) return null;
        return [hit.x, -hit.z];
    }

    private getZoneBoundary(zone: ProjectData['activeScheme']['zones'][number] | undefined): Polygon2D | null {
        return (zone?.computedBoundary || zone?.rawBoundary || null) as Polygon2D | null;
    }

    private getBoundaryShell(boundary: Polygon2D | { shell?: Point2D[] }): Point2D[] {
        return Array.isArray(boundary) ? boundary : (boundary.shell || []);
    }

    private getSelectableZoneShells(data: ProjectData): Point2D[][] {
        return (data.activeScheme?.zones || [])
            .map(zone => this.getZoneBoundary(zone))
            .map(boundary => boundary ? this.getBoundaryShell(boundary) : [])
            .filter(shell => shell.length >= 3);
    }

    private computeProjectAabb(data: ProjectData, fallbackShells: Point2D[][]): Aabb {
        const points: Point2D[] = [];

        for (const wall of data.baseline?.walls || []) {
            points.push(...(wall.polygon || []));
        }

        for (const column of data.baseline?.columns || []) {
            points.push(...(column.polygon || []));
        }

        for (const opening of data.baseline?.openings || []) {
            if (opening.line) {
                points.push(...opening.line);
            }
        }

        for (const shell of fallbackShells) {
            points.push(...shell);
        }

        return this.computeAabb(points);
    }

    private computeAabb(points: Point2D[]): Aabb {
        return points.reduce<Aabb>((box, [x, y]) => ({
            minX: Math.min(box.minX, x),
            minY: Math.min(box.minY, y),
            maxX: Math.max(box.maxX, x),
            maxY: Math.max(box.maxY, y)
        }), {
            minX: Infinity,
            minY: Infinity,
            maxX: -Infinity,
            maxY: -Infinity
        });
    }

    private isFiniteAabb(aabb: Aabb): boolean {
        return Number.isFinite(aabb.minX) &&
            Number.isFinite(aabb.minY) &&
            Number.isFinite(aabb.maxX) &&
            Number.isFinite(aabb.maxY) &&
            aabb.maxX > aabb.minX &&
            aabb.maxY > aabb.minY;
    }

    private isCellSelectable(cell: GridSelectionCell, cellSize: number, shells: Point2D[][]): boolean {
        const center: Point2D = [
            (cell.col + 0.5) * cellSize,
            (cell.row + 0.5) * cellSize
        ];
        return shells.some(shell => this.isPointInsidePolygon(center, shell));
    }

    private isPointInsidePolygon(point: Point2D, polygon: Point2D[]): boolean {
        let inside = false;
        const [x, y] = point;
        for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i, i += 1) {
            const [xi, yi] = polygon[i]!;
            const [xj, yj] = polygon[j]!;
            const intersects = ((yi > y) !== (yj > y)) &&
                (x < ((xj - xi) * (y - yi)) / ((yj - yi) || Number.EPSILON) + xi);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    private cellKey(cell: GridSelectionCell): string {
        return `${cell.col}:${cell.row}`;
    }

    private areSameCell(left: GridSelectionCell | null, right: GridSelectionCell | null): boolean {
        if (!left && !right) return true;
        if (!left || !right) return false;
        return left.col === right.col && left.row === right.row;
    }
}
