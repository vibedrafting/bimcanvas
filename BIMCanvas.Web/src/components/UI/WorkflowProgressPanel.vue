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

const now = ref(Date.now())
let timer: ReturnType<typeof setInterval> | null = null
onMounted(() => {
  timer = setInterval(() => { now.value = Date.now() }, 1000)
  if (hasCompletedWorkflow.value) void loadTranscript()
})
onUnmounted(() => { if (timer) clearInterval(timer) })
watch(hasCompletedWorkflow, (done) => { if (done) void loadTranscript() })

const showTranscript = computed(
  () => hasCompletedWorkflow.value
    && transcriptStatus.value === 'loaded'
    && !!transcript.value
    && transcript.value.phases.length > 0
)

const titleText = computed(() => {
  if (showTranscript.value && transcript.value) {
    return transcript.value.workflowName || workflow.value?.label || 'Workflow'
  }
  return workflow.value?.label || 'Workflow'
})
const subTitleText = computed(() => (showTranscript.value && transcript.value ? transcript.value.summary : ''))

const statusText = computed(() => {
  if (!workflow.value) return ''
  if (workflow.value.status === 'running') return 'Running'
  if (workflow.value.status === 'failed') return 'Failed'
  return 'Completed'
})

const formatTokens = (n: number | undefined | null): string | null => {
  if (typeof n !== 'number' || !isFinite(n)) return null
  if (n < 1000) return String(n)
  if (n < 1_000_000) return `${(n / 1000).toFixed(1)}k`
  return `${(n / 1_000_000).toFixed(1)}M`
}
const formatMs = (ms: number | undefined | null): string | null => {
  if (typeof ms !== 'number' || !isFinite(ms)) return null
  const sec = Math.round(ms / 1000)
  return sec < 60 ? `${sec}s` : `${Math.floor(sec / 60)}m ${sec % 60}s`
}
const formatLiveDuration = (startTime?: number, endTime?: number): string => {
  if (!startTime) return ''
  const sec = Math.max(0, Math.round(((endTime ?? now.value) - startTime) / 1000))
  return sec < 60 ? `${sec}s` : `${Math.floor(sec / 60)}m ${sec % 60}s`
}

const headerMeta = computed(() => {
  if (!workflow.value) return ''
  if (showTranscript.value && transcript.value) {
    const t = transcript.value
    const n = t.agentCount ?? t.phases.reduce((s, p) => s + p.agents.length, 0)
    return [
      `${n} agent${n === 1 ? '' : 's'}`,
      formatMs(t.durationMs),
      formatTokens(t.totalTokens) ? `${formatTokens(t.totalTokens)} tok` : null
    ].filter(Boolean).join(' · ')
  }
  const dur = formatLiveDuration(workflow.value.startTime, workflow.value.endTime)
  return dur ? `运行中 · ${dur}` : '运行中'
})

// === 展开 ===
const expandedKeys = ref<Set<string>>(new Set())
const promptOpen = ref<Set<string>>(new Set())
const toggle = (set: typeof expandedKeys, key: string) => {
  const next = new Set(set.value)
  next.has(key) ? next.delete(key) : next.add(key)
  set.value = next
}
const toggleExpand = (key: string) => toggle(expandedKeys, key)
const togglePrompt = (key: string) => toggle(promptOpen, key)

