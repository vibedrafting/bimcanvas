import type { CanvasDocument } from '../../types/canvas';

export class TimelineManager {
    private history: string[] = [];
    private currentIndex: number = -1;
    private maxHistory: number = 50;

    constructor() { }

    public push(state: CanvasDocument) {
        // If we are not at the end of history, discard future states
        if (this.currentIndex < this.history.length - 1) {
            this.history = this.history.slice(0, this.currentIndex + 1);
        }

        // Add new state
        this.history.push(JSON.stringify(state));
        this.currentIndex++;

        // Limit history size
        if (this.history.length > this.maxHistory) {
            this.history.shift();
            this.currentIndex--;
        }
    }

    public undo(): CanvasDocument | null {
        if (this.canUndo) {
            this.currentIndex--;
            const state = this.history[this.currentIndex];
            return state ? JSON.parse(state) : null;
        }
        return null;
    }

    public redo(): CanvasDocument | null {
        if (this.canRedo) {
            this.currentIndex++;
            const state = this.history[this.currentIndex];
            return state ? JSON.parse(state) : null;
        }
        return null;
    }

    public get canUndo(): boolean {
        return this.currentIndex > 0;
    }

    public get canRedo(): boolean {
        return this.currentIndex < this.history.length - 1;
    }

    public clear() {
        this.history = [];
        this.currentIndex = -1;
    }
}
