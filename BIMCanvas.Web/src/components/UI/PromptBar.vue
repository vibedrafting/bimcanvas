<template>
    <Transition name="slide-up">
        <div v-if="promptMessage || hasControls" class="prompt-bar" :class="{ 'with-controls': hasControls }">
            <div v-if="promptMessage" class="prompt-content">
                <span class="icon">ℹ️</span>
                <span class="text">{{ promptMessage }}</span>
            </div>
            <div v-if="hasControls" class="controls-row">
                <slot name="controls"></slot>
            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { storeToRefs } from 'pinia';
import { computed, useSlots } from 'vue';

const store = useCanvasStore();
const { promptMessage } = storeToRefs(store);

const slots = useSlots();
// 由调用方在 <template v-if="..." #controls> 上控制：未传 slot 时退化为简短文本 pill
const hasControls = computed(() => !!slots.controls);
</script>

<style scoped>
.prompt-bar {
    position: fixed;
    bottom: 32px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 1000;
    pointer-events: none; /* 默认 pill 文本路径放行 */
    display: flex;
    flex-direction: column;
    align-items: stretch;
}

/* 默认 pill：单行文本，无控件 */
.prompt-content {
    background: rgba(20, 20, 25, 0.8);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    border: 1px solid rgba(255, 255, 255, 0.1);
    padding: 12px 24px;
    border-radius: 999px; /* Pill */
    display: flex;
    align-items: center;
    gap: 12px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
    color: #e0e0e0;
    font-family: 'Inter', sans-serif;
    font-size: 14px;
    font-weight: 500;
    letter-spacing: 0.02em;
    align-self: center;
}

.icon {
    font-size: 16px;
}

/* 含控件时：扩成可承载 slider/输入框的卡片 */
.prompt-bar.with-controls {
    pointer-events: auto;
    width: min(540px, 90vw);
}

.prompt-bar.with-controls .prompt-content {
    border-radius: 18px 18px 0 0;
    border-bottom: none;
    width: 100%;
    box-sizing: border-box;
    align-self: stretch;
    box-shadow: none;
}

.controls-row {
    background: rgba(20, 20, 25, 0.85);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    border: 1px solid rgba(255, 255, 255, 0.1);
    border-top: none;
    padding: 12px 18px 14px;
    border-radius: 0 0 18px 18px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
    color: #e0e0e0;
}

/* Transition */
.slide-up-enter-active,
.slide-up-leave-active {
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.slide-up-enter-from,
.slide-up-leave-to {
    opacity: 0;
    transform: translate(-50%, 20px);
}
</style>
