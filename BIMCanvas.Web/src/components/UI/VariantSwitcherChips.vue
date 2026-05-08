<script setup lang="ts">
/**
 * VariantSwitcherChips：module-relocation-agent 产出的变体方案 chip 切换器。
 *
 * 用途：当某个叶子分区下存在 modules-alt-{n}.json 变体时，渲染一条 chip 条
 * `[原方案] [alt-1] [alt-2] ...`，点击 chip 切换该分区的渲染源；每个 chip 旁
 * 有"采纳"按钮，把变体扣为 canonical 并删除其他 alt 文件。
 *
 * 状态：active variant 由 canvasStore.activeVariantByZone 维护，仅存内存。
 *
 * 挂载位置（待集成）：建议放进 PropertyPanel.vue 的 zone 属性区，或 ZoneGroup.vue
 * 的展开面板里——只在 selectedObject.type === 'zone' 且 zone 是叶子分区时显示。
 *
 * 数据刷新：监听 window 事件 `bimcanvas:variant-files-changed`（来自 canvasStore
 * 对 SignalR `data.file.startsWith('modules-alt-')` 的分发），收到即 refetch。
 */
import { ref, computed, onMounted, onUnmounted, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { SchemeService, type VariantDescriptor } from '../../services/SchemeService';

const props = defineProps<{
    /** 叶子分区 ID（用于 active 状态键） */
    leafZoneId: string;
    /** 叶子分区相对 schemes/ 的路径，如 "rz_3/dz_1" */
    leafZonePath: string;
}>();

const canvasStore = useCanvasStore();

const variants = ref<VariantDescriptor[]>([]);
const isLoading = ref(false);
const adoptingVariantId = ref<string | null>(null);
const errorMessage = ref<string | null>(null);

const activeVariantId = computed(() => canvasStore.getActiveVariant(props.leafZoneId));

/** 按 confidenceTier（recommended → acceptable → fallback → 未知）+ variantId 字典序排序 */
const sortedVariants = computed(() => {
    const tierRank = (tier: unknown): number => {
        switch (tier) {
            case 'recommended': return 0;
            case 'acceptable': return 1;
            case 'fallback': return 2;
            default: return 3;
        }
    };
    return [...variants.value].sort((a, b) => {
        const ta = tierRank(a.meta?.confidenceTier);
        const tb = tierRank(b.meta?.confidenceTier);
        if (ta !== tb) return ta - tb;
        return a.variantId.localeCompare(b.variantId);
    });
});

async function refetchVariants() {
    if (!props.leafZonePath) {
        variants.value = [];
        return;
    }
    isLoading.value = true;
    errorMessage.value = null;
    try {
        const list = await SchemeService.listVariants(props.leafZonePath);
        variants.value = list ?? [];
    } catch (err: any) {
        errorMessage.value = err?.message ?? '加载变体列表失败';
        variants.value = [];
    } finally {
        isLoading.value = false;
    }
}

async function selectCanonical() {
    await canvasStore.clearActiveVariant(props.leafZoneId);
}

async function selectVariant(variantId: string) {
    await canvasStore.setActiveVariant(props.leafZoneId, props.leafZonePath, variantId);
}

async function adoptVariant(variantId: string) {
    if (adoptingVariantId.value) return;
    adoptingVariantId.value = variantId;
    errorMessage.value = null;
    // 先清 active 状态，避免采纳后 SignalR 触发刷新时还指向已删除的 alt
    await canvasStore.clearActiveVariant(props.leafZoneId);
    try {
        await SchemeService.adoptVariant({
            variantId,
            leafZonePath: props.leafZonePath
        });
        // 服务端会广播 SignalR；canvasStore 收到 trigger=variant-adopt 后会自动重载 canonical
        // 这里乐观地清空本地变体列表，等 bimcanvas:variant-files-changed 再 refetch
        variants.value = [];
    } catch (err: any) {
        errorMessage.value = err?.response?.data?.error ?? err?.message ?? '采纳失败';
    } finally {
        adoptingVariantId.value = null;
    }
}

function operationsSummary(meta: any): string {
    if (!meta || !Array.isArray(meta.operations)) return '';
    const counts: Record<string, number> = {};
    for (const op of meta.operations) {
        const t = typeof op?.type === 'string' ? op.type : 'unknown';
        if (t === 'kept') continue;
        counts[t] = (counts[t] ?? 0) + 1;
    }
    const order = ['moved', 'rotated', 'resized', 'deleted', 'added'];
    const labels: Record<string, string> = {
        moved: '移动', rotated: '旋转', resized: '改尺寸', deleted: '删除', added: '新增'
    };
    return order
        .filter(k => counts[k])
        .map(k => `${counts[k]} ${labels[k]}`)
        .join(' / ');
}

function tierLabel(tier: unknown): string {
    switch (tier) {
        case 'recommended': return '★ 推荐';
        case 'acceptable': return '可接受';
        case 'fallback': return '兜底';
        default: return '';
    }
}

function variantTooltip(v: VariantDescriptor): string {
    const summary = v.meta?.summary ?? v.variantId;
    const ops = operationsSummary(v.meta);
    const tier = tierLabel(v.meta?.confidenceTier);
    return [summary, ops, tier].filter(Boolean).join('\n');
}

const onVariantFilesChanged = () => { refetchVariants(); };

onMounted(() => {
    refetchVariants();
    window.addEventListener('bimcanvas:variant-files-changed', onVariantFilesChanged);
});

onUnmounted(() => {
    window.removeEventListener('bimcanvas:variant-files-changed', onVariantFilesChanged);
});

// leafZonePath 切换 → 清旧、重拉
watch(() => props.leafZonePath, () => { refetchVariants(); });
</script>

<template>
    <div v-if="variants.length > 0 || isLoading" class="variant-switcher-chips">
        <span class="vsc-label">布置变体：</span>
        <button
            type="button"
            class="vsc-chip vsc-chip--canonical"
            :class="{ 'is-active': !activeVariantId }"
            @click="selectCanonical"
            title="原方案 (canonical modules.json)"
        >
            原方案
        </button>
        <template v-for="v in sortedVariants" :key="v.variantId">
            <span class="vsc-chip-group">
                <button
                    type="button"
                    class="vsc-chip"
                    :class="{
                        'is-active': activeVariantId === v.variantId,
                        'is-recommended': v.meta?.confidenceTier === 'recommended'
                    }"
                    :title="variantTooltip(v)"
                    @click="selectVariant(v.variantId)"
                >
                    {{ v.variantId }}<span v-if="v.meta?.confidenceTier === 'recommended'"> ★</span>
                </button>
                <button
                    type="button"
                    class="vsc-adopt"
                    :disabled="adoptingVariantId === v.variantId"
                    title="采纳此变体（覆写原方案，删除其他变体）"
                    @click="adoptVariant(v.variantId)"
                >
                    {{ adoptingVariantId === v.variantId ? '采纳中…' : '采纳' }}
                </button>
            </span>
        </template>
        <span v-if="errorMessage" class="vsc-error">{{ errorMessage }}</span>
    </div>
