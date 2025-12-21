import { ref, type Ref } from 'vue';

/**
 * 数值输入配置接口
 */
export interface NumericInputConfig {
    unit: 'mm' | 'deg';
    placeholder: string;
    onConfirm: (value: number) => void;
    onCancel: () => void;
}

/**
 * 键盘数值输入管理器（单例）
 *
 * 在移动/旋转操作过程中，用户可以随时输入精确数值。
 * 当检测到数字键输入时，浮动输入框会出现在鼠标位置附近。
 */
export class NumericInputManager {
    private static instance: NumericInputManager | null = null;

    // 响应式状态（供 Vue 组件使用）
    public isActive: Ref<boolean> = ref(false);
    public inputValue: Ref<string> = ref('');
    public config: Ref<NumericInputConfig | null> = ref(null);
    public position: Ref<{ x: number; y: number }> = ref({ x: 0, y: 0 });

    private constructor() {
        // 私有构造函数，确保单例
    }

    /**
     * 获取单例实例
     */
    public static getInstance(): NumericInputManager {
        if (!this.instance) {
            this.instance = new NumericInputManager();
        }
        return this.instance;
    }

    /**
     * 开始接收数值输入
     * @param config 输入配置（单位、回调等）
     * @param cursorPos 鼠标屏幕位置
     */
    public startInput(
        config: NumericInputConfig,
        cursorPos: { x: number; y: number }
    ): void {
        this.config.value = config;
        this.position.value = cursorPos;
        this.inputValue.value = '';
        this.isActive.value = true;
    }

    /**
     * 处理键盘事件
     * @returns true 表示事件已被处理，调用方应停止传播
     */
    public handleKeyDown(event: KeyboardEvent): boolean {
        if (!this.isActive.value) {
            return false;
        }

        // Enter 确认
        if (event.key === 'Enter') {
            event.preventDefault();
            this.confirm();
            return true;
        }

        // Escape 取消
        if (event.key === 'Escape') {
            event.preventDefault();
            this.cancel();
            return true;
        }

        // Backspace 删除
        if (event.key === 'Backspace') {
            event.preventDefault();
            this.inputValue.value = this.inputValue.value.slice(0, -1);
            return true;
        }

        // 数字和小数点
        if (/^[0-9.]$/.test(event.key)) {
            event.preventDefault();
            // 防止多个小数点
            if (event.key === '.' && this.inputValue.value.includes('.')) {
                return true;
            }
            this.inputValue.value += event.key;
            return true;
        }

        // 负号（仅在开头允许）
        if (event.key === '-' && this.inputValue.value === '') {
            event.preventDefault();
            this.inputValue.value = '-';
            return true;
        }

        // 其他按键也拦截，防止触发快捷键
        event.preventDefault();
        return true;
    }

    /**
     * 确认输入
     */
    public confirm(): void {
        const value = parseFloat(this.inputValue.value);
        if (!isNaN(value) && this.config.value) {
            this.config.value.onConfirm(value);
        }
        this.deactivate();
    }

    /**
     * 取消输入
     */
    public cancel(): void {
        this.config.value?.onCancel();
        this.deactivate();
    }

    /**
     * 停用输入
     */
    private deactivate(): void {
        this.isActive.value = false;
        this.config.value = null;
        this.inputValue.value = '';
    }

    /**
     * 销毁实例（通常不需要）
     */
    public dispose(): void {
        this.deactivate();
    }
}
