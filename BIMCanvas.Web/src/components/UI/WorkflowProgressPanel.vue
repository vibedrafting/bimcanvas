<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useWorkflowProgress } from '../../composables/aiCommandCenter/useWorkflowProgress'
import type {
  WorkflowAgentState,
  WorkflowTranscriptAgent
} from '../../composables/aiCommandCenter/useWorkflowProgress'

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
  if (hasCompletedWorkflow.value) void loadTranscript()
})
onUnmounted(() => { if (timer) clearInterval(timer) })

watch(hasCompletedWorkflow, (done) => { if (done) void loadTranscript() })

// 完成且 transcript 就绪 → 详情态(per-agent)；否则实时聚合态
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
  const dur = formatDuration(workflow.value.startTime, workflow.value.endTime)
  if (showTranscript.value && transcript.value) {
    const n = transcript.value.agents.length
    return [`${n} agent${n === 1 ? '' : 's'}`, dur].filter(Boolean).join(' · ')
  }
  // 实时态只给 task 级聚合，不谎称 agent 数
  return dur ? `运行中 · ${dur}` : '运行中'
})

// === 展开状态 ===
const expandedKeys = ref<Set<string>>(new Set())
const promptOpen = ref<Set<string>>(new Set())
const toggle = (set: typeof expandedKeys, key: string) => {
  const next = new Set(set.value)
  next.has(key) ? next.delete(key) : next.add(key)
  set.value = next
}
const toggleExpand = (key: string) => toggle(expandedKeys, key)
const togglePrompt = (key: string) => toggle(promptOpen, key)

// === 实时聚合行（task 级，无 model；SDK 限制） ===
const liveStats = (a: WorkflowAgentState): string[] => {
  const out: string[] = []
  const t = formatTokens(a.usage.totalTokens)
  if (t) out.push(`${t} tok`)
  if (typeof a.usage.toolUses === 'number') out.push(`${a.usage.toolUses} 工具`)
  const dur = formatDuration(a.startTime, a.endTime)
  if (dur) out.push(dur)
  return out
}

// === 完成态 transcript 辅助 ===
function parseOutcomeObj(raw?: string): Record<string, unknown> | undefined {
  if (!raw) return undefined
  try {
    const o = JSON.parse(raw)
    return o && typeof o === 'object' && !Array.isArray(o) ? (o as Record<string, unknown>) : undefined
  } catch {
    return undefined
  }
}

const VERDICT_KEY_RE = /compliant|passed|valid|success|^ok$|^pass$/i

function verdictInfo(a: WorkflowTranscriptAgent): { key?: string; value?: boolean } {
  const o = parseOutcomeObj(a.outcome)
  if (!o) return {}
  const key = Object.keys(o).find(k => VERDICT_KEY_RE.test(k) && typeof o[k] === 'boolean')
  return key ? { key, value: o[key] as boolean } : {}
}

function groupTools(tools: { name: string }[]): { name: string; count: number }[] {
  const m = new Map<string, number>()
  for (const t of tools) m.set(t.name, (m.get(t.name) ?? 0) + 1)
  return Array.from(m, ([name, count]) => ({ name, count })).sort((x, y) => y.count - x.count)
}

function prettyOutcome(raw?: string): string {
  const o = parseOutcomeObj(raw)
  return o ? JSON.stringify(o, null, 2) : (raw ?? '')
}

