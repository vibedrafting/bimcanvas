<script setup lang="ts">
import { computed } from 'vue';
import type { TodoProgressState, TodoProgressItem } from '../../types/aiCommandCenter';

const props = defineProps<{
  progress: TodoProgressState;
}>();

const emit = defineEmits<{
  toggle: [];
}>();

const completedCount = computed(() =>
  props.progress.todos.filter(todo => todo.status === 'completed').length
);

const currentTodo = computed<TodoProgressItem | undefined>(() =>
  props.progress.todos.find(todo => todo.status === 'in_progress')
  ?? props.progress.todos.find(todo => todo.status === 'pending')
);

const currentText = computed(() => {
  if (props.progress.message) {
    return props.progress.message;
  }
  if (!currentTodo.value) {
    return '全部完成';
  }
  return currentTodo.value.status === 'in_progress'
    ? currentTodo.value.activeForm || currentTodo.value.content
    : currentTodo.value.content;
});

const headerText = computed(() =>
  `共 ${props.progress.todos.length} 个任务，已经完成 ${completedCount.value} 个`
);

const collapsedText = computed(() =>
  `${headerText.value}，当前：${currentText.value}`
);

const statusLabel = computed(() => {
  switch (props.progress.status) {
    case 'completed':
      return '全部完成';
    case 'failed':
      return '任务失败';
    case 'interrupted':
      return '已中止';
    case 'ended':
      return '本轮已结束';
    default:
      return currentText.value;
  }
});

const getTodoText = (todo: TodoProgressItem): string =>
  todo.status === 'in_progress' ? todo.activeForm || todo.content : todo.content;
</script>

<template>
  <section class="todo-progress-panel" :class="[progress.status, { collapsed: progress.isCollapsed }]">
    <button class="panel-header" type="button" @click="emit('toggle')" :title="progress.isCollapsed ? '展开任务进度' : '折叠任务进度'">
      <div class="header-left">
        <span class="panel-icon" aria-hidden="true">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8">
            <path d="M9 6h11"></path>
            <path d="M9 12h11"></path>
            <path d="M9 18h11"></path>
            <path d="M4 6h.01"></path>
            <path d="M4 12h.01"></path>
            <path d="M4 18h.01"></path>
          </svg>
        </span>
        <span class="header-summary" v-if="!progress.isCollapsed">{{ headerText }}</span>
        <span class="header-summary" v-else>{{ collapsedText }}</span>
      </div>
      <div class="header-right">
        <span class="status-label" v-if="progress.status !== 'running'">{{ statusLabel }}</span>
        <svg class="collapse-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>
    </button>

    <div class="todo-list" v-if="!progress.isCollapsed">
      <div
        class="todo-row"
        v-for="(todo, index) in progress.todos"
        :key="`${index}-${todo.content}`"
        :class="todo.status"
      >
        <span class="todo-status" aria-hidden="true">
          <svg v-if="todo.status === 'completed'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6">
            <circle cx="12" cy="12" r="8"></circle>
            <polyline points="8.5 12.5 11 15 15.8 9.5"></polyline>
          </svg>
          <span v-else-if="todo.status === 'in_progress'" class="active-ring"></span>
          <span v-else class="pending-dot"></span>
        </span>
        <span class="todo-index">{{ index + 1 }}.</span>
        <span class="todo-text">{{ getTodoText(todo) }}</span>
      </div>
    </div>
  </section>
</template>

<style scoped lang="scss">
.todo-progress-panel {
  --todo-bg: rgba(34, 34, 36, 0.94);
  --todo-border: rgba(255, 255, 255, 0.08);
  --todo-muted: rgba(255, 255, 255, 0.52);
  --todo-soft: rgba(255, 255, 255, 0.68);
  --todo-text: rgba(255, 255, 255, 0.86);
  --todo-accent: var(--accent-primary, #7aa2ff);
  --todo-success: var(--accent-green, #56d364);
  --todo-error: var(--accent-danger, #f87171);

  width: 100%;
  background: var(--todo-bg);
  border: 1px solid var(--todo-border);
  border-radius: 12px;
  box-shadow: 0 14px 36px rgba(0, 0, 0, 0.28);
  overflow: hidden;
  margin: 0 0 10px;
  color: var(--todo-text);
  backdrop-filter: blur(16px);

  &.failed,
  &.interrupted {
    border-color: rgba(248, 113, 113, 0.28);
  }

  &.completed {
    border-color: rgba(86, 211, 100, 0.22);
  }
}

.panel-header {
  width: 100%;
  border: 0;
  background: rgba(255, 255, 255, 0.025);
  color: inherit;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
  min-height: 34px;
  padding: 8px 10px;
  cursor: pointer;
  text-align: left;
}

.header-left,
.header-right {
  display: flex;
  align-items: center;
  min-width: 0;
}

.header-left {
  gap: 7px;
  flex: 1;
}

.header-right {
  gap: 8px;
  flex-shrink: 0;
}

.panel-icon {
  width: 14px;
  height: 14px;
  color: var(--todo-soft);
  display: inline-flex;
  flex-shrink: 0;

  svg {
    width: 14px;
    height: 14px;
  }
}

.header-summary {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--todo-soft);
  font-size: 12px;
  line-height: 18px;
}

.status-label {
  color: var(--todo-muted);
  font-size: 11px;
  line-height: 16px;
  white-space: nowrap;
}

.failed .status-label,
.interrupted .status-label {
  color: var(--todo-error);
}

.completed .status-label {
  color: var(--todo-success);
}

.collapse-icon {
  width: 14px;
  height: 14px;
  color: var(--todo-muted);
  transition: transform 0.18s ease;
  flex-shrink: 0;
}

.collapsed .collapse-icon {
  transform: rotate(-90deg);
}

.todo-list {
  display: flex;
  flex-direction: column;
  gap: 1px;
  padding: 4px 10px 10px;
}

.todo-row {
  display: grid;
  grid-template-columns: 14px auto minmax(0, 1fr);
  align-items: center;
  column-gap: 7px;
  min-height: 22px;
  color: var(--todo-soft);
  font-size: 12px;
  line-height: 18px;

  &.in_progress {
    color: var(--todo-text);
  }

  &.completed {
    color: var(--todo-muted);

    .todo-text {
      text-decoration: line-through;
      text-decoration-thickness: 1px;
    }
  }
}

.todo-status {
  width: 14px;
  height: 14px;
  display: inline-flex;
  align-items: center;
  justify-content: center;

  svg {
    width: 13px;
    height: 13px;
    color: var(--todo-success);
  }
}

.active-ring {
  width: 9px;
  height: 9px;
  border: 1.5px solid rgba(255, 255, 255, 0.18);
  border-top-color: var(--todo-accent);
  border-radius: 50%;
  animation: todo-spin 0.85s linear infinite;
}

.pending-dot {
  width: 8px;
  height: 8px;
  border: 1px solid var(--todo-muted);
  border-radius: 50%;
}

.todo-index {
  color: inherit;
  font-variant-numeric: tabular-nums;
}

.todo-text {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: inherit;
}

@keyframes todo-spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
