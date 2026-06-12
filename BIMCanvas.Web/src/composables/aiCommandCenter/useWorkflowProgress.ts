import { ref, computed } from 'vue'
import type { SubAgentStatus } from '../../types/agent'
import { SERVER_BASE } from '../../config/api'
import { isInteractionChannelConnected } from '../../services/InteractionChannelService'

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
  /** 起始时刻 epoch ms（完成态来自 wf_json startedAt，运行态来自 agent jsonl 首行）；供 phase 跨度计算 */
  startedAt?: number
  /** 结束/最近活动 epoch ms（完成态=startedAt+durationMs，运行态=agent jsonl 末行）；供 phase 跨度计算 */
  endedAt?: number
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
  /** 脚本 log() 叙事线（仅完成态有：wf_json.logs；运行态 journal 无 log 行） */
  logs?: string[]
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
// 阶段预声明（workflow 启动即经 SSE 推到的完整 meta.phases）：运行态据此立即渲染全部阶段，
// 不再依赖闭源 CLI 写的 per-run 脚本副本（运行态常缺失/runId 错位 → 旧实现降级单「阶段 1」）。
const predeclaredPhases = ref<WorkflowPhase[] | null>(null)
const predeclaredName = ref<string | undefined>(undefined)
// 运行态 agent→phase 钉定：agent 首次出现时所在的阶段(从心跳)记下，之后不变。
// 用于把已完成的前序阶段 agent 留在原阶段，而非随当前阶段漂移（运行态精确归属不在增量数据里，靠此近似）。
const liveAgentPhase = ref<Map<string, string>>(new Map())

// 普通后台 Task 集合（非 Workflow 工具发起，心跳 record.isWorkflow===false）：
// 供统一后台活动灯计数（只数 running）与 Task 页 BackgroundTaskPanel 卡片，不进 workflow 阶段树。
// 进=心跳（merge 更新）；完成=background_task.completed 标完成态保留（卡片显示），resetWorkflow 清空。
export interface BackgroundTaskInfo {
  description?: string
  lastToolName?: string
  usage?: { totalTokens?: number; toolUses?: number; durationMs?: number }
  startTime: number
  status: 'running' | 'completed' | 'failed'
  endTime?: number
  /** 最近一条 SSE 心跳到达时刻——reapStaleBackgroundTasks 的静默判据 */
  lastHeartbeat: number
}
const backgroundTasks = ref<Map<string, BackgroundTaskInfo>>(new Map())

const hasActiveWorkflow = computed(() => workflow.value?.status === 'running')
const hasCompletedWorkflow = computed(
  () => !!workflow.value && workflow.value.status !== 'running'
)
const workflowAgents = computed<WorkflowAgentState[]>(() => workflow.value?.agents ?? [])
// 活动灯口径：只数 running——完成态条目保留给 Task 页卡片，不该让灯常亮
const backgroundTaskCount = computed(() =>
  [...backgroundTasks.value.values()].filter(t => t.status === 'running').length)
// Task 页 BackgroundTaskPanel 消费（含完成态）
const backgroundTaskList = computed(() =>
  [...backgroundTasks.value.entries()].map(([taskId, info]) => ({ taskId, ...info })))

function noteBackgroundTask(
  taskId: string,
  data?: { description?: string; lastToolName?: string; usage?: BackgroundTaskInfo['usage'] }
): void {
  const next = new Map(backgroundTasks.value)
  const prev = next.get(taskId)
  next.set(taskId, {
    startTime: prev?.startTime ?? Date.now(),
    status: prev?.status ?? 'running',
    endTime: prev?.endTime,
    description: data?.description ?? prev?.description,
    lastToolName: data?.lastToolName ?? prev?.lastToolName,
    usage: data?.usage ?? prev?.usage,
    lastHeartbeat: Date.now()
  })
  backgroundTasks.value = next
  ensureBgSweeper()
}

