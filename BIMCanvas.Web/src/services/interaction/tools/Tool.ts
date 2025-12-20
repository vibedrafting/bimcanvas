export interface Tool {
    name: string;
    activate(): void;
    deactivate(): void;
    onMouseDown(event: MouseEvent): void;
    onMouseMove(event: MouseEvent): void;
    onMouseUp(event: MouseEvent): void;
    onKeyDown(event: KeyboardEvent): void;
    // 可选：工具是否处于选择阶段（允许 InteractionService 继续处理选择事件）
    isInSelectionPhase?(): boolean;
}
