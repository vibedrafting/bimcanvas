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

      <!-- Tag Overlay -->
      <div class="tag-overlay">
        <span
          v-for="tag in (module.tags || [])"
          :key="tag"
          class="mini-tag"
        >{{ getTagLabel(tag) }}</span>
      </div>
    </div>

    <div class="name-area">
      <div class="card-name">{{ module.name }}</div>
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
  border-radius: 8px; /* Slightly tighter radius */
  overflow: hidden;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s, box-shadow 0.15s;
  background: rgba(0, 0, 0, 0.2); /* Slightly darker bg */

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
  position: relative; /* Context for overlay */
  flex-shrink: 0;
  width: 100%;
  aspect-ratio: 1 / 1;
  display: flex;
  align-items: center;
  justify-content: center;
  background: #ffffff;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  overflow: hidden;
  contain: paint;

  img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: contain;
    object-position: center center;
    /* Add slight padding so image doesn't touch edges */
    padding: 4px;
    box-sizing: border-box; 
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

/* New Tag Overlay Styles */
.tag-overlay {
  position: absolute;
  bottom: 0;
  left: 0;
  right: 0;
  padding: 4px;
  display: flex;
  flex-wrap: wrap;
  gap: 2px;
  align-items: flex-end;
  pointer-events: none; /* Let clicks pass through to card */
  /* Optional gradient to make tags readable against complex images */
  background: linear-gradient(to top, rgba(0,0,0,0.4) 0%, transparent 100%);
}

.mini-tag {
  padding: 1px 4px;
  font-size: 0.56rem;
  border-radius: 4px;
  background: rgba(0, 0, 0, 0.65); /* Dark semi-transparent bg */
  backdrop-filter: blur(2px);
  color: rgba(255, 255, 255, 0.9);
  white-space: nowrap;
  line-height: 1.1;
  
  /* Make tags standout */
  border: 1px solid rgba(255, 255, 255, 0.15);

  &::before {
    content: ''; /* Remove # to save space in compact view, or keep if preferred */
    display: none;
  }
}

.name-area {
  flex-shrink: 0;
  height: 26px; /* Reduced height */
  padding: 0 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
}

.card-name {
  width: 100%;
  font-size: 0.7rem; /* Slightly smaller */
  color: var(--text-primary);
  text-align: center;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 1.2;
  opacity: 0.9;
}

/* Expanded state adjustments */
.module-card.expanded .name-area {
  height: 30px;
  padding: 0 6px;
}

.module-card.expanded .card-name {
  white-space: nowrap; /* Keep single line for tidiness, or normal if wrapping desired */
  /* If we want 2 lines, use: */
  /*
  white-space: normal;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  */
}
</style>