/** background_task.completed 收口：标完成态+endTime、保留条目（卡片显示），不删除。 */
function completeBackgroundTask(taskId: string, status: 'completed' | 'failed' = 'completed'): void {
  const prev = backgroundTasks.value.get(taskId)
  if (!prev || prev.status !== 'running') return
  const next = new Map(backgroundTasks.value)
  next.set(taskId, { ...prev, status, endTime: Date.now() })
  backgroundTasks.value = next
}

/**
 * 后台 Task 自治收口：持续的心跳静默 sweeper，与 workflow / turn 生命周期完全解耦。
 *
 * 背景：CLI 不向宿主投递回合内完成的 task_notification（实测 2026-06-11 金凤127
 * chat_20260611_153658.log：通知被注入主控 prompt 流后从队列 remove，宿主收不到），
 * background_task.completed 在"主控回合内消费结果"场景下永远不来。旧实现把静默检查
 * 锚定在 turn.completed/failed 的一次性宽限期——主控长回合（如等 Workflow）期间任务
 * 中途完成时，回合不结束就没人检查，卡片虚挂 running 直到回合边界（实测 2026-06-12
 * 金凤127：3 个后台任务完成后陪 Workflow 多挂 3 分钟）。
 *
 * 现判据：running 期间 SDK TaskProgress 逐 tick 推心跳（秒级），存在 running 条目时
 * sweeper 每 SWEEP_MS 检查一次，静默超 SILENCE_MS → 标 completed；无 running 条目自停。
 * 真终态（含 failed）仍以带外 background_task.completed 为权威（先到先得，
 * completeBackgroundTask 自带幂等守卫）。SSE 断连期间跳过检查——断连时心跳静默
 * 不代表任务结束，误收口比晚收口更糟。
 */
const BG_SWEEP_MS = 5_000
const BG_SILENCE_MS = 15_000
let bgSweeper: ReturnType<typeof setInterval> | null = null
function ensureBgSweeper(): void {
  if (bgSweeper) return
  bgSweeper = setInterval(() => {
    let anyRunning = false
    const now = Date.now()
    const connected = isInteractionChannelConnected()
    for (const [taskId, info] of backgroundTasks.value) {
      if (info.status !== 'running') continue
      if (connected && now - info.lastHeartbeat > BG_SILENCE_MS) {
        completeBackgroundTask(taskId, 'completed')
      } else {
        anyRunning = true
      }
    }
    if (!anyRunning && bgSweeper) {
      clearInterval(bgSweeper)
      bgSweeper = null
    }
  }, BG_SWEEP_MS)
}

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
  predeclaredPhases.value = null
  predeclaredName.value = undefined
}

/**
 * Workflow 阶段预声明（启动即经 SSE 旁路 background_task.progress / kind=workflow_phases 到达）。
 * 也是兜底触发：若 workflow 视图尚未开，首条预声明即开。携带 sdkSessionId/taskId 供后续归并 + tier C。
 */
function onWorkflowPhases(record: {
  taskId?: string | null
  sdkSessionId?: string | null
  workflowName?: string | null
  phases?: { index: number; title: string; detail?: string | null }[] | null
}): void {
  if (workflow.value && workflow.value.status !== 'running') return // 已完成不复活
  if (!workflow.value) {
    startWorkflow({ label: record.workflowName || 'Workflow' })
  }
  if (workflow.value && record.sdkSessionId) workflow.value.sdkSessionId = record.sdkSessionId
  if (workflow.value && record.taskId && !workflow.value.taskId) workflow.value.taskId = record.taskId
  if (record.phases && record.phases.length) {
    // 预声明阶段无 per-agent；补空 agents 以对齐 WorkflowPhase（运行态由 liveAgents 钉定填充）
    predeclaredPhases.value = record.phases.map(p => ({
      index: p.index, title: p.title, detail: p.detail ?? undefined, agents: []
    }))
  }
  if (record.workflowName) predeclaredName.value = record.workflowName
}

