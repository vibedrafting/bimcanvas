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
const barTitle = computed(() =>
    currentSummary.value ? `${currentLabel.value}：${currentSummary.value}` : currentLabel.value
);

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
            :title="barTitle"
        >
            <button
                class="vnav-arrow"
                type="button"
                :disabled="!!adoptingVariantId"
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
                <span class="vnav-label">{{ currentLabel }}</span>
                <span class="vnav-indicator">{{ indicator }}</span>
            </div>
            <button
                class="vnav-arrow"
                type="button"
                :disabled="!!adoptingVariantId"
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
                    :disabled="!!adoptingVariantId"
                    @click="onAdopt"
                    title="采纳此变体（覆写原方案，删除其他变体）"
                >
                    {{ adoptingVariantId ? '采纳中' : '采纳' }}
                </button>
                <span v-else class="vnav-status">基准</span>
            </div>
            <div v-if="errorMessage" class="vnav-error" :title="errorMessage">{{ errorMessage }}</div>
        </div>
    </Transition>
</template>

<style scoped lang="scss">
.variant-navigator-bar {
    position: fixed;
    /* PromptBar 在 bottom: 32px，pill 高 ~44px；这里只保留紧凑间距。 */
    bottom: 88px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 999;

    width: min(318px, calc(100vw - 32px));
    height: 40px;
    padding: 5px;
    box-sizing: border-box;
    display: grid;
    grid-template-columns: 28px minmax(0, 1fr) 28px 58px;
    align-items: center;
    gap: 6px;

    background: rgba(14, 15, 24, 0.92);
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);
    border: 1px solid rgba(255, 255, 255, 0.14);
    border-radius: 10px;
    box-shadow:
        0 8px 24px rgba(0, 0, 0, 0.36),
        0 1px 0 rgba(255, 255, 255, 0.08) inset;

    color: var(--text-primary);
    user-select: none;
    pointer-events: auto;
}

.vnav-center {
    min-width: 0;
    display: flex;
    flex-direction: row;
    align-items: center;
    justify-content: center;
    gap: 7px;
    height: 100%;
    padding: 0 2px;
    line-height: 1;
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

.vnav-indicator {
    font-size: 10px;
    color: var(--text-secondary);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
}

.vnav-arrow {
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
    display: flex;
    justify-content: flex-end;
}

.vnav-adopt,
.vnav-status {
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

.vnav-status {
    background: rgba(255, 255, 255, 0.045);
    border: 1px solid rgba(255, 255, 255, 0.08);
    color: var(--text-secondary);
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
</style>