const dismiss = () => {
  resetWorkflow()
  expandedKeys.value = new Set()
  promptOpen.value = new Set()
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

    <div v-if="hasCompletedWorkflow && transcriptStatus === 'loading'" class="wf-hint">正在加载执行详情…</div>
    <div v-else-if="hasCompletedWorkflow && transcriptStatus === 'error'" class="wf-hint error">
      详情加载失败 <button class="wf-retry" @click="loadTranscript(true)">重试</button>
    </div>

    <!-- ========== 详情态：per-agent 卡片（model / verdict / prompt / activity / outcome） ========== -->
    <div v-if="showTranscript && transcript" class="wf-list">
      <div v-for="a in transcript.agents" :key="a.agentId" class="wf-card">
        <div class="card-head" @click="toggleExpand(a.agentId)">
          <span class="dot completed"></span>
          <span class="name">{{ a.label || a.agentId }}</span>
          <span v-if="a.model" class="model-chip">{{ a.model }}</span>
          <svg class="chevron" :class="{ open: expandedKeys.has(a.agentId) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </div>
        <div class="card-sub">
          <span
            v-if="verdictInfo(a).value !== undefined"
            class="verdict"
            :class="verdictInfo(a).value ? 'ok' : 'bad'"
          >{{ verdictInfo(a).value ? '✓' : '✗' }} {{ verdictInfo(a).key }}</span>
          <span v-if="formatTokens(a.totalTokens)" class="stat">{{ formatTokens(a.totalTokens) }} tok</span>
          <span v-if="typeof a.toolUses === 'number'" class="stat">{{ a.toolUses }} 工具</span>
        </div>

        <div v-if="expandedKeys.has(a.agentId)" class="card-detail">
          <!-- Prompt：默认折叠 -->
          <div v-if="a.prompt" class="block">
            <button class="block-toggle" @click="togglePrompt(a.agentId)">
              <span class="caret" :class="{ open: promptOpen.has(a.agentId) }">▸</span> Prompt
            </button>
            <pre v-if="promptOpen.has(a.agentId)" class="block-pre">{{ a.prompt }}</pre>
          </div>
          <!-- Activity：按工具名分组计数 -->
          <div v-if="a.tools.length" class="block">
            <div class="block-label">Activity · {{ a.tools.length }} 步</div>
            <div class="chips">
              <span v-for="g in groupTools(a.tools)" :key="g.name" class="chip">
                {{ g.name }}<span v-if="g.count > 1" class="chip-n">×{{ g.count }}</span>
              </span>
            </div>
          </div>
          <!-- Outcome：最重要，格式化突出 -->
          <div v-if="a.outcome" class="block">
            <div class="block-label">Outcome</div>
            <pre class="block-pre outcome">{{ prettyOutcome(a.outcome) }}</pre>
          </div>
        </div>
      </div>
    </div>

    <!-- ========== 实时聚合态：task 级一行（无 per-agent，SDK 限制） ========== -->
    <div v-else class="wf-list">
      <div v-if="workflowAgents.length === 0" class="wf-hint">workflow 已启动，等待执行…</div>
      <div v-for="a in workflowAgents" :key="a.key" class="wf-card">
        <div class="card-head" @click="toggleExpand(a.key)">
          <span class="dot" :class="a.status"></span>
          <span class="name">{{ a.label }}</span>
          <svg v-if="a.activity.length || a.outcome" class="chevron" :class="{ open: expandedKeys.has(a.key) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
        </div>
        <div class="card-sub">
          <span v-if="a.lastToolName && a.status === 'running'" class="live-tool">{{ a.lastToolName }}</span>
          <span v-for="s in liveStats(a)" :key="s" class="stat">{{ s }}</span>
        </div>
        <div class="track">
          <div v-if="a.status === 'running'" class="bar-pulse"></div>
          <div v-else-if="a.status === 'completed'" class="bar-solid completed"></div>
          <div v-else-if="a.status === 'failed'" class="bar-solid failed"></div>
        </div>
        <div v-if="expandedKeys.has(a.key)" class="card-detail">
          <div v-if="a.activity.length" class="block">
            <div class="block-label">Activity</div>
            <div v-for="ev in a.activity" :key="ev.toolCallId" class="act-row">
              <span class="dot sm" :class="ev.status"></span>
              <span class="act-name">{{ ev.toolName }}</span>
              <span v-if="ev.description" class="act-desc">{{ ev.description }}</span>
            </div>
          </div>
          <div v-if="a.outcome" class="block">
            <div class="block-label">Outcome</div>
            <pre class="block-pre">{{ a.outcome }}</pre>
          </div>
          <div v-else-if="a.status === 'running'" class="still">Still running…</div>
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

/* ---- Header ---- */
.wf-header {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 12px 14px;
  background: var(--surface-highlight);
  border-bottom: 1px solid var(--border-subtle);

  .wf-status-icon {
    width: 18px; height: 18px; flex-shrink: 0;
    display: flex; align-items: center; justify-content: center;
    svg { width: 100%; height: 100%; }
    &.completed { color: var(--accent-success); }
    &.failed { color: var(--accent-danger); }
    .spinner {
      width: 14px; height: 14px;
      border: 2px solid var(--accent-primary); border-top-color: transparent;
      border-radius: 50%; animation: wf-spin 1s linear infinite;
    }
  }
  .wf-title-block { flex: 1; min-width: 0; }
  .wf-title {
    font-size: 0.85rem; font-weight: 600; color: var(--text-primary);
    white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
  }
  .wf-meta { font-size: 0.7rem; color: var(--text-tertiary); font-family: var(--font-mono); margin-top: 2px; }
  .wf-status-badge {
    font-size: 0.62rem; font-weight: 700; text-transform: uppercase;
    padding: 2px 8px; border-radius: 10px; letter-spacing: 0.03em; flex-shrink: 0;
    &.running { background: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.15); color: var(--accent-primary); }
    &.completed { background: rgba(var(--accent-success-rgb), 0.15); color: var(--accent-success); }
    &.failed { background: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.15); color: var(--accent-danger); }
  }
  .wf-dismiss {
    background: transparent; border: none; cursor: pointer; padding: 2px;
    color: var(--text-tertiary); width: 20px; height: 20px;
    display: flex; align-items: center; justify-content: center; border-radius: 4px;
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

/* ---- Cards ---- */
.wf-list { padding: 8px; display: flex; flex-direction: column; gap: 8px; }

.wf-card {
  background: var(--surface-dim);
  border: 1px solid var(--border-dim);
  border-radius: 8px;
  padding: 8px 10px;

  .card-head {
    display: flex; align-items: center; gap: 8px; cursor: pointer;

    .dot {
      width: 8px; height: 8px; border-radius: 50%; flex-shrink: 0; background: var(--text-tertiary);
      &.sm { width: 6px; height: 6px; }
      &.running { background: var(--accent-primary); animation: wf-pulse 1.4s ease-in-out infinite; }
      &.completed { background: var(--accent-success); }
      &.failed { background: var(--accent-danger); }
    }
    .name {
      flex: 1; min-width: 0;
      font-size: 0.82rem; font-weight: 600; color: var(--text-primary);
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .model-chip {
      flex-shrink: 0; font-size: 0.62rem; font-family: var(--font-mono);
      color: var(--text-secondary); background: var(--surface-elevated);
      border: 1px solid var(--border-subtle); padding: 1px 6px; border-radius: 4px;
    }
    .chevron {
      width: 14px; height: 14px; color: var(--text-tertiary); flex-shrink: 0;
      transition: transform 0.2s;
      &.open { transform: rotate(180deg); }
    }
  }

  .card-sub {
    display: flex; align-items: center; flex-wrap: wrap; gap: 6px;
    margin-top: 5px; padding-left: 16px;

    .verdict {
      font-size: 0.66rem; font-weight: 700; padding: 1px 7px; border-radius: 4px;
      font-family: var(--font-mono);
      &.ok { background: rgba(var(--accent-success-rgb), 0.14); color: var(--accent-success); }
      &.bad { background: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.14); color: var(--accent-danger); }
    }
    .stat { font-size: 0.68rem; color: var(--text-tertiary); font-family: var(--font-mono); }
    .live-tool {
      font-size: 0.66rem; color: var(--accent-primary); font-family: var(--font-mono);
      max-width: 140px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
  }

  .track {
    height: 3px; background: var(--border-dim); border-radius: 2px; overflow: hidden;
    position: relative; margin-top: 7px; margin-left: 16px;
    .bar-pulse {
      position: absolute; inset: 0;
      background: linear-gradient(90deg, transparent, var(--accent-primary), transparent);
      transform: translateX(-100%); animation: wf-shimmer 1.5s infinite;
    }
    .bar-solid { width: 100%; height: 100%;
      &.completed { background: var(--accent-success); }
      &.failed { background: var(--accent-danger); }
    }
  }

  /* ---- Expanded detail ---- */
  .card-detail {
    margin-top: 8px; margin-left: 16px;
    display: flex; flex-direction: column; gap: 10px;
    padding-top: 8px; border-top: 1px dashed var(--border-dim);

    .block { display: flex; flex-direction: column; gap: 4px; }
    .block-label {
      font-size: 0.6rem; font-weight: 700; text-transform: uppercase;
      color: var(--text-tertiary); letter-spacing: 0.05em;
    }
    .block-toggle {
      align-self: flex-start; background: transparent; border: none; cursor: pointer; padding: 0;
      font-size: 0.6rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em;
      color: var(--text-tertiary); display: flex; align-items: center; gap: 4px;
      &:hover { color: var(--text-secondary); }
      .caret { transition: transform 0.15s; display: inline-block; &.open { transform: rotate(90deg); } }
    }
    .block-pre {
      margin: 0; font-size: 0.7rem; font-family: var(--font-mono); line-height: 1.5;
      color: var(--text-secondary); white-space: pre-wrap; word-break: break-word;
      max-height: 220px; overflow-y: auto;
      background: var(--surface-elevated); border-radius: 6px; padding: 8px;
      &.outcome { color: var(--text-primary); }
    }
    .chips { display: flex; flex-wrap: wrap; gap: 5px; }
    .chip {
      font-size: 0.66rem; font-family: var(--font-mono); color: var(--text-secondary);
      background: var(--surface-elevated); border: 1px solid var(--border-subtle);
      padding: 1px 7px; border-radius: 10px;
      .chip-n { color: var(--text-tertiary); margin-left: 2px; }
    }
    .act-row {
      display: flex; align-items: center; gap: 6px; font-size: 0.7rem;
      .act-name { font-family: var(--font-mono); color: var(--text-primary); }
      .act-desc { color: var(--text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; flex: 1; min-width: 0; }
    }
    .still { font-size: 0.7rem; color: var(--text-tertiary); font-style: italic; }
  }
}

@keyframes wf-spin { to { transform: rotate(360deg); } }
@keyframes wf-pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
@keyframes wf-shimmer { 0% { transform: translateX(-100%); } 100% { transform: translateX(100%); } }
</style>
