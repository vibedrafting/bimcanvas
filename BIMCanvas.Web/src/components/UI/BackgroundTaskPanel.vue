<script setup lang="ts">
/**
 * BackgroundTaskPanel：Task 页的普通后台任务监控面板（与 wf-panel 卡片语言对齐）。
 *
 * 数据源 = useWorkflowProgress 单例的 backgroundTaskList（SSE 心跳 merge 维护 + 自治 sweeper 收口）。
 * 展示结构：
 *  - 按归属分组（ownerKind：main=主控派发 / workflow=Workflow 派生 / subagent=子代理派生），
 *    组内 running 在前、按 startTime 升序——解决多任务并行时的归属混淆。
 *  - 终态条目折叠进「已结束 N 项」（默认收起），running 常显——监控面板不变成清理负担。
 *  - 行可展开：按需 GET /api/workflows/{sdkSessionId}/tasks/{taskId} 拉详情
 *    （Bash 输出尾部 / Agent 型 outcome / Workflow 内派生精确归属），失败不再是死胡同。
 *  - 终态来源标注：事件确认（实）vs 心跳静默推断（虚，「·推断」弱化后缀）。
 */
import { computed, ref, onMounted, onUnmounted } from 'vue';
import { useWorkflowProgress } from '../../composables/aiCommandCenter/useWorkflowProgress';
import type { BackgroundTaskInfo } from '../../composables/aiCommandCenter/useWorkflowProgress';
import { SERVER_BASE } from '../../config/api';

type TaskEntry = BackgroundTaskInfo & { taskId: string };

const { backgroundTaskList, clearFinishedBackgroundTasks } = useWorkflowProgress();

const runningCount = computed(() =>
  backgroundTaskList.value.filter(t => t.status === 'running').length);
const hasFinished = computed(() =>
  backgroundTaskList.value.some(t => t.status !== 'running'));

// === 分组（归属分类） ===
const OWNER_LABELS: Record<string, string> = {
  main: '主控派发',
  workflow: 'Workflow 派生',
  subagent: '子代理派生'
};
const OWNER_ORDER = ['main', 'subagent', 'workflow', 'unknown'];

interface TaskGroup { ownerKind: string; label: string; tasks: TaskEntry[] }

function groupTasks(tasks: TaskEntry[]): TaskGroup[] {
  const byOwner = new Map<string, TaskEntry[]>();
  for (const t of tasks) {
    const kind = t.ownerKind && OWNER_LABELS[t.ownerKind] ? t.ownerKind : 'unknown';
    if (!byOwner.has(kind)) byOwner.set(kind, []);
    byOwner.get(kind)!.push(t);
  }
  const groups: TaskGroup[] = [];
  for (const kind of OWNER_ORDER) {
    const list = byOwner.get(kind);
    if (!list?.length) continue;
    list.sort((a, b) => a.startTime - b.startTime);
    groups.push({ ownerKind: kind, label: OWNER_LABELS[kind] ?? '其他', tasks: list });
  }
  return groups;
}

const runningGroups = computed(() =>
  groupTasks(backgroundTaskList.value.filter(t => t.status === 'running')));
const finishedTasks = computed(() =>
  backgroundTaskList.value.filter(t => t.status !== 'running'));
const finishedGroups = computed(() => groupTasks(finishedTasks.value));
const finishedOpen = ref(false);

// === 标题清洗：命令行型 description 截取首个可读片段 ===
function displayTitle(t: TaskEntry): string {
  const d = (t.description || '').trim();
  if (!d) return t.taskId;
  // 形如 "Running python -c " import json, ..." 的原始命令描述：截到引号/换行前并限长
  const firstLine = d.split('\n')[0] ?? d;
  return firstLine.length > 80 ? `${firstLine.slice(0, 80)}…` : firstLine;
}

// === 时长 / tokens ===
const nowTick = ref(Date.now());
let timer: ReturnType<typeof setInterval> | null = null;
onMounted(() => { timer = setInterval(() => { nowTick.value = Date.now(); }, 1000); });
onUnmounted(() => { if (timer) clearInterval(timer); });

