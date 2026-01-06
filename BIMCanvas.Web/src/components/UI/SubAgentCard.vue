<script setup lang="ts">
import { ref, computed } from 'vue';
import type { SubAgent } from '../../types/agent';
import { getSubAgentDuration } from '../../types/agent';

const props = defineProps<{
  subAgent: SubAgent;
}>();

const isExpanded = ref(props.subAgent.isExpanded ?? true);

// Toggle expand/collapse
const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;
};

// Computed duration display
const durationDisplay = computed(() => {
  return getSubAgentDuration(props.subAgent);
});

// Get status class for left border color
const statusClass = computed(() => {
  switch (props.subAgent.status) {
    case 'pending': return 'status-pending';
    case 'running': return 'status-running';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
});

// Get type label display
const typeLabel = computed(() => {
  switch (props.subAgent.type) {
    case 'Explore': return 'Explore';
    case 'Plan': return 'Plan';
    case 'general-purpose': return 'Task';
    default: return props.subAgent.type;
  }
});

// Count visible tool calls (show first 3, rest collapsed)
const visibleToolCalls = computed(() => {
  return props.subAgent.toolCalls.slice(0, 3);
});

const hiddenToolCallsCount = computed(() => {
  return Math.max(0, props.subAgent.toolCalls.length - 3);
});
</script>

<template>
  <div class="subagent-card" :class="[statusClass, { expanded: isExpanded }]">
    <!-- Header -->
    <div class="card-header" @click="toggleExpand">
      <div class="header-left">
        <span class="type-badge">{{ typeLabel }}</span>
        <span class="agent-name">{{ subAgent.name }}</span>
      </div>
      <div class="header-right">
        <span v-if="subAgent.status === 'running'" class="status-indicator">
          <span class="spinner"></span>
          <span class="status-text">Running...</span>
        </span>
        <span v-else-if="subAgent.status === 'completed'" class="status-indicator completed">
          <svg class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="20 6 9 17 4 12"></polyline>
          </svg>
          <span class="duration" v-if="durationDisplay">{{ durationDisplay }}</span>
        </span>
        <span v-else-if="subAgent.status === 'failed'" class="status-indicator failed">
          <svg class="error-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10"></circle>
            <line x1="15" y1="9" x2="9" y2="15"></line>
            <line x1="9" y1="9" x2="15" y2="15"></line>
          </svg>
          <span class="status-text">Failed</span>
        </span>
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>
    </div>

    <!-- Tool Calls List (Expanded) -->
    <div class="tool-calls-section" v-if="isExpanded && subAgent.toolCalls.length > 0">
      <div
        class="tool-call-item"
        v-for="tc in visibleToolCalls"
        :key="tc.id"
        :class="{ 'is-running': tc.status === 'running', 'is-completed': tc.status === 'completed', 'is-failed': tc.status === 'failed' }"
      >
        <div class="tool-header">
          <span class="tool-name">{{ tc.toolName }}</span>
          <span class="tool-status">
            <span v-if="tc.status === 'running'" class="mini-spinner"></span>
            <svg v-else-if="tc.status === 'completed'" class="mini-check" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <polyline points="20 6 9 17 4 12"></polyline>
            </svg>
            <svg v-else-if="tc.status === 'failed'" class="mini-error" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"></circle>
              <line x1="15" y1="9" x2="9" y2="15"></line>
              <line x1="9" y1="9" x2="15" y2="15"></line>
            </svg>
          </span>
        </div>
        <div class="tool-description" v-if="tc.description">{{ tc.description }}</div>
        <div class="tool-output" v-if="tc.output">
          <pre>{{ tc.output.slice(0, 200) }}{{ tc.output.length > 200 ? '...' : '' }}</pre>
        </div>
      </div>

      <!-- Collapsed Indicator -->
      <div class="collapsed-indicator" v-if="hiddenToolCallsCount > 0">
        +{{ hiddenToolCallsCount }} more tool call{{ hiddenToolCallsCount > 1 ? 's' : '' }}
      </div>
    </div>

    <!-- Result (if completed) -->
    <div class="result-section" v-if="isExpanded && subAgent.result">
      <div class="result-label">Result</div>
      <div class="result-content">{{ subAgent.result }}</div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.subagent-card {
  background: var(--surface-card);
  border: 1px solid var(--border-subtle);
  border-radius: 8px;
  overflow: hidden;
  margin: 8px 0;
  transition: all 0.2s;

  // Left border status indicator
  border-left: 3px solid var(--border-subtle);

  &.status-pending {
    border-left-color: var(--text-tertiary);
  }

  &.status-running {
    border-left-color: var(--accent-primary);
    animation: pulse-border 2s ease-in-out infinite;
  }

  &.status-completed {
    border-left-color: var(--accent-green, #34c759);
  }

  &.status-failed {
    border-left-color: var(--accent-danger);
  }

  &.expanded {
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);

    .chevron {
      transform: rotate(180deg);
    }
  }
}

.card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 10px 12px;
  cursor: pointer;

  &:hover {
    background: var(--surface-highlight);
  }
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
  flex: 1;
  min-width: 0;
}

