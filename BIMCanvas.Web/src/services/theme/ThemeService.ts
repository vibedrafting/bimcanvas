/**
 * ThemeService - 配色管理核心服务
 * 
 * 提供暗色和亮色两套主题配色，集中管理所有 Builder 的配色定义。
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
        bounds: number; // BoxHelper 包围盒
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

    /** 标签配色 (CSS 字符串) */
    label: {
        background: string;
        text: string;
        border: string;
    };
}

// ============================================================================
// 主题定义
// ============================================================================

/**
 * 暗色主题 - 提取自当前代码中的配色
 */
export const darkTheme: ColorTheme = {
    name: 'dark',
    scene: {
        wall: 0x5c5c5c,       // 中灰色墙体
        column: 0x707070,     // 浅灰色柱子
        module: 0x3b82f6,     // 蓝色模块
        floor: 0x0a0a0f,      // 深色地板
        doorFrame: 0x4a5568,  // 冷灰色门框
        doorPanel: 0x718096,  // 浅灰色门板
        windowFrame: 0x4a5568,// 冷灰色窗框
        glass: 0xAADDFF,      // 浅蓝色玻璃
        swingArc: 0xffffff,   // 白色门弧线
        bounds: 0xffff00,     // 黄色包围盒
    },
    grid: {
        centerLine: 0x888888, // 中心线 (亮灰)
        gridLine: 0x333333,   // 网格线 (暗灰)
    },
    semantic: {
        line: 0x00ff00,       // 亮绿色语义线
    },
    label: {
        background: 'rgba(0, 0, 0, 0.8)',
        text: '#00ff00',
        border: '1px solid #00ff00',
    },
};

/**
 * 亮色主题 - 适合明亮背景的配色方案
 */
export const lightTheme: ColorTheme = {
    name: 'light',
    scene: {
        wall: 0xd4d4d8,       // 浅灰色墙体
        column: 0xa1a1aa,     // 中灰色柱子
        module: 0x2563eb,     // 深蓝色模块 (更饱和)
        floor: 0xf4f4f5,      // 浅色地板
        doorFrame: 0x71717a,  // 中灰色门框
        doorPanel: 0x52525b,  // 深灰色门板
        windowFrame: 0x71717a,// 中灰色窗框
        glass: 0x93c5fd,      // 柔和蓝色玻璃
        swingArc: 0x374151,   // 深灰色门弧线
        bounds: 0xf59e0b,     // 橙色包围盒 (更醒目)
    },
    grid: {
        centerLine: 0x52525b, // 中心线 (深灰)
        gridLine: 0xd4d4d8,   // 网格线 (浅灰)
    },
    semantic: {
        line: 0x059669,       // 深绿色语义线 (更柔和)
    },
    label: {
        background: 'rgba(255, 255, 255, 0.9)',
        text: '#059669',
        border: '1px solid #059669',
    },
};

// ============================================================================
// 服务单例
// ============================================================================

class ThemeServiceClass {
    /** 当前主题 (响应式) */
    private _currentTheme = ref<ColorTheme>(darkTheme);

    /** 只读的当前主题引用 */
    public readonly currentTheme = readonly(this._currentTheme);

    /** 是否为暗色主题 */
    public get isDark(): boolean {
        return this._currentTheme.value.name === 'dark';
    }

    /**
     * 切换主题
     */
    public toggleTheme(): void {
        this._currentTheme.value = this.isDark ? lightTheme : darkTheme;

        // 分发主题变化事件，供 Builders 监听
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

            window.dispatchEvent(new CustomEvent('bimcanvas:theme-change', {
                detail: this._currentTheme.value
            }));

            console.log(`[ThemeService] 主题已设置为: ${targetTheme.name}`);
        }
    }
}

/** 导出服务单例 */
export const themeService = new ThemeServiceClass();
