import { computed, nextTick, onMounted, onUnmounted, ref } from 'vue';
import type { EffortLevel, ModelOption, ThinkingLevel } from '../../types/aiCommandCenter';
import { effortLevels, thinkingLevels } from '../../constants/aiCommandCenter';
import type { RuntimeCapabilityMap, RuntimeCapabilityMatrixRow } from '../../types/agent';

// 图层预设配置类型
export interface LayerPresetConfig {
  enabledLayers: string[];
}

export interface LayerPresetsConfig {
  human?: LayerPresetConfig;
  ai?: LayerPresetConfig;
}

export const useAgentConfig = (agentApiBase: string, serverApiBase: string) => {
  // 这三项当前前端没有对应 UI，因此天然满足 fallback：
  // hide-token-usage / disable-trace-export / keep-approval-ui-disabled
  // 若未来新增相关 UI，请统一走 hasFallback() 接入。
  const models = ref<ModelOption[]>([]);
  const currentModel = ref<ModelOption | null>(null);
  const defaultThinking: ThinkingLevel = thinkingLevels[0] ?? { id: 'off', label: 'Off' };
  const defaultEffort: EffortLevel = effortLevels[2] ?? { id: 'medium', label: 'Medium' };
  const currentThinking = ref<ThinkingLevel>(defaultThinking);
  const currentEffort = ref<EffortLevel>(defaultEffort); // 默认 "medium"
  const isModelMenuOpen = ref(false);
  const isThinkingMenuOpen = ref(false);
  const isEffortMenuOpen = ref(false);
  const isAddingModel = ref(false);
  const newModelId = ref('');
  const newModelInputRef = ref<HTMLInputElement | null>(null);
  const layerPresets = ref<LayerPresetsConfig>({});
  const capabilityMatrix = ref<RuntimeCapabilityMatrixRow[]>([]);
  const capabilityMap = ref<RuntimeCapabilityMap>({});
  const supportsThinking = ref(true);
  const supportsSubtaskCausality = ref(true);
  const supportsTrace = ref(false);
  const supportsUsage = ref(false);
  const supportsPermissionPauseResume = ref(false);
  const fallbackSet = computed(() => new Set(
    capabilityMatrix.value
      .map(row => row.frontendFallback)
      .filter((key): key is string => typeof key === 'string' && key.trim().length > 0)
  ));

  const isCapabilityEnabled = (capabilityKey: string): boolean => {
    const capability = capabilityMap.value[capabilityKey];
    return capability?.level === 'required' || capability?.level === 'optional';
  };

  const hasFallback = (key: string): boolean => fallbackSet.value.has(key);

  const applyCapabilityMatrix = (rows: RuntimeCapabilityMatrixRow[] | undefined) => {
    const normalizedRows = Array.isArray(rows) ? rows : [];
    capabilityMatrix.value = normalizedRows;
    capabilityMap.value = normalizedRows.reduce<RuntimeCapabilityMap>((map, row) => {
      map[row.capabilityKey] = row;
      return map;
    }, {});

    supportsThinking.value = isCapabilityEnabled('thinking');
    supportsSubtaskCausality.value = isCapabilityEnabled('subtask_causality');
    supportsTrace.value = isCapabilityEnabled('trace');
    supportsUsage.value = isCapabilityEnabled('usage');
    supportsPermissionPauseResume.value = isCapabilityEnabled('permission_pause_resume');

    if (!supportsThinking.value) {
      currentThinking.value = defaultThinking;
      isThinkingMenuOpen.value = false;
    }
  };

  const applyWebConfig = (webConfig: any, mode: 'replace' | 'merge' = 'replace') => {
    const incomingModels = webConfig.customModels || [];
    if (mode === 'replace') {
      models.value = incomingModels;
    } else {
      const merged = new Map(models.value.map(model => [model.id, model]));
      for (const model of incomingModels) {
        merged.set(model.id, model);
      }
      models.value = Array.from(merged.values());
    }
    layerPresets.value = webConfig.layerPresets || {};

    window.dispatchEvent(new CustomEvent('bimcanvas:layer-presets-loaded', {
      detail: layerPresets.value
    }));
  };

  const applyDefaultModel = (defaultModelId?: string) => {
    const normalized = defaultModelId?.trim();
    if (normalized) {
      let found = models.value.find(model => model.id === normalized);
      if (!found) {
        found = { id: normalized, label: normalized };
        models.value.push(found);
      }
      currentModel.value = found;
      return;
    }

    if (!currentModel.value && models.value.length > 0) {
      currentModel.value = models.value[0] ?? null;
    }
  };

  const saveCustomModels = async () => {
    try {
      await fetch(`${serverApiBase}/api/web_config`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ customModels: models.value })
      });
    } catch (error) {
      console.warn('保存模型列表失败:', error);
    }
  };

  const selectModel = (model: ModelOption) => {
    currentModel.value = model;
    isModelMenuOpen.value = false;
    isAddingModel.value = false;
  };

  const startAddModel = () => {
    isAddingModel.value = true;
    newModelId.value = '';
    nextTick(() => {
      newModelInputRef.value?.focus();
    });
  };

  const confirmAddModel = async () => {
    const id = newModelId.value.trim();
    if (id && !models.value.some(m => m.id === id)) {
      const newModel = { id, label: id };
      models.value.push(newModel);
      selectModel(newModel);
      await saveCustomModels();
    }
    cancelAddModel();
  };

  const cancelAddModel = () => {
    isAddingModel.value = false;
    newModelId.value = '';
  };

  const selectThinking = (level: ThinkingLevel) => {
    if (!supportsThinking.value) {
      currentThinking.value = defaultThinking;
      isThinkingMenuOpen.value = false;
      return;
    }
    currentThinking.value = level;
    isThinkingMenuOpen.value = false;
  };

  const selectEffort = (level: EffortLevel) => {
    currentEffort.value = level;
    isEffortMenuOpen.value = false;
  };

  const fetchAgentConfig = async () => {
    try {
      let webDefaultModel = '';
      const [configRes, webConfigRes] = await Promise.all([
        fetch(`${agentApiBase}/api/config`),
        fetch(`${serverApiBase}/api/web_config`)
      ]);

      if (webConfigRes.ok) {
        const webConfig = await webConfigRes.json();
        webDefaultModel = typeof webConfig.defaultModel === 'string' ? webConfig.defaultModel : '';
        applyWebConfig(webConfig, 'replace');
        console.log('图层预设配置已加载:', layerPresets.value);
      }

      if (configRes.ok) {
        const config = await configRes.json();
        const {
          models: agentModels,
          defaultEffort: cfgEffort,
          defaultThinking: cfgThinking,
          capabilityMatrix: cfgCapabilityMatrix
        } = config;

        // Agent 返回了 models → 用作主模型列表（优先于 web_config 的 customModels）
        if (agentModels && agentModels.length > 0) {
          const agentModelIds = new Set(agentModels.map((m: { id: string }) => m.id));
          const extraModels = models.value.filter(m => !agentModelIds.has(m.id));
          models.value = [...agentModels, ...extraModels];
        }

        applyCapabilityMatrix(cfgCapabilityMatrix);

        if (cfgEffort) {
          const foundEffort = effortLevels.find(e => e.id === cfgEffort);
          if (foundEffort) {
            currentEffort.value = foundEffort;
          }
        }

        if (cfgThinking && supportsThinking.value) {
          const foundThinking = thinkingLevels.find(t => t.id === cfgThinking);
          if (foundThinking) {
            currentThinking.value = foundThinking;
          }
        }
      }

      applyDefaultModel(webDefaultModel);

      console.log('Agent 配置已加载:', {
        model: currentModel.value?.id,
        effort: currentEffort.value.id,
        thinking: currentThinking.value.id
      });
    } catch (error) {
      console.warn('获取 Agent 配置失败:', error);
    }
  };

  const handleWebConfigUpdated = (event: Event) => {
    const customEvent = event as CustomEvent;
    if (customEvent.detail) {
      applyWebConfig(customEvent.detail, 'merge');
    }
  };

  onMounted(() => {
    window.addEventListener('bimcanvas:web-config-updated', handleWebConfigUpdated as EventListener);
  });

  onUnmounted(() => {
    window.removeEventListener('bimcanvas:web-config-updated', handleWebConfigUpdated as EventListener);
  });

  return {
    models,
    currentModel,
    currentThinking,
    currentEffort,
    thinkingLevels,
    effortLevels,
    isModelMenuOpen,
    isThinkingMenuOpen,
    isEffortMenuOpen,
    isAddingModel,
    newModelId,
    newModelInputRef,
    layerPresets,
    capabilityMatrix,
    capabilityMap,
    hasFallback,
    supportsThinking,
    supportsSubtaskCausality,
    supportsTrace,
    supportsUsage,
    supportsPermissionPauseResume,
    fetchAgentConfig,
    selectModel,
    startAddModel,
    confirmAddModel,
    cancelAddModel,
    selectThinking,
    selectEffort,
    saveCustomModels
  };
};
