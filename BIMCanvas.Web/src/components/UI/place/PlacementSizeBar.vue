<!--
  PlacementSizeBar — PlaceTool 期间注入到 PromptBar 的"工具选项条"。

  挂载条件：MainLayout 用 v-if="canvasStore.currentOperation === 'placing'" 控制
  数据流：
    - 读取 store.placementSize（PlaceTool 在 activate 时写入）
    - 反查 moduleLibraryService.getModuleById 拿到 ModuleDefinition + morphology
    - 用户调整时 → store.setPlacementSize(...) → PlaceTool watch 重画预览
-->

<script setup lang="ts">
import { computed } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../../stores/canvasStore';
import { moduleLibraryService } from '../../../services/ModuleLibraryService';
import {
  resolveDimensionMode,
  clampDimension
} from '../../../utils/moduleSize';
import ModuleSizeEditor from '../property/ModuleSizeEditor.vue';

const store = useCanvasStore();
const { placementSize } = storeToRefs(store);

const moduleDef = computed(() => {
  const id = placementSize.value?.moduleId;
  return id ? moduleLibraryService.getModuleById(id) : undefined;
});

const widthMode = computed(() =>
  resolveDimensionMode(
    moduleDef.value?.morphology,
    'width',
    moduleDef.value?.size.width ?? 0
  )
);

const depthMode = computed(() =>
  resolveDimensionMode(
    moduleDef.value?.morphology,
    'depth',
    moduleDef.value?.size.depth ?? 0
  )
);

const currentWidth = computed(() => placementSize.value?.width ?? 0);
const currentDepth = computed(() => placementSize.value?.depth ?? 0);

// fixed 模块两个维度都是 readonly → 整条 bar 不渲染（只显 PromptBar 默认提示）
const hasAnyEditable = computed(
  () => widthMode.value.mode !== 'readonly' || depthMode.value.mode !== 'readonly'
);

const onWidthChange = (next: number) => {
  if (!placementSize.value) return;
  const clamped = clampDimension(next, widthMode.value);
  store.setPlacementSize({
    moduleId: placementSize.value.moduleId,
    width: clamped,
    depth: placementSize.value.depth
  });
};

const onDepthChange = (next: number) => {
  if (!placementSize.value) return;
  const clamped = clampDimension(next, depthMode.value);
  store.setPlacementSize({
    moduleId: placementSize.value.moduleId,
    width: placementSize.value.width,
    depth: clamped
  });
};
</script>

<template>
  <div v-if="placementSize && hasAnyEditable" class="placement-size-bar">
    <ModuleSizeEditor
      label="Width"
      :value="currentWidth"
      :mode="widthMode"
      @update:value="onWidthChange"
    />
    <ModuleSizeEditor
      label="Depth"
      :value="currentDepth"
      :mode="depthMode"
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
