import { NumericInputManager } from './NumericInputManager';

export type ShortcutHandler = () => void;

export class ShortcutManager {
    private shortcuts: Map<string, ShortcutHandler>;
    private enabled: boolean = true;
    private boundOnKeyDown: (e: KeyboardEvent) => void;

    // Sequence support
    private keyBuffer: string = '';
    private sequenceTimeout: number | null = null;
    private readonly SEQUENCE_DELAY = 500; // ms to wait for next key

    constructor() {
        this.shortcuts = new Map();
        this.boundOnKeyDown = this.onKeyDown.bind(this);
        this.setupListeners();
    }

    private setupListeners() {
        window.addEventListener('keydown', this.boundOnKeyDown);
    }

    private onKeyDown(event: KeyboardEvent) {
        if (!this.enabled) return;

        // 数值输入激活时，不处理快捷键
        if (NumericInputManager.getInstance().isActive.value) {
            return;
        }

        // Ignore if typing in an input
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
            return;
        }

        // 1. Check for standard Combo Shortcuts (Ctrl+Z, Delete, etc.)
        const comboKey = this.getKeyString(event);
        if (this.shortcuts.has(comboKey)) {
            event.preventDefault();
            this.shortcuts.get(comboKey)!();
            this.clearSequence(); // Reset sequence on valid combo
            return;
        }

        // 2. Handle Key Sequences (d->i, m->o, etc.)
        // Only trigger sequence logic for single character keys without modifiers
        if (!event.ctrlKey && !event.altKey && !event.metaKey && event.key.length === 1) {
            this.updateSequence(event.key);
        } else {
            // If modifier used or special key, break sequence
            this.clearSequence();
        }
    }

    private updateSequence(key: string) {
        // Append key to buffer (case insensitive)
        this.keyBuffer += key.toLowerCase();

        // Clear previous timeout
        if (this.sequenceTimeout) {
            clearTimeout(this.sequenceTimeout);
        }

        // Check for matches
        // We check from longest possible match to shortest? 
        // Or just exact match?
        // The registry stores keys like "DI".

        // Let's iterate all registered shortcuts to see if any MATCH the buffer
        // Or if the buffer is a PREFIX of any shortcut.

        let matchFound = false;
        let prefixFound = false;

        for (const [registeredKey, handler] of this.shortcuts.entries()) {
            const lowerKey = registeredKey.toLowerCase();

            if (lowerKey === this.keyBuffer) {
                // Exact match! Execute
                handler();
                this.clearSequence();
                matchFound = true;
                break;
            }

            if (lowerKey.startsWith(this.keyBuffer)) {
                prefixFound = true;
            }
        }

        if (!matchFound) {
            if (prefixFound) {
                // Wait for more keys
                this.sequenceTimeout = window.setTimeout(() => {
                    this.clearSequence();
                }, this.SEQUENCE_DELAY);
            } else {
                // Invalid sequence, maybe the LAST key started a new one?
                // Example: typed "X", buffer "X". No "X..." commands.
                // Reset.
                // But wait, what if user typed "M" (Move) but we have "MI" (Mirror)?
                // If "M" is bound, it would have executed in Combo check? 
                // No, "M" is a combo "M".

                // Refinment:
                // If we defined "M" as a shortcut, getKeyString returns "M".
                // If we defined "DI" as a shortcut.

                // Issue: "M" is triggered immediately by `standard Combo Shortcuts`.
                // So "MI" would be impossible if "M" executes immediately.
                // CAD/Revit logic: "M" -> Wait -> If no more, exec "M". If "I", exec "MI".

                // Current implementation priority:
                // 1. Combo map check.
                // 2. Sequence check.

                // If I register "M", `shortcuts.has("M")` is true. It executes immediately.
                // If I want "MI", I can't having "M" execute immediately.

                // The user only asked for "DI". "Move" is currently "M".
                // Be careful not to break "M".

                // For "DI":
                // 1. "D" pressed. `getKeyString` -> "D". `shortcuts` has "D"? No (Delete is "Delete").
                // 2. `updateSequence("d")`. Buffer="d".
                // 3. "I" pressed. `getKeyString` -> "I". `shortcuts` has "I"? No.
                // 4. `updateSequence("i")`. Buffer="di". Match "di". Execute.

                // This seems safe for "DI" as long as "D" is not mapped.
                // "D" is not mapped in `InteractionService.ts`?
                // "Delete" is mapped. "M", "R", "C" are mapped.

                // If the user maps "D", "DI" needs care. But currently safe.

                this.clearSequence();

                // If the just-typed key itself starts a valid new sequence (e.g. user typed "Z" "D" -> buffer "zd" (fail) -> retry "d")
                // For now, simple clear is robust enough.
            }
        }
    }

    private clearSequence() {
        this.keyBuffer = '';
        if (this.sequenceTimeout) {
            clearTimeout(this.sequenceTimeout);
            this.sequenceTimeout = null;
        }
    }

    private getKeyString(event: KeyboardEvent): string {
        const parts: string[] = [];
        if (event.ctrlKey || event.metaKey) parts.push('Cmd'); // Normalize Ctrl/Cmd
        if (event.shiftKey) parts.push('Shift');
        if (event.altKey) parts.push('Alt');

        // Handle special keys or regular keys
        let key = event.key;
        if (key === ' ') key = 'Space';

        // Single char keys -> Uppercase for storage consistency (e.g., "M", "DI")
        // But for sequences, "DI" is stored.
        // Combo "Shift+M"

        if (key.length === 1) key = key.toUpperCase();

        parts.push(key);
        return parts.join('+');
    }

    public register(keyCombo: string, handler: ShortcutHandler) {
        // keyCombo can be "Ctrl+Z" or "DI"
        this.shortcuts.set(keyCombo, handler);
    }

    public unregister(keyCombo: string) {
        this.shortcuts.delete(keyCombo);
    }

    public setEnabled(enabled: boolean) {
        this.enabled = enabled;
        if (!enabled) this.clearSequence();
    }

    public dispose() {
        window.removeEventListener('keydown', this.boundOnKeyDown);
        this.shortcuts.clear();
        this.clearSequence();
    }
}
