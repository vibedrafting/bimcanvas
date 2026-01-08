<script setup lang="ts">
import { computed } from 'vue';
import type { WaitingState } from '../../types/agent';

const props = defineProps<{
  state: WaitingState;
}>();

// 将等待词拆分为单个字符数组
const verbChars = computed(() => {
  return (props.state.waitingVerb || 'Processing').split('');
});
</script>

<template>
  <div class="waiting-indicator" v-if="state.isWaiting">
    <span class="generating-text">
      <span
        v-for="(char, i) in verbChars"
        :key="i"
        class="char"
        :style="{ animationDelay: (i * 0.05) + 's' }"
      >{{ char }}</span>
    </span>
    <span class="dot">.</span>
    <span class="dot">.</span>
    <span class="dot">.</span>
  </div>
</template>

<style scoped lang="scss">
.waiting-indicator {
  display: flex;
  align-items: center;
  font-size: 0.8rem;
  color: var(--text-tertiary);
  font-style: italic;
  padding: 4px 0;
  margin-top: 4px;
}

.generating-text {
  display: inline;

  .char {
    display: inline;
    animation: text-breathe 1.5s ease-in-out infinite;
  }
}

.dot {
  animation: dot-fade 1.5s infinite;
  opacity: 0;

  &:nth-child(2) {
    animation-delay: 0.3s;
  }

  &:nth-child(3) {
    animation-delay: 0.6s;
  }

  &:nth-child(4) {
    animation-delay: 0.9s;
  }
}

@keyframes text-breathe {
  0%, 100% { opacity: 0.4; }
  50% { opacity: 1; }
}

@keyframes dot-fade {
  0% { opacity: 0; }
  50% { opacity: 1; }
  100% { opacity: 0; }
}
</style>
