<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';

interface Option {
  label: string;
  value: string | number;
  icon?: string;
  tags?: string[]; // Added tags support
}

interface Props {
  modelValue: string | number | null;
  options: Option[];
  placeholder?: string;
  width?: string;
}

const props = withDefaults(defineProps<Props>(), {
  placeholder: 'Select...',
  width: '160px'
});

const emit = defineEmits<{
  (e: 'update:modelValue', value: string | number): void;
}>();

const isOpen = ref(false);
const containerRef = ref<HTMLElement | null>(null);

const selectedOption = computed(() => 
  props.options.find(opt => opt.value === props.modelValue)
);

const toggleDropdown = () => {
  console.log('[GlassSelect] toggleDropdown, current isOpen:', isOpen.value);
  isOpen.value = !isOpen.value;
};

const selectOption = (option: Option) => {
  console.log('[GlassSelect] selectOption called:', option.label, option.value);
  emit('update:modelValue', option.value);
  console.log('[GlassSelect] emit update:modelValue done');
  isOpen.value = false;
};

const closeDropdown = (e: MouseEvent) => {
  if (containerRef.value && !containerRef.value.contains(e.target as Node)) {
    console.log('[GlassSelect] closeDropdown triggered by click outside');
    isOpen.value = false;
  }
};

onMounted(() => {
  document.addEventListener('click', closeDropdown);
});

onUnmounted(() => {
  document.removeEventListener('click', closeDropdown);
});
</script>

<template>
  <div class="glass-select-container" ref="containerRef" :style="{ width: width }">
    <!-- Trigger Button -->
    <button 
      class="select-trigger" 
      :class="{ active: isOpen }"
      @click="toggleDropdown"
    >
      <span class="selected-text" :class="{ placeholder: !selectedOption }">
        <span v-if="selectedOption?.icon" class="option-icon" v-html="selectedOption.icon"></span>
        {{ selectedOption ? selectedOption.label : placeholder }}
        <!-- Selected Tags (Optional: only show first tag if space permits, or hide in trigger) -->
        <!-- For now, we keep trigger clean, tags are mainly for selection context -->
      </span>
      <svg class="chevron" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </button>

    <!-- Dropdown Menu -->
    <transition name="dropdown">
      <div class="select-dropdown" v-if="isOpen">
        <div 
          v-for="option in options" 
          :key="option.value"
          class="select-option"
          :class="{ selected: modelValue === option.value }"
          @click="selectOption(option)"
        >
          <div class="option-main">
            <span v-if="option.icon" class="option-icon" v-html="option.icon"></span>
            <span class="option-label">{{ option.label }}</span>
          </div>
          
          <!-- Tags Display -->
          <div v-if="option.tags && option.tags.length > 0" class="option-tags">
            <span v-for="tag in option.tags" :key="tag" class="tag-badge">{{ tag }}</span>
          </div>

          <svg v-if="modelValue === option.value" class="check-icon" viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
        </div>
      </div>
    </transition>
  </div>
</template>

<style scoped lang="scss">
.glass-select-container {
  position: relative;
  display: inline-block;
}

.select-trigger {
  width: 100%;
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 12px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  color: var(--text-primary);
  font-family: var(--font-sans);
  font-size: 0.85rem;
  cursor: pointer;
  transition: all 0.2s;
  outline: none;

  &:hover {
    background: rgba(255, 255, 255, 0.08);
    border-color: var(--border-subtle);
  }

  &.active {
    background: rgba(255, 255, 255, 0.1);
    border-color: var(--accent-blue);
    
    .chevron {
      transform: rotate(180deg);
    }
  }
}

.selected-text {
  display: flex;
  align-items: center;
  gap: 8px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;

  &.placeholder {
    color: var(--text-secondary);
  }
}

.chevron {
  color: var(--text-secondary);
  transition: transform 0.3s var(--ease-spring);
  flex-shrink: 0;
  margin-left: 8px;
}

.select-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  width: 100%;
  min-width: 240px; /* Increased min-width for tags */
  background-color: var(--glass-bg-solid);
  border: var(--glass-border);
  border-radius: 8px;
  padding: 4px;
  box-shadow: var(--shadow-panel);
  z-index: 200;
  max-height: 300px;
  overflow-y: auto;
  
  /* Glare effect */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg-solid), var(--glass-bg-solid));
  background-origin: border-box;
  background-clip: padding-box, border-box;
}

.select-option {
  display: flex;
  align-items: center;
  justify-content: space-between; /* Space between label and tags/check */
  gap: 8px;
  padding: 8px 12px;
  color: var(--text-secondary);
  font-size: 0.85rem;
  cursor: pointer;
  border-radius: 4px;
  transition: all 0.2s;
  position: relative;

  &:hover {
    background: rgba(255, 255, 255, 0.05);
    color: var(--text-primary);
  }

  &.selected {
    background: rgba(59, 130, 246, 0.15);
    color: var(--accent-blue);
  }
}

.option-main {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.option-tags {
  display: flex;
  gap: 4px;
  margin-right: 8px;
}

.tag-badge {
  font-size: 0.7rem;
  padding: 1px 6px;
  background: rgba(255, 255, 255, 0.1);
  border-radius: 4px;
  color: var(--text-secondary);
  white-space: nowrap;
}

.option-icon {
  font-size: 1rem;
  line-height: 1;
  display: flex;
  align-items: center;
}

.check-icon {
  flex-shrink: 0;
  color: var(--accent-blue);
}

/* Transitions */
.dropdown-enter-active,
.dropdown-leave-active {
  transition: all 0.2s var(--ease-spring);
  transform-origin: top center;
}

.dropdown-enter-from,
.dropdown-leave-to {
  opacity: 0;
  transform: translateY(-8px) scale(0.98);
}
</style>
