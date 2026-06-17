<script setup lang="ts">
/**
 * VariantNavigatorBar：屏幕中下浮层，用左右大箭头切换当前选中设计区的布置变体。
 * 显示条件：选中 zone 能解析为 designZoneId 且 该设计区存在变体目录。
 * variant 协议按 designZoneId 索引，同一设计区下所有叶子共享一份 variantSlug。
 */
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { SchemeService, type VariantDescriptor } from '../../services/SchemeService';
import GlassButton from './base/GlassButton.vue';
import { createLogger } from '../../utils/logger';

const log = createLogger('USER');
const canvasStore = useCanvasStore();

const variants = ref<VariantDescriptor[]>([]);
const isLoading = ref(false);
const adoptingSlug = ref<string | null>(null);
const deletingSlug = ref<string | null>(null);
const errorMessage = ref<string | null>(null);

// 删除确认弹窗：暂存待删设计区 + 变体 slug
interface PendingDelete {
    designZoneId: string;
    variantSlug: string;
}
const pendingDelete = ref<PendingDelete | null>(null);
const showDeleteConfirm = computed(() => pendingDelete.value !== null);

const busy = computed(() => !!adoptingSlug.value || !!deletingSlug.value);

/**
 * 选中 zone 反查所属 designZoneId：
 *  - 选中顶层 zone（容器或顶层叶子）→ obj.id
 *  - 选中子叶子 → obj.parentZoneId
 *  - 其他 → null
 */
const variantContext = computed<{ designZoneId: string } | null>(() => {
    const obj: any = canvasStore.selectedObject;
    if (!obj || obj.type !== 'zone') return null;
    const parentZoneId = obj.parentZoneId as string | undefined;
    const designZoneId = parentZoneId ? parentZoneId : (obj.id as string | undefined);
    if (!designZoneId) return null;
    return { designZoneId };
});

// 临时显示隐藏候选（_ 前缀，state=hidden）的开关；默认关，刷新即重置。
const showHidden = ref(false);
const hasHidden = computed(() => variants.value.some(v => v.state === 'hidden'));

// 指针模型：隐藏候选（_ 前缀，state=hidden）默认不进导航条，showHidden 开启时临时纳入；
// adopted 方案由位置0 canonical 槽（clearActiveVariant 渲染父指针指向的方案；标签显示其 slug、
// 动作区显示灰色「已采纳」）代表，不在列表里再重复出现一条 —— 避免采纳后同一方案显示两次。
const visibleVariants = computed(() =>
    variants.value.filter(v =>
        v.state !== 'adopted' && (showHidden.value || v.state !== 'hidden')
    )
);
// server 已按 createdAt 排序，保留原顺序即可；后续如需稳定排序可在此扩展
const sortedVariants = computed(() => [...visibleVariants.value]);

// 纯指针平级模型：仅当确有 adopted 方案时才存在「已采纳方案」canonical 槽（clearActiveVariant
// 渲染父指针指向的已采纳方案）；无 adopted 时不再恒插空槽，导航条只列 peer 变体。
const hasAdopted = computed(() => variants.value.some(v => v.state === 'adopted'));

const CANONICAL_SLOT = '__canonical__';
const sequence = computed<string[]>(() =>
    hasAdopted.value
        ? [CANONICAL_SLOT, ...sortedVariants.value.map(v => v.slug)]
        : sortedVariants.value.map(v => v.slug)
);

const activeVariantSlug = computed(() =>
    variantContext.value ? canvasStore.getActiveVariant(variantContext.value.designZoneId) : null
);

const currentIndex = computed(() => {
    const fallback = hasAdopted.value ? CANONICAL_SLOT : (sequence.value[0] ?? CANONICAL_SLOT);
    const id = activeVariantSlug.value ?? fallback;
    const i = sequence.value.indexOf(id);
    return i >= 0 ? i : 0;
});

