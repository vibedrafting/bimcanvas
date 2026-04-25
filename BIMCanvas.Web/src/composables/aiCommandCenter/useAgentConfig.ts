import { computed, onMounted, onUnmounted, ref } from 'vue';
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
  const layerPresets = ref<LayerPresetsConfig>({});
  const capabilityMatrix = ref<RuntimeCapabilityMatrixRow[]>([]);
  const capabilityMap = ref<RuntimeCapabilityMap>({});
  const supportsThinking = ref(true);
  const supportsSubtaskCausality = ref(true);
  const supportsTrace = ref(false);
  const supportsUsage = ref(false);
  const supportsPermissionPauseResume = ref(false);
  const currentRuntime = ref<'claude' | 'openai'>('claude');
  const fallbackSet = computed(() => new Set(
    capabilityMatrix.value
      .filter(row => row.level === 'unsupported')
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

  const applyWebConfig = (webConfig: any) => {
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

  const selectModel = (model: ModelOption) => {
    currentModel.value = model;
    isModelMenuOpen.value = false;
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
      let agentDefaultModel = '';
      const [configRes, webConfigRes] = await Promise.all([
        fetch(`${agentApiBase}/api/config`),
        fetch(`${serverApiBase}/api/web_config`)
      ]);

      if (webConfigRes.ok) {
        const webConfig = await webConfigRes.json();
        applyWebConfig(webConfig);
        console.log('图层预设配置已加载:', layerPresets.value);
      }

      if (configRes.ok) {
        const config = await configRes.json();
        const runtimeId = config.runtime === 'openai' ? 'openai' : 'claude';
        const {
          models: agentModels,
          defaultModel: cfgDefaultModel,
          defaultEffort: cfgEffort,
          defaultThinking: cfgThinking,
          capabilityMatrix: cfgCapabilityMatrix
        } = config;
        currentRuntime.value = runtimeId;
        agentDefaultModel = typeof cfgDefaultModel === 'string' ? cfgDefaultModel : '';

        models.value = Array.isArray(agentModels) ? agentModels : [];

        applyCapabilityMatrix(cfgCapabilityMatrix);

        if (runtimeId === 'claude' && cfgEffort) {
          const foundEffort = effortLevels.find(e => e.id === cfgEffort);
          if (foundEffort) {
            currentEffort.value = foundEffort;
          }
        } else if (runtimeId === 'openai') {
          currentEffort.value = defaultEffort;
        }

        if (runtimeId === 'claude' && cfgThinking && supportsThinking.value) {
          const foundThinking = thinkingLevels.find(t => t.id === cfgThinking);
          if (foundThinking) {
            currentThinking.value = foundThinking;
          }
        } else if (runtimeId === 'openai') {
          currentThinking.value = defaultThinking;
        }
      }

      applyDefaultModel(agentDefaultModel);

      console.log('Agent 配置已加载:', {
        runtime: currentRuntime.value,
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
      applyWebConfig(customEvent.detail);
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
    layerPresets,
    capabilityMatrix,
    capabilityMap,
    hasFallback,
    supportsThinking,
    supportsSubtaskCausality,
    supportsTrace,
    supportsUsage,
    supportsPermissionPauseResume,
    currentRuntime,
    fetchAgentConfig,
    selectModel,
    selectThinking,
    selectEffort
  };
};
