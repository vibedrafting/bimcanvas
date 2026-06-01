<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useWorkflowProgress } from '../../composables/aiCommandCenter/useWorkflowProgress'
import type { WorkflowAgentState } from '../../composables/aiCommandCenter/useWorkflowProgress'

const {
  workflow,
  transcript,
  transcriptStatus,
  hasCompletedWorkflow,
  workflowAgents,
  resetWorkflow,
  loadTranscript
} = useWorkflowProgress()

// === 实时时长计时 ===
const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => {
  timer = setInterval(() => { now.value = Date.now() }, 1000)
  // 若挂载时 workflow 已完成（如切回 Task 页），按需拉一次详情。
  if (hasCompletedWorkflow.value) void loadTranscript()
})
onUnmounted(() => { if (timer) clearInterval(timer) })

// 完成即按需拉 transcript（绝不轮询）。
watch(hasCompletedWorkflow, (done) => {
  if (done) void loadTranscript()
})

// === 展示模式：running/live-final 用实时聚合；完成且 transcript 就绪用详情态 ===
const showTranscript = computed(
  () => hasCompletedWorkflow.value
    && transcriptStatus.value === 'loaded'
    && !!transcript.value
    && transcript.value.agents.length > 0
)

const statusText = computed(() => {
  if (!workflow.value) return ''
  if (workflow.value.status === 'running') return 'Running'
  if (workflow.value.status === 'failed') return 'Failed'
  return 'Completed'
})

const formatTokens = (n: number | undefined): string | null => {
  if (typeof n !== 'number' || !isFinite(n)) return null
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  return `${(n / 1_000_000).toFixed(1)}M`
}

const formatDuration = (startTime?: number, endTime?: number): string => {
  if (!startTime) return ''
  const end = endTime ?? now.value
  const sec = Math.max(0, Math.round((end - startTime) / 1000))
  if (sec < 60) return `${sec}s`
  return `${Math.floor(sec / 60)}m ${sec % 60}s`
}

const headerMeta = computed(() => {
  if (!workflow.value) return ''
  const parts: string[] = []
  const agents = showTranscript.value && transcript.value ? transcript.value.agents.length : workflowAgents.value.length
  parts.push(`${agents} agent${agents === 1 ? '' : 's'}`)
  const dur = formatDuration(workflow.value.startTime, workflow.value.endTime)
  if (dur) parts.push(dur)
  return parts.join(' · ')
})

// === 展开状态 ===
const expandedKeys = ref<Set<string>>(new Set())
const toggleExpand = (key: string) => {
  const next = new Set(expandedKeys.value)
  if (next.has(key)) next.delete(key)
  else next.add(key)
  expandedKeys.value = next
}

const agentStats = (a: WorkflowAgentState): string[] => {
  const stats: string[] = []
  const t = formatTokens(a.usage.totalTokens)
  if (t) stats.push(`${t}🔤`)
  if (typeof a.usage.toolUses === 'number') stats.push(`${a.usage.toolUses}🔧`)
  const dur = formatDuration(a.startTime, a.endTime)
  if (dur) stats.push(dur)
  return stats
}

const dismiss = () => {
  resetWorkflow()
  expandedKeys.value = new Set()
}
</script>

