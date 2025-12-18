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
