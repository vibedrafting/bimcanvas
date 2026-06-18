<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import GlassButton from './base/GlassButton.vue';
import {
    ProjectHealthService,
    type ProjectInspectionReport,
    type ProjectRepairReport,
    type HealthCheckInfo
} from '../../services/ProjectHealthService';

interface Props {
    visible: boolean;
    projectName: string;
    folderPath: string;
    // 'standalone' = 首页扳手按钮触发（先进配置面板）；'import' = 项目导入前自动健康检查（用已存偏好直接 inspect）
    mode?: 'standalone' | 'import';
}

const props = withDefaults(defineProps<Props>(), { mode: 'standalone' });
const emit = defineEmits<{
    (e: 'closed'): void;
    (e: 'proceed'): void; // import 模式：放行，可继续 loadInitialProject（无问题/已修复/用户跳过）
    (e: 'abort'): void;   // import 模式：用户取消导入
}>();

type Phase = 'config' | 'inspecting' | 'preview' | 'repairing' | 'done' | 'error';

const phase = ref<Phase>('config');
const inspection = ref<ProjectInspectionReport | null>(null);
const repairResult = ref<ProjectRepairReport | null>(null);
const errorMessage = ref<string>('');
// 记录 error 阶段失败发生在哪个步骤，决定重试入口
const errorStage = ref<'inspect' | 'repair'>('inspect');

// 配置面板状态
const availableChecks = ref<HealthCheckInfo[]>([]);
const checkedIds = ref<string[]>([]);
const autoCheckOnLoad = ref<boolean>(false);
// 本轮 inspect/repair 实际使用的 check 子集（null = 全部）
const activeCheckIds = ref<string[] | null>(null);

const ISSUES_PER_CHECK_PREVIEW = 10;
const isImportMode = computed(() => props.mode === 'import');

// 全选时存 null（语义：跟随未来新增的 check），否则存显式子集
const normalizedSelection = computed<string[] | null>(() =>
    checkedIds.value.length === availableChecks.value.length ? null : [...checkedIds.value]
);

const runInspect = async (checkIds: string[] | null) => {
    activeCheckIds.value = checkIds;
    phase.value = 'inspecting';
    inspection.value = null;
    repairResult.value = null;
    errorMessage.value = '';
    try {
        const report = await ProjectHealthService.inspect(props.folderPath, checkIds);
        inspection.value = report;
        // import 模式且无问题：静默放行，不展示对话框内容
        if (isImportMode.value && report.totalIssues === 0) {
            emit('proceed');
            return;
        }
        phase.value = 'preview';
    } catch (err: any) {
        errorStage.value = 'inspect';
        errorMessage.value = err?.response?.data?.message || err?.message || '检查失败';
        phase.value = 'error';
    }
};

// 进入配置面板：加载可选 check + 已存偏好
const enterConfig = async () => {
    phase.value = 'config';
    try {
        const [checks, prefs] = await Promise.all([
            ProjectHealthService.listChecks(),
            ProjectHealthService.getPrefs()
        ]);
        availableChecks.value = checks;
        autoCheckOnLoad.value = prefs.autoCheckOnLoad;
        checkedIds.value = prefs.enabledCheckIds ?? checks.map(c => c.id);
    } catch (err: any) {
        availableChecks.value = [];
        checkedIds.value = [];
        errorStage.value = 'inspect';
        errorMessage.value = err?.response?.data?.message || err?.message || '加载检查项失败';
        phase.value = 'error';
    }
};

// 「开始检查」：存偏好 + 跑 inspect
const handleStartInspect = async () => {
    try {
        await ProjectHealthService.savePrefs({
            autoCheckOnLoad: autoCheckOnLoad.value,
            enabledCheckIds: normalizedSelection.value
        });
    } catch {
        // 偏好存盘失败不阻断检查本身
    }
    await runInspect(normalizedSelection.value);
};

