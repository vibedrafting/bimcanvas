import * as THREE from 'three';

// 图层预设配置类型
export interface LayerPresetConfig {
    enabledLayers: string[];
}

export interface LayerPresetsConfig {
    // 新格式（与 UI 一致）
    User?: LayerPresetConfig;
    Agent?: LayerPresetConfig;
    // 旧格式（向后兼容）
    human?: LayerPresetConfig;
    ai?: LayerPresetConfig;
}

export class LayerManager {
    // Layer Definitions
    public static readonly LAYER_MODEL = 0; // 默认模型层
    public static readonly LAYER_GRID = 2;  // 语义网格
    public static readonly LAYER_LABELS = 3; // ID 标签
    public static readonly LAYER_BOUNDS = 4; // 家具模块 OBB + 朝向箭头
    public static readonly LAYER_OUTLINE = 5; // 边界描边（原 SEMANTIC）
    public static readonly LAYER_SVG = 6;   // SVG 预览层
    public static readonly LAYER_ZONES = 7;  // 设计区 / 禁区（预留）
    public static readonly LAYER_SEMANTIC = 8; // 设计场线（预留）
    public static readonly LAYER_AI_VISION = 9; // AI 视觉层 (高对比度)
    public static readonly LAYER_ARCHITECTURE = 10; // 建筑图层（墙柱门窗）
    public static readonly LAYER_FURNITURE = 11;    // 模块预览图层（家具）

    // 图层名称到 ID 的映射（首字母大写格式，与 UI 显示一致）
    // 注意：配置文件中的图层名称应使用此格式
    private static readonly LAYER_NAME_MAP: Record<string, number> = {
        'Grid': LayerManager.LAYER_GRID,
        'Labels': LayerManager.LAYER_LABELS,
        'Bounds': LayerManager.LAYER_BOUNDS,
        'Outline': LayerManager.LAYER_OUTLINE,
        'SVG Preview': LayerManager.LAYER_SVG,
        'Zones': LayerManager.LAYER_ZONES,
        'Semantic': LayerManager.LAYER_SEMANTIC,
        'AI Vision': LayerManager.LAYER_AI_VISION,
        'Architecture': LayerManager.LAYER_ARCHITECTURE,
        'Furniture': LayerManager.LAYER_FURNITURE
        // 注意：Model 不在此映射中，因为它是强制启用的
    };

    // 旧格式映射（向后兼容大写格式）
    private static readonly LAYER_NAME_MAP_LEGACY: Record<string, number> = {
        'GRID': LayerManager.LAYER_GRID,
        'LABELS': LayerManager.LAYER_LABELS,
        'BOUNDS': LayerManager.LAYER_BOUNDS,
        'OUTLINE': LayerManager.LAYER_OUTLINE,
        'SVG': LayerManager.LAYER_SVG,
        'ZONES': LayerManager.LAYER_ZONES,
        'SEMANTIC': LayerManager.LAYER_SEMANTIC,
        'AI_VISION': LayerManager.LAYER_AI_VISION,
        'ARCHITECTURE': LayerManager.LAYER_ARCHITECTURE,
        'FURNITURE': LayerManager.LAYER_FURNITURE
    };

    // 图层 ID 到显示名称的映射（供 UI 使用）
    public static readonly LAYER_DISPLAY_NAMES: Record<number, string> = {
        [LayerManager.LAYER_MODEL]: 'Model',
        [LayerManager.LAYER_GRID]: 'Grid',
        [LayerManager.LAYER_LABELS]: 'Labels',
        [LayerManager.LAYER_BOUNDS]: 'Bounds',
        [LayerManager.LAYER_OUTLINE]: 'Outline',
        [LayerManager.LAYER_SVG]: 'SVG Preview',
        [LayerManager.LAYER_ZONES]: 'Zones',
        [LayerManager.LAYER_SEMANTIC]: 'Semantic',
        [LayerManager.LAYER_AI_VISION]: 'AI Vision',
        [LayerManager.LAYER_ARCHITECTURE]: 'Architecture',
        [LayerManager.LAYER_FURNITURE]: 'Furniture'
    };

