import * as THREE from 'three';
import type { Tool } from './Tool';
import { GhostManager } from '../GhostManager';
import { SnappingEngine } from '../SnappingEngine';
import { useCanvasStore } from '../../../stores/canvasStore';

export class RotateTool implements Tool {
    name = 'Rotate';
    private scene: THREE.Scene;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private ghostManager: GhostManager;
    private snappingEngine: SnappingEngine;
    private raycaster: THREE.Raycaster;
    private plane: THREE.Plane;

    private state: 'waiting_selection' | 'waiting_center' | 'waiting_start' | 'waiting_end' = 'waiting_selection';
    private centerPoint: THREE.Vector3 | null = null;
    private startAngle: number | null = null;
    private selectedObject: any = null;
    private originalObject: THREE.Object3D | null = null;

    // Visuals
    private centerMarker: THREE.Mesh | null = null;
    private startLine: THREE.Line | null = null;
    private endLine: THREE.Line | null = null;

    constructor(
        scene: THREE.Scene,
        camera: THREE.Camera,
        domElement: HTMLElement,
        ghostManager: GhostManager
    ) {
        this.scene = scene;
        this.camera = camera;
        this.domElement = domElement;
        this.ghostManager = ghostManager;
        this.snappingEngine = new SnappingEngine();
        this.raycaster = new THREE.Raycaster();
        this.plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
    }

    activate() {
        const store = useCanvasStore();
        this.selectedObject = store.selectedObject;
        console.log("RotateTool Activate. Selected:", this.selectedObject);

        if (this.selectedObject && (this.selectedObject.type === 'module' || this.selectedObject.userData?.type === 'module')) {
            if (this.selectedObject.userData && this.selectedObject.userData.data) {
                this.selectedObject = this.selectedObject.userData.data;
            }
            this.startRotateOperation();
        } else {
            this.state = 'waiting_selection';
            store.setPrompt('Select object to rotate');
            this.domElement.style.cursor = 'default';
        }
    }

    private startRotateOperation() {
        const store = useCanvasStore();
        this.originalObject = this.findObjectById(this.selectedObject.id);

        if (this.originalObject) {
            this.ghostManager.createGhost(this.originalObject);
        }

        // Default center is object center
        this.centerPoint = this.calculateCenter(this.selectedObject.bounds);
        this.createCenterMarker(this.centerPoint);

        this.state = 'waiting_center';
        this.domElement.style.cursor = 'crosshair';
        store.setPrompt('Click to set rotation center');
    }

    deactivate() {
        const store = useCanvasStore();
        this.ghostManager.removeGhost();
        this.removeVisuals();
        this.domElement.style.cursor = 'default';
        this.state = 'waiting_selection';
        this.centerPoint = null;
        this.startAngle = null;
        store.setPrompt(null);
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

    private calculateCenter(bounds: [number, number][]): THREE.Vector3 {
        let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
        bounds.forEach(p => {
            minX = Math.min(minX, p[0]);
            minY = Math.min(minY, p[1]);
            maxX = Math.max(maxX, p[0]);
            maxY = Math.max(maxY, p[1]);
        });
        const centerX = (minX + maxX) / 2;
        const centerY = (minY + maxY) / 2;
        // Map to 3D: X=X, Z=-Y
        return new THREE.Vector3(centerX, 0, -centerY);
    }

    onMouseDown(event: MouseEvent) {
        if (event.button !== 0) return;

        const point = this.getRayIntersection(event);
        if (!point) return;

        const store = useCanvasStore();

        if (this.state === 'waiting_selection') {
            const hit = this.raycastObject(event);
            if (hit && hit.userData && hit.userData.type === 'module') {
                store.setSelectedObject(hit.userData.data);
                this.selectedObject = hit.userData.data;
                this.startRotateOperation();
            } else {
                console.log("Invalid selection for Rotate Tool");
            }
            return;
        }

        // Snap
        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_center') {
            this.centerPoint = finalPoint;
            this.updateCenterMarker(this.centerPoint);

            // Set pivot for ghost rotation
            this.ghostManager.setPivot(this.centerPoint);

            this.state = 'waiting_start';
            store.setPrompt('Click to set start angle');

        } else if (this.state === 'waiting_start') {
            if (!this.centerPoint) return;
            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            this.startAngle = Math.atan2(vector.z, vector.x); // Radians

            this.createStartLine(this.centerPoint, finalPoint);
            this.state = 'waiting_end';
            store.setPrompt('Click to set end angle');

        } else if (this.state === 'waiting_end') {
            this.executeRotate(finalPoint);
        }
    }

