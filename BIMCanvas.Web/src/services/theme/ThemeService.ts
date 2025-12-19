/**
 * ThemeService - 配色管理核心服务
 * 
 * 提供暗色和亮色两套主题配色，集中管理所有 Builder 和 UI 组件的配色定义。
 * 借鉴 Apple 设计风格：简洁、优雅、高对比度。
 */
import { ref, readonly } from 'vue';

// ============================================================================
// 类型定义
// ============================================================================

/**
 * 配色主题接口
 */
export interface ColorTheme {
    name: 'dark' | 'light';

    /** 场景背景色 */
    background: number;

    /** 3D 场景材质配色 */
    scene: {
        wall: number;
        column: number;
        module: number;
        floor: number;
        doorFrame: number;
        doorPanel: number;
        windowFrame: number;
        glass: number;
        swingArc: number;
        bounds: number;
    };

    /** 网格配色 */
    grid: {
        centerLine: number;
        gridLine: number;
    };

    /** 语义线配色 */
    semantic: {
        line: number;
    };

    /** 区域配色 (Zone & Exclusion) */
    zones: {
        innerBoundary: number;
        exclusion: number;
        opacity: number;
    };

    /** Grid 图层标签配色 (行列头) */
    gridLabel: {
        text: string;
        shadow: string;
        background?: string; // 可选背景色
        padding?: string;    // 可选内边距
        borderRadius?: string; // 可选圆角
    };

    /** 构件标签配色 (墙、柱、模块、门窗) */
    componentLabel: {
        text: string;
        shadow: string;
        background?: string; // 可选背景色
        padding?: string;    // 可选内边距
        borderRadius?: string; // 可选圆角
    };

    /** UI 配色 (CSS 变量) - Apple 风格 */
    css: {
        // 背景色
        bgCanvas: string;
        surfaceGlass: string;
        surfaceGlassHover: string;
        surfaceSolid: string;        // 纯色表面 (非透明)
        surfaceElevated: string;     // 悬浮表面

        // 边框
        borderSubtle: string;
        borderStrong: string;

        // 文字
        textPrimary: string;
        textSecondary: string;
        textTertiary: string;

        // 强调色
        accentBlue: string;
        accentGlow: string;
        accentDanger: string;
        accentDangerGlow: string;
    };
}

// ============================================================================
// 主题定义
// ============================================================================

/**
 * 暗色主题 - 深色背景，亮色 UI (默认)
 */
export const darkTheme: ColorTheme = {
    name: 'dark',
    background: 0x0a0a0f,
    scene: {
        wall: 0x6b7280,
        column: 0x9ca3af,
        module: 0x3b82f6,
        floor: 0x0a0a0f,
        doorFrame: 0x4a5568,
        doorPanel: 0x718096,
        windowFrame: 0x4a5568,
        glass: 0xAADDFF,
        swingArc: 0xffffff,
        bounds: 0xfbbf24,
    },
    grid: {
        centerLine: 0x6b7280,
        gridLine: 0x27272a,
    },
    semantic: {
        line: 0x22c55e,
    },
    zones: {
        innerBoundary: 0x22c55e, // Green
        exclusion: 0xef4444,     // Red
        opacity: 0.2,
    },
    gridLabel: {
        text: '#6b7280',   // 灰色
        shadow: '0 1px 2px rgba(0,0,0,0.5)',
        background: 'transparent',
    },
    componentLabel: {
        text: '#ffffff',   // 纯白 - 与绿色构件区分，高对比度
        // 极细黑色描边 - 增强轮廓感
        shadow: '-1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000',
        background: 'transparent',
    },
    css: {
        // 暗色主题 - 深黑背景
        bgCanvas: '#0a0a0f',
        surfaceGlass: 'rgba(20, 20, 30, 0.75)',
        surfaceGlassHover: 'rgba(40, 40, 55, 0.85)',
        surfaceSolid: '#1c1c1e',
        surfaceElevated: '#2c2c2e',

        borderSubtle: 'rgba(255, 255, 255, 0.1)',
        borderStrong: 'rgba(255, 255, 255, 0.25)',

        textPrimary: 'rgba(255, 255, 255, 0.95)',
        textSecondary: 'rgba(255, 255, 255, 0.65)',
        textTertiary: 'rgba(255, 255, 255, 0.4)',

        accentBlue: '#0a84ff',
        accentGlow: 'rgba(10, 132, 255, 0.25)',
        accentDanger: '#ff453a',
        accentDangerGlow: 'rgba(255, 69, 58, 0.3)',
    },
};

/**
 * 亮色主题 - V2 极致清爽版 (Apple Freeform 风格)
 * 纯白背景，深灰墙体，高对比度，极致通透
 */
