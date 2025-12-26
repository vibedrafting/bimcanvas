import * as THREE from 'three';
import { SelectionManager } from './SelectionManager';
import { useCanvasStore } from '../../stores/canvasStore';
import { ShortcutManager } from './ShortcutManager';
import type { Tool } from './tools/Tool';
import { MoveTool } from './tools/MoveTool';
import { GhostManager } from './GhostManager';
import { useDebugStore } from '../../stores/debugStore';
import { RotateTool } from './tools/RotateTool';
import { MirrorTool } from './tools/MirrorTool';
import { CopyTool } from './tools/CopyTool';
import { LayerManager } from '../three/LayerManager';

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

    // Box Selection State
    private isMouseDown = false;
    private isBoxSelecting = false;
    private mouseDownStart = new THREE.Vector2(); // Screen coords (pixels)
    private selectionBoxElement: HTMLDivElement;

    // Bound Event Handlers
    private boundOnMouseMove: (event: MouseEvent) => void;
    private boundOnMouseDown: (event: MouseEvent) => void;
    private boundOnMouseUp: (event: MouseEvent) => void;
    private boundOnClick: (event: MouseEvent) => void;
    private boundOnKeyDown: (event: KeyboardEvent) => void;
    private boundOnKeyUp: (event: KeyboardEvent) => void;
    private boundToolCancelled: () => void;
    private boundToolCompleted: () => void;

    constructor(camera: THREE.Camera, domElement: HTMLElement, scene: THREE.Scene, selectionManager: SelectionManager) {
        this.domElement = domElement;
        this.camera = camera;
        this.scene = scene;
        this.raycaster = new THREE.Raycaster();
        // 启用所有层以支持 LAYER_ZONES 中的禁区和分区点击
        this.raycaster.layers.enableAll();
        this.mouse = new THREE.Vector2();
        this.selectionManager = selectionManager;
        this.store = useCanvasStore();
        this.shortcutManager = new ShortcutManager();
        this.ghostManager = GhostManager.getInstance(scene);

        // Create Selection Box Element
        this.selectionBoxElement = document.createElement('div');
        this.selectionBoxElement.style.position = 'fixed';
        this.selectionBoxElement.style.pointerEvents = 'none';
        this.selectionBoxElement.style.display = 'none';
        this.selectionBoxElement.style.zIndex = '9999';
        document.body.appendChild(this.selectionBoxElement);

        // Bind handlers
        this.boundOnMouseMove = this.onMouseMove.bind(this);
        this.boundOnMouseDown = this.onMouseDown.bind(this);
        this.boundOnMouseUp = this.onMouseUp.bind(this);
        this.boundOnClick = this.onClick.bind(this);
        this.boundOnKeyDown = this.onKeyDown.bind(this);
        this.boundOnKeyUp = this.onKeyUp.bind(this);
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
        // State is now managed by the Tool itself after validation
    }

    private preventNextClick = false;

    public cancelTool() {
        if (this.activeTool) {
            this.activeTool.deactivate();
            this.activeTool = null;
            this.store.currentOperation = null;

            // Prevent immediate re-selection if tool finished via mouse click
            this.preventNextClick = true;
            setTimeout(() => {
                this.preventNextClick = false;
            }, 100);
        }
    }

    private setupShortcuts() {
        this.shortcutManager.register('R', () => this.rotateSelection());
        this.shortcutManager.register('M', () => this.activateMoveTool());
        this.shortcutManager.register('C', () => this.activateCopyTool());
        this.shortcutManager.register('Delete', () => this.deleteSelection());
        this.shortcutManager.register('Backspace', () => this.deleteSelection());

        const NUDGE_AMOUNT = 100;
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

        const updated = this.store.projectData?.activeScheme?.modules?.find(m => m.id === selected.id);
        if (updated) {
            this.store.setSelectedObject(updated);
        }
    }

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
        // State is now managed by the Tool itself after validation
    }

    public activateMirrorTool() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Mirror Triggered');
        if (this.activeTool) this.activeTool.deactivate();

        this.activeTool = new MirrorTool(
            this.scene,
            this.camera,
            this.domElement,
            this.ghostManager
        );
        this.activeTool.activate();
    }

    public activateCopyTool() {
        const debugStore = useDebugStore();
        debugStore.log('Command: Copy Triggered');
        if (this.activeTool) this.activeTool.deactivate();

        this.activeTool = new CopyTool(
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
        const selectedIds = this.store.selectedIds;
        if (selectedIds.length === 0) return;

        // 1. Filter for valid modules (Furniture)
        // We need to check the type of each selected object.
        // The store.selectedObjects array contains the data objects.
        const modulesToDelete = this.store.selectedObjects.filter((obj: any) => obj.type === 'module');

        if (modulesToDelete.length === 0) {
            debugStore.warn('Delete ignored: No deletable modules selected');
            // Optional: Show feedback that nothing can be deleted?
            // For now, just do nothing and definitely DO NOT show "Deleted"
            return;
        }

        // 2. Delete valid modules
        modulesToDelete.forEach((obj: any) => {
            if (obj.id) {
                this.store.removeModule(obj.id);
            }
        });

        this.store.clearSelection();

        // 3. Only show feedback if something was actually deleted
        this.store.currentOperation = 'deleted';
        setTimeout(() => {
            if (this.store.currentOperation === 'deleted') {
                this.store.currentOperation = null;
            }
        }, 2000);
    }

    private setupEvents() {
        this.domElement.addEventListener('mousemove', this.boundOnMouseMove);
        this.domElement.addEventListener('mousedown', this.boundOnMouseDown);
        this.domElement.addEventListener('mouseup', this.boundOnMouseUp);
        this.domElement.addEventListener('click', this.boundOnClick);
        window.addEventListener('keydown', this.boundOnKeyDown);
        window.addEventListener('keyup', this.boundOnKeyUp);
    }

    private onKeyDown(event: KeyboardEvent) {
        if (this.activeTool) {
            this.activeTool.onKeyDown(event);
            return;
        }

        // Base Arrow Path (Revit-like)
        const arrowPath = '<path d="M0 0L0 16L4.5 12L8.5 12L0 0Z" fill="black" stroke="white" stroke-width="1"/>';

        // Cursor hints
        if (event.key === 'Control') {
            // Custom Add Cursor (+)
            const plusCursor = `url('data:image/svg+xml;utf8,<svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg">${arrowPath}<path d="M12 4v8M8 8h8" stroke="black" stroke-width="2" fill="none"/><path d="M12 4v8M8 8h8" stroke="white" stroke-width="0.5" fill="none"/></svg>') 0 0, auto`;
            this.domElement.style.cursor = plusCursor;
        } else if (event.key === 'Shift') {
            // Custom Remove Cursor (-)
            const minusCursor = `url('data:image/svg+xml;utf8,<svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg">${arrowPath}<path d="M8 8h8" stroke="black" stroke-width="2" fill="none"/><path d="M8 8h8" stroke="white" stroke-width="0.5" fill="none"/></svg>') 0 0, auto`;
            this.domElement.style.cursor = minusCursor;
        }
    }

    private onKeyUp(event: KeyboardEvent) {
        if (event.key === 'Control' || event.key === 'Shift') {
            this.domElement.style.cursor = 'default';
        }
    }

    private onMouseDown(event: MouseEvent) {
        if (this.activeTool) {
            // 如果工具处于选择阶段，不拦截事件，让选择逻辑继续
            if (this.activeTool.isInSelectionPhase?.()) {
                // 不调用工具的 onMouseDown，继续执行选择逻辑
            } else {
                this.activeTool.onMouseDown(event);
                return;
            }
        }
        if (event.button !== 0) return; // Only Left Click

        this.isMouseDown = true;
        this.mouseDownStart.set(event.clientX, event.clientY);
        this.isBoxSelecting = false;
    }

    private onMouseMove(event: MouseEvent) {
        if (this.activeTool) {
            // 如果工具处于选择阶段，不拦截事件
            if (this.activeTool.isInSelectionPhase?.()) {
                // 不调用工具的 onMouseMove，继续执行框选逻辑
            } else {
                this.activeTool.onMouseMove(event);
                return;
            }
        }

        const rect = this.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

        // Handle Box Selection Drag
        if (this.isMouseDown) {
            const deltaX = event.clientX - this.mouseDownStart.x;
            const deltaY = event.clientY - this.mouseDownStart.y;
            const distance = Math.sqrt(deltaX * deltaX + deltaY * deltaY);

            if (distance > 5) { // Threshold to start box selection
                this.isBoxSelecting = true;
                this.updateSelectionBoxVisual(event.clientX, event.clientY);
            }
        }
    }

    private onMouseUp(event: MouseEvent) {
        if (this.activeTool) {
            // 如果工具处于选择阶段，不拦截事件
            if (this.activeTool.isInSelectionPhase?.()) {
                // 不调用工具的 onMouseUp，继续执行框选逻辑
            } else {
                this.activeTool.onMouseUp(event);
                return;
            }
        }

        if (this.isBoxSelecting) {
            this.performBoxSelection(event.clientX, event.clientY, event.ctrlKey || event.metaKey, event.shiftKey);
            this.selectionBoxElement.style.display = 'none';
        }

        this.isMouseDown = false;
    }

    private updateSelectionBoxVisual(currentX: number, currentY: number) {
        const startX = this.mouseDownStart.x;
        const startY = this.mouseDownStart.y;

        const minX = Math.min(startX, currentX);
        const maxX = Math.max(startX, currentX);
        const minY = Math.min(startY, currentY);
        const maxY = Math.max(startY, currentY);

        const width = maxX - minX;
        const height = maxY - minY;

        this.selectionBoxElement.style.display = 'block';
        this.selectionBoxElement.style.left = `${minX}px`;
        this.selectionBoxElement.style.top = `${minY}px`;
        this.selectionBoxElement.style.width = `${width}px`;
        this.selectionBoxElement.style.height = `${height}px`;

        // CAD Style:
        // Left-to-Right (startX < currentX): Blue (Inclusive)
        // Right-to-Left (startX > currentX): Green (Crossing)
        if (currentX > startX) {
            // Blue - Inclusive
            this.selectionBoxElement.style.backgroundColor = 'rgba(0, 0, 255, 0.1)';
            this.selectionBoxElement.style.border = '1px solid rgba(0, 0, 255, 0.5)';
        } else {
            // Green - Crossing
            this.selectionBoxElement.style.backgroundColor = 'rgba(0, 255, 0, 0.1)';
            this.selectionBoxElement.style.border = '1px solid rgba(0, 255, 0, 0.5)';
        }
    }

    private performBoxSelection(endX: number, endY: number, isCtrl: boolean, isShift: boolean) {
        const startX = this.mouseDownStart.x;
        const startY = this.mouseDownStart.y;

        // Determine mode
        const isCrossing = endX < startX; // Right-to-Left

        // Calculate Box in Screen Space
        const minX = Math.min(startX, endX);
        const maxX = Math.max(startX, endX);
        const minY = Math.min(startY, endY);
        const maxY = Math.max(startY, endY);

        // Find all selectable objects
        const candidates: THREE.Mesh[] = [];
        this.scene.traverse((child) => {
            if (child instanceof THREE.Mesh && child.userData && child.userData.id) {
                // Ignore ghosts and helpers
                if (!child.userData.isGhost && child.visible) {
                    candidates.push(child);
                }
            }
        });

        // 使用 Set 去重，因为每个模块有两个 Mesh（普通渲染 + AI Vision）共享相同 ID
        const selectedIdSet = new Set<string>();

        for (const mesh of candidates) {
            if (this.isObjectInBox(mesh, minX, maxX, minY, maxY, isCrossing)) {
                selectedIdSet.add(mesh.userData.id);
            }
        }

        const selectedIds = Array.from(selectedIdSet);

        // Update Store
        if (isShift) {
            // Remove from selection
            selectedIds.forEach(id => {
                const obj = this.findObjectById(id); // Helper needed
                if (obj) this.store.removeFromSelection(obj);
            });
        } else if (isCtrl) {
            // Add to selection
            selectedIds.forEach(id => {
                const obj = this.findObjectById(id);
                if (obj) this.store.addToSelection(obj);
            });
        } else {
            // Replace selection
            this.store.setSelection(selectedIds);
        }
    }

    private isObjectInBox(mesh: THREE.Mesh, minX: number, maxX: number, minY: number, maxY: number, isCrossing: boolean): boolean {
        // Project object bounding box to screen
        if (!mesh.geometry.boundingBox) mesh.geometry.computeBoundingBox();
        const bbox = mesh.geometry.boundingBox!.clone();
        bbox.applyMatrix4(mesh.matrixWorld);

        const corners = [
            new THREE.Vector3(bbox.min.x, bbox.min.y, bbox.min.z),
            new THREE.Vector3(bbox.min.x, bbox.min.y, bbox.max.z),
            new THREE.Vector3(bbox.min.x, bbox.max.y, bbox.min.z),
            new THREE.Vector3(bbox.min.x, bbox.max.y, bbox.max.z),
            new THREE.Vector3(bbox.max.x, bbox.min.y, bbox.min.z),
            new THREE.Vector3(bbox.max.x, bbox.min.y, bbox.max.z),
            new THREE.Vector3(bbox.max.x, bbox.max.y, bbox.min.z),
            new THREE.Vector3(bbox.max.x, bbox.max.y, bbox.max.z),
        ];

        let allInside = true;
        let anyInside = false;

        for (const corner of corners) {
            const screenPos = corner.project(this.camera);
            // screenPos is in NDC [-1, 1]
            // Convert to pixels
            const x = (screenPos.x * .5 + .5) * this.domElement.clientWidth;
            const y = (-(screenPos.y * .5) + .5) * this.domElement.clientHeight; // Y is inverted in CSS

            const inside = x >= minX && x <= maxX && y >= minY && y <= maxY;
            if (inside) anyInside = true;
            else allInside = false;
        }

        if (isCrossing) {
            return anyInside; // Any point inside (Simplified crossing check)
            // Note: True crossing check requires checking edge intersections, but point check is usually "good enough" for simple boxes.
            // For better crossing, we should check if the screen-projected bbox intersects the selection rect.
        } else {
            return allInside; // All points inside
        }
    }

    // Helper to find object data for Store (since store expects data object, not just ID, for some methods)
    // Actually store.setSelection takes IDs. addToSelection takes object.
    // We can construct a minimal object with ID.
    private findObjectById(id: string): any {
        // We just need { id: string } for store methods usually, 
        // but store.addToSelection might need more?
        // store.addToSelection(obj) -> selectedIds.push(obj.id || obj)
        // So { id } is enough.
        return { id };
    }

    private onClick(event: MouseEvent) {
        if (this.preventNextClick) {
            this.preventNextClick = false;
            return;
        }

        if (this.activeTool) {
            // 如果工具处于选择阶段，不拦截点击事件，继续执行选择逻辑
            if (this.activeTool.isInSelectionPhase?.()) {
                // 不 return，继续执行下方的选择逻辑
            } else {
                // 工具不在选择阶段，onMouseDown 已经处理过了，这里直接 return
                // 不要再调用 onMouseDown，否则会导致状态机跳两步
                return;
            }
        }

        // If we were box selecting, consume the click
        if (this.isBoxSelecting) {
            this.isBoxSelecting = false;
            return;
        }

        // Only handle left click
        if (event.button !== 0) return;

        // ... Existing Click Logic ...
        const rect = this.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;

        this.raycaster.setFromCamera(this.mouse, this.camera);
        const intersects = this.raycaster.intersectObjects(this.scene.children, true);

        const debugStore = useDebugStore();
        debugStore.log(`Click: ${this.mouse.x.toFixed(2)},${this.mouse.y.toFixed(2)} Hits: ${intersects.length}`);

        const isCtrlMode = event.ctrlKey || event.metaKey;
        const isShiftMode = event.shiftKey;
        const isModifierMode = isCtrlMode || isShiftMode;

        if (intersects.length > 0) {
            // 查找有效的点击目标（跳过图层被禁用的对象）
            let target: THREE.Object3D | null = null;
            for (const hit of intersects) {
                if (!(hit.object instanceof THREE.Mesh)) continue;

                let candidate: THREE.Object3D | null = hit.object;
                while (candidate && !candidate.userData?.id && candidate.parent && candidate.parent !== this.scene) {
                    candidate = candidate.parent;
                }

                if (candidate && candidate.userData?.id) {
                    const objType = candidate.userData.type;
                    // Zone 和 Exclusion 只有在 LAYER_ZONES 启用时才能被选中
                    if (objType === 'zone' || objType === 'exclusion') {
                        if (!this.camera.layers.isEnabled(LayerManager.LAYER_ZONES)) {
                            debugStore.log(`Skip ${objType}: LAYER_ZONES disabled`);
                            continue; // 图层未启用，跳过此对象
                        }
                    }
                    target = candidate;
                    break; // 找到有效目标
                }
            }

            if (target && target.userData?.id) {
                debugStore.success(`Hit: ${target.userData.type} ${target.userData.id}`);

                if (isShiftMode) {
                    this.store.removeFromSelection(target.userData);
                } else if (isCtrlMode) {
                    this.store.toggleSelection(target.userData);
                } else {
                    this.store.setSelectedObject(target.userData);
                }
            } else {
                debugStore.warn('No valid target found');
                if (!isModifierMode) {
                    this.store.clearSelection();
                }
            }
        } else {
            debugStore.warn('No Intersects');
            if (!isModifierMode) {
                this.store.clearSelection();
            }
        }
    }

    public update() {
        // Hover effects can go here
    }

    public dispose() {
        this.domElement.removeEventListener('mousemove', this.boundOnMouseMove);
        this.domElement.removeEventListener('mousedown', this.boundOnMouseDown);
        this.domElement.removeEventListener('mouseup', this.boundOnMouseUp);
        this.domElement.removeEventListener('click', this.boundOnClick);
        window.removeEventListener('keydown', this.boundOnKeyDown);
        window.removeEventListener('keyup', this.boundOnKeyUp);
        window.removeEventListener('bimcanvas:tool-cancelled', this.boundToolCancelled);
        window.removeEventListener('bimcanvas:tool-completed', this.boundToolCompleted);
        this.shortcutManager.dispose();

        if (this.selectionBoxElement && this.selectionBoxElement.parentNode) {
            this.selectionBoxElement.parentNode.removeChild(this.selectionBoxElement);
        }
    }
}

