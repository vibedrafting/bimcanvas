/**
 * ThemeService - UI theme tokens only.
 *
 * Canvas visuals are controlled by CanvasStyleService.
 */
import { ref, readonly } from 'vue';
import { createLogger } from '../../utils/logger';

const log = createLogger('SYS');

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

    /** AI 视觉层配色 (高对比度) */
    aiVision: {
        wall: number;
        column: number;
        module: number;
        door: number;
        window: number;
        slab: number;
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
        bgScene: string; // New: Solid scene background color
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

        // Premium Glass Tokens (Aurora/Frosty)
        glassBg: string;
        glassBgHeader: string; // New variable for extra transparent header
        glassBgSolid: string;  // New: Solid background for dropdowns
        glassBlur: string;
        glassBorder: string;
        glassInnerHighlight: string;
        glassGlare: string;
        shadowIsland: string;
        shadowPanel: string;

        // Button States (Ghost)
        btnGhostBg: string;
        btnGhostBgHover: string;
        btnGhostBgActive: string;
        btnGhostTextActive: string;

        // Tab States (Ribbon)
        tabTextActive: string;
        tabBgActive: string;
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
        bounds: 0xFF6B6B,     // Coral Red (Lighter, visible on dark bg)
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
    aiVision: {
        wall: 0x37474F,      // Blue Grey 800
        column: 0x90A4AE,    // Blue Grey 300
        module: 0xFFB74D,    // Orange 300
        door: 0x00E676,      // Spring Green (Bright, safe, distinct from walls/windows)
        window: 0x64B5F6,    // Blue 300
        slab: 0xCFD8DC,      // Blue Grey 100
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
        // 暗色主题 - 深黑背景 (Radial Gradient for Atmosphere)
        bgCanvas: 'radial-gradient(circle at 50% -20%, #1e1e2e 0%, #0a0a0f 100%)',
        bgScene: '#0a0a0f', // Matches theme.background
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

        // Aurora Glass (Dark Mode) - High Transparency
        glassBg: 'linear-gradient(180deg, rgba(30, 30, 40, 0.3) 0%, rgba(20, 20, 30, 0.2) 100%)',
        glassBgHeader: 'rgba(20, 20, 30, 0.15)', // Ultra transparent header
        glassBgSolid: 'rgba(30, 30, 40, 0.95)', // Solid dark for dropdowns
        glassBlur: 'blur(24px) saturate(180%)',
        glassBorder: '1px solid rgba(255, 255, 255, 0.12)',
        glassInnerHighlight: 'inset 0 1px 0 rgba(255, 255, 255, 0.2)',
        glassGlare: 'radial-gradient(circle at 50% 0%, rgba(255, 255, 255, 0.12) 0%, rgba(255, 255, 255, 0) 60%)',
        shadowIsland: '0 16px 40px rgba(0, 0, 0, 0.3), 0 0 0 1px rgba(0, 0, 0, 0.1)',
        shadowPanel: '0 4px 30px rgba(0, 0, 0, 0.2), 0 0 0 1px rgba(0, 0, 0, 0.1)',

        // Button States (Ghost)
        btnGhostBg: 'rgba(255, 255, 255, 0.03)',
        btnGhostBgHover: 'rgba(255, 255, 255, 0.08)',
        btnGhostBgActive: 'rgba(255, 255, 255, 0.1)',
        btnGhostTextActive: '#ffffff',

        // Tab States
        tabTextActive: '#ffffff',
        tabBgActive: 'rgba(255, 255, 255, 0.1)',
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
        bounds: 0xFF6B6B,     // Coral Red
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
    aiVision: {
        wall: 0x37474F,      // Blue Grey 800
        column: 0x90A4AE,    // Blue Grey 300
        module: 0xFFB74D,    // Orange 300
        door: 0x00E676,      // Spring Green
        window: 0x64B5F6,    // Blue 300
        slab: 0xCFD8DC,      // Blue Grey 100
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
        // 亮色主题 - Apple Freeform 风格 (Radial Gradient for Atmosphere)
        bgCanvas: 'radial-gradient(circle at 50% -20%, #ffffff 0%, #f2f6fa 100%)',
        bgScene: '#ffffff', // Matches theme.background
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

        // Curved Glass (Light Mode) - High Transparency
        glassBg: 'linear-gradient(160deg, rgba(255, 255, 255, 0.4) 0%, rgba(240, 245, 255, 0.3) 100%)',
        glassBgHeader: 'rgba(255, 255, 255, 0.2)', // Ultra transparent header
        glassBgSolid: 'rgba(255, 255, 255, 0.95)', // Solid white for dropdowns
        glassBlur: 'blur(24px) saturate(180%)',
        glassBorder: '1px solid rgba(0, 10, 30, 0.12)', // 加深边框，更锐利
        glassInnerHighlight: 'inset 0 1px 2px rgba(255, 255, 255, 0.9), inset 0 -2px 4px rgba(200, 210, 230, 0.15)', // 上亮下暗模拟厚度
        glassGlare: 'linear-gradient(180deg, rgba(255, 255, 255, 0.6) 0%, rgba(255, 255, 255, 0) 30%)', // 顶部强反光
        shadowIsland: '0 24px 48px rgba(0, 20, 50, 0.18), 0 4px 12px rgba(0, 0, 0, 0.1)', // 显著加深悬浮阴影
        shadowPanel: '0 8px 24px rgba(0, 20, 50, 0.08), 0 1px 2px rgba(0, 0, 0, 0.05)',

        // Button States (Ghost)
        btnGhostBg: 'rgba(0, 0, 0, 0.03)',
        btnGhostBgHover: 'rgba(0, 0, 0, 0.06)',
        btnGhostBgActive: 'rgba(0, 122, 255, 0.1)', // Light Blue Tint for active state
        btnGhostTextActive: '#007aff', // Active text is Blue in light mode

        // Tab States
        tabTextActive: '#000000', // Black text for active tab in light mode
        tabBgActive: 'rgba(0, 0, 0, 0.05)', // Subtle dark tint
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

        // 设置 data-theme 属性，使 variables.css 中的 [data-theme="light"] 选择器生效
        root.setAttribute('data-theme', theme.name);

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
        root.style.setProperty('--accent-danger', css.accentDanger);
        root.style.setProperty('--accent-danger-glow', css.accentDangerGlow);

        // Glass Tokens
        root.style.setProperty('--glass-bg', css.glassBg);
        root.style.setProperty('--glass-bg-header', css.glassBgHeader);
        root.style.setProperty('--glass-bg-solid', css.glassBgSolid);
        root.style.setProperty('--glass-blur', css.glassBlur);
        root.style.setProperty('--glass-border', css.glassBorder);
        root.style.setProperty('--glass-inner-highlight', css.glassInnerHighlight);
        root.style.setProperty('--glass-glare', css.glassGlare);
        root.style.setProperty('--shadow-island', css.shadowIsland);
        root.style.setProperty('--shadow-panel', css.shadowPanel);

        // Button Tokens
        root.style.setProperty('--btn-ghost-bg', css.btnGhostBg);
        root.style.setProperty('--btn-ghost-bg-hover', css.btnGhostBgHover);
        root.style.setProperty('--btn-ghost-bg-active', css.btnGhostBgActive);
        root.style.setProperty('--btn-ghost-text-active', css.btnGhostTextActive);

        // Tab Tokens
        root.style.setProperty('--tab-text-active', css.tabTextActive);
        root.style.setProperty('--tab-bg-active', css.tabBgActive);

        // Scene Background (Solid)
        root.style.setProperty('--bg-scene', css.bgScene);

        log.debug('css variables updated', { theme: theme.name });
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

        log.debug('theme toggled', { theme: this._currentTheme.value.name });
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

            log.debug('theme set', { theme: targetTheme.name });
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
