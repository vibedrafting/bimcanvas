import { ref, computed } from 'vue'
import type { SubAgentStatus } from '../../types/agent'
import { SERVER_BASE } from '../../config/api'

// ============================================================================
// Workflow 进度状态层（平台级 · Task 页可视化）
//
// 设计：模块级单例。被 useChatStream（喂已解析的 SSE 事件）、AICommandCenter
// （读状态驱动 UI）、useBackgroundTask（标记完成）共享同一实例，不走 prop 透传。
//
// 数据来源分两态（见任务包承重点结论）：
//   - 进行中（tier A）：纯前端重组现有 subtask.progress / tool.* / subtask.completed
//     聚合成 per-agent 行（label/状态/tokens/tools/last_tool/耗时 + 工具时间线）。
//     SDK 实时流不给 per-agent 模型名（types.py 实证），故进行中无 model。
//   - 完成后（tier C）：按 sdkSessionId 读 transcript，补齐 model/prompt/outcome。
//
// 触发信号（可配置 + 稳健默认）：SDK Workflow 工具调用的工具名 / task_started 的
// task_type 取值在 bundled CLI 内部，静态拿不到、未经 live 探针实测（任务包 Step 0
// 决议：不自主跑 live，做成可配置，真实取值由用户端到端测时回填）。默认匹配工具名
// 'Workflow'，可经 localStorage 覆盖。
// ============================================================================

const DEFAULT_WORKFLOW_TOOL_NAMES = ['Workflow']
const DEFAULT_WORKFLOW_TASK_TYPES = ['workflow']

function readOverride(key: string, fallback: string[]): string[] {
  try {
    const raw = typeof localStorage !== 'undefined' ? localStorage.getItem(key) : null
    if (raw) {
      const parsed = JSON.parse(raw)
      if (Array.isArray(parsed) && parsed.every(x => typeof x === 'string')) {
        return parsed as string[]
      }
    }
  } catch {
    /* ignore malformed override */
  }
  return fallback
}

const workflowToolNames = readOverride('bimcanvas.workflowToolNames', DEFAULT_WORKFLOW_TOOL_NAMES)
const workflowTaskTypes = readOverride('bimcanvas.workflowTaskTypes', DEFAULT_WORKFLOW_TASK_TYPES)

export type WorkflowStatus = 'running' | 'completed' | 'failed'

export interface WorkflowToolEvent {
  toolCallId: string
  toolName: string
  description?: string
  status: 'running' | 'completed' | 'failed'
  startTime: number
  endTime?: number
}

export interface WorkflowAgentState {
  /** 聚合 key：优先 subtaskId，缺失时退到 taskId（SDK 实时流对 workflow 内 agent 可能只给 taskId） */
  key: string
  label: string
  type?: string
  status: SubAgentStatus
  description?: string
  lastToolName?: string
  usage: { totalTokens?: number; toolUses?: number; durationMs?: number }
  /** 工具时间线（best-effort：仅当 tool 事件带 subtaskId 时可归属） */
  activity: WorkflowToolEvent[]
  outcome?: string
  startTime: number
  endTime?: number
}

/** tier C：完成后从 orchestrator 运行态(wf_*.json)读出的 CLI 风 phase 树 */
export interface WorkflowTranscriptAgent {
  agentId: string
  label?: string
  model?: string
  state?: string
  tokens?: number
  toolCalls?: number
  durationMs?: number
  prompt?: string
  outcome?: string
  tools: { name: string; input?: string }[]
}

export interface WorkflowPhase {
  index: number
  title: string
  detail?: string
  agents: WorkflowTranscriptAgent[]
}

export interface WorkflowTranscript {
  sdkSessionId: string
  runId?: string
  workflowName?: string
  summary?: string
  status?: string
  durationMs?: number
  totalTokens?: number
  agentCount?: number
  /** true=运行态(增量 transcript)：phases 仅作步进条、agent 在 liveAgents 扁平列表 */
  live?: boolean
  phases: WorkflowPhase[]
  liveAgents?: WorkflowTranscriptAgent[]
}

export interface WorkflowState {
  status: WorkflowStatus
  label: string
  /** 触发它的 workflow 工具调用 id */
  toolCallId?: string
  taskId?: string
  sdkSessionId?: string
  startTime: number
  endTime?: number
  agents: WorkflowAgentState[]
}

type TranscriptStatus = 'idle' | 'loading' | 'loaded' | 'error'