// 当前 slot 是否为 canonical（已采纳方案）——按 slot 值判定，不依赖「canonical 恒在 0」的索引偏移，
// 兼容有/无 canonical 两种 sequence 形态。
const currentSlot = computed(() => sequence.value[currentIndex.value] ?? null);
const isCanonicalCurrent = computed(() => currentSlot.value === CANONICAL_SLOT);

const currentVariant = computed<VariantDescriptor | null>(() =>
    isCanonicalCurrent.value
        ? null
        : sortedVariants.value.find(v => v.slug === currentSlot.value) ?? null
);

// 已采纳方案不进 visibleVariants 列表，但仍需取其 slug/summary 用于 canonical 槽的标签与 tooltip。
const adoptedVariant = computed<VariantDescriptor | null>(() =>
    variants.value.find(v => v.state === 'adopted') ?? null);

// canonical 槽直接显示被采纳方案的 slug（而非笼统的「已采纳方案」）；adoptedVariant 缺失时兜底。
const currentLabel = computed(() =>
    isCanonicalCurrent.value
        ? (adoptedVariant.value?.slug ?? '已采纳方案')
        : currentVariant.value?.slug ?? '');

const currentSummary = computed(() => {
    const v = isCanonicalCurrent.value ? adoptedVariant.value : currentVariant.value;
    return (v?.summary && v.summary.trim()) || '';
});

const showAdopt = computed(() => !isCanonicalCurrent.value);
// 当前停在的是否为隐藏候选——用于 label 视觉标记（slug 本身带 _ 前缀，这里再加色提示）。
const isHiddenCurrent = computed(() =>
    !isCanonicalCurrent.value && currentVariant.value?.state === 'hidden');
const indicator = computed(() => `${currentIndex.value + 1}/${sequence.value.length}`);
const barTitle = computed(() =>
    currentSummary.value ? `${currentLabel.value}：${currentSummary.value}` : currentLabel.value);

async function gotoIndex(nextIndex: number) {
    const ctx = variantContext.value;
    if (!ctx) return;
    const len = sequence.value.length;
    const wrapped = ((nextIndex % len) + len) % len;
    const targetSlug = sequence.value[wrapped];
    if (!targetSlug || targetSlug === CANONICAL_SLOT) {
        log.info('variant switch', { dz: ctx.designZoneId, slug: '@canonical' });
        await canvasStore.clearActiveVariant(ctx.designZoneId);
    } else {
        log.info('variant switch', { dz: ctx.designZoneId, slug: targetSlug });
        await canvasStore.setActiveVariant(ctx.designZoneId, targetSlug);
    }
}
const onPrev = () => gotoIndex(currentIndex.value - 1);
const onNext = () => gotoIndex(currentIndex.value + 1);

function toggleHidden() {
    showHidden.value = !showHidden.value;
    // 关闭时若当前正停在某隐藏候选上，该 slug 会从 sequence 移除，导致 bar 索引回落却仍渲染隐藏方案——
    // 主动回退到 canonical，保持 bar 角标与画布一致。
    if (!showHidden.value) {
        const ctx = variantContext.value;
        const active = activeVariantSlug.value;
        if (ctx && active && variants.value.find(v => v.slug === active)?.state === 'hidden') {
            void canvasStore.clearActiveVariant(ctx.designZoneId);
        }
    }
}

function isEditableTarget(target: EventTarget | null): boolean {
    if (!(target instanceof Element)) return false;
    const tagName = target.tagName.toLowerCase();
    return tagName === 'input'
        || tagName === 'textarea'
        || tagName === 'select'
        || !!target.closest('[contenteditable="true"], [contenteditable=""]');
}

function onKeydown(event: KeyboardEvent) {
    if (event.defaultPrevented || event.ctrlKey || event.metaKey || event.altKey || event.shiftKey) return;
    if (isEditableTarget(event.target)) return;
    if (!variantContext.value || sequence.value.length === 0 || busy.value) return;

    if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') {
        event.preventDefault();
        event.stopImmediatePropagation();
        void onPrev();
        return;
    }

    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') {
        event.preventDefault();
        event.stopImmediatePropagation();
        void onNext();
    }
}

