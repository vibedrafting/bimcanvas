<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue';
import type { ToolCall } from '../../types/agent';

const props = defineProps<{
  toolCalls: ToolCall[];
}>();

// 默认展开
const isExpanded = ref(true);
const toggleExpand = () => { isExpanded.value = !isExpanded.value; };

// === 实时计时器逻辑 ===
const elapsedSeconds = ref(0);
let timerInterval: ReturnType<typeof setInterval> | null = null;

const hasRunningTools = computed(() =>
  props.toolCalls.some(tc => tc.status === 'running')
);

const startTimer = () => {
  if (timerInterval) return;
  const firstTool = props.toolCalls[0];
  if (firstTool?.startTime) {
    elapsedSeconds.value = Math.round((Date.now() - firstTool.startTime) / 1000);
  }
  timerInterval = setInterval(() => {
    const firstTool = props.toolCalls[0];
    if (firstTool?.startTime) {
      elapsedSeconds.value = Math.round((Date.now() - firstTool.startTime) / 1000);
    }
  }, 1000);
};

const stopTimer = () => {
  if (timerInterval) {
    clearInterval(timerInterval);
    timerInterval = null;
  }
};

watch(hasRunningTools, (isRunning) => {
  if (isRunning) {
    startTimer();
  } else {
    stopTimer();
  }
}, { immediate: true });

onUnmounted(() => stopTimer());

// 显示时间
const durationDisplay = computed(() => {
  if (hasRunningTools.value) {
    return `${elapsedSeconds.value}s`;
  }
  // 计算总耗时
  const first = props.toolCalls[0];
  const last = props.toolCalls[props.toolCalls.length - 1];
  if (first?.startTime && last?.endTime) {
    const duration = Math.round((last.endTime - first.startTime) / 1000);
    return `${duration}s`;
  }
  return null;
});

// 工具数量显示
const visibleToolCalls = computed(() => props.toolCalls.slice(0, 8));
const hiddenToolCallsCount = computed(() => Math.max(0, props.toolCalls.length - 8));

// === 工具详情提取 ===
const getToolDetail = (tc: ToolCall): string | null => {
  if (tc.description) return tc.description;

  const params = tc.params || {};
  switch (tc.toolName) {
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
};

// 状态计算
const panelStatus = computed(() => {
  if (props.toolCalls.some(tc => tc.status === 'running')) return 'running';
  if (props.toolCalls.some(tc => tc.status === 'failed')) return 'has-errors';
  if (props.toolCalls.every(tc => tc.status === 'completed')) return 'completed';
  return 'pending';
});

// 自动展开逻辑：运行中自动展开
watch(() => panelStatus.value, (newStatus) => {
  if (newStatus === 'running') {
    isExpanded.value = true;
  }
});
</script>

<template>
  <div class="main-agent-tools" :class="[panelStatus, { expanded: isExpanded }]" v-if="toolCalls.length > 0">
    <!-- Header -->
    <div class="panel-header" @click="toggleExpand">
      <div class="header-left">
        <div class="status-icon-container">
          <div v-if="panelStatus === 'running'" class="spinner-loader"></div>
          <svg v-else-if="panelStatus === 'completed'" class="icon-success" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <svg v-else-if="panelStatus === 'has-errors'" class="icon-error" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <circle cx="12" cy="12" r="10"></circle>
            <line x1="12" y1="8" x2="12" y2="12"></line>
            <line x1="12" y1="16" x2="12.01" y2="16"></line>
          </svg>
          <div v-else class="dot-pending"></div>
        </div>
        <span class="panel-title">Tool Calls</span>
        <span class="tools-count">{{ toolCalls.length }}</span>
        <span class="panel-time" v-if="durationDisplay">{{ durationDisplay }}</span>
      </div>
      <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <polyline points="6 9 12 15 18 9"></polyline>
      </svg>
    </div>

    <!-- Tool List -->
    <div class="panel-body" v-if="isExpanded">
      <div class="tool-list">
        <div
          class="tool-item"
          v-for="tc in visibleToolCalls"
          :key="tc.id"
          :class="tc.status"
        >
          <div class="tool-status-dot"></div>
          <div class="tool-content">
            <span class="tool-name">{{ tc.toolName }}</span>
            <span class="tool-args" v-if="getToolDetail(tc)">{{ getToolDetail(tc) }}</span>
            <span class="tool-error" v-if="tc.status === 'failed' && tc.error">
              {{ tc.error.length > 80 ? tc.error.slice(0, 80) + '...' : tc.error }}
            </span>
            <span class="tool-output-preview" v-else-if="tc.output && tc.status !== 'failed'">
              {{ tc.output.replace(/\n/g, ' ').slice(0, 40) }}...
            </span>
          </div>
        </div>

        <div class="tool-more" v-if="hiddenToolCallsCount > 0">
          <span>+{{ hiddenToolCallsCount }} more</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
/* --- Container --- */
.main-agent-tools {
  --card-bg: rgba(20, 20, 22, 0.4);
  --card-border: rgba(255, 255, 255, 0.06);
  --accent-color: var(--accent-primary);

  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 10px;
  margin: 8px 0;
  overflow: hidden;
  transition: all 0.2s ease;
  font-family: 'Inter', system-ui, sans-serif;

  &:hover {
    background: rgba(25, 25, 30, 0.6);
    border-color: rgba(255, 255, 255, 0.12);
  }

  &.running {
    border-color: rgba(var(--accent-primary-rgb), 0.2);
    background: rgba(var(--accent-primary-rgb), 0.02);
  }

  &.has-errors {
    border-color: rgba(248, 113, 113, 0.2);
  }
}

/* --- Header --- */
.panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  cursor: pointer;
  user-select: none;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.panel-title {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-primary);
}

