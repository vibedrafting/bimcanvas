import { computed, onUnmounted, watch } from 'vue';
import type { Ref } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';
import { SpatialMarksService } from '../../services/SpatialMarksService';
import type {
  ChatWindow,
  GridSelectionCell,
  SpatialMark,
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
      const createdMarks: SpatialMark[] = [];
      for (const zone of topLevelZones.value) {
        const response = await SpatialMarksService.mergeGridSelection({
          zoneId: zone.id,
          cellSize: draft.cellSize,
          gridOriginX: 0,
          gridOriginY: 0,
          cells: cloneCells(draft.selectedCells)
        });

        if (!response.geometry || response.geometry.length === 0) {
          continue;
        }

        createdMarks.push({
          id: generateSpatialMarkId([...win.pendingSpatialMarks, ...createdMarks]),
          zoneId: zone.id,
          label: draft.label.trim(),
          description: draft.description.trim(),
          geometry: response.geometry
        });
      }

      if (createdMarks.length === 0) {
        draft.error = '标记区域为空或完全在设计区外';
        return;
      }

      win.pendingSpatialMarks.push(...createdMarks);
      win.spatialMarkDraft = createDraft(draft.cellSize);
      dispatchModeChange();
    } catch (error: any) {
      draft.error = error?.response?.data?.message || error?.message || '空间标记合并失败';
    } finally {
      if (draft) {
        draft.isCompleting = false;
      }
    }
  };

  const removePendingMark = (markId: string) => {
    const win = options.activeWindow.value;
    if (!win) return;
    win.pendingSpatialMarks = win.pendingSpatialMarks.filter(mark => mark.id !== markId);
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
    removePendingMark,
    updatePendingMark,
    clearPendingSpatialMarks
  };
}
