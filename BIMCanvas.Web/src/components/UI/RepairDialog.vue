<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import GlassButton from './base/GlassButton.vue';
import {
    ProjectHealthService,
    type ProjectInspectionReport,
    type ProjectRepairReport
} from '../../services/ProjectHealthService';

interface Props {
    visible: boolean;
    projectName: string;
    folderPath: string;
}

const props = defineProps<Props>();
const emit = defineEmits<{ (e: 'closed'): void }>();

type Phase = 'inspecting' | 'preview' | 'repairing' | 'done' | 'error';

const phase = ref<Phase>('inspecting');
const inspection = ref<ProjectInspectionReport | null>(null);
const repairResult = ref<ProjectRepairReport | null>(null);
const errorMessage = ref<string>('');

const ISSUES_PER_CHECK_PREVIEW = 10;

// 可见性变化时自动 inspect
watch(() => props.visible, async (visible) => {
    if (!visible) return;
    phase.value = 'inspecting';
    inspection.value = null;
    repairResult.value = null;
    errorMessage.value = '';
    try {
        inspection.value = await ProjectHealthService.inspect(props.folderPath);
        phase.value = 'preview';
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.message || err?.message || '检查失败';
        phase.value = 'error';
    }
}, { immediate: true });

const totalIssues = computed(() => inspection.value?.totalIssues ?? 0);

// preview 阶段把 issues 分组截断展示
const previewGroups = computed(() => {
    if (!inspection.value) return [];
    return inspection.value.checks
        .filter(c => c.issues.length > 0 || c.errors.length > 0)
        .map(check => ({
            checkId: check.checkId,
            description: check.checkDescription,
            issues: check.issues.slice(0, ISSUES_PER_CHECK_PREVIEW),
            extraCount: Math.max(0, check.issues.length - ISSUES_PER_CHECK_PREVIEW),
            errors: check.errors
        }));
});

const shortHash = computed(() => {
    const h = repairResult.value?.snapshotCommitHash;
    return h ? h.slice(0, 8) : null;
});

const repairTotals = computed(() => {
    if (!repairResult.value) return { migrated: 0, skipped: 0, errors: 0 };
    let migrated = 0, skipped = 0, errors = 0;
    for (const c of repairResult.value.checks) {
        migrated += c.migrated.length;
        skipped += c.skipped.length;
        errors += c.errors.length;
    }
    return { migrated, skipped, errors };
});

const handleConfirm = async () => {
    phase.value = 'repairing';
    try {
        repairResult.value = await ProjectHealthService.repair(props.folderPath);
        phase.value = 'done';
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.message || err?.message || '修复失败';
        phase.value = 'error';
    }
};

const handleClose = () => emit('closed');
</script>

