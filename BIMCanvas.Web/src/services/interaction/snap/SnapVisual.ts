import * as THREE from 'three';
import { CSS2DObject } from 'three-stdlib';
import { LayerManager } from '../../three/LayerManager';
import type { SnapResult } from './SnapSolver';
import type { SnapType } from './SnapTypes';

export class SnapVisual {
    private scene: THREE.Scene;
    private domElement: HTMLElement;
    private icon: CSS2DObject | null = null;
    private tooltip: HTMLDivElement | null = null;
    private currentType: SnapType | null = null;
    private isVisible = false;
    private hoverTimer: number | null = null;
    private pendingLabel: string = '';
    private pendingScreen: { x: number; y: number } = { x: 0, y: 0 };
    private lastScreen: { x: number; y: number } | null = null;
    private tooltipVisible = false;
    private readonly hoverDelayMs = 350;
    private readonly hoverMoveThresholdPx = 1;

    constructor(scene: THREE.Scene, domElement: HTMLElement) {
        this.scene = scene;
        this.domElement = domElement;
        this.createIcon();
        this.createTooltip();
    }

    public show(result: SnapResult, screenPoint: { x: number; y: number }): void {
        if (!this.icon || !this.tooltip) return;

        const typeChanged = this.currentType !== result.type;
        if (typeChanged) {
            this.updateIconType(result.type);
        }

        this.icon.position.copy(result.worldPoint);
        this.icon.position.y = 1;

        if (!this.isVisible) {
            this.scene.add(this.icon);
            this.icon.visible = true;
            this.isVisible = true;
        }

        this.queueTooltip(result.label, screenPoint, typeChanged);
    }

    public hide(): void {
        if (this.icon && this.isVisible) {
            this.icon.visible = false;
            this.scene.remove(this.icon);
            this.isVisible = false;
        }
        this.hideTooltip();
    }

    public dispose(): void {
        this.hide();
        if (this.icon) {
            this.scene.remove(this.icon);
            if (this.icon.element?.parentNode) {
                this.icon.element.parentNode.removeChild(this.icon.element);
            }
            this.icon = null;
        }
        this.clearHoverTimer();
        if (this.tooltip && this.tooltip.parentNode) {
            this.tooltip.parentNode.removeChild(this.tooltip);
            this.tooltip = null;
        }
        this.currentType = null;
    }

    private createIcon(): void {
        const div = document.createElement('div');
        div.className = 'snap-icon snap-icon--endpoint';
        div.style.pointerEvents = 'none';
        this.icon = new CSS2DObject(div);
        this.icon.layers.set(LayerManager.LAYER_LABELS);
        this.icon.renderOrder = 1000;
        this.icon.visible = false;
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

    private updateIconType(type: SnapType): void {
        if (!this.icon) return;
        this.currentType = type;
        const el = this.icon.element as HTMLDivElement;
        el.className = `snap-icon snap-icon--${type}`;
    }

    private queueTooltip(label: string, screenPoint: { x: number; y: number }, typeChanged: boolean): void {
        if (!this.tooltip) return;

        const moved =
            !this.lastScreen ||
            this.screenDistance(this.lastScreen, screenPoint) > this.hoverMoveThresholdPx ||
            typeChanged;

        this.lastScreen = { ...screenPoint };
        this.pendingLabel = label;
        this.pendingScreen = { ...screenPoint };

        if (moved) {
            this.hideTooltip();
            this.clearHoverTimer();
            this.hoverTimer = window.setTimeout(() => {
                this.showTooltip();
            }, this.hoverDelayMs);
        } else if (this.tooltipVisible) {
            this.updateTooltipPosition(screenPoint);
        }
    }

    private showTooltip(): void {
        if (!this.tooltip) return;
        this.tooltip.textContent = this.pendingLabel;
        this.updateTooltipPosition(this.pendingScreen);
        this.tooltip.style.display = 'block';
        this.tooltipVisible = true;
    }

    private updateTooltipPosition(screenPoint: { x: number; y: number }): void {
        if (!this.tooltip) return;
        this.tooltip.style.transform = `translate(${screenPoint.x + 12}px, ${screenPoint.y + 12}px)`;
    }

    private hideTooltip(): void {
        if (!this.tooltip) return;
        this.tooltip.style.display = 'none';
        this.tooltipVisible = false;
        this.clearHoverTimer();
    }

    private clearHoverTimer(): void {
        if (this.hoverTimer !== null) {
            window.clearTimeout(this.hoverTimer);
            this.hoverTimer = null;
        }
    }

    private screenDistance(a: { x: number; y: number }, b: { x: number; y: number }): number {
        const dx = a.x - b.x;
        const dy = a.y - b.y;
        return Math.sqrt(dx * dx + dy * dy);
    }
}
