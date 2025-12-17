export interface Tool {
    name: string;
    activate(): void;
    deactivate(): void;
    onMouseDown(event: MouseEvent): void;
    onMouseMove(event: MouseEvent): void;
    onMouseUp(event: MouseEvent): void;
    onKeyDown(event: KeyboardEvent): void;
}
