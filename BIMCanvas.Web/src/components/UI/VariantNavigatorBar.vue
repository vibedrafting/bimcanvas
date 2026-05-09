<script setup lang="ts">
/**
 * VariantNavigatorBar：屏幕中下浮层，用左右大箭头切换当前选中叶子分区的布置变体。
 * 显示条件：选中叶子分区 且 该分区存在 modules-alt-*.json 变体。
 * 与旧 VariantSwitcherChips 共享 store / service 契约，但作为唯一入口（PropertyPanel 不再嵌入 chips）。
 */
import { ref, computed, watch, onMounted, onUnmounted } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { SchemeService, type VariantDescriptor } from '../../services/SchemeService';

const canvasStore = useCanvasStore();

const variants = ref<VariantDescriptor[]>([]);
const isLoading = ref(false);
const adoptingVariantId = ref<string | null>(null);
const errorMessage = ref<string | null>(null);

const variantContext = computed<{ leafZoneId: string; leafZonePath: string } | null>(() => {
    const obj: any = canvasStore.selectedObject;
    if (!obj || obj.type !== 'zone') return null;
    const subZones = obj.subZones;
    if (Array.isArray(subZones) && subZones.length > 0) return null;
    const parentZoneId = obj.parentZoneId as string | undefined;
    const leafZonePath = parentZoneId ? `${parentZoneId}/${obj.id}` : obj.id;
    return { leafZoneId: obj.id, leafZonePath };
});

const sortedVariants = computed(() =>
    [...variants.value].sort((a, b) => a.variantId.localeCompare(b.variantId))
);

const CANONICAL_SLOT = '__canonical__';
const sequence = computed<string[]>(() =>
    [CANONICAL_SLOT, ...sortedVariants.value.map(v => v.variantId)]
);

const activeVariantId = computed(() =>
    variantContext.value ? canvasStore.getActiveVariant(variantContext.value.leafZoneId) : null
);

// 找不到时回到 0（canonical），兜底 SignalR 时序问题
const currentIndex = computed(() => {
    const id = activeVariantId.value ?? CANONICAL_SLOT;
    const i = sequence.value.indexOf(id);
    return i >= 0 ? i : 0;
});

const currentLabel = computed(() =>
    currentIndex.value === 0 ? '原方案' : sortedVariants.value[currentIndex.value - 1]!.variantId
);

const currentSummary = computed(() => {
    if (currentIndex.value === 0) return '';
    const v = sortedVariants.value[currentIndex.value - 1];
    return (v?.summary && v.summary.trim()) || '';
});

const showAdopt = computed(() => currentIndex.value !== 0);
const indicator = computed(() => `${currentIndex.value + 1}/${sequence.value.length}`);

async function gotoIndex(nextIndex: number) {
    const ctx = variantContext.value;
    if (!ctx) return;
    const len = sequence.value.length;
    const wrapped = ((nextIndex % len) + len) % len;
    const targetId = sequence.value[wrapped];
    if (!targetId || targetId === CANONICAL_SLOT) {
        await canvasStore.clearActiveVariant(ctx.leafZoneId);
    } else {
        await canvasStore.setActiveVariant(ctx.leafZoneId, ctx.leafZonePath, targetId);
    }
}
const onPrev = () => gotoIndex(currentIndex.value - 1);
const onNext = () => gotoIndex(currentIndex.value + 1);

async function onAdopt() {
    const ctx = variantContext.value;
    if (!ctx || !showAdopt.value || adoptingVariantId.value) return;
    const variantId = currentLabel.value;
    adoptingVariantId.value = variantId;
    errorMessage.value = null;
    // 先清 active，避免采纳后 SignalR 触发刷新时还指向已删除的 alt
    await canvasStore.clearActiveVariant(ctx.leafZoneId);
    try {
        await SchemeService.adoptVariant({ variantId, leafZonePath: ctx.leafZonePath });
        // 乐观清空，等 bimcanvas:variant-files-changed 兜底 refetch
        variants.value = [];
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.error ?? err?.message ?? '采纳失败';
    } finally {
        adoptingVariantId.value = null;
    }
}

async function refetchVariants() {
    const ctx = variantContext.value;
    if (!ctx) { variants.value = []; return; }
    isLoading.value = true;
    errorMessage.value = null;
    try {
        variants.value = (await SchemeService.listVariants(ctx.leafZonePath)) ?? [];
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
});
onUnmounted(() => {
    window.removeEventListener('bimcanvas:variant-files-changed', onVariantFilesChanged);
});

