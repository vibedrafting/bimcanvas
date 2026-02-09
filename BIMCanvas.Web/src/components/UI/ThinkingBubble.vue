<script setup lang="ts">
import type { ChatBubble } from '../../types/agent';
import MarkdownText from './base/MarkdownText.vue';

const props = defineProps<{
  bubble: ChatBubble;
}>();

const toggle = () => {
  props.bubble.isExpanded = !props.bubble.isExpanded;
};
</script>

<template>
  <div class="thinking-bubble">
    <div class="thinking-header" @click="toggle">
      <svg
        class="thinking-chevron"
        :class="{ expanded: bubble.isExpanded }"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
      >
        <polyline points="9 18 15 12 9 6"></polyline>
      </svg>
      <span class="thinking-label">
        <template v-if="bubble.status === 'streaming'">
          Thinking for {{ bubble.thinkingDuration || '0s' }}<span class="dot">.</span><span class="dot">.</span><span class="dot">.</span>
        </template>
        <template v-else>
          Thought for {{ bubble.thinkingDuration || '0s' }}
        </template>
      </span>
    </div>
    <transition name="thinking-expand">
      <div v-show="bubble.isExpanded" class="thinking-content">
        <MarkdownText :content="bubble.content || ''" />
      </div>
    </transition>
  </div>
</template>

<style scoped lang="scss">
.thinking-bubble {
  margin-bottom: 4px;
}

.thinking-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 0;
  cursor: pointer;
  user-select: none;
  opacity: 0.7;
  transition: opacity 0.2s;

  &:hover {
    opacity: 1;
  }
}

.thinking-label {
  font-size: 0.8rem;
  color: var(--text-tertiary);
  font-style: italic;

  .dot {
    animation: dot-fade 1.5s infinite;
    opacity: 0;
  }
  .dot:nth-child(1) { animation-delay: 0.0s; }
  .dot:nth-child(2) { animation-delay: 0.5s; }
  .dot:nth-child(3) { animation-delay: 1.0s; }
}

.thinking-chevron {
  width: 14px;
  height: 14px;
  color: var(--text-tertiary);
  transition: transform 0.2s;

  &.expanded {
    transform: rotate(90deg);
  }
}

.thinking-content {
  padding: 6px 10px;
  color: var(--text-secondary);
  line-height: 1.4;
  font-size: 0.8rem;
  border-left: 2px solid var(--border-dim);
  margin-left: 6px;
  margin-top: 2px;
  margin-bottom: 6px;
  background: rgba(0, 0, 0, 0.2);
  border-radius: 0 8px 8px 0;
}

@keyframes dot-fade {
  0%, 100% { opacity: 0; }
  50% { opacity: 1; }
}

.thinking-expand-enter-active,
.thinking-expand-leave-active {
  transition: all 0.2s ease;
  max-height: 200px;
  overflow: hidden;
}

.thinking-expand-enter-from,
.thinking-expand-leave-to {
  opacity: 0;
  max-height: 0;
}
</style>
