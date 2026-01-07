<script setup lang="ts">
import { ref, computed, watch, onUnmounted } from 'vue';
import type { SubAgent, ToolCall } from '../../types/agent';
import { getSubAgentDuration } from '../../types/agent';

const props = defineProps<{
  subAgent: SubAgent;
}>();

// Default: Expanded if running, collapsed if done
const isExpanded = ref(props.subAgent.status === 'running');

watch(() => props.subAgent.status, (newStatus) => {
  if (newStatus === 'completed' || newStatus === 'failed') {
    setTimeout(() => { isExpanded.value = false; }, 2000);
  } else if (newStatus === 'running') {
    isExpanded.value = true;
  }
});

const toggleExpand = () => { isExpanded.value = !isExpanded.value; };

// === 实时计时器逻辑 ===
const elapsedSeconds = ref(0);
let timerInterval: ReturnType<typeof setInterval> | null = null;

const startTimer = () => {
  if (timerInterval) return;
  // 立即计算一次
  if (props.subAgent.startTime) {
    elapsedSeconds.value = Math.round((Date.now() - props.subAgent.startTime) / 1000);
  }
  timerInterval = setInterval(() => {
    if (props.subAgent.startTime) {
      elapsedSeconds.value = Math.round((Date.now() - props.subAgent.startTime) / 1000);
    }
  }, 1000);
};

const stopTimer = () => {
  if (timerInterval) {
    clearInterval(timerInterval);
    timerInterval = null;
  }
};

// 监听状态变化启动/停止计时器
watch(() => props.subAgent.status, (newStatus) => {
  if (newStatus === 'running') {
    startTimer();
  } else {
    stopTimer();
  }
}, { immediate: true });

// 组件卸载时清理计时器
onUnmounted(() => stopTimer());

// 显示时间：运行中用实时计算，完成后用最终时间
const durationDisplay = computed(() => {
  if (props.subAgent.status === 'running') {
    return `${elapsedSeconds.value}s`;
  }
  return getSubAgentDuration(props.subAgent);
});

const statusClass = computed(() => {
  switch (props.subAgent.status) {
    case 'running': return 'status-running';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
});

const typeLabel = computed(() => {
  const t = props.subAgent.type || 'AGENT';
  return t === 'general-purpose' ? 'TASK' : t.toUpperCase();
});

const visibleToolCalls = computed(() => props.subAgent.toolCalls.slice(0, 5));
const hiddenToolCallsCount = computed(() => Math.max(0, props.subAgent.toolCalls.length - 5));

// === 工具详情提取 ===
// 根据工具类型提取关键参数作为详情显示
const getToolDetail = (tc: ToolCall): string | null => {
  // 优先使用 description
  if (tc.description) return tc.description;

  // 根据工具类型提取关键参数
  const params = tc.params || {};
  switch (tc.toolName) {
    case 'Read':
    case 'Write':
    case 'Edit':
      return (params.file_path as string) || null;
    case 'Glob':
      return (params.pattern as string) || null;
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
</script>

<template>
  <div class="subagent-card" :class="[statusClass, { expanded: isExpanded }]">
    <!-- Redesigned Header -->
    <div class="card-header" @click="toggleExpand">
      
      <!-- Row 1: Identity & Meta (Eyebrow) -->
      <div class="header-meta">
        <div class="meta-left">
          <span class="agent-type">{{ typeLabel }}</span>
          <span class="meta-divider" v-if="durationDisplay">·</span>
          <span class="agent-time" v-if="durationDisplay">{{ durationDisplay }}</span>
        </div>
        <!-- Chevron moved to top right for better touch target -->
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>

      <!-- Row 2: Status & Name (Main Content) -->
      <div class="header-main">
        <div class="status-icon-container">
          <div v-if="subAgent.status === 'running'" class="spinner-loader"></div>
          <svg v-else-if="subAgent.status === 'completed'" class="icon-success" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <svg v-else-if="subAgent.status === 'failed'" class="icon-error" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
          <div v-else class="dot-pending"></div>
        </div>
        <span class="agent-name">{{ subAgent.name }}</span>
      </div>

    </div>

    <!-- Compact Body -->
    <div class="card-body" v-if="isExpanded">
      <!-- Tool List -->
      <div class="tool-list" v-if="subAgent.toolCalls.length > 0">
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
              {{ tc.error.length > 50 ? tc.error.slice(0, 50) + '...' : tc.error }}
            </span>
            <span class="tool-output-preview" v-else-if="tc.output && tc.status !== 'failed'">
               → {{ tc.output.replace(/\n/g, ' ').slice(0, 40) }}...
            </span>
          </div>
        </div>
        
        <div class="tool-more" v-if="hiddenToolCallsCount > 0">
          <span>+{{ hiddenToolCallsCount }} more</span>
        </div>
      </div>

      <!-- Result -->
      <div class="result-box" v-if="subAgent.result">
        <svg class="result-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
          <polyline points="22 4 12 14.01 9 11.01"></polyline>
        </svg>
        <span class="result-text">{{ subAgent.result }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
/* --- Container --- */
.subagent-card {
  --card-bg: rgba(20, 20, 22, 0.4);
  --card-border: rgba(255, 255, 255, 0.06);
  --accent-color: var(--accent-primary);
  
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 10px; /* Slightly rounder */
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

/* --- Header --- */
.card-header {
  display: flex;
  flex-direction: column;
  padding: 10px 12px;
  cursor: pointer;
  user-select: none;
  gap: 2px;
}

/* Row 1: Meta (Eyebrow) */
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
  /* Removed background pill style for cleaner look */
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

/* Row 2: Main */
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
.card-body {
  border-top: 1px solid var(--card-border);
  background: rgba(0, 0, 0, 0.15); /* Darker body background */
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
  padding: 5px 14px; /* More horizontal padding */
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

/* Result */
.result-box {
  margin: 6px 10px 8px;
  padding: 8px 12px;
  background: rgba(var(--accent-green-rgb, 74, 222, 128), 0.08);
  border-radius: 6px;
  display: flex;
  align-items: flex-start;
  gap: 8px;
  border: 1px solid rgba(var(--accent-green-rgb, 74, 222, 128), 0.1);
}

.result-icon {
  width: 14px;
  height: 14px;
  color: var(--accent-green, #4ade80);
  margin-top: 2px;
  flex-shrink: 0;
}

.result-text {
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.5;
}

@keyframes spin { to { transform: rotate(360deg); } }

/* 呼吸灯脉冲动画 */
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