    // 所有图层 ID 列表（用于禁用全部）
    private static readonly ALL_LAYER_IDS: number[] = [
        LayerManager.LAYER_MODEL,
        LayerManager.LAYER_GRID,
        LayerManager.LAYER_LABELS,
        LayerManager.LAYER_BOUNDS,
        LayerManager.LAYER_OUTLINE,
        LayerManager.LAYER_SVG,
        LayerManager.LAYER_ZONES,
        LayerManager.LAYER_SEMANTIC,
        LayerManager.LAYER_AI_VISION,
        LayerManager.LAYER_ARCHITECTURE,
        LayerManager.LAYER_FURNITURE
    ];

    // Presets（与 UI 显示名称一致）
    public static readonly PRESET_USER = 'User';
    public static readonly PRESET_AGENT = 'Agent';

    // 向后兼容旧名称
    public static readonly PRESET_HUMAN = 'User';  // @deprecated 使用 PRESET_USER
    public static readonly PRESET_AI = 'Agent';    // @deprecated 使用 PRESET_AGENT

    private camera: THREE.Camera;
    private presetConfig: LayerPresetsConfig | null = null;

    constructor(camera: THREE.Camera) {
        this.camera = camera;
        this.applyPreset(LayerManager.PRESET_USER);
    }

    /**
     * 设置预设配置（从外部配置文件加载）
     * @param config 图层预设配置
     */
    public setPresetConfig(config: LayerPresetsConfig): void {
        this.presetConfig = config;
        console.log('[LayerManager] 预设配置已更新:', config);
    }

    public toggleLayer(layerId: number, visible: boolean) {
        if (visible) {
            this.camera.layers.enable(layerId);
        } else {
            this.camera.layers.disable(layerId);
        }
    }

    public applyPreset(preset: string) {
        // 如果有外部配置，使用外部配置
        if (this.presetConfig) {
            this.applyPresetFromConfig(preset);
            return;
        }

        // 回退到硬编码默认值（兼容性保留）
        this.applyPresetHardcoded(preset);
    }

    /**
     * 根据图层名称查找图层 ID（支持新旧两种格式）
     */
    private static resolveLayerId(layerName: string): number | undefined {
        // 优先使用新格式（首字母大写）
        if (LayerManager.LAYER_NAME_MAP[layerName] !== undefined) {
            return LayerManager.LAYER_NAME_MAP[layerName];
        }
        // 向后兼容旧格式（全大写）
        if (LayerManager.LAYER_NAME_MAP_LEGACY[layerName] !== undefined) {
            return LayerManager.LAYER_NAME_MAP_LEGACY[layerName];
        }
        return undefined;
    }

    /**
     * 从外部配置应用预设
     */
    private applyPresetFromConfig(preset: string): void {
        // 查找配置：优先使用新格式，回退到旧格式
        let config: LayerPresetConfig | undefined;
        if (preset === 'User' || preset === 'human') {
            config = this.presetConfig?.User ?? this.presetConfig?.human;
        } else if (preset === 'Agent' || preset === 'ai') {
            config = this.presetConfig?.Agent ?? this.presetConfig?.ai;
        }

        // 先禁用所有图层
        LayerManager.ALL_LAYER_IDS.forEach(id => {
            this.camera.layers.disable(id);
        });

        // 构建图层状态记录
        const layerStates: Record<number, boolean> = {};
        LayerManager.ALL_LAYER_IDS.forEach(id => {
            layerStates[id] = false;
        });

        // MODEL 图层强制启用（无论配置如何）
        this.camera.layers.enable(LayerManager.LAYER_MODEL);
        layerStates[LayerManager.LAYER_MODEL] = true;

        // 如果配置不存在，仅启用 MODEL
        if (!config || !config.enabledLayers) {
            console.log(`[LayerManager] 预设 "${preset}" 配置不存在，仅启用 MODEL 图层`);
            this.dispatchLayerStateChangeEvent(preset, layerStates);
            return;
        }

        // 根据配置启用指定图层
        config.enabledLayers.forEach(layerName => {
            const layerId = LayerManager.resolveLayerId(layerName);
            if (layerId !== undefined) {
                this.camera.layers.enable(layerId);
                layerStates[layerId] = true;
            } else {
                console.warn(`[LayerManager] 未知图层名称: ${layerName}`);
            }
        });

        console.log(`[LayerManager] 已应用预设 "${preset}":`, config.enabledLayers);
        this.dispatchLayerStateChangeEvent(preset, layerStates);
    }