async function onAdopt() {
    const ctx = variantContext.value;
    const slug = currentVariant.value?.slug;
    if (!ctx || !showAdopt.value || busy.value || !slug) return;
    adoptingSlug.value = slug;
    errorMessage.value = null;
    // 先清 active，避免采纳后 SignalR 触发刷新时还指向已删除的变体
    await canvasStore.clearActiveVariant(ctx.designZoneId);
    try {
        await SchemeService.adoptVariant({ designZoneId: ctx.designZoneId, variantSlug: slug });
        log.info('variant adopt', { dz: ctx.designZoneId, slug });
        // 乐观清空，等 SignalR variant-files-changed 兜底 refetch（含 prev-* 新增）
        variants.value = [];
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.error ?? err?.message ?? '采纳失败';
    } finally {
        adoptingSlug.value = null;
    }
}

function onDelete() {
    const ctx = variantContext.value;
    const slug = currentVariant.value?.slug;
    if (!ctx || !showAdopt.value || busy.value || !slug) return;
    pendingDelete.value = { designZoneId: ctx.designZoneId, variantSlug: slug };
}

function cancelDelete() {
    pendingDelete.value = null;
}

async function confirmDelete() {
    const pending = pendingDelete.value;
    pendingDelete.value = null;
    if (!pending) return;
    deletingSlug.value = pending.variantSlug;
    errorMessage.value = null;
    // 先回到原方案，避免删完后 active 还指向已不存在的 slug
    await canvasStore.clearActiveVariant(pending.designZoneId);
    try {
        await SchemeService.deleteVariant({
            designZoneId: pending.designZoneId,
            variantSlug: pending.variantSlug
        });
        log.info('variant delete', { dz: pending.designZoneId, slug: pending.variantSlug });
        // 乐观从本地列表移除；SignalR variant-files-changed 会兜底 refetch
        variants.value = variants.value.filter(v => v.slug !== pending.variantSlug);
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.error ?? err?.message ?? '删除失败';
    } finally {
        deletingSlug.value = null;
    }
}

async function refetchVariants() {
    const ctx = variantContext.value;
    if (!ctx) { variants.value = []; return; }
    isLoading.value = true;
    errorMessage.value = null;
    try {
        const list = (await SchemeService.listVariants(ctx.designZoneId)) ?? [];
        variants.value = list;
        canvasStore.cacheVariantMetadata(ctx.designZoneId, list);
    } catch (err: any) {
        errorMessage.value = err?.message ?? '加载变体失败';
        variants.value = [];
    } finally {
        isLoading.value = false;
    }
}

const onVariantFilesChanged = () => { void refetchVariants(); };

onMounted(() => {
    void refetchVariants();
    window.addEventListener('bimcanvas:variant-files-changed', onVariantFilesChanged);
    window.addEventListener('keydown', onKeydown, true);
});
onUnmounted(() => {
    window.removeEventListener('bimcanvas:variant-files-changed', onVariantFilesChanged);
    window.removeEventListener('keydown', onKeydown, true);
});

watch(() => variantContext.value?.designZoneId ?? null, () => { void refetchVariants(); });
</script>