function formatDuration(ms: number): string {
  if (!isFinite(ms) || ms < 0) return '—';
  const s = Math.floor(ms / 1000);
  if (s < 60) return `${s}s`;
  return `${Math.floor(s / 60)}m ${s % 60}s`;
}
function taskDuration(t: TaskEntry): string {
  // 终态优先用心跳里的权威 durationMs（墙钟含收口延迟，会虚涨）
  if (typeof t.usage?.durationMs === 'number' && t.status !== 'running') return formatDuration(t.usage.durationMs);
  const end = t.status === 'running' ? nowTick.value : (t.endTime ?? nowTick.value);
  return formatDuration(end - t.startTime);
}
function formatTokens(n?: number): string | null {
  if (typeof n !== 'number' || !isFinite(n) || n <= 0) return null;
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k tok` : `${n} tok`;
}

// === 行展开 + 详情按需拉取 ===
interface TaskDetail {
  taskId: string;
  kind?: string | null;
  outputTail?: string | null;
  outputTruncated?: boolean;
  agent?: { label?: string; model?: string; prompt?: string; outcome?: string; tokens?: number } | null;
  originAgentType?: string | null;
  originRunId?: string | null;
}
const expanded = ref<Set<string>>(new Set());
const details = ref<Map<string, { status: 'loading' | 'loaded' | 'error'; data?: TaskDetail }>>(new Map());

async function toggleExpand(t: TaskEntry): Promise<void> {
  const next = new Set(expanded.value);
  if (next.has(t.taskId)) {
    next.delete(t.taskId);
    expanded.value = next;
    return;
  }
  next.add(t.taskId);
  expanded.value = next;
  if (details.value.has(t.taskId) && details.value.get(t.taskId)?.status !== 'error') return;
  if (!t.sdkSessionId) {
    details.value = new Map(details.value).set(t.taskId, { status: 'error' });
    return;
  }
  details.value = new Map(details.value).set(t.taskId, { status: 'loading' });
  try {
    const qs = t.toolUseId ? `?toolUseId=${encodeURIComponent(t.toolUseId)}` : '';
    const resp = await fetch(
      `${SERVER_BASE}/api/workflows/${encodeURIComponent(t.sdkSessionId)}/tasks/${encodeURIComponent(t.taskId)}${qs}`);
    if (!resp.ok) throw new Error(String(resp.status));
    const data = (await resp.json()) as TaskDetail;
    details.value = new Map(details.value).set(t.taskId, { status: 'loaded', data });
  } catch {
    details.value = new Map(details.value).set(t.taskId, { status: 'error' });
  }
}

// 极简状态显示：绿点即"已完成"（含推断收口，不另加文字），仅失败保留文字提示。
function detailHasContent(d?: TaskDetail | null): boolean {
  return !!(d && (d.originAgentType || d.agent?.outcome || (d.outputTail && d.outputTail.trim())));
}
</script>

<template>
  <section class="bg-task-panel">
    <div class="btp-head">
      <span class="btp-title">后台任务</span>
      <span v-if="runningCount > 0" class="btp-count">{{ runningCount }} 运行中</span>
      <button v-if="hasFinished" class="btp-clear" title="清除已结束任务" @click="clearFinishedBackgroundTasks">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
      </button>
    </div>

    <div class="btp-body">
      <!-- 运行中：按归属分组常显 -->
      <template v-for="g in runningGroups" :key="`run-${g.ownerKind}`">
        <div class="btp-group-head">{{ g.label }}</div>
        <div
          v-for="t in g.tasks"
          :key="t.taskId"
          class="btp-card"
          :class="t.status"
        >
          <div class="btp-row" @click="toggleExpand(t)">
            <span class="btp-dot" :class="t.status"></span>
            <span class="btp-desc" :title="t.description || t.taskId">{{ displayTitle(t) }}</span>
            <span v-if="t.lastToolName" class="btp-tag">{{ t.lastToolName }}</span>
            <svg class="btp-chev" :class="{ open: expanded.has(t.taskId) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"></polyline></svg>
          </div>
          <div class="btp-meta">
            <span class="btp-stat">{{ taskDuration(t) }}</span>
            <span v-if="formatTokens(t.usage?.totalTokens)" class="btp-stat">{{ formatTokens(t.usage?.totalTokens) }}</span>
          </div>
          <div v-if="expanded.has(t.taskId)" class="btp-detail">
            <div v-if="details.get(t.taskId)?.status === 'loading'" class="btp-hint">正在加载详情…</div>
            <div v-else-if="details.get(t.taskId)?.status === 'error'" class="btp-hint">暂无可用详情</div>
            <template v-else-if="details.get(t.taskId)?.data">
              <div v-if="details.get(t.taskId)!.data!.originAgentType" class="btp-origin">
                来源：{{ details.get(t.taskId)!.data!.originAgentType }}（Workflow 内）
              </div>
              <div v-if="details.get(t.taskId)!.data!.agent?.outcome" class="btp-block">
                <div class="btp-label">Outcome</div>
                <pre class="btp-pre">{{ details.get(t.taskId)!.data!.agent!.outcome }}</pre>
              </div>
              <div v-if="details.get(t.taskId)!.data!.outputTail?.trim()" class="btp-block">
                <div class="btp-label">Output{{ details.get(t.taskId)!.data!.outputTruncated ? '（尾部）' : '' }}</div>
                <pre class="btp-pre">{{ details.get(t.taskId)!.data!.outputTail }}</pre>
              </div>
              <div v-if="!detailHasContent(details.get(t.taskId)?.data)" class="btp-hint">
                {{ details.get(t.taskId)!.data!.kind === 'bash' ? '该任务无输出' : '暂无可用详情' }}
              </div>
            </template>
          </div>
        </div>
      </template>

      <div v-if="!runningGroups.length && !finishedTasks.length" class="btp-hint">暂无后台任务</div>

      <!-- 已结束：折叠区，默认收起；无运行区时去顶部分隔线（standalone），避免孤立分隔+空带 -->
      <div v-if="finishedTasks.length" class="btp-finished" :class="{ standalone: !runningGroups.length }">
        <div class="btp-finished-head" @click="finishedOpen = !finishedOpen">
          <svg class="btp-chev" :class="{ open: finishedOpen }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="9 18 15 12 9 6"></polyline></svg>
          <span>已结束 {{ finishedTasks.length }} 项</span>
          <span v-if="finishedTasks.some(t => t.status === 'failed')" class="btp-fail-hint">
            {{ finishedTasks.filter(t => t.status === 'failed').length }} 失败
          </span>
        </div>
        <template v-if="finishedOpen">
          <template v-for="g in finishedGroups" :key="`fin-${g.ownerKind}`">
            <div class="btp-group-head dim">{{ g.label }}</div>
            <div
              v-for="t in g.tasks"
              :key="t.taskId"
              class="btp-card finished"
              :class="t.status"
            >
              <div class="btp-row" @click="toggleExpand(t)">
                <span class="btp-dot" :class="t.status"></span>
                <span class="btp-desc" :title="t.description || t.taskId">{{ displayTitle(t) }}</span>
                <span v-if="t.lastToolName" class="btp-tag">{{ t.lastToolName }}</span>
                <svg class="btp-chev" :class="{ open: expanded.has(t.taskId) }" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="6 9 12 15 18 9"></polyline></svg>
              </div>
              <div class="btp-meta">
                <span class="btp-stat">{{ taskDuration(t) }}</span>
                <span v-if="formatTokens(t.usage?.totalTokens)" class="btp-stat">{{ formatTokens(t.usage?.totalTokens) }}</span>
                <span v-if="t.status === 'failed'" class="btp-state failed">失败</span>
              </div>
              <div v-if="expanded.has(t.taskId)" class="btp-detail">
                <div v-if="details.get(t.taskId)?.status === 'loading'" class="btp-hint">正在加载详情…</div>
                <div v-else-if="details.get(t.taskId)?.status === 'error'" class="btp-hint">暂无可用详情</div>
                <template v-else-if="details.get(t.taskId)?.data">
                  <div v-if="details.get(t.taskId)!.data!.originAgentType" class="btp-origin">
                    来源：{{ details.get(t.taskId)!.data!.originAgentType }}（Workflow 内）
                  </div>
                  <div v-if="details.get(t.taskId)!.data!.agent?.outcome" class="btp-block">
                    <div class="btp-label">Outcome</div>
                    <pre class="btp-pre">{{ details.get(t.taskId)!.data!.agent!.outcome }}</pre>
                  </div>
                  <div v-if="details.get(t.taskId)!.data!.outputTail" class="btp-block">
                    <div class="btp-label">Output{{ details.get(t.taskId)!.data!.outputTruncated ? '（尾部）' : '' }}</div>
                    <pre class="btp-pre">{{ details.get(t.taskId)!.data!.outputTail }}</pre>
                  </div>
                  <div v-if="!details.get(t.taskId)!.data!.kind" class="btp-hint">暂无可用详情</div>
                </template>
              </div>
            </div>
          </template>
        </template>
      </div>
    </div>
  </section>
</template>

<style scoped lang="scss">
.bg-task-panel {
  background: var(--surface-elevated, rgba(255, 255, 255, 0.02));
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  overflow: hidden;
  flex-shrink: 0; /* 对齐 wf-panel：在 view-tasks(flex 列+overflow-y:auto)里保持自然高度 */
}

.btp-head {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 11px 14px;
  background: linear-gradient(180deg, var(--surface-highlight), transparent);
  border-bottom: 1px solid var(--border-subtle);

  .btp-title {
    font-size: 0.82rem;
    font-weight: 600;
    color: var(--text-primary);
  }

  .btp-count {
    font-size: 0.7rem;
    color: var(--accent-primary, rgba(79, 172, 254, 0.95));
    background: rgba(var(--accent-primary-rgb, 79, 172, 254), 0.14);
    border-radius: 8px;
    padding: 1px 8px;
  }

  .btp-clear {
    margin-left: auto;
    flex-shrink: 0;
    background: transparent;
    border: none;
    cursor: pointer;
    padding: 2px;
    color: var(--text-tertiary);
    width: 18px;
    height: 18px;
    border-radius: 4px;
    display: flex;
    align-items: center;
    justify-content: center;

    &:hover { background: var(--surface-dim); color: var(--text-secondary); }
    svg { width: 13px; height: 13px; }
  }
}

.btp-body { padding: 8px 10px 10px; display: flex; flex-direction: column; gap: 6px; }

.btp-group-head {
  font-size: 0.62rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-tertiary);
  padding: 4px 4px 0;
  &.dim { opacity: 0.8; }
}

.btp-card {
  background: var(--surface-card, var(--surface-dim));
  border: 1px solid var(--border-dim);
  border-radius: 8px;
  padding: 7px 10px;
  &.failed { border-color: rgba(var(--accent-danger-rgb, 239, 68, 68), 0.35); }
  &.finished { opacity: 0.85; }
}

.btp-row {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.btp-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;

  &.running {
    background: var(--accent-primary, rgba(79, 172, 254, 0.95));
    animation: btp-pulse 1.5s ease-in-out infinite;
  }
  &.completed { background: var(--accent-success, #4ade80); }
  &.failed { background: var(--accent-danger, #ef4444); }
}

@keyframes btp-pulse {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.4; transform: scale(1.2); }
}

.btp-desc {
  flex: 1;
  min-width: 0;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.btp-tag {
  flex-shrink: 0;
  font-size: 0.6rem;
  font-family: var(--font-mono);
  color: var(--text-secondary);
  background: var(--surface-elevated);
  border: 1px solid var(--border-subtle);
  padding: 1px 6px;
  border-radius: 4px;
}

.btp-chev {
  width: 13px;
  height: 13px;
  color: var(--text-tertiary);
  flex-shrink: 0;
  transition: transform 0.2s;
  &.open { transform: rotate(180deg); }
}

.btp-meta {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-top: 4px;
  padding-left: 15px;
  font-variant-numeric: tabular-nums;

  .btp-stat { font-size: 0.66rem; color: var(--text-tertiary); font-family: var(--font-mono); }
  .btp-state {
    font-size: 0.64rem;
    font-weight: 600;
    &.running { color: var(--accent-primary); }
    &.completed { color: var(--accent-success, #4ade80); }
    &.failed { color: var(--accent-danger, #ef4444); }
  }
}

.btp-detail {
  margin-top: 8px;
  margin-left: 15px;
  padding-top: 8px;
  border-top: 1px dashed var(--border-dim);
  display: flex;
  flex-direction: column;
  gap: 8px;

  .btp-origin { font-size: 0.68rem; color: var(--text-secondary); }
  .btp-block { display: flex; flex-direction: column; gap: 4px; }
  .btp-label {
    font-size: 0.58rem;
    font-weight: 700;
    text-transform: uppercase;
    color: var(--text-tertiary);
    letter-spacing: 0.05em;
  }
  .btp-pre {
    margin: 0;
    font-size: 0.68rem;
    font-family: var(--font-mono);
    line-height: 1.5;
    color: var(--text-secondary);
    white-space: pre-wrap;
    word-break: break-word;
    max-height: 200px;
    overflow-y: auto;
    background: var(--surface-elevated);
    border-radius: 6px;
    padding: 8px;
  }
}

.btp-hint {
  font-size: 0.7rem;
  color: var(--text-tertiary);
  font-style: italic;
  padding: 2px 4px;
}

.btp-finished {
  border-top: 1px solid var(--border-dim);
  padding-top: 6px;

  /* 全部已结束（无运行区）：折叠头直接贴面板头，无分隔线/空带 */
  &.standalone {
    border-top: none;
    padding-top: 0;
  }
  display: flex;
  flex-direction: column;
  gap: 6px;

  .btp-finished-head {
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
    padding: 4px;
    border-radius: 6px;
    font-size: 0.72rem;
    font-weight: 600;
    color: var(--text-secondary);

    &:hover { background: var(--surface-dim); }
    .btp-chev { transition: transform 0.18s; transform: rotate(0deg); &.open { transform: rotate(90deg); } }
    .btp-fail-hint { color: var(--accent-danger, #ef4444); font-size: 0.66rem; }
  }
}
</style>
