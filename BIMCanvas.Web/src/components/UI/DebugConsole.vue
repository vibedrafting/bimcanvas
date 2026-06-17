<script setup lang="ts">
import { computed } from 'vue';
import { useLogStore } from '../../stores/logStore';
import type { LogLevel, LogRecord } from '../../utils/logger';

const store = useLogStore();

const LEVELS: LogLevel[] = ['error', 'warn', 'info', 'debug'];

const fmtFields = (fields?: Record<string, unknown>): string => {
  if (!fields) return '';
  return Object.entries(fields)
    .filter(([, v]) => v !== undefined)
    .map(([k, v]) => {
      let s = v instanceof Error ? v.message : typeof v === 'object' && v !== null ? JSON.stringify(v) : String(v);
      if (/\s/.test(s)) s = `"${s}"`;
      return `${k}=${s}`;
    })
    .join(' ');
};

const lineText = (l: LogRecord) =>
  `[${l.time}] [Web:${l.domain}] ${l.msg}${fmtFields(l.fields) ? ' ' + fmtFields(l.fields) : ''}`;

const copyAll = () => {
  const text = [...store.visibleLogs].reverse().map(lineText).join('\n');
  navigator.clipboard?.writeText(text);
};

const count = computed(() => store.visibleLogs.length);
</script>

<template>
  <div v-if="store.isVisible" class="debug-console">
    <header>
      <span class="title">Web Log <span class="count">{{ count }}</span></span>
      <div class="actions">
        <button @click="copyAll" title="复制全部">Copy</button>
        <button @click="store.clear()">Clear</button>
        <button @click="store.toggle()">Close</button>
      </div>
    </header>

    <div class="toolbar">
      <div class="group">
        <button
          v-for="lv in LEVELS"
          :key="lv"
          :class="['chip', 'lv-' + lv, { active: store.level === lv }]"
          @click="store.changeLevel(lv)"
        >{{ lv }}</button>
      </div>
      <div class="group">
        <button
          v-for="d in store.domains"
          :key="d"
          :class="['chip', 'dm-' + d, { off: store.hiddenDomains.has(d) }]"
          @click="store.toggleDomain(d)"
        >{{ d }}</button>
      </div>
    </div>

    <div class="logs">
      <div v-if="count === 0" class="empty">No logs</div>
      <div v-for="log in store.visibleLogs" :key="log.id" class="log-entry">
        <span class="time">{{ log.time }}</span>
        <span :class="['domain', 'dm-' + log.domain]">{{ log.domain }}</span>
        <span :class="['message', 'lv-' + log.level]">{{ log.msg }}<span class="fields">{{ fmtFields(log.fields) ? ' ' + fmtFields(log.fields) : '' }}</span></span>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.debug-console {
  position: fixed;
  bottom: 20px;
  right: 20px;
  width: 560px;
  height: 340px;
  background: rgba(0, 0, 0, 0.85);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  z-index: 10000;
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
      .count { color: #888; font-weight: normal; margin-left: 4px; }
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

        &:hover { background: rgba(255, 255, 255, 0.1); color: #fff; }
      }
    }
  }

  .toolbar {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    padding: 6px 12px;
    border-bottom: 1px solid rgba(255, 255, 255, 0.08);

    .group { display: flex; gap: 4px; flex-wrap: wrap; }

    .chip {
      background: transparent;
      border: 1px solid rgba(255, 255, 255, 0.15);
      color: #888;
      padding: 1px 7px;
      border-radius: 10px;
      cursor: pointer;
      font-size: 10px;
      text-transform: uppercase;

      &:hover { color: #fff; }
      &.active { background: rgba(255, 255, 255, 0.15); color: #fff; border-color: rgba(255, 255, 255, 0.4); }
      &.off { opacity: 0.35; text-decoration: line-through; }
    }
  }

  .logs {
    flex: 1;
    overflow-y: auto;
    padding: 8px;
    display: flex;
    flex-direction: column;
    gap: 3px;

    .empty { color: #666; text-align: center; margin-top: 20px; }

    .log-entry {
      display: flex;
      gap: 8px;
      line-height: 1.4;
      border-bottom: 1px solid rgba(255, 255, 255, 0.02);
      padding-bottom: 2px;

      .time { color: #666; flex-shrink: 0; }

      .domain {
        font-weight: bold;
        flex-shrink: 0;
        width: 56px;
      }

      .message {
        word-break: break-all;
        white-space: pre-wrap;
        .fields { color: #9ca3af; }
      }
    }
  }

  // 域配色(与 logger.ts DOMAIN_COLOR 一致)
  .dm-USER { color: #a78bfa; }
  .dm-STREAM { color: #38bdf8; }
  .dm-RECV { color: #34d399; }
  .dm-RENDER { color: #f472b6; }
  .dm-SYS { color: #9ca3af; }

  // 级别配色(消息体)
  .message.lv-error { color: #f87171; }
  .message.lv-warn { color: #facc15; }
  .message.lv-info { color: #d1d5db; }
  .message.lv-debug { color: #9ca3af; }

  // toolbar 级别 chip 着色
  .chip.lv-error.active { background: rgba(248, 113, 113, 0.25); }
  .chip.lv-warn.active { background: rgba(250, 204, 21, 0.25); }
  .chip.lv-info.active { background: rgba(209, 213, 219, 0.2); }
  .chip.lv-debug.active { background: rgba(156, 163, 175, 0.2); }
}
</style>