<template>
    <Transition name="vnav-fade">
        <div
            v-if="variantContext && sequence.length > 0"
            class="variant-navigator-bar"
            role="group"
            aria-label="布置变体切换"
            :title="barTitle"
        >
            <button
                class="vnav-arrow"
                type="button"
                :disabled="busy"
                @click="onPrev"
                aria-label="上一个变体"
                title="上一个变体"
            >
                <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
                    <path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor"
                          stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
            </button>
            <div class="vnav-center" :title="barTitle">
                <span class="vnav-label" :class="{ 'is-adopted': isCanonicalCurrent, 'is-hidden-variant': isHiddenCurrent }">{{ currentLabel }}</span>
                <span class="vnav-indicator">{{ indicator }}</span>
            </div>
            <button
                class="vnav-arrow"
                type="button"
                :disabled="busy"
                @click="onNext"
                aria-label="下一个变体"
                title="下一个变体"
            >
                <svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true">
                    <path d="M9 6l6 6-6 6" fill="none" stroke="currentColor"
                          stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
            </button>
            <div class="vnav-action-slot">
                <button
                    v-if="showAdopt"
                    class="vnav-adopt"
                    type="button"
                    :disabled="busy"
                    @click="onAdopt"
                    title="采纳此方案（翻转 adopted 指针使其生效；原方案保留、不删除、可回溯）"
                >
                    {{ adoptingSlug ? '采纳中' : '采纳' }}
                </button>
                <button
                    v-else
                    class="vnav-adopted"
                    type="button"
                    disabled
                    title="此方案为当前已采纳方案"
                >已采纳</button>
            </div>
            <button
                v-if="showAdopt"
                class="vnav-delete"
                type="button"
                :disabled="busy"
                @click="onDelete"
                aria-label="删除此变体"
                :title="deletingSlug ? '删除中…' : '删除此可变方案'"
            >
                <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
                    <path d="M9 3h6m-9 4h12m-1 0l-1 12a2 2 0 0 1-2 2h-6a2 2 0 0 1-2-2L5 7m4 4v6m4-6v6"
                          fill="none" stroke="currentColor"
                          stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                </svg>
            </button>
            <button
                v-if="hasHidden"
                class="vnav-toggle-hidden"
                :class="{ active: showHidden }"
                type="button"
                :disabled="busy"
                :aria-pressed="showHidden"
                @click="toggleHidden"
                :title="showHidden ? '收起「_」前缀的废弃/隐藏方案' : '临时显示「_」前缀的废弃/隐藏方案'"
            >
                <svg v-if="showHidden" viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
                    <path d="M3 3l18 18M10.6 10.7a2 2 0 002.7 2.8M9.4 5.2A9.4 9.4 0 0112 5c5 0 9 4.5 10 7-.4 1-1.2 2.3-2.4 3.5M6.1 6.2C3.8 7.6 2.4 9.8 2 12c1 2.5 5 7 10 7 1 0 2-.2 2.9-.5"
                          fill="none" stroke="currentColor" stroke-width="2"
                          stroke-linecap="round" stroke-linejoin="round" />
                </svg>
                <svg v-else viewBox="0 0 24 24" width="16" height="16" aria-hidden="true">
                    <path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7-10-7-10-7z"
                          fill="none" stroke="currentColor" stroke-width="2"
                          stroke-linecap="round" stroke-linejoin="round" />
                    <circle cx="12" cy="12" r="3" fill="none" stroke="currentColor" stroke-width="2" />
                </svg>
            </button>
            <div v-if="errorMessage" class="vnav-error" :title="errorMessage">{{ errorMessage }}</div>
        </div>
    </Transition>

    <!-- 删除确认对话框（与 HomePage 删除项目对话框同款 dialog-card 风格） -->
    <Teleport to="body">
        <Transition name="dialog">
            <div v-if="showDeleteConfirm" class="dialog-overlay" @click.self="cancelDelete">
                <div class="delete-dialog">
                    <div class="dialog-header">
                        <svg viewBox="0 0 24 24" width="24" height="24" fill="none"
                             stroke="var(--accent-danger)" stroke-width="2">
                            <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                        </svg>
                        <h3>确认删除</h3>
                    </div>
                    <div class="dialog-content">
                        <p>确定删除可变方案 "<strong>{{ pendingDelete?.variantSlug }}</strong>" 吗？</p>
                        <p class="warning-text">此操作不可撤销，整个变体目录将被永久删除。</p>
                    </div>
                    <div class="dialog-actions">
                        <GlassButton variant="danger" @click="confirmDelete">删除</GlassButton>
                        <GlassButton variant="ghost" @click="cancelDelete">取消</GlassButton>
                    </div>
                </div>
            </div>
        </Transition>
    </Teleport>
