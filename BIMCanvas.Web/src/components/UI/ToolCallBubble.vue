<script setup lang="ts">
import { ref, computed } from 'vue';
import type { ChatBubble } from '../../types/agent';

const props = defineProps<{
  bubble: ChatBubble;
}>();

// 展开/折叠状态
const isExpanded = ref(false);
const toggleExpand = () => { isExpanded.value = !isExpanded.value; };

// 状态计算
const statusClass = computed(() => {
  switch (props.bubble.status) {
    case 'streaming': return 'status-running';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
});

// 获取工具详情
const toolDetail = computed((): string | null => {
  if (props.bubble.toolDescription) return props.bubble.toolDescription;

  const params = props.bubble.toolParams || {};
  switch (props.bubble.toolName) {
    case 'Read':
    case 'Write':
    case 'Edit':
      return (params.file_path as string) || null;
    case 'Glob':
    case 'Grep':
      return (params.pattern as string) || null;
    case 'Bash': {
      const cmd = params.command as string;
      return cmd ? (cmd.length > 60 ? cmd.slice(0, 60) + '...' : cmd) : null;
    }
    case 'Task': {
      const desc = params.description as string;
      if (desc) return desc;
      const prompt = params.prompt as string;
      return prompt ? (prompt.length > 50 ? prompt.slice(0, 50) + '...' : prompt) : null;
    }
    default:
      return null;
  }
});
</script>

<template>
  <div class="tool-call-bubble" :class="[statusClass, { expanded: isExpanded }]">
    <!-- Header -->
    <div class="bubble-header" @click="toggleExpand">
      <div class="header-left">
        <div class="status-icon-container">
          <div v-if="bubble.status === 'streaming'" class="spinner-loader"></div>
          <svg v-else-if="bubble.status === 'completed'" class="icon-success" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <svg v-else-if="bubble.status === 'failed'" class="icon-error" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <circle cx="12" cy="12" r="10"></circle>
            <line x1="12" y1="8" x2="12" y2="12"></line>
            <line x1="12" y1="16" x2="12.01" y2="16"></line>
          </svg>
          <div v-else class="dot-pending"></div>
        </div>
        <span class="tool-name">{{ bubble.toolName }}</span>
        <span class="tool-detail" v-if="toolDetail">{{ toolDetail }}</span>
      </div>
      <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </div>

    <!-- Error display (always visible when failed) -->
    <div class="tool-error" v-if="bubble.status === 'failed' && bubble.toolError">
      {{ bubble.toolError.length > 100 ? bubble.toolError.slice(0, 100) + '...' : bubble.toolError }}
    </div>

    <!-- Expanded content -->
    <div class="bubble-body" v-if="isExpanded && bubble.toolOutput">
      <div class="tool-output">
        <pre>{{ bubble.toolOutput.slice(0, 500) }}{{ bubble.toolOutput.length > 500 ? '...' : '' }}</pre>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.tool-call-bubble {
  --card-bg: rgba(20, 20, 22, 0.4);
  --card-border: rgba(255, 255, 255, 0.06);
  --accent-color: var(--accent-primary);

  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 8px;
  margin: 6px 0;
  overflow: hidden;
  transition: all 0.2s ease;
  font-family: 'Inter', system-ui, sans-serif;

  &:hover {
    background: rgba(25, 25, 30, 0.6);
    border-color: rgba(255, 255, 255, 0.12);
  }

  &.status-running {
    border-color: rgba(var(--accent-primary-rgb), 0.2);
    background: rgba(var(--accent-primary-rgb), 0.02);
  }

  &.status-failed {
    border-color: rgba(248, 113, 113, 0.2);
  }
}

.bubble-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 10px;
  cursor: pointer;
  user-select: none;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.status-icon-container {
  width: 14px;
  height: 14px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.spinner-loader {
  width: 10px;
  height: 10px;
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-top-color: var(--accent-color);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.icon-success { color: var(--accent-green, #4ade80); width: 14px; height: 14px; }
.icon-error { color: var(--accent-danger, #f87171); width: 14px; height: 14px; }
.dot-pending { width: 5px; height: 5px; background: var(--text-tertiary); border-radius: 50%; opacity: 0.5; }

.tool-name {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--accent-color);
  font-family: var(--font-mono);
  white-space: nowrap;
  flex-shrink: 0;
}

.tool-detail {
  font-size: 0.75rem;
  color: var(--text-secondary);
  opacity: 0.8;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 20px;
}

.chevron {
  width: 12px;
  height: 12px;
  color: var(--text-tertiary);
  transition: transform 0.2s;
  opacity: 0.5;
  flex-shrink: 0;
}

.expanded .chevron { transform: rotate(180deg); }

.tool-error {
  padding: 6px 10px;
  font-size: 0.7rem;
  color: var(--accent-danger, #f87171);
  background: rgba(248, 113, 113, 0.05);
  border-top: 1px solid rgba(248, 113, 113, 0.1);
}

.bubble-body {
  border-top: 1px solid var(--card-border);
  background: rgba(0, 0, 0, 0.15);
  padding: 8px 10px;
}

.tool-output {
  pre {
    font-size: 0.7rem;
    font-family: var(--font-mono);
    color: var(--text-secondary);
    margin: 0;
    white-space: pre-wrap;
    word-break: break-all;
    max-height: 200px;
    overflow-y: auto;
  }
}

@keyframes spin { to { transform: rotate(360deg); } }
</style>