// === 模块级单例状态 ===
const workflow = ref<WorkflowState | null>(null)
const transcript = ref<WorkflowTranscript | null>(null)
const transcriptStatus = ref<TranscriptStatus>('idle')

const hasActiveWorkflow = computed(() => workflow.value?.status === 'running')
const hasCompletedWorkflow = computed(
  () => !!workflow.value && workflow.value.status !== 'running'
)
const workflowAgents = computed<WorkflowAgentState[]>(() => workflow.value?.agents ?? [])

function isRunning(): boolean {
  return workflow.value?.status === 'running'
}

function findAgent(key: string): WorkflowAgentState | undefined {
  return workflow.value?.agents.find(a => a.key === key)
}

function ensureAgent(key: string, init?: Partial<WorkflowAgentState>): WorkflowAgentState | null {
  if (!workflow.value) return null
  let agent = findAgent(key)
  if (!agent) {
    agent = {
      key,
      label: init?.label ?? init?.description ?? 'Agent',
      type: init?.type,
      status: 'running',
      usage: {},
      activity: [],
      startTime: Date.now()
    }
    workflow.value.agents.push(agent)
  }
  return agent
}

// === 触发判定（供 useChatStream 调用） ===
function isWorkflowTool(toolName?: string | null): boolean {
  return !!toolName && workflowToolNames.includes(toolName)
}
function isWorkflowTaskType(taskType?: string | null): boolean {
  return !!taskType && workflowTaskTypes.includes(taskType)
}

// === Ingest（全部在 status==='running' 时才聚合；非活跃 workflow 期的 subtask 事件被忽略） ===

/** workflow 工具被主控调用（tool.started，无 subtaskId，工具名命中触发信号） */
function startWorkflow(meta: { toolCallId?: string; label?: string; sdkSessionId?: string }): void {
  if (workflow.value?.status === 'running') return // 已在跑，不重复开
  workflow.value = {
    status: 'running',
    label: meta.label || 'Workflow',
    toolCallId: meta.toolCallId,
    sdkSessionId: meta.sdkSessionId,
    startTime: Date.now(),
    agents: []
  }
  transcript.value = null
  transcriptStatus.value = 'idle'
}

function onSubtaskStarted(key: string, name?: string, type?: string): void {
  if (!isRunning()) return
  ensureAgent(key, { label: name, type })
}

function onSubtaskProgress(
  key: string,
  data: { description?: string; lastToolName?: string; usage?: WorkflowAgentState['usage'] }
): void {
  if (!isRunning()) return
  const agent = ensureAgent(key, { description: data.description })
  if (!agent) return
  if (data.description !== undefined) {
    agent.description = data.description
    if (agent.label === 'Agent' && data.description) agent.label = data.description
  }
  if (data.lastToolName !== undefined) agent.lastToolName = data.lastToolName
  if (data.usage) agent.usage = { ...agent.usage, ...data.usage }
}

/**
 * 后台 Workflow 进度（detach 后经 SSE 旁路 background_task.progress 到达）。
 * 也是兜底触发：若 in-turn 的 workflow tool.started 被错过，首条进度即开 workflow 视图。
 * SDK 实时只给 task 级聚合，故聚合到以 taskId 为 key 的单行；per-agent 详情完成后读 transcript。
 */
function onWorkflowProgress(record: {
  taskId?: string | null
  sdkSessionId?: string | null
  description?: string | null
  lastToolName?: string | null
  usage?: { total_tokens?: number; tool_uses?: number; duration_ms?: number } | null
}): void {
  // 已完成的 workflow 收到迟到进度 → 不复活
  if (workflow.value && workflow.value.status !== 'running') return
  if (!workflow.value) {
    startWorkflow({ label: 'Workflow' })
  }
  if (workflow.value && record.sdkSessionId) {
    workflow.value.sdkSessionId = record.sdkSessionId
  }
  if (workflow.value && record.taskId && !workflow.value.taskId) {
    workflow.value.taskId = record.taskId
  }
  const key = record.taskId || 'workflow'
  const usage = record.usage
    ? {
        totalTokens: record.usage.total_tokens,
        toolUses: record.usage.tool_uses,
        durationMs: record.usage.duration_ms
      }
    : undefined
  onSubtaskProgress(key, {
    description: record.description ?? undefined,
    lastToolName: record.lastToolName ?? undefined,
    usage
  })
}