</template>

<style scoped>
.variant-switcher-chips {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px;
    padding: 6px 8px;
    font-size: 12px;
    line-height: 1.4;
}

.vsc-label {
    color: rgba(255, 255, 255, 0.7);
}

.vsc-chip-group {
    display: inline-flex;
    align-items: center;
    gap: 2px;
}

.vsc-chip {
    border: 1px solid rgba(255, 255, 255, 0.25);
    background: rgba(255, 255, 255, 0.06);
    color: rgba(255, 255, 255, 0.85);
    border-radius: 12px;
    padding: 3px 10px;
    cursor: pointer;
    transition: background 120ms ease, border-color 120ms ease;
}

.vsc-chip:hover {
    background: rgba(255, 255, 255, 0.12);
}

.vsc-chip.is-active {
    background: rgba(120, 180, 255, 0.25);
    border-color: rgba(120, 180, 255, 0.6);
    color: #ffffff;
}

.vsc-chip--canonical {
    font-weight: 500;
}

.vsc-chip.is-recommended {
    border-color: rgba(255, 200, 60, 0.55);
}

.vsc-adopt {
    border: none;
    background: transparent;
    color: rgba(180, 220, 255, 0.85);
    cursor: pointer;
    padding: 2px 6px;
    font-size: 11px;
    text-decoration: underline;
}

.vsc-adopt:hover:not(:disabled) {
    color: #ffffff;
}

.vsc-adopt:disabled {
    opacity: 0.6;
    cursor: progress;
}

.vsc-error {
    color: rgba(255, 130, 130, 0.9);
    margin-left: 6px;
}
</style>
