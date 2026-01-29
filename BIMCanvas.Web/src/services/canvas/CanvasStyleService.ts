import { ref, readonly } from 'vue';

export interface LabelStyle {
    text: string;
    shadow: string;
    background?: string;
    padding?: string;
    borderRadius?: string;
    fontSize: number;
    fontWeight: string;
    fontFamily?: string;
}

export interface GridLayerStyle {
    spacing: number;
    size: number;
    centerLine: number;
    gridLine: number;
    label: LabelStyle;
}

export interface OutlineLayerStyle {
    color: number;
    lineWidth: number;
    depthTest: boolean;
}

export interface BoundsLayerStyle {
    color: number;
    opacity: number;
    arrow: {
        shaftLength: number;
        shaftWidth: number;
        headLength: number;
        headWidth: number;
        height: number;
    };
}

export interface ZoneFillStyle {
    color: number;
    opacity: number;
}

export interface ZoneLayerStyle {
    room: ZoneFillStyle;
    designable: ZoneFillStyle;
    exclusion: ZoneFillStyle;
    exclusionDoorSwing: ZoneFillStyle;
}

export interface StandardMaterialStyle {
    color: number;
    roughness: number;
    metalness: number;
    doubleSided?: boolean;
}

export interface PhysicalMaterialStyle {
    color: number;
    roughness: number;
    metalness: number;
    transmission: number;
    transparent: boolean;
    opacity: number;
}

export interface BasicMaterialStyle {
    color: number;
}

export interface LineStyle {
    color: number;
    opacity?: number;
    transparent?: boolean;
    lineWidth?: number;
}

export interface ArchitectureLayerStyle {
    wall: StandardMaterialStyle;
    column: StandardMaterialStyle;
    floor: StandardMaterialStyle;
    doorFrame: StandardMaterialStyle;
    doorPanel: StandardMaterialStyle;
    windowFrame: StandardMaterialStyle;
    glass: PhysicalMaterialStyle;
    swingArc: LineStyle;
}

export interface FurnitureLayerStyle {
    module: StandardMaterialStyle;
}

export interface AiVisionLayerStyle {
    wall: BasicMaterialStyle;
    column: BasicMaterialStyle;
    module: BasicMaterialStyle;
    door: BasicMaterialStyle;
    window: BasicMaterialStyle;
    slab: BasicMaterialStyle;
}

export interface SvgLayerStyle {
    replaceBlackFill: string;
    replaceBlackStroke: string;
    defaultStrokeWidth: number;
    depthTest: boolean;
}

export interface CanvasSceneStyle {
    background: number;
    fog: {
        color: number;
        density: number;
    };
}

export interface CanvasLightingStyle {
    ambient: {
        color: number;
        intensity: number;
    };
    directional: {
        color: number;
        intensity: number;
        position: [number, number, number];
        shadow: {
            mapSize: number;
            bias: number;
            near: number;
            far: number;
        };
    };
    hemisphere: {
        skyColor: number;
        groundColor: number;
        intensity: number;
    };
}

export interface CanvasStyle {
    name: string;
    scene: CanvasSceneStyle;
    lighting: CanvasLightingStyle;
    layers: {
        grid: GridLayerStyle;
        labels: {
            component: LabelStyle;
        };
        outline: OutlineLayerStyle;
        bounds: BoundsLayerStyle;
        zones: ZoneLayerStyle;
        architecture: ArchitectureLayerStyle;
        furniture: FurnitureLayerStyle;
        aiVision: AiVisionLayerStyle;
        svg: SvgLayerStyle;
    };
}

