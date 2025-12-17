<script setup lang="ts">
interface Props {
  label: string;
  icon?: string; // Optional icon class or name
}

defineProps<Props>();
</script>

<template>
  <div class="icon-badge">
    <span v-if="icon" class="icon">{{ icon }}</span>
    <span class="label">{{ label }}</span>
  </div>
</template>

<style scoped>
.icon-badge {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: 2px var(--spacing-sm);
  border-radius: var(--radius-sm);
  font-family: var(--font-sans);
  font-size: 0.75rem;
  font-weight: 500;
  letter-spacing: 0.5px;
  
  /* Style */
  background: rgba(59, 130, 246, 0.1);
  border: 1px solid rgba(59, 130, 246, 0.2);
  color: var(--accent-blue);
  
  /* Glow */
  box-shadow: 0 0 5px rgba(59, 130, 246, 0.05);
}

.icon {
  font-size: 1.1em;
}
</style>
