import * as THREE from 'three';
import type { SnapResult } from './SnapSolver';
import type { SnapType } from './SnapTypes';

export class SnapVisual {
    private scene: THREE.Scene;
    private domElement: HTMLElement;
    private marker: THREE.LineSegments | null = null;
    private tooltip: HTMLDivElement | null = null;
    private currentType: SnapType | null = null;
    private isVisible = false;

    constructor(scene: THREE.Scene, domElement: HTMLElement) {
        this.scene = scene;
        this.domElement = domElement;
        this.createMarker();
        this.createTooltip();
    }

    public show(result: SnapResult, screenPoint: { x: number; y: number }): void {
        if (!this.marker || !this.tooltip) return;

        this.currentType = result.type;
        this.marker.position.copy(result.worldPoint);
        this.marker.position.y = 1;

        if (!this.isVisible) {
            this.scene.add(this.marker);
            this.marker.visible = true;
            this.isVisible = true;
        }

        // Tooltip: show immediately, no delay
        this.tooltip.textContent = result.label;
        this.tooltip.style.transform = `translate(${screenPoint.x + 12}px, ${screenPoint.y + 12}px)`;
        this.tooltip.style.display = 'block';
    }

    public hide(): void {
        if (this.marker && this.isVisible) {
            this.marker.visible = false;
            this.scene.remove(this.marker);
            this.isVisible = false;
        }
        if (this.tooltip) {
            this.tooltip.style.display = 'none';
        }
    }

    public dispose(): void {
        this.hide();
        if (this.marker) {
            this.scene.remove(this.marker);
            this.marker.geometry.dispose();
            (this.marker.material as THREE.Material).dispose();
            this.marker = null;
        }
        if (this.tooltip && this.tooltip.parentNode) {
            this.tooltip.parentNode.removeChild(this.tooltip);
            this.tooltip = null;
        }
        this.currentType = null;
    }

    private createMarker(): void {
        const size = 60;
        const geometry = new THREE.BufferGeometry();
        const vertices = new Float32Array([
            -size, 0, -size, size, 0, size,   // diagonal 1
            -size, 0, size, size, 0, -size     // diagonal 2
        ]);
        geometry.setAttribute('position', new THREE.BufferAttribute(vertices, 3));
        const material = new THREE.LineBasicMaterial({
            color: 0x00ff00,
            depthTest: false,
            transparent: true,
            opacity: 0.8
        });
        this.marker = new THREE.LineSegments(geometry, material);
        this.marker.renderOrder = 1000;
        this.marker.visible = false;
    }

    private createTooltip(): void {
        const div = document.createElement('div');
        div.className = 'snap-tooltip';
        div.style.display = 'none';
        div.style.position = 'fixed';
        div.style.left = '0';
        div.style.top = '0';
        div.style.pointerEvents = 'none';
        div.style.zIndex = '9999';
        this.tooltip = div;
        (this.domElement.ownerDocument.body ?? document.body).appendChild(div);
    }
}
