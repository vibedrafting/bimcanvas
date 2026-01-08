<script setup lang="ts">
import type { WaitingState } from '../../types/agent';

defineProps<{
  state: WaitingState;
}>();
</script>

<template>
  <div class="waiting-indicator" v-if="state.isWaiting">
    <div class="waiting-content">
      <div class="waiting-dots">
        <span class="dot"></span>
        <span class="dot"></span>
        <span class="dot"></span>
      </div>
      <span class="waiting-verb">{{ state.waitingVerb }}...</span>
    </div>
  </div>
</template>

<style scoped lang="scss">
.waiting-indicator {
  display: flex;
  align-items: center;
  padding: 8px 0;
  min-height: 32px;
}

.waiting-content {
  display: flex;
  align-items: center;
  gap: 8px;
}

.waiting-dots {
  display: flex;
  align-items: center;
  gap: 3px;
}

.dot {
  width: 4px;
  height: 4px;
  background: var(--accent-primary);
  border-radius: 50%;
  animation: bounce 1.4s ease-in-out infinite both;

  &:nth-child(1) {
    animation-delay: -0.32s;
  }

  &:nth-child(2) {
    animation-delay: -0.16s;
  }

  &:nth-child(3) {
    animation-delay: 0s;
  }
}

.waiting-verb {
  font-size: 0.85rem;
  color: var(--text-tertiary);
  font-style: italic;
  opacity: 0.8;
}

@keyframes bounce {
  0%, 80%, 100% {
    transform: scale(0.6);
    opacity: 0.5;
  }
  40% {
    transform: scale(1);
    opacity: 1;
  }
}
</style>
