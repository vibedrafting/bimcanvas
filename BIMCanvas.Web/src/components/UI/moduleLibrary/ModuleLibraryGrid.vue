<script setup lang="ts">
import type { PropType } from 'vue';
import type { ModuleDefinition } from '../../../services/ModuleLibraryService';
import ModuleCard from './ModuleCard.vue';

const props = defineProps({
  modules: {
    type: Array as PropType<ModuleDefinition[]>,
    required: true
  },
  expanded: {
    type: Boolean,
    required: true
  },
  emptyText: {
    type: String,
    required: true
  },
  getSvgUrl: {
    type: Function as PropType<(moduleId: string) => Promise<string>>,
    required: true
  },
  getTagLabel: {
    type: Function as PropType<(tag: string) => string>,
    required: true
  }
});

const emit = defineEmits<{
  (e: 'select', module: ModuleDefinition): void;
}>();

const onSelect = (module: ModuleDefinition) => {
  emit('select', module);
};
</script>

<template>
  <div class="module-grid" :class="{ expanded }">
    <ModuleCard
      v-for="mod in modules"
      :key="mod.id"
      :module="mod"
      :expanded="expanded"
      :get-svg-url="getSvgUrl"
      :get-tag-label="getTagLabel"
      @select="onSelect"
    />

    <div v-if="modules.length === 0" class="empty-state-overlay">
      {{ emptyText }}
    </div>
  </div>
</template>

<style scoped lang="scss">
.module-grid {
  --grid-gap: 8px;
  display: grid;
  grid-template-columns: repeat(3, 96px);
  grid-auto-rows: max-content;
  gap: var(--grid-gap);
  padding: 12px;
  overflow-y: auto;
  flex: 1;
  align-content: start;
  justify-content: start;
  position: relative;

  &::-webkit-scrollbar {
    width: 4px;
  }

  &::-webkit-scrollbar-track {
    background: transparent;
  }

  &::-webkit-scrollbar-thumb {
    background: var(--border-strong);
    border-radius: 2px;
  }

  scrollbar-width: thin;
  scrollbar-color: rgba(255, 255, 255, 0.15) transparent;

  &.expanded {
    --grid-gap: 12px;
    grid-template-columns: repeat(8, 136px);
    gap: var(--grid-gap);
    padding: 14px;
  }
}

@media (max-width: 1240px) {
  .module-grid.expanded {
    grid-template-columns: repeat(7, 136px);
  }
}

@media (max-width: 1090px) {
  .module-grid.expanded {
    grid-template-columns: repeat(6, 136px);
  }
}

@media (max-width: 940px) {
  .module-grid.expanded {
    grid-template-columns: repeat(5, 136px);
  }
}

.empty-state-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--text-secondary);
  font-size: 0.85rem;
  pointer-events: none;
}
</style>
