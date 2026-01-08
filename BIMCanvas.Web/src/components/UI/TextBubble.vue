<script setup lang="ts">
import { computed } from 'vue';
import type { ChatBubble } from '../../types/agent';

const props = defineProps<{
  bubble: ChatBubble;
}>();

// 计算气泡状态类
const statusClass = computed(() => {
  switch (props.bubble.status) {
    case 'streaming': return 'status-streaming';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
});

// 检查是否有内容
const hasContent = computed(() => {
  return props.bubble.content && props.bubble.content.trim().length > 0;
});
</script>

<template>
  <div class="text-bubble" :class="statusClass" v-if="hasContent">
    <div class="text-content">{{ bubble.content }}</div>
  </div>
</template>

<style scoped lang="scss">
.text-bubble {
  line-height: 1.6;
  color: var(--text-primary);
  font-size: 0.9rem;

  &.status-failed {
    color: var(--accent-danger, #f87171);
  }

  &.status-streaming {
    .text-content {
      display: inline;
    }
  }
}

.text-content {
  white-space: pre-wrap;
  word-break: break-word;
}
</style>
