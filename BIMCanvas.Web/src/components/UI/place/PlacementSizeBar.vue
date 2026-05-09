<!--
  PlacementSizeBar — PlaceTool 期间注入到 PromptBar 的"工具选项条"。

  挂载条件：MainLayout 用 v-if="canvasStore.currentOperation === 'placing'" 控制
  数据流：
    - 读取 store.placementSize（PlaceTool 在 activate 时写入）
    - 反查 moduleLibraryService.getModuleById 拿到 ModuleDefinition + morphology
    - 用户输入 → store.setPlacementSize(...) → PlaceTool watch 重画预览
  策略：
    - 始终显示宽/深两行（含 fixed 模块也允许调整）
    - 不强制 clamp；推荐范围用灰色文本提示
-->

<script setup lang="ts">
import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../../stores/canvasStore';
import { moduleLibraryService } from '../../../services/ModuleLibraryService';
import { formatSizeHint } from '../../../utils/moduleSize';
import ModuleSizeEditor from '../property/ModuleSizeEditor.vue';

const store = useCanvasStore();
const { placementSize } = storeToRefs(store);

const moduleDef = computed(() => {
  const id = placementSize.value?.moduleId;
  return id ? moduleLibraryService.getModuleById(id) : undefined;
});

const widthHint = computed(() =>
  formatSizeHint(moduleDef.value?.morphology, 'width', moduleDef.value?.size.width ?? 0)
);

const depthHint = computed(() =>
  formatSizeHint(moduleDef.value?.morphology, 'depth', moduleDef.value?.size.depth ?? 0)
);

const currentWidth = computed(() => placementSize.value?.width ?? 0);
const currentDepth = computed(() => placementSize.value?.depth ?? 0);

const onWidthChange = (next: number) => {
  if (!placementSize.value) return;
  store.setPlacementSize({
    moduleId: placementSize.value.moduleId,
    width: next,
    depth: placementSize.value.depth
  });
};

const onDepthChange = (next: number) => {
  if (!placementSize.value) return;
  store.setPlacementSize({
    moduleId: placementSize.value.moduleId,
    width: placementSize.value.width,
    depth: next
  });
};
</script>

<template>
  <div v-if="placementSize" class="placement-size-bar">
    <ModuleSizeEditor
      label="Width"
      :value="currentWidth"
      :hint="widthHint"
      @update:value="onWidthChange"
    />
    <ModuleSizeEditor
      label="Depth"
      :value="currentDepth"
      :hint="depthHint"
      @update:value="onDepthChange"
    />
  </div>
</template>

<style scoped>
.placement-size-bar {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
</style>
