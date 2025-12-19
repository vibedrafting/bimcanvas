export type ShortcutHandler = () => void;

export class ShortcutManager {
    private shortcuts: Map<string, ShortcutHandler>;
    private enabled: boolean = true;
    private boundOnKeyDown: (e: KeyboardEvent) => void;

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

        // Ignore if typing in an input
        if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
            return;
        }

        const key = this.getKeyString(event);
        if (this.shortcuts.has(key)) {
            event.preventDefault();
            this.shortcuts.get(key)!();
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
        if (key.length === 1) key = key.toUpperCase();

        parts.push(key);
        return parts.join('+');
    }

    public register(keyCombo: string, handler: ShortcutHandler) {
        this.shortcuts.set(keyCombo, handler);
    }

    public unregister(keyCombo: string) {
        this.shortcuts.delete(keyCombo);
    }

    public setEnabled(enabled: boolean) {
        this.enabled = enabled;
    }

    public dispose() {
        window.removeEventListener('keydown', this.boundOnKeyDown);
        this.shortcuts.clear();
    }
}
