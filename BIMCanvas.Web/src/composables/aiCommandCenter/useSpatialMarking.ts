import { computed, onUnmounted, watch } from 'vue';
import type { Ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';
import { SpatialMarksService } from '../../services/SpatialMarksService';
import type {
  ChatWindow,
  GridSelectionCell,
  SpatialMark,
  SpatialGeometry,
  SpatialMarkDraft
} from '../../types/aiCommandCenter';
import type { Point2D, Polygon2D } from '../../types/canvas';

interface SpatialMarkingOptions {
  windows: Ref<ChatWindow[]>;
  activeWindowId: Ref<string>;
  activeWindow: Ref<ChatWindow | undefined>;
}

const DEFAULT_CELL_SIZE = 200;
const MIN_CELL_SIZE = 50;

const cloneCells = (cells: GridSelectionCell[]): GridSelectionCell[] =>
  cells.map(cell => ({ col: cell.col, row: cell.row }));

const normalizeCellSize = (value: number): number => {
  if (!Number.isFinite(value)) return DEFAULT_CELL_SIZE;
  return Math.max(MIN_CELL_SIZE, Math.round(value));
};

const getBoundaryShell = (boundary: Polygon2D | { shell?: Point2D[] } | undefined | null): Point2D[] => {
  if (!boundary) return [];
  return Array.isArray(boundary) ? boundary : (boundary.shell || []);
};

const isPointInsidePolygon = (point: Point2D, polygon: Point2D[]): boolean => {
  let inside = false;
  const [x, y] = point;
  for (let i = 0, j = polygon.length - 1; i < polygon.length; j = i, i += 1) {
    const [xi, yi] = polygon[i]!;
    const [xj, yj] = polygon[j]!;
    const intersects = ((yi > y) !== (yj > y)) &&
      (x < ((xj - xi) * (y - yi)) / ((yj - yi) || Number.EPSILON) + xi);
    if (intersects) inside = !inside;
  }
  return inside;
};

const computeAabb = (points: Point2D[]) => {
  return points.reduce((box, [x, y]) => ({
    minX: Math.min(box.minX, x),
    minY: Math.min(box.minY, y),
    maxX: Math.max(box.maxX, x),
    maxY: Math.max(box.maxY, y)
  }), {
    minX: Infinity,
    minY: Infinity,
    maxX: -Infinity,
    maxY: -Infinity
  });
};

const getGeometryAabb = (geometry: SpatialGeometry) => {
  if ('aabb' in geometry && geometry.aabb) {
    const [minX, minY, maxX, maxY] = geometry.aabb;
    return { minX, minY, maxX, maxY };
  }

  if ('polygon' in geometry && geometry.polygon) {
    const shell = getBoundaryShell(geometry.polygon);
    return computeAabb(shell);
  }

  return null;
};

const isPointInsideGeometry = (point: Point2D, geometry: SpatialGeometry): boolean => {
  if ('aabb' in geometry && geometry.aabb) {
    const [minX, minY, maxX, maxY] = geometry.aabb;
    return point[0] >= minX && point[0] <= maxX && point[1] >= minY && point[1] <= maxY;
  }

  if (!('polygon' in geometry) || !geometry.polygon) return false;

  const shell = getBoundaryShell(geometry.polygon);
  if (shell.length < 3 || !isPointInsidePolygon(point, shell)) return false;

  const holes = Array.isArray(geometry.polygon) ? [] : (geometry.polygon.holes || []);
  return !holes.some(hole => hole.length >= 3 && isPointInsidePolygon(point, hole));
};

export function useSpatialMarking(options: SpatialMarkingOptions) {
  const store = useCanvasStore();
  const { projectData } = storeToRefs(store);

  const topLevelZones = computed(() =>
    (projectData.value?.activeScheme?.zones || []).map(zone => ({
      id: zone.id,
      label: zone.name || zone.id
    }))
  );

  const activeDraft = computed(() => options.activeWindow.value?.spatialMarkDraft ?? null);
  const pendingSpatialMarks = computed(() => options.activeWindow.value?.pendingSpatialMarks ?? []);
  const isSpatialMarking = computed(() => !!activeDraft.value);

  const draftScopeDisplayText = computed(() => {
    const draft = activeDraft.value;
    if (!draft || draft.selectedCells.length === 0) return 'All spaces';

    const zones = projectData.value?.activeScheme?.zones || [];
    const zoneNames: string[] = [];

    for (const zone of zones) {
      const shell = getBoundaryShell(zone.computedBoundary || zone.rawBoundary);
      if (shell.length < 3) continue;

      const hasSelectedCell = draft.selectedCells.some(cell => {
        const center: Point2D = [
          (cell.col + 0.5) * draft.cellSize,
          (cell.row + 0.5) * draft.cellSize
        ];
        return isPointInsidePolygon(center, shell);
      });

      if (hasSelectedCell) {
        zoneNames.push(zone.name || zone.id);
      }
    }

    if (zoneNames.length === 0) return 'All spaces';
    if (zoneNames.length === 1) return zoneNames[0]!;
    if (zoneNames.length <= 2) return zoneNames.join(', ');
    return `${zoneNames.length} 个区域`;
  });

  const createDraft = (cellSize?: number): SpatialMarkDraft | null => {
    if (topLevelZones.value.length === 0) return null;
    return {
      zoneId: '__all__',
      zoneName: 'All spaces',
      cellSize: normalizeCellSize(cellSize ?? DEFAULT_CELL_SIZE),
      selectedCells: [],
      label: '',
      description: '',
      isCompleting: false,
      error: null
    };
  };

  const getTopLevelZoneName = (zoneId: string): string => {
    return topLevelZones.value.find(zone => zone.id === zoneId)?.label || zoneId;
  };

  const getCellsForZone = (cells: GridSelectionCell[], cellSize: number, zoneId: string): GridSelectionCell[] => {
    const zone = projectData.value?.activeScheme?.zones?.find(item => item.id === zoneId);
    const shell = getBoundaryShell(zone?.computedBoundary || zone?.rawBoundary);
    if (shell.length < 3) return cloneCells(cells);

    return cells.filter(cell => {
      const center: Point2D = [
        (cell.col + 0.5) * cellSize,
        (cell.row + 0.5) * cellSize
      ];
      return isPointInsidePolygon(center, shell);
    });
  };

  const getCellsFromGeometry = (geometry: SpatialGeometry[], cellSize: number): GridSelectionCell[] => {
    const cells = new Map<string, GridSelectionCell>();

    for (const item of geometry) {
      const box = getGeometryAabb(item);
      if (!box || !Number.isFinite(box.minX) || !Number.isFinite(box.minY) ||
        !Number.isFinite(box.maxX) || !Number.isFinite(box.maxY)) {
        continue;
      }

      const minCol = Math.floor(box.minX / cellSize);
      const maxCol = Math.floor(box.maxX / cellSize);
      const minRow = Math.floor(box.minY / cellSize);
      const maxRow = Math.floor(box.maxY / cellSize);

      for (let row = minRow; row <= maxRow; row += 1) {
        for (let col = minCol; col <= maxCol; col += 1) {
          const center: Point2D = [
            (col + 0.5) * cellSize,
            (row + 0.5) * cellSize
          ];
          if (isPointInsideGeometry(center, item)) {
            cells.set(`${col}:${row}`, { col, row });
          }
        }
      }
    }

    return Array.from(cells.values())
      .sort((a, b) => a.row === b.row ? a.col - b.col : a.row - b.row);
  };

  const getMarkCells = (mark: SpatialMark): GridSelectionCell[] => {
    if (mark.cells && mark.cells.length > 0) {
      return cloneCells(mark.cells);
    }

    return getCellsFromGeometry(mark.geometry, normalizeCellSize(mark.cellSize ?? DEFAULT_CELL_SIZE));
  };

  const dispatchModeChange = () => {
    const draft = activeDraft.value;
    window.dispatchEvent(new CustomEvent('bimcanvas:spatial-mark-mode-change', {
      detail: draft
        ? {
            active: true,
            cellSize: draft.cellSize,
            selectedCells: cloneCells(draft.selectedCells)
          }
        : { active: false }
    }));
  };

  const startSpatialMarking = () => {
    const win = options.activeWindow.value;
    if (!win) return;

    const draft = createDraft(win.spatialMarkDraft?.cellSize);
    if (!draft) return;

    win.spatialMarkDraft = draft;
    dispatchModeChange();
  };

  const cancelSpatialMarking = () => {
    const win = options.activeWindow.value;
    if (!win) return;
    win.spatialMarkDraft = null;
    dispatchModeChange();
  };

  const setDraftCellSize = (cellSize: number) => {
    const draft = activeDraft.value;
    if (!draft) return;
    draft.cellSize = normalizeCellSize(cellSize);
    draft.selectedCells = [];
    draft.error = null;
    dispatchModeChange();
  };

  const setDraftLabel = (label: string) => {
    const draft = activeDraft.value;
    if (draft) draft.label = label;
  };

  const setDraftDescription = (description: string) => {
    const draft = activeDraft.value;
    if (draft) draft.description = description;
  };

  const setSelectedCells = (cells: GridSelectionCell[]) => {
    const draft = activeDraft.value;
    if (!draft) return;
    draft.selectedCells = cloneCells(cells);
    draft.error = null;
    dispatchModeChange();
  };

  const clearDraftSelection = () => {
    setSelectedCells([]);
  };

  const generateSpatialMarkId = (marks: SpatialMark[]): string => {
    let index = marks.length + 1;
    let id = `sm_${String(index).padStart(2, '0')}`;
    const usedIds = new Set(marks.map(mark => mark.id));
    while (usedIds.has(id)) {
      index += 1;
      id = `sm_${String(index).padStart(2, '0')}`;
    }
    return id;
  };

  const buildMarksByZone = async (
    draft: SpatialMarkDraft,
    existingMarks: SpatialMark[],
    preferredIdByZone = new Map<string, string>()
  ): Promise<SpatialMark[]> => {
    const createdMarks: SpatialMark[] = [];

    for (const zone of topLevelZones.value) {
      const zoneCells = getCellsForZone(draft.selectedCells, draft.cellSize, zone.id);
      if (zoneCells.length === 0) {
        continue;
      }

      const response = await SpatialMarksService.mergeGridSelection({
        zoneId: zone.id,
        cellSize: draft.cellSize,
        gridOriginX: 0,
        gridOriginY: 0,
        cells: cloneCells(zoneCells)
      });

      if (!response.geometry || response.geometry.length === 0) {
        continue;
      }

      createdMarks.push({
        id: preferredIdByZone.get(zone.id) || generateSpatialMarkId([...existingMarks, ...createdMarks]),
        zoneId: zone.id,
        label: draft.label.trim(),
        description: draft.description.trim(),
        geometry: response.geometry,
        cellSize: draft.cellSize,
        cells: cloneCells(zoneCells)
      });
    }

    return createdMarks;
  };

  const completeDraft = async () => {
    const win = options.activeWindow.value;
    const draft = activeDraft.value;
    if (!win || !draft || draft.isCompleting) return;

    if (!draft.label.trim()) {
      draft.error = '请输入空间标记标签';
      return;
    }
    if (draft.selectedCells.length === 0) {
      draft.error = '请先在画布中选择标记区域';
      return;
    }

    draft.isCompleting = true;
    draft.error = null;

    try {
      if (draft.editingMarkId) {
        const markIndex = win.pendingSpatialMarks.findIndex(mark => mark.id === draft.editingMarkId);
        if (markIndex < 0) {
          draft.error = '未找到要修改的空间标记';
          return;
        }

        const existingMark = win.pendingSpatialMarks[markIndex]!;
        const otherMarks = win.pendingSpatialMarks.filter(mark => mark.id !== draft.editingMarkId);
        const updatedMarks = await buildMarksByZone(
          draft,
          otherMarks,
          new Map([[existingMark.zoneId, existingMark.id]])
        );

        if (updatedMarks.length === 0) {
          draft.error = '标记区域为空或完全在设计区外';
          return;
        }

        win.pendingSpatialMarks = [
          ...win.pendingSpatialMarks.slice(0, markIndex),
          ...updatedMarks,
          ...win.pendingSpatialMarks.slice(markIndex + 1)
        ];
        win.spatialMarkDraft = null;
        dispatchModeChange();
        return;
      }

      const createdMarks = await buildMarksByZone(draft, win.pendingSpatialMarks);

      if (createdMarks.length === 0) {
        draft.error = '标记区域为空或完全在设计区外';
        return;
      }

      win.pendingSpatialMarks.push(...createdMarks);
      win.spatialMarkDraft = null;
      dispatchModeChange();
    } catch (error: any) {
      draft.error = error?.response?.data?.message || error?.message || '空间标记合并失败';
    } finally {
      if (draft) {
        draft.isCompleting = false;
      }
    }
  };

  const editPendingMark = (markId: string) => {
    const win = options.activeWindow.value;
    if (!win) return;

    const mark = win.pendingSpatialMarks.find(item => item.id === markId);
    if (!mark) return;

    const cellSize = normalizeCellSize(mark.cellSize ?? DEFAULT_CELL_SIZE);
    const selectedCells = getMarkCells(mark);
    const draft = createDraft(cellSize);
    if (!draft) return;

    win.spatialMarkDraft = {
      ...draft,
      zoneId: mark.zoneId,
      zoneName: getTopLevelZoneName(mark.zoneId),
      cellSize,
      selectedCells,
      label: mark.label,
      description: mark.description,
      editingMarkId: mark.id,
      error: selectedCells.length === 0 ? '该标记缺少可恢复的网格数据' : null
    };
    dispatchModeChange();
  };

  const removePendingMark = (markId: string) => {
    const win = options.activeWindow.value;
    if (!win) return;
    win.pendingSpatialMarks = win.pendingSpatialMarks.filter(mark => mark.id !== markId);
    if (win.spatialMarkDraft?.editingMarkId === markId) {
      win.spatialMarkDraft = null;
      dispatchModeChange();
    }
  };

  const updatePendingMark = (markId: string, updates: Pick<SpatialMark, 'label' | 'description'>) => {
    const mark = pendingSpatialMarks.value.find(item => item.id === markId);
    if (!mark) return;
    mark.label = updates.label.trim();
    mark.description = updates.description.trim();
  };

  const clearPendingSpatialMarks = (windowId?: string) => {
    const targetId = windowId || options.activeWindowId.value;
    const win = options.windows.value.find(item => item.id === targetId);
    if (!win) return;
    win.pendingSpatialMarks = [];
  };

  const handleSelectionChange = (event: Event) => {
    const draft = activeDraft.value;
    if (!draft) return;

    const detail = (event as CustomEvent).detail || {};
    if (!Array.isArray(detail.selectedCells)) return;

    draft.selectedCells = cloneCells(detail.selectedCells);
    draft.error = null;
  };

  window.addEventListener('bimcanvas:spatial-mark-selection-change', handleSelectionChange as EventListener);

  watch(
    () => [
      options.activeWindowId.value,
      activeDraft.value?.zoneId,
      activeDraft.value?.editingMarkId,
      activeDraft.value?.cellSize,
      activeDraft.value?.selectedCells.map(cell => `${cell.col}:${cell.row}`).join(',')
    ],
    () => dispatchModeChange()
  );

  watch(topLevelZones, () => {
    const draft = activeDraft.value;
    if (!draft) {
      dispatchModeChange();
      return;
    }

    if (topLevelZones.value.length === 0) {
      cancelSpatialMarking();
      return;
    }

    draft.zoneName = 'All spaces';
    dispatchModeChange();
  });

  onUnmounted(() => {
    window.removeEventListener('bimcanvas:spatial-mark-selection-change', handleSelectionChange as EventListener);
    window.dispatchEvent(new CustomEvent('bimcanvas:spatial-mark-mode-change', {
      detail: { active: false }
    }));
  });

  return {
    activeDraft,
    draftScopeDisplayText,
    pendingSpatialMarks,
    topLevelZones,
    isSpatialMarking,
    startSpatialMarking,
    cancelSpatialMarking,
    setDraftCellSize,
    setDraftLabel,
    setDraftDescription,
    setSelectedCells,
    clearDraftSelection,
    completeDraft,
    editPendingMark,
    removePendingMark,
    updatePendingMark,
    clearPendingSpatialMarks
  };
}
