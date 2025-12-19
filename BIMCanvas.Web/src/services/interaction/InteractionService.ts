import * as THREE from 'three';
import { SelectionManager } from './SelectionManager';
import { useCanvasStore } from '../../stores/canvasStore';
import { ShortcutManager } from './ShortcutManager';
import type { Tool } from './tools/Tool';
import { MoveTool } from './tools/MoveTool';
import { GhostManager } from './GhostManager';
import { useDebugStore } from '../../stores/debugStore';
import { RotateTool } from './tools/RotateTool';

export class InteractionService {
    private raycaster: THREE.Raycaster;
    private mouse: THREE.Vector2;
    private camera: THREE.Camera;
    private domElement: HTMLElement;
    private scene: THREE.Scene;
    private selectionManager: SelectionManager;
    private shortcutManager: ShortcutManager;
    private store: ReturnType<typeof useCanvasStore>;
    private activeTool: Tool | null = null;
    private ghostManager: GhostManager;

    // Bound event handlers (用于正确移除监听器)
    private boundOnMouseMove: (e: MouseEvent) => void;
    private boundOnClick: (e: MouseEvent) => void;
    private boundOnKeyDown: (e: KeyboardEvent) => void;
    private boundToolCancelled: () => void;
    private boundToolCompleted: () => void;

    constructor(camera: THREE.Camera, domElement: HTMLElement, scene: THREE.Scene, selectionManager: SelectionManager) {
        this.camera = camera;
        this.domElement = domElement;
        this.scene = scene;
        this.raycaster = new THREE.Raycaster();
        this.mouse = new THREE.Vector2();
        this.selectionManager = selectionManager;
        this.store = useCanvasStore();
        this.shortcutManager = new ShortcutManager();
        this.ghostManager = new GhostManager(scene);

        // 绑定事件处理器
        this.boundOnMouseMove = this.onMouseMove.bind(this);
        this.boundOnClick = this.onClick.bind(this);
        this.boundOnKeyDown = this.onKeyDown.bind(this);
        this.boundToolCancelled = () => this.cancelTool();
        this.boundToolCompleted = () => this.cancelTool();

        this.setupEvents();
        this.setupShortcuts();
        this.setupToolEvents();
    }

    private setupToolEvents() {
        window.addEventListener('bimcanvas:tool-cancelled', this.boundToolCancelled);
        window.addEventListener('bimcanvas:tool-completed', this.boundToolCompleted);
    }

    public activateMoveTool() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Move Triggered');
        if (this.activeTool) this.activeTool.deactivate();

