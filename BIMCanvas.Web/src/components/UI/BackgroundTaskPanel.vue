<script setup lang="ts">
/**
 * BackgroundTaskPanel：Task 页的普通后台任务监控卡片（workflow 进度面板的简化版）。
 * 数据源 = useWorkflowProgress 单例的 backgroundTaskList（SSE 心跳 merge 维护，含完成态留存）；
 * 与 WorkflowProgressPanel 同款取数方式（组件内直取单例，不走 props）。
 * 时长优先用心跳里的 usage.durationMs（权威），缺省回退 (endTime ?? now) - startTime。
 */
import { computed, ref, onMounted, onUnmounted } from 'vue';
import { useWorkflowProgress } from '../../composables/aiCommandCenter/useWorkflowProgress';

const { backgroundTaskList } = useWorkflowProgress();

const runningCount = computed(() =>
  backgroundTaskList.value.filter(t => t.status === 'running').length);

// running 任务的时长需要随时间跳动：低频 tick（1s）驱动重算，仅面板挂载期间运行。
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

function taskDuration(t: { status: string; startTime: number; endTime?: number; usage?: { durationMs?: number } }): string {
  if (typeof t.usage?.durationMs === 'number' && t.status !== 'running') return formatDuration(t.usage.durationMs);
  const end = t.status === 'running' ? nowTick.value : (t.endTime ?? nowTick.value);
  return formatDuration(end - t.startTime);
}

function formatTokens(n?: number): string | null {
  if (typeof n !== 'number' || !isFinite(n) || n <= 0) return null;
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k tok` : `${n} tok`;
}
</script>

<template>
  <section class="bg-task-panel">
    <div class="btp-head">
      <span class="btp-title">后台任务</span>
      <span v-if="runningCount > 0" class="btp-count">{{ runningCount }} 运行中</span>
    </div>
    <div class="btp-list">
      <div
        v-for="t in backgroundTaskList"
        :key="t.taskId"
        class="btp-item"
        :class="t.status"
      >
        <span class="btp-dot" :class="t.status"></span>
        <div class="btp-main">
          <div class="btp-desc" :title="t.description || t.taskId">{{ t.description || t.taskId }}</div>
          <div class="btp-meta">
            <span class="btp-duration">{{ taskDuration(t) }}</span>
            <span v-if="t.status === 'running' && t.lastToolName" class="btp-tool">· {{ t.lastToolName }}</span>
            <span v-if="formatTokens(t.usage?.totalTokens)" class="btp-tokens">· {{ formatTokens(t.usage?.totalTokens) }}</span>
            <span v-if="t.status === 'completed'" class="btp-state completed">· 已完成</span>
            <span v-else-if="t.status === 'failed'" class="btp-state failed">· 失败</span>
          </div>
        </div>
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
}

.btp-list {
  padding: 6px 8px 8px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.btp-item {
  display: flex;
  align-items: flex-start;
  gap: 9px;
  padding: 7px 8px;
  border-radius: 8px;

  &:hover { background: var(--surface-dim); }
  &.completed, &.failed { opacity: 0.75; }
}

.btp-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  flex-shrink: 0;
  margin-top: 5px;

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

.btp-main { min-width: 0; flex: 1; }

.btp-desc {
  font-size: 0.78rem;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.btp-meta {
  display: flex;
  align-items: center;
  gap: 4px;
  margin-top: 2px;
  font-size: 0.68rem;
  color: var(--text-tertiary);
  font-variant-numeric: tabular-nums;

  .btp-state.completed { color: var(--accent-success, #4ade80); }
  .btp-state.failed { color: var(--accent-danger, #ef4444); }
}
</style>
