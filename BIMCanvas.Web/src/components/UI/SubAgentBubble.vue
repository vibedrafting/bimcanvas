<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue';
import type { ChatBubble } from '../../types/agent';

const props = defineProps<{
  bubble: ChatBubble;
}>();

// Default: Expanded if running, collapsed if done
const isExpanded = ref(props.bubble.status === 'streaming');
let collapseTimer: ReturnType<typeof setTimeout> | null = null;

const clearCollapseTimer = () => {
  if (collapseTimer) {
    clearTimeout(collapseTimer);
    collapseTimer = null;
  }
};

const shouldKeepExpandedAfterCompletion = computed(() => {
  return Boolean(props.bubble.subAgentResult) || (props.bubble.childBubbles?.length || 0) > 0;
});

watch([() => props.bubble.status, shouldKeepExpandedAfterCompletion], ([newStatus, keepExpanded]) => {
  clearCollapseTimer();

  if (newStatus === 'completed' || newStatus === 'failed') {
    if (keepExpanded) {
      isExpanded.value = true;
    } else {
      collapseTimer = setTimeout(() => { isExpanded.value = false; }, 2000);
    }
  } else if (newStatus === 'streaming' || newStatus === 'background') {
    isExpanded.value = true;
  }
});

const toggleExpand = () => { isExpanded.value = !isExpanded.value; };

// 结果折叠状态（默认折叠）
const isResultExpanded = ref(false);
const toggleResult = () => { isResultExpanded.value = !isResultExpanded.value; };

watch(() => props.bubble.subAgentResult, (result) => {
  if (result) {
    clearCollapseTimer();
    isExpanded.value = true;
    isResultExpanded.value = true;
  }
}, { immediate: true });

// === 实时计时器逻辑 ===
const elapsedSeconds = ref(0);
let timerInterval: ReturnType<typeof setInterval> | null = null;

const startTimer = () => {
  if (timerInterval) return;
  if (props.bubble.timestamp) {
    elapsedSeconds.value = Math.round((Date.now() - props.bubble.timestamp) / 1000);
  }
  timerInterval = setInterval(() => {
    if (props.bubble.timestamp) {
      elapsedSeconds.value = Math.round((Date.now() - props.bubble.timestamp) / 1000);
    }
  }, 1000);
};

const stopTimer = () => {
  if (timerInterval) {
    clearInterval(timerInterval);
    timerInterval = null;
  }
};

watch(() => props.bubble.status, (newStatus, oldStatus) => {
  // [调试] 状态变化日志
  console.log(`[SubAgentBubble] status: ${oldStatus} → ${newStatus}`);

  if (newStatus === 'streaming' || newStatus === 'background') {
    startTimer();
  } else {
    stopTimer();
  }
}, { immediate: true });

onUnmounted(() => {
  clearCollapseTimer();
  stopTimer();
});

// 显示时间
const durationDisplay = computed(() => {
  if (props.bubble.status === 'streaming') {
    return `${elapsedSeconds.value}s`;
  } else if (props.bubble.status === 'background') {
    return `${elapsedSeconds.value}s (后台)`;
  }
  return null;
});