// === transcript agent 辅助 ===
function parseOutcomeObj(raw?: string): Record<string, unknown> | undefined {
  if (!raw) return undefined
  try {
    const o = JSON.parse(raw)
    return o && typeof o === 'object' && !Array.isArray(o) ? (o as Record<string, unknown>) : undefined
  } catch { return undefined }
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
function agentStateClass(state?: string): string {
  if (state === 'done' || state === 'completed') return 'completed'
  if (state === 'error' || state === 'failed') return 'failed'
  if (state === 'running') return 'running'
  return ''
}

// === 实时聚合行 ===
const liveStats = (a: WorkflowAgentState): string[] => {
  const out: string[] = []
  const t = formatTokens(a.usage.totalTokens)
  if (t) out.push(`${t} tok`)
  if (typeof a.usage.toolUses === 'number') out.push(`${a.usage.toolUses} 工具`)
  const dur = formatLiveDuration(a.startTime, a.endTime)
  if (dur) out.push(dur)
  return out
}

const dismiss = () => {
  resetWorkflow()
  expandedKeys.value = new Set()
  promptOpen.value = new Set()
}
</script>

<template>
  <div v-if="workflow" class="wf-panel" :class="workflow.status">
    <!-- Header -->
    <div class="wf-head">
      <div class="wf-icon" :class="workflow.status">
        <div v-if="workflow.status === 'running'" class="spinner"></div>
        <svg v-else-if="workflow.status === 'completed'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"></polyline></svg>
        <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
      </div>
      <div class="wf-titles">
        <div class="wf-title">{{ titleText }}</div>
        <div class="wf-sub">{{ subTitleText || headerMeta }}</div>
        <div v-if="subTitleText" class="wf-meta">{{ headerMeta }}</div>
      </div>
      <span class="wf-badge" :class="workflow.status">{{ statusText }}</span>
      <button v-if="hasCompletedWorkflow" class="wf-x" title="清除" @click="dismiss">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
      </button>
    </div>

    <div v-if="hasCompletedWorkflow && transcriptStatus === 'loading'" class="wf-hint">正在加载执行详情…</div>
    <div v-else-if="hasCompletedWorkflow && transcriptStatus === 'error'" class="wf-hint error">
      详情加载失败 <button class="wf-retry" @click="loadTranscript(true)">重试</button>
    </div>

    <!-- ============ Phase 树（完成态） ============ -->
    <div v-if="showTranscript && transcript" class="wf-body">
      <div v-for="ph in transcript.phases" :key="ph.index" class="phase">
        <div class="phase-head">
          <span class="phase-rail"></span>
          <span class="phase-title">{{ ph.title || `阶段 ${ph.index}` }}</span>
          <span class="phase-count">{{ ph.agents.length }}</span>
          <span v-if="ph.detail" class="phase-detail">{{ ph.detail }}</span>
        </div>

        <div class="phase-agents">
          <div v-if="!ph.agents.length" class="phase-empty">无 agent</div>
          <div v-for="a in ph.agents" :key="a.agentId" class="agent">
            <div class="agent-head" @click="toggleExpand(a.agentId)">
              <span class="dot" :class="agentStateClass(a.state)"></span>
              <span class="agent-name">{{ a.label || a.agentId }}</span>
              <span v-if="a.model" class="model">{{ a.model }}</span>
              <svg class="chev" :class="{ open: expandedKeys.has(a.agentId) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"></polyline></svg>
            </div>
            <div class="agent-stats">
              <span v-if="verdictInfo(a).value !== undefined" class="verdict" :class="verdictInfo(a).value ? 'ok' : 'bad'">
                {{ verdictInfo(a).value ? '✓' : '✗' }} {{ verdictInfo(a).key }}
              </span>
              <span v-if="formatTokens(a.tokens)" class="stat">{{ formatTokens(a.tokens) }} tok</span>
              <span v-if="typeof a.toolCalls === 'number'" class="stat">{{ a.toolCalls }} 工具</span>
              <span v-if="formatMs(a.durationMs)" class="stat">{{ formatMs(a.durationMs) }}</span>
            </div>

            <div v-if="expandedKeys.has(a.agentId)" class="detail">
              <div v-if="a.prompt" class="block">
                <button class="b-toggle" @click="togglePrompt(a.agentId)">
                  <span class="caret" :class="{ open: promptOpen.has(a.agentId) }">▸</span> Prompt
                </button>
                <pre v-if="promptOpen.has(a.agentId)" class="pre">{{ a.prompt }}</pre>
              </div>
              <div v-if="a.tools.length" class="block">
                <div class="b-label">Activity · {{ a.tools.length }} 步</div>
                <div class="chips">
                  <span v-for="g in groupTools(a.tools)" :key="g.name" class="chip">{{ g.name }}<span v-if="g.count > 1" class="chip-n">×{{ g.count }}</span></span>
                </div>
              </div>
              <div v-if="a.outcome" class="block">
                <div class="b-label">Outcome</div>
                <pre class="pre out">{{ prettyOutcome(a.outcome) }}</pre>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- ============ 实时聚合（运行中，task 级） ============ -->
    <div v-else class="wf-body">
      <div v-if="workflowAgents.length === 0" class="wf-hint">workflow 已启动，等待执行…</div>
      <div v-for="a in workflowAgents" :key="a.key" class="agent live">
        <div class="agent-head" @click="toggleExpand(a.key)">
          <span class="dot" :class="a.status"></span>
          <span class="agent-name">{{ a.label }}</span>
          <svg v-if="a.activity.length || a.outcome" class="chev" :class="{ open: expandedKeys.has(a.key) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"></polyline></svg>
        </div>
        <div class="agent-stats">
          <span v-if="a.lastToolName && a.status === 'running'" class="live-tool">{{ a.lastToolName }}</span>
          <span v-for="s in liveStats(a)" :key="s" class="stat">{{ s }}</span>
        </div>
        <div class="track">
          <div v-if="a.status === 'running'" class="bar-pulse"></div>
          <div v-else-if="a.status === 'completed'" class="bar-solid completed"></div>
          <div v-else-if="a.status === 'failed'" class="bar-solid failed"></div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.wf-panel {
  background: var(--surface-elevated, rgba(255,255,255,0.02));
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  overflow: hidden;

  &.running { border-color: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.45); }
  &.completed { border-color: rgba(var(--accent-success-rgb), 0.35); }
  &.failed { border-color: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.45); }
}

