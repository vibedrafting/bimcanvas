<script setup lang="ts">
import { computed } from 'vue';

interface Props {
  variant?: 'primary' | 'ghost' | 'danger';
  active?: boolean;
  disabled?: boolean;
  title?: string;
}

const props = withDefaults(defineProps<Props>(), {
  variant: 'ghost',
  active: false,
  disabled: false,
});

const emit = defineEmits<{
  (e: 'click', event: MouseEvent): void;
}>();

const classes = computed(() => {
  return [
    'glass-btn',
    `variant-${props.variant}`,
    { active: props.active }
  ];
});
</script>

<template>
  <button 
    :class="classes" 
    :disabled="disabled" 
    :title="title"
    @click="emit('click', $event)"
  >
    <slot></slot>
  </button>
</template>

<style scoped>
.glass-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-md);
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.3s var(--ease-spring);
  border: 1px solid transparent;
  outline: none;
  color: var(--text-primary);
  background: transparent;
  
  /* Glass Effect Base */
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
}

.glass-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  pointer-events: none;
}

/* Variants */

/* Ghost (Default) */
.glass-btn.variant-ghost {
  background: rgba(255, 255, 255, 0.03);
  border-color: transparent;
}

.glass-btn.variant-ghost:hover:not(:disabled) {
  background: rgba(255, 255, 255, 0.08);
  border-color: var(--border-subtle);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.glass-btn.variant-ghost:active:not(:disabled) {
  transform: translateY(0) scale(0.98);
}

.glass-btn.variant-ghost.active {
  background: rgba(255, 255, 255, 0.1);
  border-color: var(--border-subtle);
  color: #fff;
  box-shadow: var(--glass-inner-highlight);
}

/* Primary */
.glass-btn.variant-primary {
  background: rgba(59, 130, 246, 0.2);
  border-color: rgba(59, 130, 246, 0.4);
  color: #fff;
}

.glass-btn.variant-primary:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.3);
  border-color: var(--accent-blue);
  box-shadow: 0 0 15px var(--accent-glow);
  transform: translateY(-1px);
}

/* Danger */
.glass-btn.variant-danger {
  background: rgba(255, 107, 107, 0.1);
  border-color: rgba(255, 107, 107, 0.3);
  color: var(--accent-danger);
}

.glass-btn.variant-danger:hover:not(:disabled) {
  background: rgba(255, 107, 107, 0.2);
  border-color: var(--accent-danger);
  box-shadow: 0 0 10px var(--accent-danger-glow);
}
</style>