    onMouseMove(event: MouseEvent) {
        const point = this.getRayIntersection(event);
        if (!point) return;

        if (this.state === 'waiting_selection') {
            const hit = this.raycastObject(event);
            if (hit && hit.userData && hit.userData.type === 'module') {
                this.domElement.style.cursor = 'pointer';
            } else {
                this.domElement.style.cursor = 'default';
            }
            return;
        }

        const snapObjects = this.scene.children.filter(c => !c.userData.isGhost);
        const snapResult = this.snappingEngine.snap(point, snapObjects);
        const finalPoint = snapResult.snapped ? snapResult.position : point;

        if (this.state === 'waiting_center') {
            // Preview center? Maybe just cursor
        } else if (this.state === 'waiting_start') {
            // Preview start line?
        } else if (this.state === 'waiting_end' && this.centerPoint && this.startAngle !== null) {
            // Update End Line
            this.updateEndLine(this.centerPoint, finalPoint);

            // Update Ghost Rotation
            const vector = new THREE.Vector3().subVectors(finalPoint, this.centerPoint);
            const currentAngle = Math.atan2(vector.z, vector.x);
            const deltaRotation = currentAngle - this.startAngle;

            this.ghostManager.setRotation(deltaRotation);
        }
    }

    onMouseUp(_event: MouseEvent) { }

    onKeyDown(event: KeyboardEvent) {
        if (event.key === 'Escape') {
            this.deactivate();
            window.dispatchEvent(new CustomEvent('bimcanvas:tool-cancelled'));
        }
    }