/* ---- Header ---- */
.wf-head {
  display: flex; align-items: flex-start; gap: 10px;
  padding: 13px 14px;
  background: linear-gradient(180deg, var(--surface-highlight), transparent);
  border-bottom: 1px solid var(--border-subtle);

  .wf-icon {
    width: 18px; height: 18px; flex-shrink: 0; margin-top: 1px;
    display: flex; align-items: center; justify-content: center;
    svg { width: 100%; height: 100%; }
    &.completed { color: var(--accent-success); }
    &.failed { color: var(--accent-danger); }
    .spinner { width: 14px; height: 14px; border: 2px solid var(--accent-primary); border-top-color: transparent; border-radius: 50%; animation: wf-spin 1s linear infinite; }
  }
  .wf-titles { flex: 1; min-width: 0; }
  .wf-title { font-size: 0.88rem; font-weight: 650; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .wf-sub { font-size: 0.72rem; color: var(--text-secondary); margin-top: 2px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .wf-meta { font-size: 0.68rem; color: var(--text-tertiary); font-family: var(--font-mono); margin-top: 3px; }
  .wf-badge {
    flex-shrink: 0; font-size: 0.6rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em;
    padding: 3px 8px; border-radius: 6px;
    &.running { background: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.16); color: var(--accent-primary); }
    &.completed { background: rgba(var(--accent-success-rgb), 0.16); color: var(--accent-success); }
    &.failed { background: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.16); color: var(--accent-danger); }
  }
  .wf-x {
    flex-shrink: 0; background: transparent; border: none; cursor: pointer; padding: 2px;
    color: var(--text-tertiary); width: 18px; height: 18px; border-radius: 4px;
    display: flex; align-items: center; justify-content: center;
    &:hover { background: var(--surface-dim); color: var(--text-secondary); }
    svg { width: 13px; height: 13px; }
  }
}

.wf-hint {
  padding: 12px 14px; font-size: 0.74rem; color: var(--text-tertiary); font-style: italic;
  &.error { color: var(--accent-danger); font-style: normal; }
  .wf-retry { margin-left: 8px; background: transparent; border: 1px solid var(--border-subtle); border-radius: 4px; color: var(--text-secondary); cursor: pointer; padding: 1px 8px; font-size: 0.7rem; &:hover { background: var(--surface-dim); } }
}

.wf-body { padding: 6px 0 8px; }

