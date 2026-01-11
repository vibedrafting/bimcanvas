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
  padding: var(--spacing-sm, 8px) var(--spacing-md, 16px);
  border-radius: var(--radius-md, 8px);
  font-family: var(--font-sans);
  font-size: 0.9rem;
  font-weight: 500;
  cursor: pointer;
  
  /* ANTI-SHAKE: Only transition background-color to minimize rendering recalculations */
  transition: background-color 0.15s ease-out;
  
  /* ANTI-SHAKE: No backdrop-filter - it causes sub-pixel rendering shifts on hover */
  /* The parent panel already has glass blur effect */
  
  /* ANTI-SHAKE: Create isolated stacking context */
  isolation: isolate;
  
  /* Fixed border to prevent any border-related layout shifts */
  box-sizing: border-box;
  border: 1px solid transparent;
  outline: none;
  color: var(--text-primary);
  background: transparent;
}

.glass-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
  pointer-events: none;
}

/* Variants */

/* Ghost (Default) */
.glass-btn.variant-ghost {
  background: var(--btn-ghost-bg, transparent);
  border-color: transparent;
}

.glass-btn.variant-ghost:hover:not(:disabled) {
  background: var(--btn-ghost-bg-hover, rgba(255, 255, 255, 0.08));
  /* ANTI-SHAKE: No transform, no border-color change, no box-shadow change */
}

.glass-btn.variant-ghost:active:not(:disabled) {
  background: var(--btn-ghost-bg-hover, rgba(255, 255, 255, 0.12));
  /* ANTI-SHAKE: No transform */
}

.glass-btn.variant-ghost.active {
  background: var(--btn-ghost-bg-active, rgba(255, 255, 255, 0.1));
  border-color: var(--border-subtle);
  color: var(--btn-ghost-text-active, var(--text-primary));
}

/* Primary */
.glass-btn.variant-primary {
  background: rgba(59, 130, 246, 0.2);
  border-color: rgba(59, 130, 246, 0.4);
  color: #fff;
}

.glass-btn.variant-primary:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.35);
  /* ANTI-SHAKE: No transform */
}

/* Danger */
.glass-btn.variant-danger {
  background: rgba(255, 107, 107, 0.1);
  border-color: rgba(255, 107, 107, 0.3);
  color: var(--accent-danger);
}

.glass-btn.variant-danger:hover:not(:disabled) {
  background: rgba(255, 107, 107, 0.25);
  /* ANTI-SHAKE: No transform */
}
</style>
