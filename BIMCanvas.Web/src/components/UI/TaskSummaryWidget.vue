<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue';
import type { SubAgent } from '../../types/agent';

const props = defineProps<{
  subAgents: SubAgent[];
  expanded: boolean;
}>();

const emit = defineEmits(['update:expanded']);

// === 状态计算 ===
const hasAgents = computed(() => props.subAgents.length > 0);

// 统计各状态数量
const runningCount = computed(() => props.subAgents.filter(a => a.status === 'running').length);
const completedCount = computed(() => props.subAgents.filter(a => a.status === 'completed').length);
const failedCount = computed(() => props.subAgents.filter(a => a.status === 'failed').length);

// 全局状态判断
const isAllIdle = computed(() => !hasAgents.value);
const hasRunning = computed(() => runningCount.value > 0);
const isAllCompleted = computed(() => hasAgents.value && runningCount.value === 0);

// Header 文案
const headerText = computed(() => {
  if (isAllIdle.value) return 'No active agents';
  
  const parts: string[] = [];
  if (runningCount.value > 0) {
    parts.push(`${runningCount.value} running`);
  }
  if (completedCount.value > 0) {
    parts.push(`${completedCount.value} completed`);
  }
  if (failedCount.value > 0) {
    parts.push(`${failedCount.value} failed`);
  }
  return parts.join(', ');
});

// === Timer Logic ===
const now = ref(Date.now());
let timerInterval: ReturnType<typeof setInterval> | null = null;

onMounted(() => {
  timerInterval = setInterval(() => {
    now.value = Date.now();
  }, 1000);
});

onUnmounted(() => {
  if (timerInterval) clearInterval(timerInterval);
});

const getDuration = (agent: SubAgent) => {
  if (agent.endTime && agent.startTime) {
    const duration = Math.round((agent.endTime - agent.startTime) / 1000);
    return formatTime(duration);
  }
  if (agent.startTime) {
    const duration = Math.round((now.value - agent.startTime) / 1000);
    return formatTime(duration);
  }
  return '0s';
};

const formatTime = (seconds: number) => {
  if (seconds < 60) return `${seconds}s`;
  const mins = Math.floor(seconds / 60);
  const secs = seconds % 60;
  return `${mins}m ${secs}s`;
};

// === Helper for Action Text ===
const getCurrentAction = (agent: SubAgent) => {
  if (agent.status === 'completed') return 'Completed';
  if (agent.status === 'failed') return 'Failed';
  
  const runningTool = agent.toolCalls.slice().reverse().find(t => t.status === 'running');
  if (runningTool) {
    return `Executing: ${runningTool.toolName}...`;
  }
  return 'Thinking...';
};

const toggleExpand = () => {
  if (hasAgents.value) {
    emit('update:expanded', !props.expanded);
  }
};

// 获取单个 Agent 的状态 class
const getAgentStatusClass = (agent: SubAgent) => {
  return agent.status; // 'running' | 'completed' | 'failed'
};
</script>