<template>
    <Teleport to="body">
        <Transition name="dialog">
            <div v-if="visible" class="repair-dialog-overlay" @click.self="handleClose">
                <div class="repair-dialog">
                    <div class="dialog-header">
                        <svg class="wrench-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                            stroke-width="2">
                            <path d="M14.7 6.3a1 1 0 000 1.4l1.6 1.6a1 1 0 001.4 0l3.77-3.77a6 6 0 01-7.94 7.94l-6.91 6.91a2.12 2.12 0 01-3-3l6.91-6.91a6 6 0 017.94-7.94l-3.76 3.76z" />
                        </svg>
                        <h3>修复项目「{{ projectName }}」</h3>
                    </div>

                    <!-- inspecting -->
                    <div v-if="phase === 'inspecting'" class="dialog-content">
                        <p class="hint">检查中...</p>
                    </div>

                    <!-- preview -->
                    <div v-else-if="phase === 'preview'" class="dialog-content">
                        <template v-if="totalIssues === 0">
                            <p>项目 schema 已是最新版本，无需修复。</p>
                        </template>
                        <template v-else>
                            <p>发现 <strong>{{ totalIssues }}</strong> 个待修复问题。修复前 Server 会自动 git 存档，可随时
                                <code>git reset</code> 回滚。
                            </p>
                            <div v-for="group in previewGroups" :key="group.checkId" class="check-group">
                                <div class="check-title">{{ group.description }}</div>
                                <ul class="issue-list">
                                    <li v-for="issue in group.issues" :key="issue.relativePath">
                                        <span class="issue-type">[{{ issue.issueType }}]</span>
                                        <span class="issue-path" :title="issue.description">{{ issue.relativePath
                                        }}</span>
                                    </li>
                                    <li v-if="group.extraCount > 0" class="more">… 还有 {{ group.extraCount }} 条</li>
                                    <li v-for="(err, idx) in group.errors" :key="`err-${idx}`" class="error-line">
                                        ⚠ {{ err }}
                                    </li>
                                </ul>
                            </div>
                        </template>
                    </div>

                    <!-- repairing -->
                    <div v-else-if="phase === 'repairing'" class="dialog-content">
                        <p class="hint">修复中...</p>
                    </div>

                    <!-- done -->
                    <div v-else-if="phase === 'done' && repairResult" class="dialog-content">
                        <p>修复完成。</p>
                        <ul class="result-list">
                            <li>已迁移：<strong>{{ repairTotals.migrated }}</strong></li>
                            <li>已跳过：{{ repairTotals.skipped }}</li>
                            <li v-if="repairTotals.errors > 0" class="error-line">
                                错误：{{ repairTotals.errors }}
                            </li>
                            <li v-if="shortHash">
                                修复前快照：<code>{{ shortHash }}</code>
                                <span class="hint-inline">（git reset --hard 可回滚）</span>
                            </li>
                            <li v-else>
                                <span class="hint-inline">工作区原本干净，未额外创建快照</span>
                            </li>
                        </ul>
                    </div>

                    <!-- error -->
                    <div v-else-if="phase === 'error'" class="dialog-content">
                        <p class="error-line">⚠ {{ errorMessage }}</p>
                    </div>

                    <!-- actions -->
                    <div class="dialog-actions">
                        <template v-if="phase === 'preview' && totalIssues > 0">
                            <GlassButton variant="primary" @click="handleConfirm">确认修复</GlassButton>
                            <GlassButton variant="ghost" @click="handleClose">取消</GlassButton>
                        </template>
                        <template v-else-if="phase === 'preview' || phase === 'done' || phase === 'error'">
                            <GlassButton variant="primary" @click="handleClose">完成</GlassButton>
                        </template>
                        <template v-else>
                            <!-- inspecting / repairing 期间不允许关闭 -->
                            <GlassButton variant="ghost" disabled>请稍候...</GlassButton>
                        </template>
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<style scoped>
.repair-dialog-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.6);
    backdrop-filter: blur(4px);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 9999;
}

.repair-dialog {
    background: var(--bg-surface);
    border: 1px solid var(--border-subtle);
    border-radius: 12px;
    padding: 24px;
    min-width: 480px;
    max-width: 640px;
    max-height: 80vh;
    overflow-y: auto;
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

.wrench-icon {
    width: 24px;
    height: 24px;
    color: var(--color-primary, #3b82f6);
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
    color: var(--text-secondary);
    font-size: 14px;
    line-height: 1.5;
}

.dialog-content p {
    margin: 0 0 12px 0;
}

.dialog-content strong {
    color: var(--text-primary);
    font-weight: 600;
}

.dialog-content code {
    font-family: var(--font-mono, 'SF Mono', 'Monaco', 'Consolas', monospace);
    background: var(--bg-canvas);
    padding: 1px 6px;
    border-radius: 4px;
    font-size: 12px;
}

.check-group {
    margin-top: 12px;
}

.check-title {
    font-weight: 600;
    color: var(--text-primary);
    margin-bottom: 6px;
}

.issue-list,
.result-list {
    margin: 0;
    padding-left: 0;
    list-style: none;
    font-size: 13px;
}

.issue-list li,
.result-list li {
    padding: 3px 0;
    color: var(--text-muted);
}

.issue-type {
    font-family: var(--font-mono, monospace);
    color: var(--color-warning, #f59e0b);
    margin-right: 8px;
}

.issue-path {
    font-family: var(--font-mono, monospace);
    color: var(--text-secondary);
    word-break: break-all;
}

.more {
    font-style: italic;
    color: var(--text-muted);
    padding-left: 12px;
}

.hint {
    color: var(--text-muted) !important;
}

.hint-inline {
    color: var(--text-muted);
    font-size: 12px;
    margin-left: 6px;
}

.error-line {
    color: var(--color-danger, #ef4444);
}

.dialog-actions {
    display: flex;
    gap: 12px;
    justify-content: flex-end;
}

.dialog-enter-active,
.dialog-leave-active {
    transition: all 0.2s ease;
}

.dialog-enter-from,
.dialog-leave-to {
    opacity: 0;
}

.dialog-enter-from .repair-dialog,
.dialog-leave-to .repair-dialog {
    transform: scale(0.95) translateY(-10px);
    opacity: 0;
}

.dialog-enter-active .repair-dialog,
.dialog-leave-active .repair-dialog {
    transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