<template>
  <div v-if="workflow" class="workflow-progress-panel" :class="workflow.status">
    <!-- Header -->
    <div class="wf-header">
      <div class="wf-status-icon" :class="workflow.status">
        <div v-if="workflow.status === 'running'" class="spinner"></div>
        <svg v-else-if="workflow.status === 'completed'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
          <polyline points="20 6 9 17 4 12"></polyline>
        </svg>
        <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
          <line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </div>
      <div class="wf-title-block">
        <div class="wf-title">{{ workflow.label }}</div>
        <div class="wf-meta">{{ headerMeta }}</div>
      </div>
      <span class="wf-status-badge" :class="workflow.status">{{ statusText }}</span>
      <button v-if="hasCompletedWorkflow" class="wf-dismiss" title="清除" @click="dismiss">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line>
        </svg>
      </button>
    </div>

    <!-- 完成态 transcript 加载提示 -->
    <div v-if="hasCompletedWorkflow && transcriptStatus === 'loading'" class="wf-hint">正在加载执行详情…</div>
    <div v-else-if="hasCompletedWorkflow && transcriptStatus === 'error'" class="wf-hint error">
      详情加载失败 <button class="wf-retry" @click="loadTranscript(true)">重试</button>
    </div>

    <!-- 详情态：transcript per-agent（model / prompt / outcome） -->
    <div v-if="showTranscript && transcript" class="wf-agents">
      <div
        v-for="a in transcript.agents"
        :key="a.agentId"
        class="wf-agent"
      >
        <div class="agent-row" @click="toggleExpand(a.agentId)">
          <span class="agent-dot completed"></span>
          <span class="agent-label">{{ a.label || a.agentId }}</span>
          <span v-if="a.model" class="agent-model">{{ a.model }}</span>
          <span class="agent-stats">
            <template v-if="formatTokens(a.totalTokens)">{{ formatTokens(a.totalTokens) }}🔤</template>
            <template v-if="typeof a.toolUses === 'number'"> · {{ a.toolUses }}🔧</template>
          </span>
          <svg class="chevron" :class="{ open: expandedKeys.has(a.agentId) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </div>
        <div v-if="expandedKeys.has(a.agentId)" class="agent-detail">
          <div v-if="a.prompt" class="detail-section">
            <div class="detail-label">Prompt</div>
            <pre class="detail-pre">{{ a.prompt }}</pre>
          </div>
          <div v-if="a.tools.length" class="detail-section">
            <div class="detail-label">Activity</div>
            <div v-for="(t, i) in a.tools" :key="i" class="activity-item">
              <span class="tool-name">{{ t.name }}</span>
              <span v-if="t.input" class="tool-input">{{ t.input }}</span>
            </div>
          </div>
          <div v-if="a.outcome" class="detail-section">
            <div class="detail-label">Outcome</div>
            <pre class="detail-pre">{{ a.outcome }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- 实时聚合态（进行中 / 详情未就绪）。SDK 实时流不给 model,故此处无模型名。 -->
    <div v-else class="wf-agents">
      <div v-if="workflowAgents.length === 0" class="wf-hint">workflow 已启动，等待 agent 执行…</div>
      <div
        v-for="a in workflowAgents"
        :key="a.key"
        class="wf-agent"
      >
        <div class="agent-row" @click="toggleExpand(a.key)">
          <span class="agent-dot" :class="a.status"></span>
          <span class="agent-label">{{ a.label }}</span>
          <span class="agent-stats">{{ agentStats(a).join(' · ') }}</span>
          <span v-if="a.lastToolName && a.status === 'running'" class="agent-lasttool">{{ a.lastToolName }}</span>
          <svg
            v-if="a.activity.length || a.outcome"
            class="chevron"
            :class="{ open: expandedKeys.has(a.key) }"
            viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"
          >
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </div>
        <!-- 实时进度条 -->
        <div class="activity-track">
          <div v-if="a.status === 'running'" class="bar-pulse"></div>
          <div v-else-if="a.status === 'completed'" class="bar-solid completed"></div>
          <div v-else-if="a.status === 'failed'" class="bar-solid failed"></div>
        </div>
        <div v-if="expandedKeys.has(a.key)" class="agent-detail">
          <div v-if="a.activity.length" class="detail-section">
            <div class="detail-label">Activity</div>
            <div v-for="ev in a.activity" :key="ev.toolCallId" class="activity-item">
              <span class="activity-dot" :class="ev.status"></span>
              <span class="tool-name">{{ ev.toolName }}</span>
              <span v-if="ev.description" class="tool-input">{{ ev.description }}</span>
            </div>
          </div>
          <div v-if="a.outcome" class="detail-section">
            <div class="detail-label">Outcome</div>
            <pre class="detail-pre">{{ a.outcome }}</pre>
          </div>
          <div v-else-if="a.status === 'running'" class="detail-section">
            <div class="detail-still">Still running…</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.workflow-progress-panel {
  background: var(--surface-elevated);
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  overflow: hidden;
  margin-bottom: 12px;

  &.running { border-color: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.4); }
  &.completed { border-color: rgba(var(--accent-success-rgb), 0.35); }
  &.failed { border-color: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.4); }
}

.wf-header {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  background: var(--surface-highlight);
  border-bottom: 1px solid var(--border-subtle);

  .wf-status-icon {
    width: 18px; height: 18px;
    display: flex; align-items: center; justify-content: center;
    flex-shrink: 0;

    svg { width: 100%; height: 100%; }
    &.completed { color: var(--accent-success); }
    &.failed { color: var(--accent-danger); }

    .spinner {
      width: 14px; height: 14px;
      border: 2px solid var(--accent-primary);
      border-top-color: transparent;
      border-radius: 50%;
      animation: wf-spin 1s linear infinite;
    }
  }

  .wf-title-block { flex: 1; min-width: 0; }
  .wf-title {
    font-size: 0.85rem; font-weight: 600; color: var(--text-primary);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .wf-meta { font-size: 0.7rem; color: var(--text-tertiary); font-family: var(--font-mono); margin-top: 2px; }

  .wf-status-badge {
    font-size: 0.65rem; font-weight: 700; text-transform: uppercase;
    padding: 2px 8px; border-radius: 10px; letter-spacing: 0.03em;
    &.running { background: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.15); color: var(--accent-primary); }
    &.completed { background: rgba(var(--accent-success-rgb), 0.15); color: var(--accent-success); }
    &.failed { background: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.15); color: var(--accent-danger); }
  }

  .wf-dismiss {
    background: transparent; border: none; cursor: pointer; padding: 2px;
    color: var(--text-tertiary); width: 20px; height: 20px;
    display: flex; align-items: center; justify-content: center;
    border-radius: 4px;
    &:hover { background: var(--surface-dim); color: var(--text-secondary); }
    svg { width: 14px; height: 14px; }
  }
}