const darkCanvasStyle: CanvasStyle = {
    name: 'dark-v1',
    scene: {
        background: 0x0a0a0f,
        fog: {
            color: 0x0a0a0f,
            density: 0.00005
        }
    },
    lighting: {
        ambient: {
            color: 0xffffff,
            intensity: 0.4
        },
        directional: {
            color: 0xffffff,
            intensity: 0.8,
            position: [-5000, 10000, 5000],
            shadow: {
                mapSize: 2048,
                bias: -0.0001,
                near: 0.5,
                far: 50000
            }
        },
        hemisphere: {
            skyColor: 0xeeeeff,
            groundColor: 0x777788,
            intensity: 0.2
        }
    },
    layers: {
        grid: {
            spacing: 1000,
            size: 100000,
            centerLine: 0x6b7280,
            gridLine: 0x27272a,
            label: {
                text: '#6b7280',
                shadow: '0 1px 2px rgba(0,0,0,0.5)',
                background: 'transparent',
                fontSize: 14,
                fontWeight: 'bold',
                fontFamily: 'var(--font-sans)'
            }
        },
        labels: {
            component: {
                text: '#ffffff',
                shadow: '-1px -1px 0 #000, 1px -1px 0 #000, -1px 1px 0 #000, 1px 1px 0 #000',
                background: 'transparent',
                fontSize: 10,
                fontWeight: 'normal',
                fontFamily: 'monospace'
            }
        },
        outline: {
            color: 0x22c55e,
            lineWidth: 2,
            depthTest: false
        },
        bounds: {
            color: 0xff6b6b,
            opacity: 0.9,
            arrow: {
                shaftLength: 250,
                shaftWidth: 40,
                headLength: 120,
                headWidth: 100,
                height: 800
            }
        },
        zones: {
            room: {
                color: 0x22c55e,
                opacity: 0.1
            },
            designable: {
                color: 0x22c55e,
                opacity: 0.2
            },
            exclusion: {
                color: 0xef4444,
                opacity: 0.4
            },
            exclusionDoorSwing: {
                color: 0xef4444,
                opacity: 0.3
            }
        },
        architecture: {
            wall: {
                color: 0x6b7280,
                roughness: 0.8,
                metalness: 0.1
            },
            column: {
                color: 0x9ca3af,
                roughness: 0.8,
                metalness: 0.1
            },
            floor: {
                color: 0x0a0a0f,
                roughness: 0.9,
                metalness: 0.1
            },
            doorFrame: {
                color: 0x4a5568,
                roughness: 0.8,
                metalness: 0.3
            },
            doorPanel: {
                color: 0x718096,
                roughness: 0.7,
                metalness: 0.1
            },
            windowFrame: {
                color: 0x4a5568,
                roughness: 0.8,
                metalness: 0.3
            },
            glass: {
                color: 0xaaddff,
                roughness: 0.1,
                metalness: 0.1,
                transmission: 0.6,
                transparent: true,
                opacity: 0.6
            },
            swingArc: {
                color: 0xffffff,
                opacity: 0.3,
                transparent: true,
                lineWidth: 1
            }
        },
        furniture: {
            module: {
                color: 0x3b82f6,
                roughness: 0.6,
                metalness: 0.2,
                doubleSided: true
            }
        },
        aiVision: {
            wall: { color: 0x37474f },
            column: { color: 0x90a4ae },
            module: { color: 0xffb74d },
            door: { color: 0x00e676 },
            window: { color: 0x64b5f6 },
            slab: { color: 0xcfd8dc }
        },
        svg: {
            replaceBlackFill: '#ffffff',
            replaceBlackStroke: '#ffffff',
            defaultStrokeWidth: 20,
            depthTest: false
        }
    }
};

class CanvasStyleServiceClass {
    private _currentStyle = ref<CanvasStyle>(darkCanvasStyle);
    public readonly currentStyle = readonly(this._currentStyle);

    public setStyle(style: CanvasStyle): void {
        this._currentStyle.value = style;
        window.dispatchEvent(new CustomEvent('bimcanvas:canvas-style-change', {
            detail: style
        }));
    }

    public setGridSpacing(spacing: 600 | 1000): void {
        this._currentStyle.value.layers.grid.spacing = spacing;
    }
}

export const canvasStyleService = new CanvasStyleServiceClass();