.tools-count {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--text-tertiary);
  background: rgba(255, 255, 255, 0.06);
  padding: 2px 6px;
  border-radius: 4px;
}

.panel-time {
  font-size: 0.7rem;
  color: var(--text-tertiary);
  font-family: var(--font-mono);
  opacity: 0.8;
}

.chevron {
  width: 14px;
  height: 14px;
  color: var(--text-tertiary);
  transition: transform 0.2s;
  opacity: 0.5;
  transform: rotate(-90deg);
}

.expanded .chevron { transform: rotate(0deg); }

/* Icons */
.status-icon-container {
  width: 16px;
  height: 16px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.spinner-loader {
  width: 12px;
  height: 12px;
  border: 1.5px solid rgba(255, 255, 255, 0.1);
  border-top-color: var(--accent-color);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

.icon-success { color: var(--accent-green, #4ade80); width: 16px; height: 16px; }
.icon-error { color: var(--accent-danger, #f87171); width: 16px; height: 16px; }
.dot-pending { width: 6px; height: 6px; background: var(--text-tertiary); border-radius: 50%; opacity: 0.5; }

/* --- Body --- */
.panel-body {
  border-top: 1px solid var(--card-border);
  background: rgba(0, 0, 0, 0.15);
  padding: 6px 0;
}

/* Tool List */
.tool-list {
  display: flex;
  flex-direction: column;
}

.tool-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 5px 14px;
  font-size: 0.75rem;
  height: 26px;
}

.tool-status-dot {
  width: 4px;
  height: 4px;
  border-radius: 50%;
  background: var(--text-tertiary);
  opacity: 0.5;
  flex-shrink: 0;
}

.tool-item.running .tool-status-dot {
  background: var(--accent-color);
  opacity: 1;
  animation: pulse-glow 1.5s ease-in-out infinite;
}

.tool-item.completed .tool-status-dot { background: var(--accent-green, #4ade80); opacity: 0.8; }
.tool-item.failed .tool-status-dot { background: var(--accent-danger, #f87171); }

.tool-content {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
  overflow: hidden;
}

.tool-name {
  color: var(--accent-color);
  font-family: var(--font-mono);
  font-weight: 500;
  white-space: nowrap;
  flex-shrink: 0;
}

.tool-args {
  color: var(--text-secondary);
  opacity: 0.8;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 20px;
}

// 展开状态下显示完整路径
.expanded .tool-args {
  white-space: normal;
  overflow: visible;
  text-overflow: clip;
  word-break: break-all;
}

.tool-error {
  color: var(--accent-danger, #f87171);
  font-size: 0.7rem;
  opacity: 0.9;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  min-width: 20px;
}

.tool-output-preview {
  color: var(--text-tertiary);
  font-family: var(--font-mono);
  font-size: 0.7rem;
  opacity: 0.6;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 120px;
  flex-shrink: 0;
}

.tool-more {
  padding: 2px 14px 4px;
  font-size: 0.7rem;
  color: var(--text-tertiary);
  opacity: 0.6;
  font-style: italic;
}

@keyframes spin { to { transform: rotate(360deg); } }

@keyframes pulse-glow {
  0%, 100% {
    opacity: 1;
    box-shadow: 0 0 4px var(--accent-color);
    transform: scale(1);
  }
  50% {
    opacity: 0.6;
    box-shadow: 0 0 8px var(--accent-color), 0 0 12px var(--accent-color);
    transform: scale(1.3);
  }
}
</style>