    /**
     * 派发图层状态变化事件，通知 UI 同步状态
     */
    private dispatchLayerStateChangeEvent(preset: string, layerStates: Record<number, boolean>): void {
        window.dispatchEvent(new CustomEvent('bimcanvas:layer-state-change', {
            detail: { preset, layerStates }
        }));
        console.log(`[LayerManager] 已派发 layer-state-change 事件:`, { preset, layerStates });
    }

    /**
     * 硬编码默认预设（回退方案）
     */
    private applyPresetHardcoded(preset: string): void {
        // 构建图层状态记录
        const layerStates: Record<number, boolean> = {};
        LayerManager.ALL_LAYER_IDS.forEach(id => {
            layerStates[id] = false;
        });

        // Always enable model
        this.camera.layers.enable(LayerManager.LAYER_MODEL);
        layerStates[LayerManager.LAYER_MODEL] = true;

        if (preset === LayerManager.PRESET_USER || preset === 'human') {
            this.camera.layers.enable(LayerManager.LAYER_GRID); // Enable Grid by default for User
            this.camera.layers.enable(LayerManager.LAYER_ARCHITECTURE); // 建筑图层默认启用
            this.camera.layers.enable(LayerManager.LAYER_FURNITURE);    // 模块图层默认启用
            this.camera.layers.disable(LayerManager.LAYER_LABELS);
            this.camera.layers.disable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.disable(LayerManager.LAYER_OUTLINE);
            this.camera.layers.disable(LayerManager.LAYER_SVG);
            this.camera.layers.disable(LayerManager.LAYER_ZONES);
            this.camera.layers.disable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.disable(LayerManager.LAYER_AI_VISION);

            // 更新状态记录
            layerStates[LayerManager.LAYER_GRID] = true;
            layerStates[LayerManager.LAYER_ARCHITECTURE] = true;
            layerStates[LayerManager.LAYER_FURNITURE] = true;
        } else if (preset === LayerManager.PRESET_AGENT || preset === 'ai') {
            // Agent Mode: Enable all auxiliary layers (LAYER_MODEL always stays enabled)
            this.camera.layers.enable(LayerManager.LAYER_GRID);
            this.camera.layers.enable(LayerManager.LAYER_LABELS);
            this.camera.layers.enable(LayerManager.LAYER_BOUNDS);
            this.camera.layers.enable(LayerManager.LAYER_OUTLINE);
            this.camera.layers.enable(LayerManager.LAYER_SVG);
            this.camera.layers.enable(LayerManager.LAYER_ZONES);
            this.camera.layers.enable(LayerManager.LAYER_SEMANTIC);
            this.camera.layers.enable(LayerManager.LAYER_AI_VISION);
            this.camera.layers.enable(LayerManager.LAYER_ARCHITECTURE);
            this.camera.layers.enable(LayerManager.LAYER_FURNITURE);
            // Note: LAYER_MODEL is NOT disabled here. AI Vision is an OVERLAY, not a replacement.

            // 更新状态记录 - 所有图层启用
            LayerManager.ALL_LAYER_IDS.forEach(id => {
                layerStates[id] = true;
            });
        }

        // 派发事件通知 UI 同步状态
        this.dispatchLayerStateChangeEvent(preset, layerStates);
    }

    // Deprecated, kept for compatibility during refactor if needed, but better to remove or alias
    public setMode(mode: 'human' | 'ai') {
        this.applyPreset(mode);
    }
}
