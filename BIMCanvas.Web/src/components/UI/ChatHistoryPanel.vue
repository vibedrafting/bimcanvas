<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue';
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
    (e: 'new'): void;
    (e: 'close'): void;
}>();

type Phase = 'loading' | 'ready' | 'error';
const phase = ref<Phase>('loading');
const sessions = ref<ChatHistorySessionListItem[]>([]);
const errorMessage = ref('');
const panelRef = ref<HTMLElement | null>(null);

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

// 点击面板外部关闭。排除"历史"切换按钮(让它自己 toggle,避免开关打架)。
const onDocMouseDown = (e: MouseEvent) => {
    const t = e.target as HTMLElement | null;
    if (!t) return;
    if (panelRef.value && panelRef.value.contains(t)) return;
    if (typeof t.closest === 'function' && t.closest('.history-btn')) return;
    emit('close');
};

watch(() => props.visible, (visible) => {
    if (visible) {
        load();
        // 延一拍注册,避免"打开这一下"的 mousedown 立即把自己关掉。
        setTimeout(() => document.addEventListener('mousedown', onDocMouseDown), 0);
    } else {
        document.removeEventListener('mousedown', onDocMouseDown);
    }
}, { immediate: true });

onBeforeUnmount(() => document.removeEventListener('mousedown', onDocMouseDown));

// 短/相对时间:今天 HH:MM / 昨天 HH:MM / M/D / YYYY/M/D，去掉秒与冗余年份。
const formatTime = (iso?: string | null): string => {
    if (!iso) return '';
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return '';
    const now = new Date();
    const hm = `${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`;
    if (d.toDateString() === now.toDateString()) return `今天 ${hm}`;
    const yest = new Date(now);
    yest.setDate(now.getDate() - 1);
    if (d.toDateString() === yest.toDateString()) return `昨天 ${hm}`;
    if (d.getFullYear() === now.getFullYear()) return `${d.getMonth() + 1}/${d.getDate()}`;
    return `${d.getFullYear()}/${d.getMonth() + 1}/${d.getDate()}`;
};

const onSelect = (sessionId: string) => emit('select', sessionId);
</script>

<template>
    <div v-if="visible" class="history-panel" ref="panelRef">
        <button class="new-chat-btn" @click="emit('new')">
            <span class="new-chat-plus">＋</span>新对话
        </button>

        <div class="dropdown-divider"></div>

        <div class="panel-body">
            <div v-if="phase === 'loading'" class="panel-hint">加载中…</div>
            <div v-else-if="phase === 'error'" class="panel-error">⚠ {{ errorMessage }}</div>
            <div v-else-if="sessions.length === 0" class="panel-hint">暂无历史会话</div>
            <ul v-else class="session-list">
                <li v-for="s in sessions" :key="s.sessionId"
                    class="session-item"
                    :class="{ active: s.sessionId === activeSessionId }"
                    @click="onSelect(s.sessionId)">
                    <span class="session-title">{{ s.title || '（无标题会话）' }}</span>
                    <span class="session-time">{{ formatTime(s.lastActiveAt || s.createdAt) }}</span>
                </li>
            </ul>
        </div>
    </div>
</template>

<style scoped>
/* 配色/填充/形态对齐 .unified-dropdown(分支下拉同款)。挂在 layer-context 下,
   left/right 等距锚定 → 左右严格相等,不受 main-content padding / 滚动条影响。 */
.history-panel {
    position: absolute;
    top: 48px;
    left: 12px;
    right: 12px;
    z-index: 200;
    max-height: 420px;
    display: flex;
    flex-direction: column;
    background: #14141e;
    border: 1px solid rgba(255, 255, 255, 0.1);
    border-radius: 8px;
    padding: 4px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
    overflow: hidden;
}

/* 新对话:同 .dropdown-option.create-new — accent 文字 + 蓝色微底 hover */
.new-chat-btn {
    display: block;
    width: 100%;
    text-align: left;
    padding: 7px 10px;
    border: none;
    border-radius: 4px;
    background: transparent;
    color: var(--accent-blue);
    font-size: 0.8rem;
    cursor: pointer;
    transition: background 0.2s;
}

.new-chat-plus {
    font-weight: 600;
    margin-right: 5px;
}

.new-chat-btn:hover {
    background: rgba(59, 130, 246, 0.1);
}

.dropdown-divider {
    height: 1px;
    background: rgba(255, 255, 255, 0.1);
    margin: 4px 0;
}

.panel-body {
    overflow-y: auto;
}

.panel-hint,
.panel-error {
    padding: 14px 10px;
    font-size: 0.8rem;
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

/* 会话项:紧凑单行 [图标] 标题 …… 时间。同 .dropdown-option 的 hover/选中配色 */
.session-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 7px 10px;
    border-radius: 4px;
    cursor: pointer;
    transition: all 0.2s;
}

.session-item:hover {
    background: rgba(255, 255, 255, 0.05);
}

.session-item:hover .session-title {
    color: var(--text-primary);
}

.session-item.active {
    background: rgba(59, 130, 246, 0.15);
}

.session-item.active .session-title,
.session-item.active .session-icon {
    color: var(--accent-blue);
}

.session-icon {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    width: 15px;
    justify-content: center;
    color: var(--text-secondary);
}

.session-icon svg {
    width: 15px;
    height: 15px;
}

.session-title {
    flex: 1;
    min-width: 0;
    font-size: 0.8rem;
    color: var(--text-secondary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    transition: color 0.2s;
}

.session-time {
    flex-shrink: 0;
    font-size: 0.65rem;
    color: var(--text-tertiary);
    white-space: nowrap;
}
</style>