</template>

<style scoped lang="scss">
.variant-navigator-bar {
    position: fixed;
    /* PromptBar 在 bottom: 32px，pill 高 ~44px；这里只保留紧凑间距。 */
    bottom: 88px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 999;

    /* 弹性宽度：bar 跟随内容（canonical 4 项 vs variant 5 项 + 中心 label 长度）伸缩，
     * 不再用固定列宽 + 占位符撑空。translateX(-50%) 居中让伸缩从中心对称展开，不抖动。 */
    width: max-content;
    max-width: min(440px, calc(100vw - 32px));
    height: 40px;
    padding: 5px;
    box-sizing: border-box;
    display: flex;
    align-items: center;
    gap: 6px;

    /* Aurora Glass — 对齐 PropertyPanel.vue:286-299 */
    background: var(--glass-bg);
    background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 20px;
    box-shadow:
        0 12px 40px rgba(0, 0, 0, 0.4),
        0 0 0 1px rgba(255, 255, 255, 0.1) inset,
        0 0 20px rgba(255, 255, 255, 0.15);

    color: var(--text-primary);
    user-select: none;
    pointer-events: auto;
}

.vnav-center {
    /* min-width 防 canonical 短文本"原方案 1/4"被压成贴脸；
     * max-width 防 variant 长 ID 撑爆 bar（超出走 vnav-label 的 ellipsis） */
    min-width: 80px;
    max-width: 220px;
    flex: 0 1 auto;
    display: flex;
    flex-direction: row;
    align-items: center;
    justify-content: center;
    gap: 7px;
    height: 100%;
    padding: 0 2px;
    line-height: 1;
    overflow: hidden;
}

