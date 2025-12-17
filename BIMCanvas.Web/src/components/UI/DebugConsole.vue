<script setup lang="ts">
import { useDebugStore } from '../../stores/debugStore';
import { computed, nextTick, ref, watch } from 'vue';

const store = useDebugStore();
const logContainer = ref<HTMLElement | null>(null);

// Auto-scroll to top when new logs arrive (since we prepend)
// Actually, prepending means top is newest, so we stay at top.

const logClass = (type: string) => {
  switch (type) {
    case 'error': return 'text-red-400';
    case 'warn': return 'text-yellow-400';
    case 'success': return 'text-green-400';
    default: return 'text-gray-300';
  }
};
</script>

<template>
  <div v-if="store.isVisible" class="debug-console">
    <header>
      <span class="title">Debug Console</span>
      <div class="actions">
        <button @click="store.clear()">Clear</button>
        <button @click="store.toggle()">Close</button>
      </div>
    </header>
    <div class="logs" ref="logContainer">
      <div v-if="store.logs.length === 0" class="empty">No logs</div>
      <div v-for="log in store.logs" :key="log.id" class="log-entry">
        <span class="time">[{{ log.timestamp }}]</span>
        <span :class="['type', log.type]">{{ log.type.toUpperCase() }}</span>
        <span :class="['message', logClass(log.type)]">{{ log.message }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.debug-console {
  position: fixed;
  bottom: 20px;
  right: 20px;
  width: 500px;
  height: 300px;
  background: rgba(0, 0, 0, 0.85);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  z-index: 9999;
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5);
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 12px;
  overflow: hidden;

  header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 8px 12px;
    background: rgba(255, 255, 255, 0.05);
    border-bottom: 1px solid rgba(255, 255, 255, 0.1);

    .title {
      color: #fff;
      font-weight: bold;
    }

    .actions {
      display: flex;
      gap: 8px;

      button {
        background: transparent;
        border: 1px solid rgba(255, 255, 255, 0.2);
        color: #aaa;
        padding: 2px 8px;
        border-radius: 4px;
        cursor: pointer;
        font-size: 11px;

        &:hover {
          background: rgba(255, 255, 255, 0.1);
          color: #fff;
        }
      }
    }
  }

  .logs {
    flex: 1;
    overflow-y: auto;
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 4px;

    .empty {
      color: #666;
      text-align: center;
      margin-top: 20px;
    }

    .log-entry {
      display: flex;
      gap: 8px;
      line-height: 1.4;
      border-bottom: 1px solid rgba(255, 255, 255, 0.02);
      padding-bottom: 2px;

      .time {
        color: #666;
        flex-shrink: 0;
      }

      .type {
        font-weight: bold;
        flex-shrink: 0;
        width: 50px;
        
        &.info { color: #3b82f6; }
        &.warn { color: #eab308; }
        &.error { color: #ef4444; }
        &.success { color: #22c55e; }
      }

      .message {
        word-break: break-all;
        white-space: pre-wrap;
        
        &.text-red-400 { color: #f87171; }
        &.text-yellow-400 { color: #facc15; }
        &.text-green-400 { color: #4ade80; }
        &.text-gray-300 { color: #d1d5db; }
      }
    }
  }
}
</style>