<template>
  <div class="task-summary-widget" :class="{ 
    expanded: expanded, 
    inactive: isAllIdle, 
    'has-running': hasRunning,
    'all-completed': isAllCompleted 
  }">
    <!-- Header -->
    <div class="widget-header" @click="toggleExpand">
      <div class="widget-content">
        
        <!-- Icon: Priority = Running > Completed > Idle -->
        <template v-if="hasRunning">
          <div class="spinner-mini"></div>
        </template>
        <template v-else-if="isAllCompleted">
          <div class="icon-completed">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path>
              <polyline points="22 4 12 14.01 9 11.01"></polyline>
            </svg>
          </div>
        </template>
        <template v-else>
          <div class="icon-idle">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <circle cx="12" cy="12" r="10"></circle>
              <polyline points="12 6 12 12 16 14"></polyline>
            </svg>
          </div>
        </template>

        <!-- Header Text -->
        <span class="info" :class="{ 
          idle: isAllIdle, 
          running: hasRunning, 
          completed: isAllCompleted 
        }">
          {{ headerText }}
        </span>

      </div>
      
      <!-- Chevron (only if has agents) -->
      <div class="widget-action" v-if="hasAgents">
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>
    </div>
    
    <!-- Expanded Details -->
    <div class="widget-details" v-if="expanded && hasAgents">
      <div 
        class="agent-item" 
        v-for="agent in subAgents" 
        :key="agent.id"
        :class="getAgentStatusClass(agent)"
      >
        
        <!-- Row 1: Name & Time -->
        <div class="row-primary">
          <span class="agent-name">
            <span class="type-tag">{{ agent.type === 'general-purpose' ? 'TASK' : agent.type }}</span>
            {{ agent.name }}
          </span>
          <span class="agent-time">{{ getDuration(agent) }}</span>
        </div>

        <!-- Row 2: Activity Bar (状态独立) -->
        <div class="activity-track">
          <div v-if="agent.status === 'running'" class="activity-bar-pulse"></div>
          <div v-else-if="agent.status === 'completed'" class="activity-bar-solid completed"></div>
          <div v-else-if="agent.status === 'failed'" class="activity-bar-solid failed"></div>
        </div>

        <!-- Row 3: Action Status -->
        <div class="row-secondary">
          <span class="current-action" :class="agent.status">{{ getCurrentAction(agent) }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.task-summary-widget {
    background: var(--surface-elevated);
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    overflow: hidden;
    transition: all 0.2s;
    margin-bottom: 4px;

    &.expanded {
        box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        
        .widget-action .chevron {
            transform: rotate(180deg);
        }
    }

    &.inactive {
        opacity: 0.7;
        .widget-header {
            cursor: default;
            &:hover { background: transparent; }
        }
    }

    /* All Completed State */
    &.all-completed {
        border-color: rgba(var(--accent-success-rgb), 0.3);
        
        .widget-header {
            background: rgba(var(--accent-success-rgb), 0.05);
        }
    }

    .widget-header {
        padding: 10px 12px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        cursor: pointer;
        
        &:hover {
            background: var(--surface-highlight);
        }
    }

    .widget-content {
        display: flex;
        align-items: center;
        gap: 8px;
        
        .spinner-mini {
            width: 14px;
            height: 14px;
            border: 2px solid var(--accent-primary);
            border-top-color: transparent;
            border-radius: 50%;
            animation: spin 1s linear infinite;
        }

        .icon-idle, .icon-completed {
            width: 14px;
            height: 14px;
            display: flex;
            align-items: center;
            justify-content: center;
            
            svg { width: 100%; height: 100%; }
        }

        .icon-idle { color: var(--text-tertiary); }
        .icon-completed { color: var(--accent-success); }
        
        .info {
            font-size: 0.8rem;
            color: var(--text-primary);
            font-weight: 500;

            &.idle { color: var(--text-tertiary); font-weight: 400; }
            &.running { color: var(--text-primary); }
            &.completed { color: var(--accent-success); }
        }
    }

    .widget-action {
        display: flex;
        align-items: center;
        color: var(--text-secondary);
        
        .chevron { 
            width: 16px; 
            height: 16px; 
            transition: transform 0.2s;
        }
    }
    
    .widget-details {
        padding: 0 12px 12px 12px;
        border-top: 1px solid var(--border-subtle);
        background: var(--surface-dim);
        
        .agent-item {
            margin-top: 12px;
            padding-bottom: 12px;
            border-bottom: 1px solid var(--border-subtle);
            display: flex;
            flex-direction: column;
            gap: 6px;
            
            &:last-child {
                border-bottom: none;
                padding-bottom: 0;
            }

            /* Agent Item 状态样式 */
            &.completed {
                opacity: 0.8;
            }
            &.failed {
                opacity: 0.8;
            }
            
            .row-primary {
                display: flex;
                justify-content: space-between;
                align-items: center;
                
                .agent-name {
                    font-size: 0.8rem;
                    font-weight: 600;
                    color: var(--text-primary);
                    display: flex;
                    align-items: center;
                    gap: 6px;
                    
                    .type-tag {
                        font-size: 0.6rem;
                        padding: 1px 4px;
                        border-radius: 3px;
                        background: rgba(255, 255, 255, 0.1);
                        color: var(--text-secondary);
                        text-transform: uppercase;
                        font-weight: 700;
                    }
                }
                
                .agent-time {
                    font-size: 0.7rem;
                    color: var(--text-tertiary);
                    font-family: var(--font-mono);
                }
            }
            
            .activity-track {
                height: 4px;
                background: var(--border-dim);
                border-radius: 2px;
                overflow: hidden;
                position: relative;
                
                .activity-bar-pulse {
                    position: absolute;
                    top: 0;
                    left: 0;
                    width: 100%;
                    height: 100%;
                    background: linear-gradient(90deg, transparent, var(--accent-primary), transparent);
                    transform: translateX(-100%);
                    animation: shimmer 1.5s infinite;
                }

                .activity-bar-solid {
                    width: 100%;
                    height: 100%;
                    
                    &.completed { background: var(--accent-success); }
                    &.failed { background: var(--accent-danger); }
                }
            }

            .row-secondary {
                display: flex;
                justify-content: space-between;
                align-items: center;
                min-height: 20px;
                
                .current-action {
                    font-size: 0.7rem;
                    color: var(--text-secondary);
                    font-style: italic;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    max-width: 70%;

                    &.completed { color: var(--accent-success); font-style: normal; }
                    &.failed { color: var(--accent-danger); font-style: normal; }
                }
            }
        }
    }
}

@keyframes spin {
    to { transform: rotate(360deg); }
}

@keyframes shimmer {
    0% { transform: translateX(-100%); }
    100% { transform: translateX(100%); }
}
</style>
