export type SnapType = 'endpoint' | 'midpoint' | 'perpendicular' | 'intersection';

export const SNAP_TYPE_LABELS: Record<SnapType, string> = {
    endpoint: '端点',
    midpoint: '中点',
    perpendicular: '垂足',
    intersection: '交点'
};

export const SNAP_TYPE_PRIORITY: Record<SnapType, number> = {
    endpoint: 1,
    midpoint: 2,
    intersection: 3,
    perpendicular: 4
};
