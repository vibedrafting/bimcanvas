<script setup lang="ts">
import { ref, watch } from 'vue';
import {
    getChatHistoryService,
    type ChatHistorySessionListItem
} from '../../services/ChatHistoryService';

interface Props {
    visible: boolean;
    projectPath: string;
    // 当前正在查看的历史会话 id（高亮用）；null = 看实时会话
    activeSessionId?: string | null;
}

const props = withDefaults(defineProps<Props>(), { activeSessionId: null });
const emit = defineEmits<{
    (e: 'select', sessionId: string): void;
    (e: 'back-to-live'): void;
    (e: 'close'): void;
}>();

type Phase = 'loading' | 'ready' | 'error';
const phase = ref<Phase>('loading');
const sessions = ref<ChatHistorySessionListItem[]>([]);
const errorMessage = ref('');

const load = async () => {
    phase.value = 'loading';
    errorMessage.value = '';
    if (!props.projectPath) {
        sessions.value = [];
        phase.value = 'ready';
        return;
    }
    try {
        sessions.value = await getChatHistoryService().listSessions(props.projectPath);
        phase.value = 'ready';
    } catch (err: any) {
        errorMessage.value = err?.message || '加载历史会话失败';
        phase.value = 'error';
    }
};

watch(() => props.visible, (visible) => {
    if (visible) load();
}, { immediate: true });

const formatTime = (iso?: string | null): string => {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    return d.toLocaleString();
};

const onSelect = (sessionId: string) => emit('select', sessionId);
</script>

<template>
    <div v-if="visible" class="history-panel-overlay" @click.self="emit('close')">
        <div class="history-panel">
            <div class="panel-header">
                <span class="panel-title">历史对话</span>
                <button class="panel-close" title="关闭" @click="emit('close')">✕</button>
            </div>

            <button class="back-live" :class="{ active: activeSessionId === null }" @click="emit('back-to-live')">
                ↩ 回到当前对话
            </button>

            <div class="panel-body">
                <div v-if="phase === 'loading'" class="panel-hint">加载中…</div>
                <div v-else-if="phase === 'error'" class="panel-error">⚠ {{ errorMessage }}</div>
                <div v-else-if="sessions.length === 0" class="panel-hint">暂无历史会话。</div>
                <ul v-else class="session-list">
                    <li v-for="s in sessions" :key="s.sessionId"
                        class="session-item"
                        :class="{ active: s.sessionId === activeSessionId }"
                        @click="onSelect(s.sessionId)">
                        <div class="session-title">{{ s.title || '（无标题会话）' }}</div>
                        <div class="session-meta">
                            <span class="session-time">{{ formatTime(s.lastActiveAt || s.createdAt) }}</span>
                            <span v-if="s.turnCount" class="session-turns">· {{ s.turnCount }} 轮</span>
                            <span v-if="s.status === 'closed'" class="session-badge">已结束</span>
                        </div>
                    </li>
                </ul>
            </div>
        </div>
    </div>
</template>

<style scoped>
.history-panel-overlay {
    position: fixed;
    inset: 0;
    z-index: 9000;
}

.history-panel {
    position: absolute;
    top: 56px;
    right: 16px;
    width: 320px;
    max-height: 70vh;
    display: flex;
    flex-direction: column;
    background: var(--glass-bg);
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
    border: var(--glass-border);
    border-radius: var(--radius-md, 8px);
    box-shadow: var(--shadow-island);
    overflow: hidden;
}

.panel-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 10px 14px;
    border-bottom: 1px solid var(--border-subtle);
}

.panel-title {
    font-size: 14px;
    font-weight: 600;
    color: var(--text-primary);
}

.panel-close {
    background: none;
    border: none;
    color: var(--text-tertiary);
    cursor: pointer;
    font-size: 14px;
    line-height: 1;
}

.panel-close:hover {
    color: var(--text-primary);
}

.back-live {
    margin: 8px 12px 4px;
    padding: 6px 10px;
    background: var(--surface-card);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-sm, 4px);
    color: var(--text-secondary);
    font-size: 13px;
    cursor: pointer;
    text-align: left;
    transition: border-color 0.15s ease, color 0.15s ease;
}

.back-live:hover,
.back-live.active {
    color: var(--text-primary);
    border-color: var(--accent-blue);
}

.panel-body {
    overflow-y: auto;
    padding: 4px 8px 10px;
}

.panel-hint,
.panel-error {
    padding: 16px;
    font-size: 13px;
    color: var(--text-tertiary);
    text-align: center;
}

.panel-error {
    color: var(--accent-danger);
}

.session-list {
    list-style: none;
    margin: 0;
    padding: 0;
}

.session-item {
    padding: 8px 10px;
    border-radius: var(--radius-sm, 4px);
    cursor: pointer;
    transition: background 0.15s ease;
}

.session-item:hover {
    background: var(--surface-highlight);
}

.session-item.active {
    background: var(--surface-highlight);
    box-shadow: inset 2px 0 0 var(--accent-blue);
}

.session-title {
    font-size: 13px;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.session-meta {
    margin-top: 3px;
    font-size: 11px;
    color: var(--text-tertiary);
    display: flex;
    align-items: center;
    gap: 4px;
}

.session-badge {
    margin-left: auto;
    padding: 0 6px;
    border-radius: 8px;
    background: var(--surface-highlight);
    color: var(--text-tertiary);
    font-size: 10px;
}
</style>