watch(() => variantContext.value?.leafZonePath ?? null, () => { void refetchVariants(); });
</script>

<template>
    <Transition name="vnav-fade">
        <div
            v-if="variantContext && variants.length > 0"
            class="variant-navigator-bar"
            role="group"
            aria-label="布置变体切换"
        >
            <div class="vnav-row vnav-row--main">
                <button
                    class="vnav-arrow"
                    type="button"
                    :disabled="!!adoptingVariantId"
                    @click="onPrev"
                    aria-label="上一个变体"
                    title="上一个变体"
                >
                    <svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true">
                        <path d="M15 6l-6 6 6 6" fill="none" stroke="currentColor"
                              stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                </button>
                <div class="vnav-center">
                    <span class="vnav-label">{{ currentLabel }}</span>
                    <span class="vnav-indicator">({{ indicator }})</span>
                </div>
                <button
                    class="vnav-arrow"
                    type="button"
                    :disabled="!!adoptingVariantId"
                    @click="onNext"
                    aria-label="下一个变体"
                    title="下一个变体"
                >
                    <svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true">
                        <path d="M9 6l6 6-6 6" fill="none" stroke="currentColor"
                              stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
                    </svg>
                </button>
            </div>
            <div v-if="currentSummary" class="vnav-summary" :title="currentSummary">{{ currentSummary }}</div>
            <div v-if="showAdopt" class="vnav-row vnav-row--action">
                <button
                    class="vnav-adopt"
                    type="button"
                    :disabled="!!adoptingVariantId"
                    @click="onAdopt"
                    title="采纳此变体（覆写原方案，删除其他变体）"
                >
                    {{ adoptingVariantId ? '采纳中…' : '采纳此变体' }}
                </button>
            </div>
            <div v-if="errorMessage" class="vnav-error">{{ errorMessage }}</div>
        </div>
    </Transition>
</template>

<style scoped lang="scss">
.variant-navigator-bar {
    position: fixed;
    /* PromptBar 在 bottom: 32px，pill 高 ~44px → 给 ~76px 缓冲 */
    bottom: 108px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 999;

    min-width: 320px;
    max-width: 420px;
    padding: 14px 18px;

    background: var(--glass-bg);
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
    border: 1px solid rgba(255, 255, 255, 0.18);
    border-radius: 14px;
    box-shadow:
        0 8px 28px rgba(0, 0, 0, 0.35),
        0 0 0 1px rgba(255, 255, 255, 0.08) inset;

    color: var(--text-primary);
    user-select: none;
    pointer-events: auto;
}

.vnav-row {
    display: flex;
    align-items: center;
    justify-content: center;
}

.vnav-row--main { gap: 16px; }
.vnav-row--action { margin-top: 10px; }

.vnav-center {
    display: flex;
    flex-direction: column;
    align-items: center;
    min-width: 140px;
}

.vnav-label {
    font-size: 15px;
    font-weight: 600;
    letter-spacing: 0.02em;
}

.vnav-indicator {
    font-size: 11px;
    color: var(--text-secondary);
    margin-top: 2px;
}

.vnav-arrow {
    width: 40px;
    height: 40px;
    border-radius: 50%;
    background: rgba(255, 255, 255, 0.06);
    border: 1px solid rgba(255, 255, 255, 0.15);
    color: var(--text-primary);
    display: inline-flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    transition: background 140ms ease, border-color 140ms ease, transform 140ms ease;
}

.vnav-arrow:hover:not(:disabled) {
    background: rgba(255, 255, 255, 0.14);
    border-color: rgba(255, 255, 255, 0.3);
}

.vnav-arrow:active:not(:disabled) {
    transform: scale(0.94);
}

.vnav-arrow:disabled {
    opacity: 0.5;
    cursor: not-allowed;
}

.vnav-summary {
    margin-top: 6px;
    text-align: center;
    font-size: 12px;
    color: var(--text-secondary);
    line-height: 1.4;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 100%;
}

.vnav-adopt {
    padding: 6px 18px;
    border-radius: 999px;
    background: rgba(120, 180, 255, 0.22);
    border: 1px solid rgba(120, 180, 255, 0.55);
    color: #fff;
    font-size: 12px;
    cursor: pointer;
    transition: background 140ms ease;
}

.vnav-adopt:hover:not(:disabled) {
    background: rgba(120, 180, 255, 0.34);
}

.vnav-adopt:disabled {
    opacity: 0.6;
    cursor: progress;
}

.vnav-error {
    margin-top: 6px;
    text-align: center;
    font-size: 11px;
    color: rgba(255, 130, 130, 0.9);
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
</style>