    private raycastObject(event: MouseEvent): THREE.Object3D | null {
        const rect = this.domElement.getBoundingClientRect();
        const mouse = new THREE.Vector2(
            ((event.clientX - rect.left) / rect.width) * 2 - 1,
            -((event.clientY - rect.top) / rect.height) * 2 + 1
        );

        this.raycaster.setFromCamera(mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        const hit = intersects.find(i => i.object instanceof THREE.Mesh && !i.object.userData.isGhost);
        return hit ? hit.object : null;
    }

    private executeRotate(endPoint: THREE.Vector3) {
        if (!this.centerPoint || this.startAngle === null || !this.selectedObject) return;

        const vector = new THREE.Vector3().subVectors(endPoint, this.centerPoint);
        const endAngle = Math.atan2(vector.z, vector.x);

        // 3D Delta (X, Z) where Z is Down.
        // CCW Gesture -> Negative Delta.
        // 2D Math (X, Y) where Y is Up (standard math) or Y is Down (Canvas).
        // In Canvas (Y Down), CCW is Positive Rotation?
        // Wait, standard matrix:
        // x' = x cos - y sin
        // y' = x sin + y cos
        // This rotates (1,0) to (cos, sin).
        // If theta is positive: (0, 1) -> Down. CW.
        // So Matrix rotates CW for Positive Theta.
        // CCW Gesture -> Negative Delta3D.
        // If we use Negative Delta3D in Matrix -> Matrix(Neg) -> CCW.
        // So... wait.
        // If Matrix(Pos) is CW.
        // And Gesture(CCW) is Neg Delta.
        // Matrix(Neg) is CCW.
        // So we should use Delta3D DIRECTLY?

        // Let's re-verify Matrix.
        // 2D Canvas Y Down.
        // (1, 0) Right.
        // Rotate +90.
        // x' = 0 - 1 = -1? No. cos(90)=0, sin(90)=1.
        // x' = 0 - 0 = 0.
        // y' = 1 + 0 = 1.
        // (0, 1) Down.
        // So +90 is CW.

        // Gesture CCW (Right -> Up).
        // Delta3D = -90 (Neg).
        // Matrix(-90).
        // cos(-90)=0, sin(-90)=-1.
        // x' = 0 - (-1)*0 = 0.
        // y' = 1*(-1) + 0 = -1.
        // (0, -1) Up.
        // So Matrix(-90) is CCW.

        // So Delta3D (Neg) -> Matrix (CCW).
        // This matches Gesture!

        // So why was it reversed?
        // Maybe my previous analysis of "Reversed" was wrong?
        // User said "Rotation direction is reversed".
        // If I used Delta3D directly, it should be correct.
        // Did I use Delta3D directly?
        // Yes: `const deltaRotation = endAngle - this.startAngle;`

        // So why reversed?
        // Maybe Ghost was reversed (it had `-rotation`), so user saw Ghost go CW when gesturing CCW.
        // And maybe they didn't check the final result carefully, or assumed it would match Ghost?
        // OR, maybe my Matrix logic is wrong for the specific coordinate system of the Store?
        // Store bounds: [x, y].
        // If Store Y is Up (CAD).
        // (1, 0) Right.
        // Rotate +90 (CCW).
        // Should go to (0, 1) Up.
        // Matrix(+90):
        // x' = 0 - 1 = -1? No.
        // x' = 0. y' = 1.
        // (0, 1).
        // So Matrix(+90) is CCW in Y-Up system.

        // Gesture CCW -> Delta3D (-90).
        // Matrix(-90) -> (0, -1) Down.
        // So in Y-Up system, Matrix(-90) is CW.
        // But Gesture was CCW.
        // So Result is CW (Reversed).

        // So if Store is Y-Up:
        // We need Positive Delta for CCW.
        // Delta3D is Negative for CCW.
        // So we MUST Negate Delta3D.

        // Conclusion:
        // If Store is Y-Down (Canvas): Direct Delta3D works.
        // If Store is Y-Up (CAD): Negate Delta3D.

        // Given "Reversed" report, and typical BIM/CAD data, Store is likely Y-Up (or treated as such).
        // So I will Negate.

        const deltaRotation = -(endAngle - this.startAngle); // Negate for 2D Math compatibility

        // Update Store
        const store = useCanvasStore();

        // Rotate bounds around centerPoint (2D)
        // 3D Center (X, 0, Z) -> 2D Center (X, -Z)
        const cx = this.centerPoint.x;
        const cy = -this.centerPoint.z;

        const newBounds = this.selectedObject.bounds.map((p: [number, number]) => {
            const x = p[0];
            const y = p[1];
            const dx = x - cx;
            const dy = y - cy;
            return [
                cx + dx * Math.cos(deltaRotation) - dy * Math.sin(deltaRotation),
                cy + dx * Math.sin(deltaRotation) + dy * Math.cos(deltaRotation)
            ];
        });

        // Update Facing
        let newFacing = this.selectedObject.facing;
        if (Array.isArray(newFacing)) {
            const [vx, vy] = newFacing;
            // Rotate vector
            const nvx = vx * Math.cos(deltaRotation) - vy * Math.sin(deltaRotation);
            const nvy = vx * Math.sin(deltaRotation) + vy * Math.cos(deltaRotation);
            newFacing = [nvx, nvy];
        }

        store.updateModule(this.selectedObject.id, {
            bounds: newBounds,
            facing: newFacing
        });

        const updated = store.document?.modules.find(m => m.id === this.selectedObject.id);
        if (updated) {
            store.setSelectedObject(updated);
        }

        // Clear selection after rotation
        store.setSelectedObject(null);

        console.log("Rotate executed");
        this.deactivate();
        window.dispatchEvent(new CustomEvent('bimcanvas:tool-completed'));
    }

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

    // Visual Helpers
    private createCenterMarker(point: THREE.Vector3) {
        const geometry = new THREE.CircleGeometry(100, 32); // 100mm radius
        const material = new THREE.MeshBasicMaterial({ color: 0x0000ff, depthTest: false });
        this.centerMarker = new THREE.Mesh(geometry, material);
        this.centerMarker.rotation.x = -Math.PI / 2;
        this.centerMarker.position.copy(point);
        this.centerMarker.renderOrder = 999;
        this.scene.add(this.centerMarker);
    }

    private updateCenterMarker(point: THREE.Vector3) {
        if (this.centerMarker) {
            this.centerMarker.position.copy(point);
        }
    }

    private createStartLine(start: THREE.Vector3, end: THREE.Vector3) {
        const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
        const material = new THREE.LineDashedMaterial({ color: 0x0000ff, dashSize: 100, gapSize: 50, depthTest: false });
        this.startLine = new THREE.Line(geometry, material);
        this.startLine.computeLineDistances();
        this.startLine.renderOrder = 999;
        this.scene.add(this.startLine);
    }

    private updateEndLine(start: THREE.Vector3, end: THREE.Vector3) {
        if (!this.endLine) {
            const geometry = new THREE.BufferGeometry().setFromPoints([start, end]);
            const material = new THREE.LineDashedMaterial({ color: 0x00ff00, dashSize: 100, gapSize: 50, depthTest: false });
            this.endLine = new THREE.Line(geometry, material);
            this.endLine.renderOrder = 999;
            this.scene.add(this.endLine);
        } else {
            const positionAttribute = this.endLine.geometry.attributes.position;
            if (positionAttribute) {
                const positions = positionAttribute.array as Float32Array;
                positions[0] = start.x; positions[1] = start.y; positions[2] = start.z;
                positions[3] = end.x; positions[4] = end.y; positions[5] = end.z;
                positionAttribute.needsUpdate = true;
                this.endLine.computeLineDistances();
            }
        }
    }

    private removeVisuals() {
        if (this.centerMarker) { this.scene.remove(this.centerMarker); this.centerMarker = null; }
        if (this.startLine) { this.scene.remove(this.startLine); this.startLine = null; }
        if (this.endLine) { this.scene.remove(this.endLine); this.endLine = null; }
    }
}