const statusClass = computed(() => {
  switch (props.bubble.status) {
    case 'streaming': return 'status-running';
    case 'background': return 'status-background';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
});

const typeLabel = computed(() => {
  const t = props.bubble.subAgentType || 'AGENT';
  return t === 'general-purpose' ? 'TASK' : String(t).toUpperCase();
});


// 工具列表展开状态
const isToolListExpanded = ref(false);

const totalToolsCount = computed(() => (props.bubble.childBubbles || []).length);

const visibleToolCalls = computed(() => {
  const all = props.bubble.childBubbles || [];
  if (isToolListExpanded.value) {
    return all;  // 展开时显示所有
  }
  return all.slice(0, 5);  // 折叠时只显示5个
});

const hiddenToolCallsCount = computed(() => {
  return isToolListExpanded.value ? 0 : Math.max(0, totalToolsCount.value - 5);
});

// 获取工具详情
const getToolDetail = (tc: ChatBubble): string | null => {
  if (tc.toolDescription) return tc.toolDescription;

  const params = tc.toolParams || {};
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

// 切换工具列表展开/折叠
const toggleToolList = () => {
  if (totalToolsCount.value > 5) {
    isToolListExpanded.value = !isToolListExpanded.value;
  }
};
</script>

<template>
  <div class="subagent-bubble" :class="[statusClass, { expanded: isExpanded }]">
    <!-- Header -->
    <div class="card-header" @click="toggleExpand">
      <!-- Row 1: Meta -->
      <div class="header-meta">
        <div class="meta-left">
          <span class="agent-type">{{ typeLabel }}</span>
          <span class="meta-divider" v-if="durationDisplay">·</span>
          <span class="agent-time" v-if="durationDisplay">{{ durationDisplay }}</span>
        </div>
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>

      <!-- Row 2: Status & Name -->
      <div class="header-main">
        <div class="status-icon-container">
          <div v-if="bubble.status === 'streaming'" class="spinner-loader"></div>
          <div v-else-if="bubble.status === 'background'" class="background-indicator">⏳</div>
          <svg v-else-if="bubble.status === 'completed'" class="icon-success" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <svg v-else-if="bubble.status === 'failed'" class="icon-error" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
          <div v-else class="dot-pending"></div>
        </div>
        <span class="agent-name">{{ bubble.subAgentName }}</span>
      </div>
    </div>

    <!-- Body -->
    <div class="card-body" v-if="isExpanded">
      <!-- Tool List -->
      <div class="tool-list" v-if="visibleToolCalls.length > 0">
        <div
          class="tool-item"
          v-for="tc in visibleToolCalls"
          :key="tc.id"
          :class="tc.status === 'streaming' ? 'running' : tc.status"
        >
          <div class="tool-status-dot"></div>
          <div class="tool-content">
            <span class="tool-name">{{ tc.toolName }}</span>
            <span class="tool-args" v-if="getToolDetail(tc)">{{ getToolDetail(tc) }}</span>
            <span class="tool-error" v-if="tc.status === 'failed' && tc.toolError">
              {{ tc.toolError.length > 50 ? tc.toolError.slice(0, 50) + '...' : tc.toolError }}
            </span>
            <span class="tool-output-preview" v-else-if="tc.toolOutput && tc.status !== 'failed'">
               {{ tc.toolOutput.replace(/\n/g, ' ').slice(0, 40) }}...
            </span>
          </div>
        </div>

        <div
          class="tool-more"
          :class="{ clickable: totalToolsCount > 5 }"
          v-if="hiddenToolCallsCount > 0 || isToolListExpanded"
          @click.stop="toggleToolList"
        >
          <span v-if="!isToolListExpanded">
            +{{ hiddenToolCallsCount }} more
          </span>
          <span v-else>
            Show less
          </span>
        </div>
      </div>

      <!-- Result Section (Collapsible) -->
      <div class="result-section" v-if="bubble.subAgentResult">
        <!-- Result Header (折叠按钮) -->
        <div class="result-header" @click.stop="toggleResult">
          <svg
            class="result-chevron"
            :class="{ expanded: isResultExpanded }"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="2"
          >
            <polyline points="9 18 15 12 9 6"></polyline>
          </svg>
          <svg class="result-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
            <polyline points="22 4 12 14.01 9 11.01"></polyline>
          </svg>
          <span class="result-label">Result</span>
        </div>
        <!-- Result Content (可折叠) -->
        <div class="result-box" v-if="isResultExpanded">
          <span class="result-text">{{ bubble.subAgentResult }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.subagent-bubble {
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

  &.status-running {
    border-color: rgba(var(--accent-primary-rgb), 0.2);
    background: rgba(var(--accent-primary-rgb), 0.02);
  }
}

.card-header {
  display: flex;
  flex-direction: column;
  padding: 10px 12px;
  cursor: pointer;
  user-select: none;
  gap: 2px;
}

.header-meta {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 2px;
}

.meta-left {
  display: flex;
  align-items: center;
  gap: 6px;
}

.agent-type {
  font-size: 0.65rem;
  font-weight: 700;
  color: var(--text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.meta-divider {
  color: var(--text-tertiary);
  opacity: 0.4;
  font-size: 0.65rem;
}

.agent-time {
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
}

.expanded .chevron { transform: rotate(180deg); }

.header-main {
  display: flex;
  align-items: center;
  gap: 8px;
}

.agent-name {
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.4;
}

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

.card-body {
  border-top: 1px solid var(--card-border);
  background: rgba(0, 0, 0, 0.15);
  padding: 6px 0;
}

.tool-list {
  display: flex;
  flex-direction: column;
}
.tool-item {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  padding: 5px 14px;
  font-size: 0.75rem;
  min-height: 26px;
}

.tool-status-dot {
  width: 4px;
  height: 4px;
  border-radius: 50%;
  background: var(--text-tertiary);
  opacity: 0.5;
  flex-shrink: 0;
  margin-top: 6px;
}

.tool-item.running .tool-status-dot,
.tool-item.streaming .tool-status-dot {
  background: var(--accent-color);
  opacity: 1;
  animation: pulse-glow 1.5s ease-in-out infinite;
}

.tool-item.completed .tool-status-dot { background: var(--accent-green, #4ade80); opacity: 0.8; }
.tool-item.failed .tool-status-dot { background: var(--accent-danger, #f87171); }

.tool-content {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  flex: 1;
  min-width: 0;
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
  white-space: normal;
  word-break: break-all;
  overflow-wrap: break-word;
  flex: 1;
  min-width: 20px;
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

  &.clickable {
    cursor: pointer;
    user-select: none;
    transition: all 0.2s ease;

    &:hover {
      background-color: rgba(74, 158, 255, 0.1);
      color: #4a9eff;
      opacity: 1;
      border-radius: 4px;
    }
  }
}

/* Result Section */
.result-section {
  margin: 4px 10px 8px;
}

.result-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 6px;
  cursor: pointer;
  user-select: none;
  opacity: 0.7;
  transition: opacity 0.2s;
  border-radius: 4px;

  &:hover {
    opacity: 1;
    background: rgba(var(--accent-green-rgb, 74, 222, 128), 0.05);
  }
}

.result-chevron {
  width: 12px;
  height: 12px;
  color: var(--accent-green, #4ade80);
  transition: transform 0.2s;
  flex-shrink: 0;

  &.expanded {
    transform: rotate(90deg);
  }
}

.result-icon {
  width: 12px;
  height: 12px;
  color: var(--accent-green, #4ade80);
  flex-shrink: 0;
}

.result-label {
  font-size: 0.75rem;
  font-weight: 500;
  color: var(--accent-green, #4ade80);
}

.result-box {
  margin-top: 4px;
  padding: 8px 12px;
  background: rgba(var(--accent-green-rgb, 74, 222, 128), 0.08);
  border-radius: 6px;
  border: 1px solid rgba(var(--accent-green-rgb, 74, 222, 128), 0.1);
}

.result-text {
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
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

/* Background Status (后台执行状态) */
.subagent-bubble.status-background {
  border-color: rgba(251, 191, 36, 0.3);  /* amber/warning color */
  background: rgba(251, 191, 36, 0.05);
}

.background-indicator {
  font-size: 12px;
  animation: pulse-bg 1.5s ease-in-out infinite;
}

@keyframes pulse-bg {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
