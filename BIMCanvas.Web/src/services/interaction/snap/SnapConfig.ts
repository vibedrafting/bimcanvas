import type { SnapType } from './SnapTypes';

export interface SnapConfigState {
    enabled: Record<SnapType, boolean>;
    snapInPx: number;
    snapOutPx: number;
}

const STORAGE_KEY = 'bimcanvas.osnap.v1';

const DEFAULT_STATE: SnapConfigState = {
    enabled: {
        endpoint: true,
        midpoint: true,
        perpendicular: true,
        intersection: true
    },
    snapInPx: 10,
    snapOutPx: 16
};

let cachedState: SnapConfigState = loadState();

function cloneState(state: SnapConfigState): SnapConfigState {
    return JSON.parse(JSON.stringify(state)) as SnapConfigState;
}

function loadState(): SnapConfigState {
    if (typeof window === 'undefined') {
        return cloneState(DEFAULT_STATE);
    }

    try {
        const raw = window.localStorage.getItem(STORAGE_KEY);
        if (!raw) return cloneState(DEFAULT_STATE);
        const parsed = JSON.parse(raw) as Partial<SnapConfigState>;
        return {
            enabled: {
                endpoint: parsed.enabled?.endpoint ?? DEFAULT_STATE.enabled.endpoint,
                midpoint: parsed.enabled?.midpoint ?? DEFAULT_STATE.enabled.midpoint,
                perpendicular: parsed.enabled?.perpendicular ?? DEFAULT_STATE.enabled.perpendicular,
                intersection: parsed.enabled?.intersection ?? DEFAULT_STATE.enabled.intersection
            },
            snapInPx: typeof parsed.snapInPx === 'number' ? parsed.snapInPx : DEFAULT_STATE.snapInPx,
            snapOutPx: typeof parsed.snapOutPx === 'number' ? parsed.snapOutPx : DEFAULT_STATE.snapOutPx
        };
    } catch {
        return cloneState(DEFAULT_STATE);
    }
}

function persistState(state: SnapConfigState) {
    if (typeof window === 'undefined') return;
    try {
        window.localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
        // ignore storage errors
    }
}

export class SnapConfig {
    public static get(): SnapConfigState {
        return cachedState;
    }

    public static set(next: SnapConfigState): void {
        cachedState = {
            enabled: { ...next.enabled },
            snapInPx: next.snapInPx,
            snapOutPx: next.snapOutPx
        };
        persistState(cachedState);
    }

    public static update(partial: Partial<SnapConfigState>): void {
        const next: SnapConfigState = {
            enabled: {
                ...cachedState.enabled,
                ...(partial.enabled ?? {})
            },
            snapInPx: partial.snapInPx ?? cachedState.snapInPx,
            snapOutPx: partial.snapOutPx ?? cachedState.snapOutPx
        };
        SnapConfig.set(next);
    }
}
