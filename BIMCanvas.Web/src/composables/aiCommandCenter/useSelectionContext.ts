import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';

export function useSelectionContext() {
  const store = useCanvasStore();
  const { selectedObjects, projectData } = storeToRefs(store);

  // 1. 分离选中对象类型
  const selectedModules = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'module')
  );
  const selectedZones = computed(() =>
    selectedObjects.value.filter(obj => obj.type === 'zone')
  );

  // 2. 模块显示
  const selectedModuleCount = computed(() => selectedModules.value.length);
  const selectedModuleNames = computed(() =>
    selectedModules.value.map((m: any) => m.moduleName || m.moduleId || m.id)
  );
  const selectionDisplayText = computed(() => {
    const names = selectedModuleNames.value;
    if (names.length === 0) return '';
    if (names.length <= 3) return names.join(', ');
    return `${names.length} 个模块`;
  });

  // 3. Scope 推断（优先级：手动选中zone > 模块推断zone > 全局）
  const inferredZoneIds = computed(() => {
    // 优先：用户直接选中了 zone 标记
    if (selectedZones.value.length > 0) {
      return selectedZones.value.map((z: any) => z.id);
    }
    // 其次：从选中模块的 zoneId 推断
    const ids = new Set<string>();
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

  // 4. 真实 zone 列表（供上下文菜单）
  const availableZones = computed(() => {
    const zones = projectData.value?.activeScheme?.zones || [];
    const roomZones = projectData.value?.computed?.roomZones || [];
    const allZones = [...zones, ...roomZones];
    return allZones.map(z => ({ id: z.id, label: z.name || z.id }));
  });

  // 5. 构建上下文 payload（智能：无选择时返回 undefined）
  const buildContextPayload = () => {
    const modules = selectedModules.value;
    const zones = inferredZoneIds.value;
    if (modules.length === 0 && zones.length === 0) return undefined;

    const ctx: Record<string, any> = {};
    if (modules.length > 0) {
      ctx.modules = modules.map((m: any) => ({
        uid: m.uid || m.id,
        name: m.moduleName || m.moduleId || m.id
      }));
    }
    if (zones.length > 0) {
      ctx.zones = inferredZoneIds.value.map((id, i) => ({
        id,
        name: inferredZoneNames.value[i]
      }));
    }
    return ctx;
  };

  return {
    selectedModules,
    selectedZones,
    selectedModuleCount,
    selectedModuleNames,
    selectionDisplayText,
    inferredZoneIds,
    inferredZoneNames,
    scopeDisplayText,
    availableZones,
    buildContextPayload
  };
}