.vnav-label {
    max-width: 100%;
    font-size: 12px;
    font-weight: 600;
    letter-spacing: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

/* label 三态着色（均不用斜体）：
 * 采纳态 → 琥珀高亮；普通变体 → 默认色（无类）；隐藏候选 → 浅蓝。 */
.vnav-label.is-adopted {
    color: rgba(255, 205, 130, 1);
}

.vnav-label.is-hidden-variant {
    color: rgba(130, 190, 255, 0.95);
}

.vnav-indicator {
    font-size: 10px;
    color: var(--text-secondary);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
}

/* 临时显示隐藏候选的切换按钮：尺寸/交互对齐 .vnav-arrow；active（已开启）时高亮琥珀色。 */
.vnav-toggle-hidden {
    flex: 0 0 28px;
    width: 28px;
    height: 28px;
    border-radius: 8px;
    background: transparent;
    border: 1px solid transparent;
    color: var(--text-secondary);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease, color 140ms ease, transform 140ms ease;
}

.vnav-toggle-hidden:hover:not(:disabled) {
    background: rgba(255, 255, 255, 0.08);
    border-color: rgba(255, 255, 255, 0.12);
    color: var(--text-primary);
}

.vnav-toggle-hidden.active {
    background: rgba(255, 190, 100, 0.16);
    border-color: rgba(255, 190, 100, 0.42);
    color: rgba(255, 205, 130, 1);
}

.vnav-toggle-hidden:active:not(:disabled) {
    transform: scale(0.94);
}

.vnav-toggle-hidden:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.vnav-arrow {
    flex: 0 0 28px;
    width: 28px;
    height: 28px;
    border-radius: 8px;
    background: transparent;
    border: 1px solid transparent;
    color: var(--text-primary);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease, transform 140ms ease;
}

.vnav-arrow:hover:not(:disabled) {
    background: rgba(255, 255, 255, 0.08);
    border-color: rgba(255, 255, 255, 0.12);
}

.vnav-arrow:active:not(:disabled) {
    transform: scale(0.94);
}

.vnav-arrow:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.vnav-action-slot {
    flex: 0 0 58px;
    display: flex;
    justify-content: flex-end;
}

/* 删除按钮：仅 variant slot 显示；canonical 状态由 v-if 整体不渲染（不再占位） */
.vnav-delete {
    flex: 0 0 28px;
    width: 28px;
    height: 28px;
    border-radius: 8px;
    background: transparent;
    border: 1px solid transparent;
    color: rgba(255, 130, 130, 0.85);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease, color 140ms ease, transform 140ms ease;
}

.vnav-delete:hover:not(:disabled) {
    background: rgba(255, 90, 90, 0.14);
    border-color: rgba(255, 130, 130, 0.42);
    color: rgba(255, 160, 160, 1);
}

.vnav-delete:active:not(:disabled) {
    transform: scale(0.94);
}

.vnav-delete:disabled {
    opacity: 0.5;
    cursor: progress;
}

.vnav-adopt,
.vnav-adopted {
    width: 58px;
    height: 26px;
    padding: 0 8px;
    box-sizing: border-box;
    border-radius: 8px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0;
    white-space: nowrap;
}

.vnav-adopt {
    background: rgba(64, 139, 255, 0.2);
    border: 1px solid rgba(118, 178, 255, 0.48);
    color: #fff;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease;
}

.vnav-adopt:hover:not(:disabled) {
    background: rgba(64, 139, 255, 0.34);
    border-color: rgba(145, 196, 255, 0.7);
}

.vnav-adopt:disabled {
    opacity: 0.6;
    cursor: progress;
}

.vnav-adopted {
    background: rgba(255, 255, 255, 0.045);
    border: 1px solid rgba(255, 255, 255, 0.08);
    color: var(--text-secondary);
    cursor: default;
}

.vnav-error {
    position: absolute;
    left: 50%;
    bottom: calc(100% + 8px);
    transform: translateX(-50%);
    max-width: min(320px, calc(100vw - 48px));
    padding: 4px 8px;
    border-radius: 999px;
    background: rgba(30, 12, 14, 0.9);
    border: 1px solid rgba(255, 130, 130, 0.35);
    text-align: center;
    font-size: 11px;
    line-height: 1.3;
    color: rgba(255, 130, 130, 0.9);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.vnav-fade-enter-active,
.vnav-fade-leave-active {
    transition: opacity 220ms ease, transform 220ms cubic-bezier(0.34, 1.56, 0.64, 1);
}

.vnav-fade-enter-from,
.vnav-fade-leave-to {
    opacity: 0;
    transform: translateX(-50%) translateY(12px);
}

.vnav-fade-enter-to,
.vnav-fade-leave-from {
    opacity: 1;
    transform: translateX(-50%) translateY(0);
}

/* ── 删除确认对话框 ──
 * 风格对齐 HomePage.vue 的删除项目对话框（HomePage.vue:705-787）。 */
.dialog-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.6);
    backdrop-filter: blur(4px);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 9999;
}

.delete-dialog {
    background: var(--glass-bg-solid);
    border: 1px solid var(--border-subtle);
    border-radius: 12px;
    padding: 24px;
    min-width: 380px;
    max-width: 460px;
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

.dialog-header h3 {
    margin: 0;
    font-size: 18px;
    font-weight: 600;
}

.dialog-content {
    margin-bottom: 20px;
}

.dialog-content p {
    margin: 0 0 8px;
    color: var(--text-secondary);
    font-size: 14px;
    line-height: 1.5;
}

.dialog-content strong {
    color: var(--text-primary);
    font-weight: 600;
}

.warning-text {
    color: var(--accent-danger) !important;
    font-size: 0.85rem !important;
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

.dialog-enter-from .delete-dialog,
.dialog-leave-to .delete-dialog {
    transform: scale(0.95) translateY(-10px);
    opacity: 0;
}

.dialog-enter-active .delete-dialog,
.dialog-leave-active .delete-dialog {
    transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
