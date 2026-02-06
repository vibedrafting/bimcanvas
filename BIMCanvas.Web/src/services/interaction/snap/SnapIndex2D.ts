import * as THREE from 'three';

export interface SnapEdge {
    id: string;
    sourceId: string;
    a: THREE.Vector2; // world (x, z)
    b: THREE.Vector2; // world (x, z)
}

export interface SnapAabb {
    minX: number;
    maxX: number;
    minZ: number;
    maxZ: number;
}

export class SnapIndex2D {
    private cellSize: number;
    private cells: Map<string, SnapEdge[]> = new Map();
    private edgeIdCounter = 1;

    constructor(cellSize: number = 2000) {
        this.cellSize = cellSize;
    }

    public rebuild(document: any): void {
        this.clear();

        if (!document) return;

        // Modules
        if (document?.activeScheme?.modules) {
            for (const m of document.activeScheme.modules) {
                if (!m?.bounds) continue;
                this.addPolygonEdges(m.bounds, m.id ?? 'module');
            }
        }

        // Walls
        if (document?.baseline?.walls) {
            for (const wall of document.baseline.walls) {
                if (!wall?.polygon) continue;
                this.addPolygonEdges(wall.polygon, wall.id ?? 'wall');
            }
        }

        // Columns
        if (document?.baseline?.columns) {
            for (const col of document.baseline.columns) {
                if (!col?.polygon) continue;
                this.addPolygonEdges(col.polygon, col.id ?? 'column');
            }
        }

        // Openings (line)
        if (document?.baseline?.openings) {
            for (const opening of document.baseline.openings) {
                if (!opening?.line) continue;
                this.addLineEdge(opening.line, opening.id ?? 'opening');
            }
        }
    }

    public clear(): void {
        this.cells.clear();
        this.edgeIdCounter = 1;
    }

    public queryEdges(aabb: SnapAabb): SnapEdge[] {
        const minCellX = Math.floor(aabb.minX / this.cellSize);
        const maxCellX = Math.floor(aabb.maxX / this.cellSize);
        const minCellZ = Math.floor(aabb.minZ / this.cellSize);
        const maxCellZ = Math.floor(aabb.maxZ / this.cellSize);

        const result: SnapEdge[] = [];
        const seen = new Set<string>();

        for (let cx = minCellX; cx <= maxCellX; cx++) {
            for (let cz = minCellZ; cz <= maxCellZ; cz++) {
                const key = `${cx},${cz}`;
                const bucket = this.cells.get(key);
                if (!bucket) continue;
                for (const edge of bucket) {
                    if (seen.has(edge.id)) continue;
                    seen.add(edge.id);
                    result.push(edge);
                }
            }
        }

        return result;
    }

    private addPolygonEdges(polygon: [number, number][], sourceId: string): void {
        if (!polygon || polygon.length < 2) return;

        const points = polygon.map(p => new THREE.Vector2(p[0], -p[1]));
        const lastIndex = points.length - 1;
        const isClosed = points.length > 2 && points[0].distanceTo(points[lastIndex]) < 0.001;
        const max = isClosed ? lastIndex : points.length;

        for (let i = 0; i < max; i++) {
            const next = (i + 1) % max;
            const a = points[i];
            const b = points[next];
            if (a.distanceToSquared(b) < 0.0001) continue;
            this.addEdge(a, b, sourceId);
        }
    }

    private addLineEdge(line: [number, number][], sourceId: string): void {
        if (!line || line.length < 2) return;
        const [p1, p2] = line;
        if (!p1 || !p2) return;
        const a = new THREE.Vector2(p1[0], -p1[1]);
        const b = new THREE.Vector2(p2[0], -p2[1]);
        if (a.distanceToSquared(b) < 0.0001) return;
        this.addEdge(a, b, sourceId);
    }

    private addEdge(a: THREE.Vector2, b: THREE.Vector2, sourceId: string): void {
        const edge: SnapEdge = {
            id: `${sourceId}:${this.edgeIdCounter++}`,
            sourceId,
            a: a.clone(),
            b: b.clone()
        };

        const minX = Math.min(edge.a.x, edge.b.x);
        const maxX = Math.max(edge.a.x, edge.b.x);
        const minZ = Math.min(edge.a.y, edge.b.y);
        const maxZ = Math.max(edge.a.y, edge.b.y);

        const minCellX = Math.floor(minX / this.cellSize);
        const maxCellX = Math.floor(maxX / this.cellSize);
        const minCellZ = Math.floor(minZ / this.cellSize);
        const maxCellZ = Math.floor(maxZ / this.cellSize);

        for (let cx = minCellX; cx <= maxCellX; cx++) {
            for (let cz = minCellZ; cz <= maxCellZ; cz++) {
                const key = `${cx},${cz}`;
                if (!this.cells.has(key)) {
                    this.cells.set(key, []);
                }
                this.cells.get(key)!.push(edge);
            }
        }
    }
}
