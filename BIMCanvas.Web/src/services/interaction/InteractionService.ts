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

        this.setupEvents();
        this.setupShortcuts();
        this.setupToolEvents();
    }

    private setupToolEvents() {
        window.addEventListener('bimcanvas:tool-cancelled', () => this.cancelTool());
        window.addEventListener('bimcanvas:tool-completed', () => this.cancelTool());
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
    }

    public cancelTool() {
        if (this.activeTool) {
            this.activeTool.deactivate();
            this.activeTool = null;
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
    }

    public deleteSelection() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Delete Triggered');
        const selected = this.store.selectedObject;
        if (!selected || !selected.id) return;

        this.store.removeModule(selected.id);
    }

    private setupEvents() {
        this.domElement.addEventListener('mousemove', this.onMouseMove.bind(this));
        this.domElement.addEventListener('click', this.onClick.bind(this));
        window.addEventListener('keydown', this.onKeyDown.bind(this));
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
            // Filter for selectable objects (e.g., modules)
            // For now, select the first hit that is a Mesh
            const hit = intersects.find(i => i.object instanceof THREE.Mesh);
            if (hit) {
                debugStore.success(`Hit: ${hit.object.id} (${hit.object.type})`);
                // Traverse up to find the root object (e.g. module group) if needed
                // For now, just select the object
                this.selectionManager.select(hit.object);
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
        this.domElement.removeEventListener('mousemove', this.onMouseMove.bind(this));
        this.domElement.removeEventListener('click', this.onClick.bind(this));
        window.removeEventListener('keydown', this.onKeyDown.bind(this));
        this.shortcutManager.dispose();
    }
}
