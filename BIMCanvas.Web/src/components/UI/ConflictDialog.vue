<script setup lang="ts">
import { computed } from 'vue';
import GlassButton from './base/GlassButton.vue';

interface Props {
    visible: boolean;
    projectName: string;
    existingPath: string;
}

const props = defineProps<Props>();

const emit = defineEmits<{
    (e: 'resolve', resolution: 'Overwrite' | 'UseExisting' | 'Cancel'): void;
}>();

const handleOverwrite = () => emit('resolve', 'Overwrite');
const handleUseExisting = () => emit('resolve', 'UseExisting');
const handleCancel = () => emit('resolve', 'Cancel');

// 格式化路径显示（截断过长路径）
const displayPath = computed(() => {
    const path = props.existingPath;
    if (path.length > 60) {
        return '...' + path.slice(-57);
    }
    return path;
});
</script>

<template>
    <Teleport to="body">
        <Transition name="dialog">
            <div v-if="visible" class="conflict-dialog-overlay" @click.self="handleCancel">
                <div class="conflict-dialog">
                    <!-- 标题 -->
                    <div class="dialog-header">
                        <svg class="warning-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                            stroke-width="2">
                            <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                        <h3>项目已存在</h3>
                    </div>

                    <!-- 内容 -->
                    <div class="dialog-content">
                        <p>
                            项目 "<strong>{{ projectName }}</strong>" 已存在于：
                        </p>
                        <p class="path" :title="existingPath">{{ displayPath }}</p>
                        <p class="hint">请选择操作：</p>
                    </div>

                    <!-- 按钮组 -->
                    <div class="dialog-actions">
                        <GlassButton variant="danger" @click="handleOverwrite">
                            <template #icon>
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                                </svg>
                            </template>
                            覆盖
                        </GlassButton>
                        <GlassButton variant="primary" @click="handleUseExisting">
                            <template #icon>
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M5 19a2 2 0 01-2-2V7a2 2 0 012-2h4l2 2h4a2 2 0 012 2v1M5 19h14a2 2 0 002-2v-5a2 2 0 00-2-2H9a2 2 0 00-2 2v5a2 2 0 01-2 2z" />
                                </svg>
                            </template>
                            打开已存在
                        </GlassButton>
                        <GlassButton variant="ghost" @click="handleCancel">
                            取消
                        </GlassButton>
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<style scoped>
.conflict-dialog-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.6);
    backdrop-filter: blur(4px);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 9999;
}

.conflict-dialog {
    background: var(--bg-surface);
    border: 1px solid var(--border-subtle);
    border-radius: 12px;
    padding: 24px;
    min-width: 400px;
    max-width: 500px;
    box-shadow:
        0 8px 32px rgba(0, 0, 0, 0.3),
        0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-bottom: 16px;
}

.warning-icon {
    width: 24px;
    height: 24px;
    color: var(--color-warning, #f59e0b);
    flex-shrink: 0;
}

.dialog-header h3 {
    margin: 0;
    font-size: 18px;
    font-weight: 600;
    color: var(--text-primary);
}

.dialog-content {
    margin-bottom: 20px;
}

.dialog-content p {
    margin: 0 0 8px 0;
    color: var(--text-secondary);
    font-size: 14px;
    line-height: 1.5;
}

.dialog-content strong {
    color: var(--text-primary);
    font-weight: 600;
}

.path {
    font-family: var(--font-mono, 'SF Mono', 'Monaco', 'Consolas', monospace);
    font-size: 12px;
    background: var(--bg-canvas);
    padding: 8px 12px;
    border-radius: 6px;
    word-break: break-all;
    color: var(--text-muted);
    border: 1px solid var(--border-subtle);
}

.hint {
    margin-top: 12px !important;
    color: var(--text-muted) !important;
}

.dialog-actions {
    display: flex;
    gap: 12px;
    justify-content: flex-end;
}

/* 动画 */
.dialog-enter-active,
.dialog-leave-active {
    transition: all 0.2s ease;
}

.dialog-enter-from,
.dialog-leave-to {
    opacity: 0;
}

.dialog-enter-from .conflict-dialog,
.dialog-leave-to .conflict-dialog {
    transform: scale(0.95) translateY(-10px);
    opacity: 0;
}

.dialog-enter-active .conflict-dialog,
.dialog-leave-active .conflict-dialog {
    transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
