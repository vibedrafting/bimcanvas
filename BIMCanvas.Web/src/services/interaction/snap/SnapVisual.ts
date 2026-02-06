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
    private geometries: Record<SnapType, THREE.BufferGeometry> | null = null;

    constructor(scene: THREE.Scene, domElement: HTMLElement) {
        this.scene = scene;
        this.domElement = domElement;
        this.createMarker();
        this.createTooltip();
    }

    public show(result: SnapResult, screenPoint: { x: number; y: number }): void {
        if (!this.marker || !this.tooltip || !this.geometries) return;

        // Switch geometry when snap type changes
        if (result.type !== this.currentType) {
            this.marker.geometry = this.geometries[result.type];
            this.currentType = result.type;
        }

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
            (this.marker.material as THREE.Material).dispose();
            this.marker = null;
        }
        if (this.geometries) {
            for (const type of Object.keys(this.geometries) as SnapType[]) {
                this.geometries[type].dispose();
            }
            this.geometries = null;
        }
        if (this.tooltip && this.tooltip.parentNode) {
            this.tooltip.parentNode.removeChild(this.tooltip);
            this.tooltip = null;
        }
        this.currentType = null;
    }

    private createMarker(): void {
        const s = 60;

        // Build 4 geometries for different snap types
        this.geometries = {
            // endpoint: □ square
            endpoint: this.buildGeometry([
                -s, 0, -s,  s, 0, -s,
                 s, 0, -s,  s, 0,  s,
                 s, 0,  s, -s, 0,  s,
                -s, 0,  s, -s, 0, -s
            ]),
            // midpoint: △ triangle
            midpoint: this.buildGeometry([
                 0, 0, -s,  s, 0,  s,
                 s, 0,  s, -s, 0,  s,
                -s, 0,  s,  0, 0, -s
            ]),
            // intersection: × cross (original)
            intersection: this.buildGeometry([
                -s, 0, -s,  s, 0,  s,
                -s, 0,  s,  s, 0, -s
            ]),
            // perpendicular: ⊥ right-angle L shape
            perpendicular: this.buildGeometry([
                -s, 0,  s,  s, 0,  s,
                -s, 0, -s, -s, 0,  s
            ])
        };

        const material = new THREE.LineBasicMaterial({
            color: 0x00ff00,
            depthTest: false,
            transparent: true,
            opacity: 0.8
        });

        // Default to intersection geometry
        this.marker = new THREE.LineSegments(this.geometries.intersection, material);
        this.marker.renderOrder = 1000;
        this.marker.visible = false;
    }

    private buildGeometry(vertices: number[]): THREE.BufferGeometry {
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(new Float32Array(vertices), 3));
        return geometry;
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
