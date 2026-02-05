import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import type { Tool } from './Tool';
import { SnappingEngine } from '../SnappingEngine';
import { SnapIndicator } from '../SnapIndicator';
import { AxisLockHelper } from '../AxisLockHelper';
import { useCanvasStore } from '../../../stores/canvasStore';
import { LayerManager } from '../../three/LayerManager';

export class MeasurementTool implements Tool {
    name = 'Measurement';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private snappingEngine: SnappingEngine;
    private snapIndicator: SnapIndicator;
    private axisLockHelper: AxisLockHelper;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    // State Machine
    // idle: Ready to start
    // measuring: First point set, waiting for second point
    // finished: Result displayed, waiting for reset or exit
    private state: 'idle' | 'measuring' | 'finished' = 'idle';

    private startPoint: THREE.Vector3 | null = null;
    private endPoint: THREE.Vector3 | null = null;

    // Visuals
    private rubberBand: THREE.Line | null = null;  // The line being drawn
    private endMarker: THREE.Mesh | null = null;   // Small 'X' or dot at end
    private distanceLabel: CSS2DObject | null = null;

    constructor(scene: THREE.Scene, camera: THREE.Camera, domElement: HTMLElement) {
        this.scene = scene;
        this.camera = camera;
        this.domElement = domElement;
        this.snappingEngine = new SnappingEngine();
        this.snapIndicator = new SnapIndicator(scene);
        this.axisLockHelper = new AxisLockHelper(scene);
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate() {
        const store = useCanvasStore();
        store.currentOperation = 'measuring';
        this.resetState();

        // Build snap points from current project data
        this.snappingEngine.buildSnapPoints(store.projectData);
    }

    deactivate() {
        this.cleanupVisuals();
        this.snapIndicator.dispose();
        this.axisLockHelper.dispose();
        this.snappingEngine.clear();

        const store = useCanvasStore();
        if (store.currentOperation === 'measuring') {
            store.currentOperation = null;
            store.setPrompt(null);
        }
        this.domElement.style.cursor = 'default';

        // Re-init helpers for next usage
        this.snapIndicator = new SnapIndicator(this.scene);
        this.axisLockHelper = new AxisLockHelper(this.scene);
    }

    private resetState() {
        this.cleanupVisuals();
        this.state = 'idle';
        this.startPoint = null;
        this.endPoint = null;
        this.axisLockHelper.hide();

        const store = useCanvasStore();
        store.setPrompt('Specify first point');
        this.domElement.style.cursor = 'crosshair';
    }

    onMouseDown(event: MouseEvent) {
        if (event.button !== 0) return;
        const point = this.getRayIntersection(event);
        if (!point) return;

        // Apply snapping
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'idle' || this.state === 'finished') {
            // Start new measurement
            if (this.state === 'finished') {
                this.resetState();
            }

            this.startPoint = finalPoint;
            this.state = 'measuring';
            this.createRubberBand(this.startPoint);

            const store = useCanvasStore();
            store.setPrompt('Specify second point');

        } else if (this.state === 'measuring') {
            // Finish measurement
            if (this.startPoint) {
                // Apply auto-ortho lock for the final point
                const lockedPoint = this.axisLockHelper.lock(this.startPoint, finalPoint, false);
                this.endPoint = lockedPoint;

                // Calculate result BEFORE cleanup
                const resultText = this.getDistanceText(this.startPoint, this.endPoint);

                // User wants: Only the bottom prompt visible after finish.
                // Clean up ALL visuals
                this.cleanupVisuals(); // Removes rubberBand, endMarker, distanceLabel
                this.axisLockHelper.hide();
                this.snapIndicator.hide();

                this.state = 'finished';
                const store = useCanvasStore();
                store.setPrompt('Measurement: ' + resultText);

                this.domElement.style.cursor = 'default';
            }
        }
    }

    onMouseMove(event: MouseEvent) {
        const point = this.getRayIntersection(event);
        if (!point) return;

        // Apply snapping (always snap to world objects)
        const snapResult = this.snappingEngine.snap(point);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        // Update snap indicator
        if (snapResult.snapped) {
            this.snapIndicator.show(snapResult.position);
        } else {
            this.snapIndicator.hide();
        }

        // Logic during measurement
        if (this.state === 'measuring' && this.startPoint) {
            // Apply Auto-Ortho Lock (Soft Snap)
            // Pass shiftHeld=false because we want auto-lock behavior described in AxisLockHelper
            const lockedPoint = this.axisLockHelper.lock(this.startPoint, finalPoint, false);

            this.updateRubberBand(lockedPoint);
            this.updateDistanceLabel(this.startPoint, lockedPoint);
        }
    }

    onMouseUp(event: MouseEvent) { }

    onKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            // User feedback: "Click once ESC is not end, must click twice to exit"
            // Fix: ESC always exits the tool immediately
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
        }
    }

    // === Helpers ===

    private getRayIntersection(event: MouseEvent): THREE.Vector3 | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((event.clientX - rect.left) / rect.width) * 2 - 1,
            -((event.clientY - rect.top) / rect.height) * 2 + 1
        );

        this.raycaster.setFromCamera(mouse, this.camera);
        const intersection = new THREE.Vector3();
        if (this.raycaster.ray.intersectPlane(this.plane, intersection)) {
            return intersection;
        }
        return null;
    }

    // === Visuals ===

    private createRubberBand(start: THREE.Vector3) {
        const geometry = new THREE.BufferGeometry().setFromPoints([start, start]);
        // Orange/Yellow high contrast color for measurement
        const material = new THREE.LineDashedMaterial({
            color: 0xffa500,  // Orange
            dashSize: 100,
            gapSize: 50,
            depthTest: false
        });
        this.rubberBand = new THREE.Line(geometry, material);
        this.rubberBand.computeLineDistances();
        this.rubberBand.renderOrder = 999;
        this.scene.add(this.rubberBand);
    }

    private updateRubberBand(end: THREE.Vector3) {
        if (this.rubberBand && this.startPoint) {
            const positions = this.rubberBand.geometry.attributes.position.array as Float32Array;
            positions[3] = end.x;
            positions[4] = end.y;
            positions[5] = end.z;
            this.rubberBand.geometry.attributes.position.needsUpdate = true;
            this.rubberBand.computeLineDistances();
        }
    }

    private createEndMarker(position: THREE.Vector3) {
        if (this.endMarker) {
            this.scene.remove(this.endMarker);
            this.endMarker.geometry.dispose();
        }

        // Small Cross 'X'
        const size = 50;
        const geometry = new THREE.BufferGeometry();
        const vertices = new Float32Array([
            -size, 0, -size, size, 0, size,
            -size, 0, size, size, 0, -size
        ]);
        geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));

        const material = new THREE.LineBasicMaterial({
            color: 0xffa500,
            depthTest: false
        });

        // Use LineSegments for the X
        // We cast to Mesh because my visuals logic might expect Object3D, but LineSegments is fine.
        // Actually, let's just make it a simple mesh sphere for simplicity or lines.
        // Let's stick to X using Lines.
        const lines = new THREE.LineSegments(geometry, material);
        lines.position.copy(position);
        lines.position.y = 2; // Slightly higher
        lines.renderOrder = 999;

        this.scene.add(lines);
        // Track it as endMarker
        this.endMarker = lines as any;
    }

    private createDistanceLabel() {
        const div = document.createElement('div');
        div.className = 'measurement-tool-label';
        // CAD Style: Dark background, white text, sharp edges
        // Positioned slightly away from center via transform
        div.style.transform = 'translate(15px, -15px)';
        div.style.cssText = `
            background: #2b2d31; 
            color: #fff;
            padding: 4px 8px;
            border: 1px solid #454545;
            border-radius: 2px;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 12px;
            line-height: 1;
            pointer-events: none;
            white-space: nowrap;
            box-shadow: 0 2px 8px rgba(0,0,0,0.3);
            z-index: 1000;
        `;
        this.distanceLabel = new CSS2DObject(div);
        this.distanceLabel.layers.set(LayerManager.LAYER_LABELS);
        this.scene.add(this.distanceLabel);
    }

    private updateDistanceLabel(start: THREE.Vector3, end: THREE.Vector3) {
        if (!this.distanceLabel) {
            this.createDistanceLabel();
        }

        if (this.state === 'measuring') {
            // Dynamic Input Style: Follow the cursor/end point
            const offset = new THREE.Vector3(20, 20, 0); // Offset in screen space logic? 
            // CSS2D object position is World Position.
            // We want it near 'end' point.
            this.distanceLabel!.position.copy(end);
            this.distanceLabel!.position.y += 0; // Keep at same height?
            // Actually, move it slightly "up" in Z or X to not overlap cursor
            // But in 2D view (Top down), Y is up.
            // Let's offset slightly in X/Z to be next to cursor.
            // For 2D overlay, we normally rely on the div offset.
            // Let's just put it AT the point, and use CSS margin to offset.
            // Current CSS has no margin.

            // Let's keep it simple: At cursor for dynamic, Midpoint for static?
            // CAD usually keeps it at cursor dynamic.
        } else {
            // Finished: Show at midpoint
            const mid = new THREE.Vector3().lerpVectors(start, end, 0.5);
            this.distanceLabel!.position.copy(mid);
        }

        const div = this.distanceLabel!.element as HTMLDivElement;
        div.textContent = this.getDistanceText(start, end);
    }

    private getDistanceText(start: THREE.Vector3, end: THREE.Vector3): string {
        const dx = end.x - start.x;
        const dz = end.z - start.z;
        const dist = Math.sqrt(dx * dx + dz * dz);
        return `${Math.round(dist)} mm`;
    }

    private cleanupVisuals() {
        if (this.rubberBand) {
            this.scene.remove(this.rubberBand);
            this.rubberBand.geometry.dispose();
            (this.rubberBand.material as THREE.Material).dispose();
            this.rubberBand = null;
        }
        if (this.endMarker) {
            this.scene.remove(this.endMarker);
            // It's a LineSegments, but we cast to any. Handle dispose safely.
            if ((this.endMarker as any).geometry) (this.endMarker as any).geometry.dispose();
            if ((this.endMarker as any).material) (this.endMarker as any).material.dispose();
            this.endMarker = null;
        }
        if (this.distanceLabel) {
            if (this.distanceLabel.element.parentNode) {
                this.distanceLabel.element.parentNode.removeChild(this.distanceLabel.element);
            }
            this.scene.remove(this.distanceLabel);
            this.distanceLabel = null;
        }
    }
}