.wf-hint {
  padding: 12px 14px; font-size: 0.75rem; color: var(--text-tertiary); font-style: italic;
  &.error { color: var(--accent-danger); font-style: normal; }
  .wf-retry {
    margin-left: 8px; background: transparent; border: 1px solid var(--border-subtle);
    border-radius: 4px; color: var(--text-secondary); cursor: pointer; padding: 1px 8px; font-size: 0.7rem;
    &:hover { background: var(--surface-dim); }
  }
}

.wf-agents { padding: 6px 0; }

.wf-agent {
  padding: 6px 14px;
  border-bottom: 1px solid var(--border-dim);
  &:last-child { border-bottom: none; }

  .agent-row {
    display: flex; align-items: center; gap: 8px; cursor: pointer;

    .agent-dot {
      width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0;
      background: var(--text-tertiary);
      &.running { background: var(--accent-primary); animation: wf-pulse 1.4s ease-in-out infinite; }
      &.completed { background: var(--accent-success); }
      &.failed { background: var(--accent-danger); }
    }

    .agent-label {
      font-size: 0.78rem; color: var(--text-primary); font-weight: 500;
      flex: 1; min-width: 0;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .agent-model {
      font-size: 0.62rem; font-family: var(--font-mono); color: var(--text-secondary);
      background: var(--surface-dim); padding: 1px 5px; border-radius: 3px; flex-shrink: 0;
    }
    .agent-stats { font-size: 0.68rem; color: var(--text-tertiary); font-family: var(--font-mono); flex-shrink: 0; }
    .agent-lasttool {
      font-size: 0.66rem; color: var(--accent-primary); font-family: var(--font-mono);
      max-width: 100px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex-shrink: 0;
    }
    .chevron {
      width: 14px; height: 14px; color: var(--text-tertiary); flex-shrink: 0;
      transition: transform 0.2s;
      &.open { transform: rotate(180deg); }
    }
  }

  .activity-track {
    height: 3px; background: var(--border-dim); border-radius: 2px; overflow: hidden;
    position: relative; margin-top: 6px;
    .bar-pulse {
      position: absolute; inset: 0; width: 100%;
      background: linear-gradient(90deg, transparent, var(--accent-primary), transparent);
      transform: translateX(-100%); animation: wf-shimmer 1.5s infinite;
    }
    .bar-solid { width: 100%; height: 100%;
      &.completed { background: var(--accent-success); }
      &.failed { background: var(--accent-danger); }
    }
  }

  .agent-detail {
    margin-top: 8px; padding: 8px 10px; background: var(--surface-dim);
    border-radius: 6px; display: flex; flex-direction: column; gap: 8px;

    .detail-section { display: flex; flex-direction: column; gap: 3px; }
    .detail-label {
      font-size: 0.6rem; font-weight: 700; text-transform: uppercase;
      color: var(--text-tertiary); letter-spacing: 0.04em;
    }
    .detail-pre {
      margin: 0; font-size: 0.7rem; font-family: var(--font-mono); color: var(--text-secondary);
      white-space: pre-wrap; word-break: break-word; max-height: 200px; overflow-y: auto;
    }
    .detail-still { font-size: 0.7rem; color: var(--text-tertiary); font-style: italic; }

    .activity-item {
      display: flex; align-items: center; gap: 6px; font-size: 0.7rem;
      .activity-dot {
        width: 6px; height: 6px; border-radius: 50%; flex-shrink: 0; background: var(--text-tertiary);
        &.running { background: var(--accent-primary); }
        &.completed { background: var(--accent-success); }
        &.failed { background: var(--accent-danger); }
      }
      .tool-name { font-family: var(--font-mono); color: var(--text-primary); }
      .tool-input {
        color: var(--text-tertiary); white-space: nowrap; overflow: hidden;
        text-overflow: ellipsis; flex: 1; min-width: 0;
      }
    }
  }
}

@keyframes wf-spin { to { transform: rotate(360deg); } }
@keyframes wf-pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
@keyframes wf-shimmer { 0% { transform: translateX(-100%); } 100% { transform: translateX(100%); } }
</style>
