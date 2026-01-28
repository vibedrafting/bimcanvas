import { nextTick, ref } from 'vue';
import type { ModelOption, ThinkingLevel } from '../../types/aiCommandCenter';
import { thinkingLevels } from '../../constants/aiCommandCenter';

export const useAgentConfig = (agentApiBase: string) => {
  const models = ref<ModelOption[]>([]);
  const currentModel = ref<ModelOption | null>(null);
  const currentThinking = ref<ThinkingLevel>(thinkingLevels[0]);
  const isModelMenuOpen = ref(false);
  const isThinkingMenuOpen = ref(false);
  const isAddingModel = ref(false);
  const newModelId = ref('');
  const newModelInputRef = ref<HTMLInputElement | null>(null);

  const saveCustomModels = async () => {
    try {
      await fetch(`${agentApiBase}/api/web_config`, {
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
    currentThinking.value = level;
    isThinkingMenuOpen.value = false;
  };

  const fetchAgentConfig = async () => {
    try {
      const [configRes, webConfigRes] = await Promise.all([
        fetch(`${agentApiBase}/api/config`),
        fetch(`${agentApiBase}/api/web_config`)
      ]);

      if (webConfigRes.ok) {
        const webConfig = await webConfigRes.json();
        models.value = webConfig.customModels || [];
      }

      if (configRes.ok) {
        const config = await configRes.json();
        const { model: defaultModel, thinkingLevel: defaultThinking } = config;

        if (defaultModel) {
          let found = models.value.find(m => m.id === defaultModel);
          if (!found) {
            found = { id: defaultModel, label: defaultModel };
            models.value.push(found);
          }
          currentModel.value = found;
        } else if (models.value.length > 0) {
          currentModel.value = models.value[0];
        }

        if (defaultThinking) {
          const foundThinking = thinkingLevels.find(t => t.id === defaultThinking);
          if (foundThinking) {
            currentThinking.value = foundThinking;
          }
        }
      }

      console.log('Agent 配置已加载:', { model: currentModel.value?.id, thinking: currentThinking.value.id });
    } catch (error) {
      console.warn('获取 Agent 配置失败:', error);
    }
  };

  return {
    models,
    currentModel,
    currentThinking,
    thinkingLevels,
    isModelMenuOpen,
    isThinkingMenuOpen,
    isAddingModel,
    newModelId,
    newModelInputRef,
    fetchAgentConfig,
    selectModel,
    startAddModel,
    confirmAddModel,
    cancelAddModel,
    selectThinking,
    saveCustomModels
  };
};
