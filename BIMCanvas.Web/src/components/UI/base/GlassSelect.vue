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
  disabled?: boolean;
  variant?: 'glass' | 'solid';
}

const props = withDefaults(defineProps<Props>(), {
  placeholder: 'Select...',
  width: '160px',
  disabled: false,
  variant: 'glass'
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
  if (props.disabled) return;
  isOpen.value = !isOpen.value;
};

const selectOption = (option: Option) => {
  emit('update:modelValue', option.value);
  isOpen.value = false;
};

const closeDropdown = (e: MouseEvent) => {
  if (containerRef.value && !containerRef.value.contains(e.target as Node)) {
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
      :class="{ 
        active: isOpen, 
        disabled: disabled,
        'variant-solid': variant === 'solid'
      }"
      @click="toggleDropdown"
      :disabled="disabled"
    >
      <span class="selected-text" :class="{ placeholder: !selectedOption }">
        <span v-if="selectedOption?.icon" class="option-icon" v-html="selectedOption.icon"></span>
        {{ selectedOption ? selectedOption.label : placeholder }}
      </span>
      <svg class="chevron" viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </button>

    <!-- Dropdown Menu -->
    <transition name="dropdown">
      <div 
        class="select-dropdown" 
        v-if="isOpen && !disabled"
        :class="{ 'variant-solid': variant === 'solid' }"
      >
        <div 
          v-for="option in options" 
          :key="option.value"
          class="select-option"
          :class="{ selected: modelValue === option.value }"
          @click="selectOption(option)"
        >
          <!-- ... (option content remains same) -->
          <div class="option-main">
            <span v-if="option.icon" class="option-icon" v-html="option.icon"></span>
            <span class="option-label">{{ option.label }}</span>
          </div>
          
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
  transition: background-color 0.2s, border-color 0.2s, color 0.2s;
  outline: none;

  &:hover:not(.disabled) {
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

  &.disabled {
    opacity: 0.5;
    cursor: not-allowed;
    background: rgba(255, 255, 255, 0.01);
    
    &:hover {
      background: rgba(255, 255, 255, 0.01);
      border-color: rgba(255, 255, 255, 0.1);
    }
  }

  // Solid Variant
  &.variant-solid {
    background: #22262e;
    border-color: rgba(255, 255, 255, 0.1);
    
    &:hover:not(.disabled) {
      background: #2a2f38;
      border-color: rgba(255, 255, 255, 0.2);
    }

    &.active {
      background: #2a2f38;
      border-color: var(--accent-blue);
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
  min-width: 100%; /* At least as wide as trigger */
  width: max-content; /* Grow to fit content */
  max-width: 360px; /* Prevent excessive width */
  background-color: var(--glass-bg-solid);
  border: var(--glass-border);
  border-radius: 8px;
  padding: 4px;
  box-shadow: var(--shadow-panel);
  z-index: 200;
  max-height: 300px;
  overflow-y: auto;
  overflow-x: hidden;
  
  /* Glare effect */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg-solid), var(--glass-bg-solid));
  background-origin: border-box;
  background-clip: padding-box, border-box;

  // Solid Variant
  &.variant-solid {
    background: #22262e;
    border: 1px solid rgba(255, 255, 255, 0.1);
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
    background-image: none; // Remove glass glare
  }
}

.select-option {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
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
  min-width: 0; /* Enable truncation in flex child */
}

.option-label {
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.option-tags {
  display: flex;
  gap: 4px;
  flex-shrink: 0;
}

.tag-badge {
  font-size: 0.65rem;
  padding: 2px 6px;
  background: rgba(255, 255, 255, 0.08);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 4px;
  color: var(--text-secondary);
  white-space: nowrap;
  max-width: 120px;
  overflow: hidden;
  text-overflow: ellipsis;
  font-family: var(--font-mono, monospace);
}

.option-icon {
  font-size: 1rem;
  line-height: 1;
  display: flex;
  align-items: center;
  flex-shrink: 0;
}

.check-icon {
  flex-shrink: 0;
  color: var(--accent-blue);
  margin-left: 4px;
}

/* Transitions */
.dropdown-enter-active,
.dropdown-leave-active {
  transition: all 0.2s var(--ease-spring);
  transform-origin: top left;
}

.dropdown-enter-from,
.dropdown-leave-to {
  opacity: 0;
  transform: translateY(-8px) scale(0.98);
}
</style>
