import * as THREE from 'three';
import { SnapConfig } from './SnapConfig';
import { SnapIndex2D, type SnapEdge, type SnapAabb } from './SnapIndex2D';
import { SNAP_TYPE_LABELS, SNAP_TYPE_PRIORITY, type SnapType } from './SnapTypes';

export interface SnapResult {
    snapped: boolean;
    type: SnapType;
    worldPoint: THREE.Vector3;
    screenPoint: { x: number; y: number };
    distancePx: number;
    label: string;
}

interface SnapCandidate {
    type: SnapType;
    world: THREE.Vector3;
    screen: { x: number; y: number };
    dist: number;
    priority: number;
}

export class SnapSolver {
    private index: SnapIndex2D;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;
    private currentSnap: SnapCandidate | null = null;

    constructor(index: SnapIndex2D, camera: THREE.Camera, domElement: HTMLElement) {
        this.index = index;
        this.camera = camera;
        this.domElement = domElement;
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    public clear(): void {
        this.currentSnap = null;
    }

    public snap(screen: { x: number; y: number }, worldPoint?: THREE.Vector3 | null, referencePoint?: THREE.Vector3 | null): SnapResult | null {
        const config = SnapConfig.get();
        const enabledTypes = config.enabled;

        if (!enabledTypes.endpoint && !enabledTypes.midpoint && !enabledTypes.perpendicular && !enabledTypes.intersection) {
            this.currentSnap = null;
            return null;
        }

        const world = worldPoint ?? this.screenToWorld(screen);
        if (!world) return null;

        // Hysteresis: keep current snap if still within snapOutPx
        if (this.currentSnap && enabledTypes[this.currentSnap.type]) {
            const currentScreen = this.worldToScreen(this.currentSnap.world);
            const dist = this.screenDistance(screen, currentScreen);
            if (dist <= config.snapOutPx) {
                this.currentSnap.screen = currentScreen;
                this.currentSnap.dist = dist;
                return this.toResult(this.currentSnap);
            }
            this.currentSnap = null;
        }

        const queryRadiusPx = Math.max(config.snapInPx, config.snapOutPx);
        const aabb = this.getWorldAabb(screen, world, queryRadiusPx);
        const edges = this.index.queryEdges(aabb);

        const candidates = this.buildCandidates(edges, world, screen, config.snapInPx, enabledTypes, referencePoint);
        if (candidates.length === 0) {
            this.currentSnap = null;
            return null;
        }

        let best: SnapCandidate | null = null;
        for (const candidate of candidates) {
            if (!best) {
                best = candidate;
                continue;
            }
            if (candidate.priority < best.priority) {
                best = candidate;
            } else if (candidate.priority === best.priority && candidate.dist < best.dist) {
                best = candidate;
            }
        }

        if (!best) {
            this.currentSnap = null;
            return null;
        }

        this.currentSnap = best;
        return this.toResult(best);
    }

    private buildCandidates(
        edges: SnapEdge[],
        _worldPoint: THREE.Vector3,
        screenPoint: { x: number; y: number },
        snapInPx: number,
        enabled: Record<SnapType, boolean>,
        referencePoint?: THREE.Vector3 | null
    ): SnapCandidate[] {
        const candidates: SnapCandidate[] = [];

        const addCandidate = (type: SnapType, point2: THREE.Vector2) => {
            if (!enabled[type]) return;
            const world = new THREE.Vector3(point2.x, 0, point2.y);
            const screen = this.worldToScreen(world);
            const dist = this.screenDistance(screenPoint, screen);
            if (dist > snapInPx) return;
            candidates.push({
                type,
                world,
                screen,
                dist,
                priority: SNAP_TYPE_PRIORITY[type]
            });
        };

        // Endpoints & Midpoints
        for (const edge of edges) {
            if (enabled.endpoint) {
                addCandidate('endpoint', edge.a);
                addCandidate('endpoint', edge.b);
            }
            if (enabled.midpoint) {
                const mid = new THREE.Vector2(
                    (edge.a.x + edge.b.x) * 0.5,
                    (edge.a.y + edge.b.y) * 0.5
                );
                addCandidate('midpoint', mid);
            }
        }

        // Perpendicular foot: only when a reference point exists
        if (enabled.perpendicular && referencePoint) {
            const ref2 = new THREE.Vector2(referencePoint.x, referencePoint.z);
            for (const edge of edges) {
                const foot = this.projectPointToLine(ref2, edge.a, edge.b);
                addCandidate('perpendicular', foot);
            }
        }

        // Intersections (segment only)
        if (enabled.intersection && edges.length > 1) {
            const dedupe = new Set<string>();
            for (let i = 0; i < edges.length; i++) {
                for (let j = i + 1; j < edges.length; j++) {
                    const edgeA = edges[i];
                    const edgeB = edges[j];
                    if (!edgeA || !edgeB) continue;
                    const p = this.lineIntersection(edgeA.a, edgeA.b, edgeB.a, edgeB.b);
                    if (!p) continue;
                    const key = `${Math.round(p.x * 10)}_${Math.round(p.y * 10)}`;
                    if (dedupe.has(key)) continue;
                    dedupe.add(key);
                    addCandidate('intersection', p);
                }
            }
        }

        return candidates;
    }

    private toResult(candidate: SnapCandidate): SnapResult {
        return {
            snapped: true,
            type: candidate.type,
            worldPoint: candidate.world.clone(),
            screenPoint: { ...candidate.screen },
            distancePx: candidate.dist,
            label: SNAP_TYPE_LABELS[candidate.type]
        };
    }

    private projectPointToLine(p: THREE.Vector2, a: THREE.Vector2, b: THREE.Vector2): THREE.Vector2 {
        const abx = b.x - a.x;
        const aby = b.y - a.y;
        const apx = p.x - a.x;
        const apy = p.y - a.y;
        const denom = abx * abx + aby * aby;
        if (denom < 1e-8) return a.clone();
        const t = (apx * abx + apy * aby) / denom;
        return new THREE.Vector2(a.x + t * abx, a.y + t * aby);
    }

    private lineIntersection(a: THREE.Vector2, b: THREE.Vector2, c: THREE.Vector2, d: THREE.Vector2): THREE.Vector2 | null {
        const r = new THREE.Vector2(b.x - a.x, b.y - a.y);
        const s = new THREE.Vector2(d.x - c.x, d.y - c.y);
        const cross = r.x * s.y - r.y * s.x;
        if (Math.abs(cross) < 1e-8) return null;
        const cma = new THREE.Vector2(c.x - a.x, c.y - a.y);
        const t = (cma.x * s.y - cma.y * s.x) / cross;
        const u = (cma.x * r.y - cma.y * r.x) / cross;
        // Only return intersection if it lies on both segments
        if (t < 0 || t > 1 || u < 0 || u > 1) return null;
        return new THREE.Vector2(a.x + t * r.x, a.y + t * r.y);
    }

    private getWorldAabb(screen: { x: number; y: number }, worldCenter: THREE.Vector3, radiusPx: number): SnapAabb {
        const rightWorld = this.screenToWorld({ x: screen.x + radiusPx, y: screen.y });
        const downWorld = this.screenToWorld({ x: screen.x, y: screen.y + radiusPx });
        const dx = rightWorld ? Math.abs(rightWorld.x - worldCenter.x) : 0;
        const dz = downWorld ? Math.abs(downWorld.z - worldCenter.z) : 0;
        const radiusX = Math.max(dx, 1);
        const radiusZ = Math.max(dz, 1);

        return {
            minX: worldCenter.x - radiusX,
            maxX: worldCenter.x + radiusX,
            minZ: worldCenter.z - radiusZ,
            maxZ: worldCenter.z + radiusZ
        };
    }

    private screenToWorld(screen: { x: number; y: number }): THREE.Vector3 | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((screen.x - rect.left) / rect.width) * 2 - 1,
            -((screen.y - rect.top) / rect.height) * 2 + 1
        );
        this.raycaster.setFromCamera(mouse, this.camera);
        const intersection = new THREE.Vector3();
        if (this.raycaster.ray.intersectPlane(this.plane, intersection)) {
            return intersection;
        }
        return null;
    }

    private worldToScreen(world: THREE.Vector3): { x: number; y: number } {
        const rect = this.domElement.getBoundingClientRect();
        const projected = world.clone().project(this.camera);
        return {
            x: (projected.x + 1) * 0.5 * rect.width + rect.left,
            y: (-projected.y + 1) * 0.5 * rect.height + rect.top
        };
    }

    private screenDistance(a: { x: number; y: number }, b: { x: number; y: number }): number {
        const dx = a.x - b.x;
        const dy = a.y - b.y;
        return Math.sqrt(dx * dx + dy * dy);
    }
}