        this.activeTool = new MoveTool(
            this.scene,
            this.camera,
            this.domElement,
            this.ghostManager
        );
        this.activeTool.activate();
        this.store.currentOperation = 'moving';
    }

    public cancelTool() {
        if (this.activeTool) {
            this.activeTool.deactivate();
            this.activeTool = null;
            this.store.currentOperation = null;
        }
    }

    private setupShortcuts() {
        // Rotate: R
        this.shortcutManager.register('R', () => this.rotateSelection());

        // Move Tool: M
        this.shortcutManager.register('M', () => this.activateMoveTool());

        // Delete: Delete or Backspace
        this.shortcutManager.register('Delete', () => this.deleteSelection());
        this.shortcutManager.register('Backspace', () => this.deleteSelection());

        // Nudge: Arrow Keys
        const NUDGE_AMOUNT = 100; // 100mm
        this.shortcutManager.register('ArrowUp', () => this.moveSelection(0, NUDGE_AMOUNT));
        this.shortcutManager.register('ArrowDown', () => this.moveSelection(0, -NUDGE_AMOUNT));
        this.shortcutManager.register('ArrowLeft', () => this.moveSelection(-NUDGE_AMOUNT, 0));
        this.shortcutManager.register('ArrowRight', () => this.moveSelection(NUDGE_AMOUNT, 0));
    }

    public moveSelection(dx: number, dy: number) {
        const selected = this.store.selectedObject;
        if (!selected || !selected.id || !selected.bounds) return;

        const newBounds = selected.bounds.map((p: [number, number]) => [p[0] + dx, p[1] + dy] as [number, number]);

        this.store.updateModule(selected.id, {
            bounds: newBounds
        });

        // Update selection reference
        const updated = this.store.document?.modules.find(m => m.id === selected.id);
        if (updated) {
            this.store.setSelectedObject(updated);
        }
    }



    // ... (inside class)

    public rotateSelection() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Rotate Triggered');
        if (this.activeTool) this.activeTool.deactivate();

        this.activeTool = new RotateTool(
            this.scene,
            this.camera,
            this.domElement,
            this.ghostManager
        );
        this.activeTool.activate();
        this.store.currentOperation = 'rotating';
    }

    public deleteSelection() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Delete Triggered');
        const selected = this.store.selectedObject;
        if (!selected || !selected.id) return;

        this.store.removeModule(selected.id);

        // Transient status for delete (since it's instant)
        this.store.currentOperation = 'deleted';
        setTimeout(() => {
            if (this.store.currentOperation === 'deleted') {
                this.store.currentOperation = null;
            }
        }, 2000);
    }

    private setupEvents() {
        this.domElement.addEventListener('mousemove', this.boundOnMouseMove);
        this.domElement.addEventListener('click', this.boundOnClick);
        window.addEventListener('keydown', this.boundOnKeyDown);
    }

    private onKeyDown(event: KeyboardEvent) {
        if (this.activeTool) {
            this.activeTool.onKeyDown(event);
        }
    }

    private onMouseMove(event: MouseEvent) {
        if (this.activeTool) {
            this.activeTool.onMouseMove(event);
            return;
        }

        const rect = this.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    }

    private onClick(event: MouseEvent) {
        if (this.activeTool) {
            this.activeTool.onMouseDown(event); // Tool handles click/mousedown
            return;
        }

        // Only handle left click
        if (event.button !== 0) return;

        // Update mouse position from click event to ensure accuracy
        const rect = this.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

        this.raycaster.setFromCamera(this.mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        const debugStore = useDebugStore();
        debugStore.log(`Click: ${this.mouse.x.toFixed(2)},${this.mouse.y.toFixed(2)} Hits: ${intersects.length}`);
        debugStore.log(`Ray: O=${this.raycaster.ray.origin.x.toFixed(0)},${this.raycaster.ray.origin.y.toFixed(0)},${this.raycaster.ray.origin.z.toFixed(0)} D=${this.raycaster.ray.direction.x.toFixed(2)},${this.raycaster.ray.direction.y.toFixed(2)},${this.raycaster.ray.direction.z.toFixed(2)}`);

        if (intersects.length > 0) {
            // Filter for selectable objects
            // We need to find the first hit that is part of a selectable object
            // For Doors/Windows, the hit is a child Mesh, but the userData is on the parent Group.

            const hit = intersects.find(i => i.object instanceof THREE.Mesh);

            if (hit) {
                let target: THREE.Object3D | null = hit.object;

                // Traverse up to find object with ID
                while (target && !target.userData?.id && target.parent && target.parent !== this.scene) {
                    target = target.parent;
                }

                // If we found a target with ID, select it. 
                // If not, we might have hit a helper or something else, but let's try to select the mesh itself if it has no ID?
                // Actually, for now, only select if it has an ID.
                if (target && target.userData?.id) {
                    debugStore.success(`Hit: ${target.userData.type} ${target.userData.id}`);
                    this.selectionManager.select(target);
                } else {
                    // Hit something without ID (e.g. grid, helper without ID)
                    // Treat as background click? Or just ignore?
                    // If it's a Mesh but has no ID, it might be a floor or something.
                    // Let's clear selection if we hit something "background-like" or nothing selectable.
                    debugStore.warn('Hit object with no ID');
                    this.selectionManager.clearSelection();
                }
            } else {
                debugStore.warn('No Mesh Hit');
                this.selectionManager.clearSelection();
            }
        } else {
            debugStore.warn('No Intersects');
            this.selectionManager.clearSelection();
        }
    }

    public update() {
        // Hover effects can go here
    }

    public dispose() {
        this.domElement.removeEventListener('mousemove', this.boundOnMouseMove);
        this.domElement.removeEventListener('click', this.boundOnClick);
        window.removeEventListener('keydown', this.boundOnKeyDown);
        window.removeEventListener('bimcanvas:tool-cancelled', this.boundToolCancelled);
        window.removeEventListener('bimcanvas:tool-completed', this.boundToolCompleted);
        this.shortcutManager.dispose();
    }
}