/* ---- Phase ---- */
.phase { padding: 4px 0; }
.phase-head {
  display: flex; align-items: center; gap: 8px; padding: 6px 14px 4px;
  .phase-rail { width: 3px; height: 12px; border-radius: 2px; background: var(--accent-primary); opacity: 0.7; flex-shrink: 0; }
  .phase-title { font-size: 0.72rem; font-weight: 700; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }
  .phase-count {
    font-size: 0.6rem; font-weight: 700; font-family: var(--font-mono);
    background: var(--surface-dim); color: var(--text-tertiary); border-radius: 8px; padding: 0 6px; min-width: 16px; text-align: center;
  }
  .phase-detail { font-size: 0.66rem; color: var(--text-tertiary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
}
.phase-agents { padding: 0 10px 0 18px; display: flex; flex-direction: column; gap: 6px; }
.phase-empty { font-size: 0.7rem; color: var(--text-tertiary); font-style: italic; padding: 2px 6px; }

/* ---- Agent card ---- */
.agent {
  background: var(--surface-card, var(--surface-dim));
  border: 1px solid var(--border-dim);
  border-radius: 8px;
  padding: 7px 10px;
  &.live { margin: 0 8px; }

  .agent-head {
    display: flex; align-items: center; gap: 8px; cursor: pointer;
    .dot {
      width: 7px; height: 7px; border-radius: 50%; flex-shrink: 0; background: var(--text-tertiary);
      &.running { background: var(--accent-primary); animation: wf-pulse 1.4s ease-in-out infinite; }
      &.completed { background: var(--accent-success); }
      &.failed { background: var(--accent-danger); }
    }
    .agent-name { flex: 1; min-width: 0; font-size: 0.8rem; font-weight: 600; color: var(--text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .model { flex-shrink: 0; font-size: 0.6rem; font-family: var(--font-mono); color: var(--text-secondary); background: var(--surface-elevated); border: 1px solid var(--border-subtle); padding: 1px 6px; border-radius: 4px; }
    .chev { width: 13px; height: 13px; color: var(--text-tertiary); flex-shrink: 0; transition: transform 0.2s; &.open { transform: rotate(180deg); } }
  }

  .agent-stats {
    display: flex; align-items: center; flex-wrap: wrap; gap: 6px; margin-top: 5px; padding-left: 15px;
    .verdict {
      font-size: 0.64rem; font-weight: 700; font-family: var(--font-mono); padding: 1px 7px; border-radius: 4px;
      &.ok { background: rgba(var(--accent-success-rgb), 0.14); color: var(--accent-success); }
      &.bad { background: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.14); color: var(--accent-danger); }
    }
    .stat { font-size: 0.66rem; color: var(--text-tertiary); font-family: var(--font-mono); }
    .live-tool { font-size: 0.64rem; color: var(--accent-primary); font-family: var(--font-mono); max-width: 150px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  }

  .track {
    height: 3px; background: var(--border-dim); border-radius: 2px; overflow: hidden; position: relative; margin-top: 7px; margin-left: 15px;
    .bar-pulse { position: absolute; inset: 0; background: linear-gradient(90deg, transparent, var(--accent-primary), transparent); transform: translateX(-100%); animation: wf-shimmer 1.5s infinite; }
    .bar-solid { width: 100%; height: 100%; &.completed { background: var(--accent-success); } &.failed { background: var(--accent-danger); } }
  }

  .detail {
    margin-top: 8px; margin-left: 15px; padding-top: 8px; border-top: 1px dashed var(--border-dim);
    display: flex; flex-direction: column; gap: 10px;
    .block { display: flex; flex-direction: column; gap: 4px; }
    .b-label { font-size: 0.58rem; font-weight: 700; text-transform: uppercase; color: var(--text-tertiary); letter-spacing: 0.05em; }
    .b-toggle {
      align-self: flex-start; background: transparent; border: none; cursor: pointer; padding: 0;
      font-size: 0.58rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-tertiary);
      display: flex; align-items: center; gap: 4px; &:hover { color: var(--text-secondary); }
      .caret { transition: transform 0.15s; &.open { transform: rotate(90deg); } }
    }
    .pre {
      margin: 0; font-size: 0.68rem; font-family: var(--font-mono); line-height: 1.5; color: var(--text-secondary);
      white-space: pre-wrap; word-break: break-word; max-height: 200px; overflow-y: auto;
      background: var(--surface-elevated); border-radius: 6px; padding: 8px;
      &.out { color: var(--text-primary); }
    }
    .chips { display: flex; flex-wrap: wrap; gap: 5px; }
    .chip {
      font-size: 0.64rem; font-family: var(--font-mono); color: var(--text-secondary);
      background: var(--surface-elevated); border: 1px solid var(--border-subtle); padding: 1px 7px; border-radius: 10px;
      .chip-n { color: var(--text-tertiary); margin-left: 2px; }
    }
  }
}

@keyframes wf-spin { to { transform: rotate(360deg); } }
@keyframes wf-pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.35; } }
@keyframes wf-shimmer { 0% { transform: translateX(-100%); } 100% { transform: translateX(100%); } }
</style>
