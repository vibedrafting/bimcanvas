# Interaction & Ghost System Renovation Plan

> **Goal**: Achieve a robust, "silky smooth" interaction experience for Moving, Rotating, and Deleting furniture modules, with perfect visual feedback (Ghost System).

## 1. Current Status & Issues
- **Selection**: ✅ Fixed (Raycaster hits correctly).
- **Commands**: ❓ Buttons exist in UI, but end-to-end triggering needs verification.
- **Ghost System**: ⚠️ Known issues:
    - Ghost might not appear or follow mouse correctly.
    - Original object might move *during* drag (should be static until drop).
    - Ghost might persist after operation.

## 2. Renovation Phases

### Phase 1: Command Integration (The "Dynamic Island")
**Objective**: Ensure UI buttons and Shortcuts correctly invoke `InteractionService`.

- [ ] **Verify UI Buttons**: Click "Move" / "Rotate" / "Delete" on the Dynamic Island.
- [ ] **Verify Shortcuts**: Press `M`, `R`, `Delete`/`Backspace`.
- [ ] **Debug Tracing**: Ensure `InteractionService` receives the commands.

### Phase 2: Move Tool & Ghost System (The Core)
**Objective**: "Solid" drag experience. The user drags a *ghost*, drops it, and *then* the object updates.

- [ ] **Ghost Logic Fix**:
    - [ ] On Drag Start: Create Ghost (Visual clone), Original stays visible but static.
    - [ ] On Drag: Ghost follows mouse (snapped to grid if enabled).
    - [ ] On Drop: Update Original's position to match Ghost, Destroy Ghost.
    - [ ] On Cancel (Esc): Destroy Ghost, Original stays put.
- [ ] **Visuals**: Ensure Ghost has distinct transparency/color (e.g., 50% opacity blue).

### Phase 3: Rotate Tool
**Objective**: Reliable 90-degree rotation.

- [ ] **Logic**: Rotate 90° clockwise around center.
- [ ] **Visuals**: Immediate update or Ghost preview? (Stick to immediate for now, or Ghost if complex).
- [ ] **Data**: Ensure `facing` property updates correctly in JSON.

### Phase 4: Delete Tool
**Objective**: Safe deletion.

- [ ] **Logic**: Remove from `canvasStore`.
- [ ] **Selection**: Clear selection after delete.
- [ ] **Undo/Redo**: Verify deletion is undoable.

## 3. Verification Checklist (Acceptance Criteria)
1.  **Select** a module -> Island shows commands.
2.  **Click Move** (or drag) -> Ghost appears under mouse.
3.  **Move Mouse** -> Ghost follows.
4.  **Click** -> Ghost disappears, Module jumps to new spot.
5.  **Undo** -> Module jumps back.

## 4. Revit-like Interaction Specs (New Requirement)

### 4.1 Move Tool (Revit Style)
**Workflow**:
1.  **Select**: User selects object (if not already selected).
2.  **Activate**: Press `M` or click Move button.
3.  **Base Point**: Click anywhere to define start point ($P_{base}$).
    *   *Visual*: Cursor changes, snapping active.
4.  **Destination**: Move mouse to define vector ($v = P_{current} - P_{base}$).
    *   *Visual*: Ghost object moves with mouse. Rubber band line from $P_{base}$ to $P_{current}$.
    *   *Constraint*: Shift key for Ortho mode (optional).
5.  **Confirm**: Click to define destination ($P_{dest}$).
    *   *Action*: Object moves by $v = P_{dest} - P_{base}$. Tool finishes.
6.  **Cancel**: Press `ESC` at any time to abort. Object returns to original.

**Fixes Needed**:
- Ensure Ghost is visible and follows mouse relative to Base Point.
- Ensure Original Object remains visible (as "Ghost at original position").
- Implement `ESC` handling in `MoveTool`.

### 4.2 Rotate Tool (Revit Style)
**Workflow**:
1.  **Select**: User selects object.
2.  **Activate**: Press `R` or click Rotate button.
3.  **Center of Rotation**:
    *   *Default*: Center of object bounding box.
    *   *Action*: User can click to pick a new center ($P_{center}$).
    *   *Visual*: A "rotation center" icon (blue dot/circle) appears.
4.  **Start Ray**: Click to define start angle reference ($P_{start}$).
    *   *Visual*: Dashed line from $P_{center}$ to cursor.
5.  **End Ray**: Move mouse to define rotation angle ($\theta$).
    *   *Visual*: Dashed line from $P_{center}$ to cursor. Ghost object rotates dynamically.
6.  **Confirm**: Click to define end angle ($P_{end}$).
    *   *Action*: Object rotates by $\Delta\theta = \text{angle}(P_{end}) - \text{angle}(P_{start})$.

**Implementation**:
- Create `RotateTool.ts` implementing `Tool` interface.
- States: `waiting_center` -> `waiting_start` -> `waiting_end`.
- Visuals: `RotationGizmo` (lines + arc).