.type-badge {
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--accent-primary);
  background: var(--accent-glow);
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  flex-shrink: 0;
}

.agent-name {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.header-right {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
}

.status-indicator {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 0.75rem;
  color: var(--text-secondary);

  &.completed {
    color: var(--accent-green, #34c759);
  }

  &.failed {
    color: var(--accent-danger);
  }
}

.spinner {
  width: 12px;
  height: 12px;
  border: 2px solid var(--accent-primary);
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

.check-icon, .error-icon {
  width: 14px;
  height: 14px;
}

.duration {
  font-size: 0.7rem;
  color: var(--text-tertiary);
}

.chevron {
  width: 16px;
  height: 16px;
  color: var(--text-secondary);
  transition: transform 0.2s;
}

// Tool Calls Section
.tool-calls-section {
  padding: 0 12px 12px 12px;
  border-top: 1px solid var(--border-subtle);
  background: var(--surface-dim);
}

.tool-call-item {
  margin-top: 8px;
  padding: 8px;
  background: var(--surface-card);
  border-radius: 6px;
  border: 1px solid var(--border-subtle);

  &.is-running {
    border-left: 2px solid var(--accent-primary);
  }

  &.is-completed {
    opacity: 0.8;
  }

  &.is-failed {
    border-left: 2px solid var(--accent-danger);
  }
}

.tool-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.tool-name {
  font-size: 0.75rem;
  font-weight: 600;
  font-family: var(--font-mono);
  color: var(--accent-primary);
}

.tool-status {
  display: flex;
  align-items: center;
}

.mini-spinner {
  width: 10px;
  height: 10px;
  border: 1.5px solid var(--accent-primary);
  border-top-color: transparent;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

.mini-check {
  width: 12px;
  height: 12px;
  color: var(--accent-green, #34c759);
}

.mini-error {
  width: 12px;
  height: 12px;
  color: var(--accent-danger);
}

.tool-description {
  font-size: 0.7rem;
  color: var(--text-secondary);
  margin-top: 4px;
}

.tool-output {
  margin-top: 6px;

  pre {
    font-size: 0.65rem;
    font-family: var(--font-mono);
    color: var(--text-tertiary);
    background: var(--surface-dim);
    padding: 6px 8px;
    border-radius: 4px;
    margin: 0;
    white-space: pre-wrap;
    word-break: break-all;
    max-height: 80px;
    overflow-y: auto;
  }
}

.collapsed-indicator {
  font-size: 0.7rem;
  color: var(--text-tertiary);
  text-align: center;
  padding: 8px 0 0 0;
}

// Result Section
.result-section {
  padding: 8px 12px 12px 12px;
  border-top: 1px solid var(--border-subtle);
}

.result-label {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--text-tertiary);
  margin-bottom: 4px;
}

.result-content {
  font-size: 0.8rem;
  color: var(--text-secondary);
  line-height: 1.4;
}

// Animations
@keyframes spin {
  to { transform: rotate(360deg); }
}

@keyframes pulse-border {
  0%, 100% {
    border-left-color: var(--accent-primary);
    box-shadow: 0 0 0 0 var(--accent-glow);
  }
  50% {
    border-left-color: var(--accent-primary);
    box-shadow: 0 0 8px 2px var(--accent-glow);
  }
}
</style>