/**
 * 从 Workflow 工具结果绑定 run 身份（taskId 最可靠的 in-turn 来源）。
 * 实时进度 SSE（background_task.progress）可能漏传 taskId（只带 sdkSessionId），导致完成后
 * loadTranscript 无 taskId → 服务端 PickRunJson 返回 null → 永久卡实时态（no-toggle / 末位 agent
 * 永远"运行中" / 启发式烂 label）。Workflow 工具结果文本含 "Task ID: <id>"，在 tool.completed
 * 就绑定，确保完成后能精确命中权威 wf_{runId}.json。
 */
function bindWorkflowIdentity(meta: { toolCallId?: string; taskId?: string; sdkSessionId?: string }): void {
  const w = workflow.value
  if (!w) return
  if (meta.toolCallId && w.toolCallId && meta.toolCallId !== w.toolCallId) return
  if (meta.taskId && !w.taskId) w.taskId = meta.taskId
  if (meta.sdkSessionId && !w.sdkSessionId) w.sdkSessionId = meta.sdkSessionId
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
  isWorkflow?: boolean | null
  sdkSessionId?: string | null
  description?: string | null
  lastToolName?: string | null
  usage?: { total_tokens?: number; tool_uses?: number; duration_ms?: number } | null
}): void {
  // 普通后台 Task（Agent 端显式标记 isWorkflow=false）→ 只进活动灯/卡片集合，不开/不喂 workflow 视图。
  // isWorkflow 缺省（旧 Agent）按 workflow 处理——保留"刷新后首条心跳兜底重开视图"的恢复能力。
  if (record.isWorkflow === false) {
    if (record.taskId) {
      noteBackgroundTask(record.taskId, {
        description: record.description ?? undefined,
        lastToolName: record.lastToolName ?? undefined,
        usage: record.usage
          ? {
              totalTokens: record.usage.total_tokens,
              toolUses: record.usage.tool_uses,
              durationMs: record.usage.duration_ms
            }
          : undefined
      })
    }
    return
  }
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
  liveAgentPhase.value = new Map()
  predeclaredPhases.value = null
  predeclaredName.value = undefined
  backgroundTasks.value = new Map()
  if (bgSweeper) { clearInterval(bgSweeper); bgSweeper = null }
}

/** 把尚未钉定的 live agent 钉到当前阶段（首见即定，之后不变）。 */
function pinLiveAgents(
  agents: WorkflowTranscriptAgent[] | undefined,
  currentPhase: string | undefined,
  phases: WorkflowPhase[]
): void {
  if (!agents || agents.length === 0) return
  const fallback = phases[0]?.title ?? ''
  const m = liveAgentPhase.value
  let changed = false
  for (const a of agents) {
    if (a.agentId && !m.has(a.agentId)) {
      m.set(a.agentId, currentPhase || fallback)
      changed = true
    }
  }
  if (changed) liveAgentPhase.value = new Map(m)
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
    // N2：真后台 completed 事件经 SSE fire-and-forget 无回放，断连即丢；轮询拉到终态时回填
    // workflow.status，否则 Task 永久卡 Running。onWorkflowCompleted 自带 status!=='running' 幂等守卫。
    if (!data.live && (data.status === 'failed' || data.status === 'completed')
        && workflow.value?.status === 'running') {
      onWorkflowCompleted({ status: data.status })
    }
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
    liveAgentPhase,
    pinLiveAgents,
    predeclaredPhases,
    predeclaredName,
    hasActiveWorkflow,
    hasCompletedWorkflow,
    workflowAgents,
    // 普通后台 Task 集合（统一后台活动灯 + Task 页卡片）
    backgroundTaskCount,
    backgroundTaskList,
    noteBackgroundTask,
    completeBackgroundTask,
    // trigger predicates
    isWorkflowTool,
    isWorkflowTaskType,
    // ingest
    startWorkflow,
    bindWorkflowIdentity,
    onSubtaskStarted,
    onSubtaskProgress,
    onWorkflowProgress,
    onWorkflowPhases,
    onSubtaskCompleted,
    onToolStarted,
    onToolCompleted,
    onWorkflowCompleted,
    resetWorkflow,
    // tier C
    loadTranscript
  }
}