export const lightTheme: ColorTheme = {
    name: 'light',
    background: 0xffffff,     // 纯白背景 (极致通透)
    scene: {
        wall: 0x48484a,       // 深灰墙体 (强调结构，类似墨线稿)
        column: 0x3a3a3c,     // 更深的柱子
        module: 0x007aff,     // 标准蓝 (在白色背景上更经典)
        floor: 0xf2f2f7,      // 极浅灰色地板 (微妙暗示)
        doorFrame: 0x2c2c2e,  // 近黑
        doorPanel: 0x3a3a3c,  // 深灰
        windowFrame: 0x2c2c2e,
        glass: 0xa2d2ff,      // 柔和蓝
        swingArc: 0x000000,   // 纯黑弧线
        bounds: 0xff9500,     // Apple 橙色
    },
    grid: {
        centerLine: 0xc7c7cc, // 稍深的中心线
        gridLine: 0xe5e5ea,   // 极淡网格 (几乎不可见)
    },
    semantic: {
        line: 0x34c759,       // Apple 绿色
    },
    zones: {
        innerBoundary: 0x34c759, // Apple Green
        exclusion: 0xff3b30,     // Apple Red
        opacity: 0.15,           // Slightly more transparent in light mode
    },
    gridLabel: {
        text: '#6b7280',   // 灰色 - 保持低调
        shadow: 'none',
        background: 'transparent',
    },
    componentLabel: {
        text: '#000000',   // 纯黑 - 与绿色构件区分，极致对比度
        // 极细锐利描边 (1px hard stroke)
        shadow: '-1px -1px 0 #fff, 1px -1px 0 #fff, -1px 1px 0 #fff, 1px 1px 0 #fff',
        background: 'transparent',
        padding: '0',
        borderRadius: '0',
    },
    css: {
        // 亮色主题 - Apple Freeform 风格
        bgCanvas: '#ffffff',                    // 纯白背景
        surfaceGlass: 'rgba(255, 255, 255, 0.9)', // 高不透明度毛玻璃
        surfaceGlassHover: 'rgba(255, 255, 255, 1)',
        surfaceSolid: '#ffffff',                // 纯白表面
        surfaceElevated: '#ffffff',             // 白色悬浮

        borderSubtle: 'rgba(0, 0, 0, 0.08)',    // 稍微加强边框
        borderStrong: 'rgba(0, 0, 0, 0.15)',    // 明显边框

        textPrimary: 'rgba(0, 0, 0, 0.9)',      // 纯黑文字
        textSecondary: 'rgba(0, 0, 0, 0.6)',    // 次要文字
        textTertiary: 'rgba(0, 0, 0, 0.4)',     // 辅助文字

        accentBlue: '#007aff',                  // 标准蓝
        accentGlow: 'rgba(0, 122, 255, 0.15)',
        accentDanger: '#ff3b30',                // Apple 红
        accentDangerGlow: 'rgba(255, 59, 48, 0.2)',
    },
};

// ============================================================================
// 服务单例
// ============================================================================

class ThemeServiceClass {
    private _currentTheme = ref<ColorTheme>(darkTheme);
    public readonly currentTheme = readonly(this._currentTheme);

    public get isDark(): boolean {
        return this._currentTheme.value.name === 'dark';
    }

    /**
     * 更新 CSS 变量到 :root
     */
    private updateCSSVariables(theme: ColorTheme) {
        const root = document.documentElement;
        const css = theme.css;

        // 背景和表面
        root.style.setProperty('--bg-canvas', css.bgCanvas);
        root.style.setProperty('--surface-glass', css.surfaceGlass);
        root.style.setProperty('--surface-glass-hover', css.surfaceGlassHover);
        root.style.setProperty('--surface-solid', css.surfaceSolid);
        root.style.setProperty('--surface-elevated', css.surfaceElevated);

        // 边框
        root.style.setProperty('--border-subtle', css.borderSubtle);
        root.style.setProperty('--border-strong', css.borderStrong);

        // 文字
        root.style.setProperty('--text-primary', css.textPrimary);
        root.style.setProperty('--text-secondary', css.textSecondary);
        root.style.setProperty('--text-tertiary', css.textTertiary);

        // 强调色
        root.style.setProperty('--accent-blue', css.accentBlue);
        root.style.setProperty('--accent-glow', css.accentGlow);
        root.style.setProperty('--accent-danger', css.accentDanger);
        root.style.setProperty('--accent-danger-glow', css.accentDangerGlow);

        console.log(`[ThemeService] CSS 变量已更新为 ${theme.name} 主题`);
    }

    /**
     * 切换主题
     */
    public toggleTheme(): void {
        this._currentTheme.value = this.isDark ? lightTheme : darkTheme;

        // 更新 CSS 变量
        this.updateCSSVariables(this._currentTheme.value);

        // 分发主题变化事件
        window.dispatchEvent(new CustomEvent('bimcanvas:theme-change', {
            detail: this._currentTheme.value
        }));

        console.log(`[ThemeService] 主题已切换为: ${this._currentTheme.value.name}`);
    }

    /**
     * 设置指定主题
     */
    public setTheme(theme: 'dark' | 'light'): void {
        const targetTheme = theme === 'dark' ? darkTheme : lightTheme;
        if (this._currentTheme.value.name !== targetTheme.name) {
            this._currentTheme.value = targetTheme;
            this.updateCSSVariables(targetTheme);

            window.dispatchEvent(new CustomEvent('bimcanvas:theme-change', {
                detail: this._currentTheme.value
            }));

            console.log(`[ThemeService] 主题已设置为: ${targetTheme.name}`);
        }
    }

    /**
     * 初始化 - 应用默认主题的 CSS 变量
     */
    public init(): void {
        this.updateCSSVariables(this._currentTheme.value);
    }
}

export const themeService = new ThemeServiceClass();