function onSubtaskCompleted(key: string, data: { success?: boolean; summary?: string }): void {
  if (!isRunning()) return
  const agent = ensureAgent(key)
  if (!agent) return
  agent.status = data.success === false ? 'failed' : 'completed'
  agent.endTime = Date.now()
  if (data.summary) agent.outcome = data.summary
}

function onToolStarted(
  subtaskKey: string,
  tool: { toolCallId: string; toolName: string; description?: string }
): void {
  if (!isRunning() || !subtaskKey) return
  const agent = ensureAgent(subtaskKey)
  if (!agent) return
  if (agent.activity.some(e => e.toolCallId === tool.toolCallId)) return
  agent.activity.push({
    toolCallId: tool.toolCallId,
    toolName: tool.toolName,
    description: tool.description,
    status: 'running',
    startTime: Date.now()
  })
}

function onToolCompleted(toolCallId: string, data: { success?: boolean }): void {
  if (!isRunning() || !workflow.value) return
  for (const agent of workflow.value.agents) {
    const ev = agent.activity.find(e => e.toolCallId === toolCallId)
    if (ev) {
      ev.status = data.success === false ? 'failed' : 'completed'
      ev.endTime = Date.now()
      return
    }
  }
}

/**
 * workflow 完成（来自 background_task.completed 旁路事件）。
 * MVP：有活跃 workflow 时即收口（单 workflow 假设）。携带 sdkSessionId 供 tier C 拉 transcript。
 */
function onWorkflowCompleted(record: {
  taskId?: string | null
  status?: string | null
  sdkSessionId?: string | null
  summary?: string | null
}): void {
  if (!workflow.value || workflow.value.status !== 'running') return
  const failed = record.status === 'failed' || record.status === 'stopped'
  workflow.value.status = failed ? 'failed' : 'completed'
  workflow.value.endTime = Date.now()
  if (record.sdkSessionId) workflow.value.sdkSessionId = record.sdkSessionId
  if (record.taskId) workflow.value.taskId = record.taskId
  for (const agent of workflow.value.agents) {
    if (agent.status === 'running') {
      agent.status = failed ? 'failed' : 'completed'
      agent.endTime = Date.now()
    }
  }
}

function resetWorkflow(): void {
  workflow.value = null
  transcript.value = null
  transcriptStatus.value = 'idle'
}

// === tier C：读 transcript ===
// 完成态：按需拉一次（force=false 有 loaded 守卫）。
// 运行态：面板按心跳静默重拉（silent=true）——orchestrator 若增量写 wf_*.json，运行中即得完整 phase 树；
// 若只在完成时写，空结果不覆盖、不降级，运行中维持 task 级聚合（best-effort，优雅降级）。
async function loadTranscript(force = false, silent = false): Promise<void> {
  const sid = workflow.value?.sdkSessionId
  if (!sid) return
  if (!force && (transcriptStatus.value === 'loading' || transcriptStatus.value === 'loaded')) return
  if (!silent) transcriptStatus.value = 'loading'
  try {
    const taskId = workflow.value?.taskId
    const qs = taskId ? `?taskId=${encodeURIComponent(taskId)}` : ''
    const resp = await fetch(`${SERVER_BASE}/api/workflows/${encodeURIComponent(sid)}/transcript${qs}`)
    if (!resp.ok) {
      if (!silent) transcriptStatus.value = 'error'
      return
    }
    const data = (await resp.json()) as WorkflowTranscript
    // 静默(运行中)刷新：文件还没写出/全空时不覆盖已有、不降级
    const empty = !data || ((data.phases?.length ?? 0) === 0 && (data.liveAgents?.length ?? 0) === 0)
    if (silent && empty) return
    transcript.value = data
    transcriptStatus.value = 'loaded'
  } catch {
    if (!silent) transcriptStatus.value = 'error'
  }
}

export function useWorkflowProgress() {
  return {
    // state
    workflow,
    transcript,
    transcriptStatus,
    hasActiveWorkflow,
    hasCompletedWorkflow,
    workflowAgents,
    // trigger predicates
    isWorkflowTool,
    isWorkflowTaskType,
    // ingest
    startWorkflow,
    onSubtaskStarted,
    onSubtaskProgress,
    onWorkflowProgress,
    onSubtaskCompleted,
    onToolStarted,
    onToolCompleted,
    onWorkflowCompleted,
    resetWorkflow,
    // tier C
    loadTranscript
  }
}
