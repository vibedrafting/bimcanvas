<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import type { SubAgent } from '../../types/agent';
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

const durationDisplay = computed(() => getSubAgentDuration(props.subAgent));

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
</script>

<template>
  <div class="subagent-card" :class="[statusClass, { expanded: isExpanded }]">
    <!-- Compact Header (2-Row Layout) -->
    <div class="card-header" @click="toggleExpand">
      
      <!-- Top Row: Icon + Name -->
      <div class="header-top">
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

      <!-- Bottom Row: Metadata + Chevron -->
      <div class="header-bottom">
        <div class="meta-left">
          <span class="meta-tag">{{ typeLabel }}</span>
          <span class="meta-time" v-if="durationDisplay">{{ durationDisplay }}</span>
        </div>
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
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
            <span class="tool-args" v-if="tc.description">{{ tc.description }}</span>
            <span class="tool-output-preview" v-if="tc.output">
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
  --card-bg: rgba(18, 18, 20, 0.6);
  --card-border: rgba(255, 255, 255, 0.08);
  --accent-color: var(--accent-primary);
  
  background: var(--card-bg);
  border: 1px solid var(--card-border);
  border-radius: 8px;
  margin: 6px 0;
  overflow: hidden;
  transition: all 0.2s ease;
  font-family: 'Inter', system-ui, sans-serif;

  &:hover {
    background: rgba(25, 25, 30, 0.8);
    border-color: rgba(255, 255, 255, 0.15);
  }

  &.status-running {
    border-color: rgba(var(--accent-primary-rgb), 0.3);
    box-shadow: 0 0 0 1px rgba(var(--accent-primary-rgb), 0.05) inset;
  }
}

/* --- Header (2-Row) --- */
.card-header {
  display: flex;
  flex-direction: column; /* Stack vertically */
  padding: 8px 10px;
  cursor: pointer;
  user-select: none;
  gap: 4px;
}

.header-top {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
}

.header-bottom {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding-left: 22px; /* Indent to align with text above */
}

.agent-name {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-primary);
  line-height: 1.2;
  word-break: break-word; /* Allow wrapping if needed */
}

/* Icons */
.status-icon-container {
  width: 14px;
  height: 14px;
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

.icon-success { color: var(--accent-green, #4ade80); width: 14px; height: 14px; }
.icon-error { color: var(--accent-danger, #f87171); width: 14px; height: 14px; }
.dot-pending { width: 6px; height: 6px; background: var(--text-tertiary); border-radius: 50%; opacity: 0.5; }

.meta-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.meta-tag {
  font-size: 0.65rem;
  font-weight: 600;
  color: var(--text-tertiary);
  background: rgba(255, 255, 255, 0.05);
  padding: 1px 5px;
  border-radius: 3px;
  letter-spacing: 0.5px;
}

.meta-time {
  font-size: 0.7rem;
  color: var(--text-tertiary);
  font-family: var(--font-mono);
  opacity: 0.6;
}

.chevron {
  width: 14px;
  height: 14px;
  color: var(--text-tertiary);
  transition: transform 0.2s;
  opacity: 0.5;
}

.expanded .chevron { transform: rotate(180deg); }

/* --- Body --- */
.card-body {
  border-top: 1px solid var(--card-border);
  background: rgba(0, 0, 0, 0.1);
  padding: 4px 0;
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
  padding: 4px 12px;
  font-size: 0.75rem;
  height: 24px;
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
  box-shadow: 0 0 4px var(--accent-color);
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
  padding: 2px 12px 4px;
  font-size: 0.7rem;
  color: var(--text-tertiary);
  opacity: 0.6;
  font-style: italic;
}

/* Result */
.result-box {
  margin: 4px 8px 8px;
  padding: 6px 10px;
  background: rgba(var(--accent-green-rgb, 74, 222, 128), 0.08);
  border-radius: 6px;
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

.result-icon {
  width: 14px;
  height: 14px;
  color: var(--accent-green, #4ade80);
  margin-top: 2px;
  flex-shrink: 0;
}

.result-text {
  font-size: 0.75rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

@keyframes spin { to { transform: rotate(360deg); } }
</style>
