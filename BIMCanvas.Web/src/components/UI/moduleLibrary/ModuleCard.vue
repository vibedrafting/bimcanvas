<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import type { PropType } from 'vue';
import type { ModuleDefinition } from '../../../services/ModuleLibraryService';

const props = defineProps({
  module: {
    type: Object as PropType<ModuleDefinition>,
    required: true
  },
  expanded: {
    type: Boolean,
    required: true
  },
  svgUrl: {
    type: String,
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

const imageLoadFailed = ref(false);

watch(
  () => props.module.id,
  () => {
    imageLoadFailed.value = false;
  }
);

const tooltip = computed(() => {
  const lines = [props.module.name, `${props.module.size.width} × ${props.module.size.depth} mm`];
  if (props.module.description) {
    lines.push(props.module.description);
  }
  return lines.join('\n');
});

const onImageError = () => {
  imageLoadFailed.value = true;
};

const onSelect = () => {
  emit('select', props.module);
};
</script>

<template>
  <div
    class="module-card"
    :class="{ expanded }"
    :title="tooltip"
    @click="onSelect"
  >
    <div class="thumbnail-area">
      <img
        v-if="!imageLoadFailed"
        :src="svgUrl"
        :alt="module.name"
        @error="onImageError"
      />
      <div v-else class="thumbnail-fallback">{{ module.name }}</div>
    </div>

    <div class="name-area">
      <div class="card-name">{{ module.name }}</div>
    </div>

    <div v-if="expanded" class="tag-area">
      <div class="card-tags">
        <span
          v-for="tag in (module.tags || [])"
          :key="tag"
          class="mini-tag"
        >{{ getTagLabel(tag) }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.module-card {
  box-sizing: border-box;
  width: 96px;
  display: flex;
  flex-direction: column;
  border: 1px solid var(--border-subtle);
  border-radius: 10px;
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s, box-shadow 0.15s;
  background: rgba(0, 0, 0, 0.15);

  &:hover {
    border-color: var(--accent-primary, #00aaff);
    transform: translateY(-2px);
    box-shadow: 0 4px 12px rgba(0, 170, 255, 0.15);
  }

  &.expanded {
    width: 136px;
  }
}

.thumbnail-area {
  flex-shrink: 0;
  width: 100%;
  aspect-ratio: 1 / 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  border-bottom: 1px solid rgba(255, 255, 255, 0.16);
  overflow: hidden;
  contain: paint;

  img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: contain;
    object-position: center center;
    clip-path: inset(0);
  }
}

.thumbnail-fallback {
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.62rem;
  color: #2d3e50;
  text-align: center;
  padding: 4px;
}

.name-area {
  flex-shrink: 0;
  height: 32px;
  padding: 0 6px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.card-name {
  width: 100%;
  font-size: 0.72rem;
  color: var(--text-primary);
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.2;
}

.module-card.expanded .name-area {
  height: 42px;
  padding: 4px 8px 2px;
}

.module-card.expanded .card-name {
  display: -webkit-box;
  -webkit-box-orient: vertical;
  -webkit-line-clamp: 2;
  white-space: normal;
  overflow: hidden;
  text-overflow: ellipsis;
}

.tag-area {
  height: 52px;
  padding: 2px 6px 8px;
}

.card-tags {
  height: 100%;
  display: flex;
  justify-content: center;
  align-content: flex-start;
  flex-wrap: wrap;
  gap: 4px;
  overflow: hidden;
}

.mini-tag {
  padding: 1px 6px;
  font-size: 0.62rem;
  border-radius: 8px;
  background: rgba(255, 255, 255, 0.08);
  color: var(--text-secondary);
  white-space: nowrap;
}
</style>