// 可见性变化：standalone 进配置面板；import 用已存偏好直接 inspect
watch(() => props.visible, async (visible) => {
    if (!visible) return;
    if (isImportMode.value) {
        const prefs = await ProjectHealthService.getPrefs();
        await runInspect(prefs.enabledCheckIds ?? null);
    } else {
        await enterConfig();
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
        repairResult.value = await ProjectHealthService.repair(props.folderPath, activeCheckIds.value);
        phase.value = 'done';
    } catch (err: any) {
        errorStage.value = 'repair';
        errorMessage.value = err?.response?.data?.message || err?.message || '修复失败';
        phase.value = 'error';
    }
};

const handleClose = () => emit('closed');
const handleProceed = () => emit('proceed');
const handleAbort = () => emit('abort');
const handleRetry = async () => {
    if (errorStage.value === 'repair') {
        await handleConfirm();
    } else if (isImportMode.value) {
        await runInspect(activeCheckIds.value);
    } else {
        await enterConfig();
    }
};
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

                    <!-- config（仅 standalone 入口） -->
                    <div v-if="phase === 'config'" class="dialog-content">
                        <p>选择要运行的检查项：</p>
                        <div v-if="availableChecks.length === 0" class="hint">暂无可用检查项。</div>
                        <ul v-else class="check-select-list">
                            <li v-for="check in availableChecks" :key="check.id">
                                <label class="check-option">
                                    <input type="checkbox" :value="check.id" v-model="checkedIds" />
                                    <span class="check-desc">{{ check.description }}</span>
                                </label>
                            </li>
                        </ul>
                        <label class="auto-toggle">
                            <input type="checkbox" v-model="autoCheckOnLoad" />
                            <span>导入 / 新建 / 恢复项目时自动运行所选检查</span>
                        </label>
                    </div>

                    <!-- inspecting -->
                    <div v-else-if="phase === 'inspecting'" class="dialog-content">
                        <p class="hint">检查中...</p>
                    </div>

                    <!-- preview -->
                    <div v-else-if="phase === 'preview'" class="dialog-content">
                        <template v-if="totalIssues === 0">
                            <p>所选检查项均无问题，项目无需修复。</p>
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
                        <template v-if="phase === 'config'">
                            <GlassButton variant="primary" :disabled="checkedIds.length === 0"
                                @click="handleStartInspect">开始检查</GlassButton>
                            <GlassButton variant="ghost" @click="handleClose">取消</GlassButton>
                        </template>
                        <template v-else-if="phase === 'inspecting' || phase === 'repairing'">
                            <GlassButton variant="ghost" disabled>请稍候...</GlassButton>
                        </template>
                        <template v-else-if="phase === 'preview' && totalIssues > 0">
                            <GlassButton variant="primary" @click="handleConfirm">立即修复</GlassButton>
                            <template v-if="isImportMode">
                                <GlassButton variant="ghost" @click="handleProceed">跳过修复并打开</GlassButton>
                                <GlassButton variant="ghost" @click="handleAbort">取消导入</GlassButton>
                            </template>
                            <template v-else>
                                <GlassButton variant="ghost" @click="handleClose">取消</GlassButton>
                            </template>
                        </template>
                        <template v-else-if="phase === 'preview'">
                            <!-- totalIssues===0：import 模式已自动 proceed，仅 standalone 落到这里 -->
                            <GlassButton variant="primary" @click="handleClose">完成</GlassButton>
                        </template>
                        <template v-else-if="phase === 'done'">
                            <GlassButton v-if="isImportMode" variant="primary" @click="handleProceed">打开项目</GlassButton>
                            <GlassButton v-else variant="primary" @click="handleClose">完成</GlassButton>
                        </template>
                        <template v-else-if="phase === 'error'">
                            <template v-if="isImportMode">
                                <GlassButton variant="primary" @click="handleRetry">重试</GlassButton>
                                <GlassButton variant="ghost" @click="handleProceed">跳过并打开</GlassButton>
                                <GlassButton variant="ghost" @click="handleAbort">取消导入</GlassButton>
                            </template>
                            <template v-else>
                                <GlassButton variant="primary" @click="handleRetry">重试</GlassButton>
                                <GlassButton variant="ghost" @click="handleClose">关闭</GlassButton>
                            </template>
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

.check-select-list {
    margin: 0 0 16px 0;
    padding: 0;
    list-style: none;
}

.check-select-list li {
    padding: 4px 0;
}

.check-option,
.auto-toggle {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    cursor: pointer;
    color: var(--text-secondary);
    font-size: 14px;
}

.check-option input,
.auto-toggle input {
    margin-top: 2px;
    flex-shrink: 0;
}

.auto-toggle {
    padding-top: 12px;
    border-top: 1px solid var(--border-subtle);
    color: var(--text-primary);
}

.check-desc {
    word-break: break-word;
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
