import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';
import type { SpatialMark } from '../../types/aiCommandCenter';
import type { SentContextSnapshot } from '../../types/agent';

// 对象类型中文标签
const TYPE_LABELS: Record<string, string> = {
  module: '模块', wall: '墙体', column: '柱', door: '门', window: '窗', exclusion: '禁区', zone: '区域'
};

function getShortId(id: string): string {
  return id.length > 4 ? id.slice(-4) : id;
}

function getObjectLabel(obj: any): string {
  if (obj.type === 'module') return obj.moduleName ?? obj.moduleId ?? obj.id;
  if (obj.type === 'zone') return obj.name ?? obj.id;
  if (obj.type === 'exclusion') return obj.name ? `禁区: ${obj.name}` : `禁区 #${getShortId(obj.id)}`;
  const label = TYPE_LABELS[obj.type] ?? obj.type;
  const suffix = obj.elementId ? String(obj.elementId).slice(-4) : getShortId(obj.id);
  return `${label} #${suffix}`;
}

export function useSelectionContext() {
  const store = useCanvasStore();
  const { selectedObjects, projectData } = storeToRefs(store);

  // 1. 分离选中对象类型（全部 7 种）
  const selectedModules = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'module')
  );
  const selectedZones = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'zone')
  );
  const selectedWalls = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'wall')
  );
  const selectedColumns = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'column')
  );
  const selectedDoors = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'door')
  );
  const selectedWindows = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'window')
  );
  const selectedExclusions = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'exclusion')
  );
  const selectedCount = computed(() => selectedObjects.value.length);

  // 2. 模块显示（保留兼容）
  const selectedModuleCount = computed(() => selectedModules.value.length);
  const selectedModuleNames = computed(() =>
    selectedModules.value.map((m: any) => m.moduleName ?? m.moduleId ?? m.id)
  );

  // 3. 选中对象显示文本（混合所有类型）
  const selectionDisplayText = computed(() => {
    const objects = selectedObjects.value;
    if (objects.length === 0) return '';
    if (objects.length <= 3) return objects.map(getObjectLabel).join(', ');
    // >3 个时按类型汇总
    const counts: Record<string, number> = {};
    for (const obj of objects) {
      const label = TYPE_LABELS[obj.type] ?? obj.type;
      counts[label] = (counts[label] ?? 0) + 1;
    }
    return Object.entries(counts).map(([label, n]) => `${n} 个${label}`).join(', ');
  });

  // 4. Scope 推断（合并两个来源，仅用于 scope chip 显示）
  // 注意：建筑元件（wall/column/door/window）不参与区域推断
  const inferredZoneIds = computed(() => {
    const ids = new Set<string>();
    // 来源1：用户直接选中的 zone
    for (const z of selectedZones.value) {
      ids.add((z as any).id);
    }
    // 来源2：从选中模块的 zoneId 推断
    for (const m of selectedModules.value) {
      if ((m as any).zoneId) ids.add((m as any).zoneId);
    }
    return Array.from(ids);
  });

  const inferredZoneNames = computed(() => {
    const zones = projectData.value?.activeScheme?.zones || [];
    const roomZones = projectData.value?.computed?.roomZones || [];
    const allZones = [...zones, ...roomZones];
    return inferredZoneIds.value.map(id => {
      const zone = allZones.find(z => z.id === id);
      return zone?.name || id;
    });
  });

  const scopeDisplayText = computed(() => {
    const names = inferredZoneNames.value;
    if (names.length === 0) return '全局';
    if (names.length === 1) return names[0];
    if (names.length <= 2) return names.join(', ');
    return `${names.length} 个区域`;
  });

  // 5. 真实 zone 列表（供上下文菜单）
  const availableZones = computed(() => {
    const zones = projectData.value?.activeScheme?.zones || [];
    const roomZones = projectData.value?.computed?.roomZones || [];
    const allZones = [...zones, ...roomZones];
    return allZones.map(z => ({ id: z.id, label: z.name || z.id }));
  });

  // 6. 构建上下文 payload（智能：无选择时返回 undefined）
  const buildContextPayload = (spatialMarks: SpatialMark[] = []) => {
    if (selectedObjects.value.length === 0 && spatialMarks.length === 0) return undefined;

    const ctx: Record<string, any> = {};
    const modules = selectedModules.value;
    const walls = selectedWalls.value;
    const columns = selectedColumns.value;
    const doors = selectedDoors.value;
    const windows = selectedWindows.value;
    const exclusions = selectedExclusions.value;

    if (modules.length > 0) {
      const allZones = [
        ...(projectData.value?.activeScheme?.zones || []),
        ...(projectData.value?.computed?.roomZones || [])
      ];
      ctx.modules = modules.map((m: any) => {
        const zoneId = m.zoneId ?? null;
        let zoneName: string | null = null;
        if (zoneId) {
          const zone = allZones.find((z: any) => z.id === zoneId);
          zoneName = zone?.name ?? null;
        }
        return {
          id: m.id,
          name: m.moduleName ?? m.moduleId ?? m.id,
          zoneId,
          zoneName
        };
      });
    }
    // 直接选中的区域标签作为独立类别（不再使用 inferredZoneIds）
    if (selectedZones.value.length > 0) {
      ctx.zones = selectedZones.value.map((z: any) => {
        const entry: Record<string, any> = { id: z.id, name: z.name ?? z.id };
        // 方案变体上下文：designZoneId 用 parentZoneId ?? id（与 VariantNavigatorBar 同口径）；
        // variantInfoByDesignZone 命中即有变体，miss 即无变体（key 由 server IsDesignZoneId 校验过）。
        const designZoneId: string = z.parentZoneId ?? z.id;
        const info = store.variantInfoByDesignZone.get(designZoneId);
        if (info) {
          // activeSlug=null 表示画布当前停在 canonical（已采纳方案）槽；
          // displayedSlug 把它解析成「实际显示哪个 slug」，避免 AI 对 null 误判。
          const activeSlug = store.getActiveVariant(designZoneId);
          const displayedSlug = activeSlug
            ?? (info.hasAdopted ? info.adoptedSlug : (info.variantSlugs[0] ?? null));
          entry.variants = {
            designZoneId,
            slugs: info.variantSlugs,
            activeSlug,
            displayedSlug,
            hasAdopted: info.hasAdopted,
            adoptedSlug: info.adoptedSlug
          };
        }
        return entry;
      });
    }
    if (walls.length > 0) {
      ctx.walls = walls.map((w: any) => ({ id: w.id, elementId: w.elementId ?? null }));
    }
    if (columns.length > 0) {
      ctx.columns = columns.map((c: any) => ({ id: c.id, elementId: c.elementId ?? null, isStructural: c.isStructural ?? false }));
    }
    if (doors.length > 0) {
      ctx.doors = doors.map((d: any) => ({ id: d.id, elementId: d.elementId ?? null }));
    }
    if (windows.length > 0) {
      ctx.windows = windows.map((w: any) => ({ id: w.id, elementId: w.elementId ?? null }));
    }
    if (exclusions.length > 0) {
      ctx.exclusions = exclusions.map((e: any) => ({ id: e.id, name: e.name ?? null }));
    }
    if (spatialMarks.length > 0) {
      ctx.spatialMarks = spatialMarks.map(mark => ({
        id: mark.id,
        zoneId: mark.zoneId,
        label: mark.label,
        description: mark.description,
        geometry: mark.geometry
      }));
    }

    return Object.keys(ctx).length > 0 ? ctx : undefined;
  };

  // 7. 构建"用户消息气泡"上的轻量快照（仅 UI，独立于发给 Agent 的 payload）。
  //    复用 scopeDisplayText / selectionDisplayText，已含 ">3 按类型汇总" 的折叠逻辑。
  const buildContextSnapshot = (spatialMarks: SpatialMark[] = []): SentContextSnapshot | undefined => {
    if (selectedObjects.value.length === 0 && spatialMarks.length === 0) return undefined;

    // scopeDisplayText 在 strict 索引模式下推断含 undefined（names[0]），运行时不会发生；兜底"全局"以满足类型契约。
    const scopeText = scopeDisplayText.value ?? '全局';
    const snap: SentContextSnapshot = {
      scope: { text: scopeText, isGlobal: scopeText === '全局' }
    };

    if (selectedObjects.value.length > 0) {
      snap.selection = {
        text: selectionDisplayText.value,
        count: selectedCount.value
      };
    }

    if (spatialMarks.length > 0) {
      snap.spatialMarks = {
        count: spatialMarks.length,
        labels: spatialMarks.slice(0, 3).map(m => m.label).filter(Boolean)
      };
    }

    return snap;
  };

  return {
    // 现有（保留兼容）
    selectedModules,
    selectedZones,
    selectedModuleCount,
    selectedModuleNames,
    selectionDisplayText,
    inferredZoneIds,
    inferredZoneNames,
    scopeDisplayText,
    availableZones,
    buildContextPayload,
    // 新增
    selectedCount,
    selectedWalls,
    selectedColumns,
    selectedDoors,
    selectedWindows,
    selectedExclusions,
    buildContextSnapshot,
  };
}
