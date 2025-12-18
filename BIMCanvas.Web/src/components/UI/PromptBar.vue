<template>
    <Transition name="slide-up">
        <div v-if="promptMessage" class="prompt-bar">
            <div class="prompt-content">
                <span class="icon">ℹ️</span>
                <span class="text">{{ promptMessage }}</span>
            </div>
        </div>
    </Transition>
</template>

<script setup lang="ts">
import { useCanvasStore } from '../../stores/canvasStore';
import { storeToRefs } from 'pinia';

const store = useCanvasStore();
const { promptMessage } = storeToRefs(store);
</script>

<style scoped>
.prompt-bar {
    position: fixed;
    bottom: 32px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 1000;
    pointer-events: none; /* Allow clicking through if needed, though usually it's just text */
}

.prompt-content {
    background: rgba(20, 20, 25, 0.8);
    backdrop-filter: blur(12px);
    -webkit-backdrop-filter: blur(12px);
    border: 1px solid rgba(255, 255, 255, 0.1);
    padding: 12px 24px;
    border-radius: 999px; /* Pill shape */
    display: flex;
    align-items: center;
    gap: 12px;
    box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
    color: #e0e0e0;
    font-family: 'Inter', sans-serif;
    font-size: 14px;
    font-weight: 500;
    letter-spacing: 0.02em;
}

.icon {
    font-size: 16px;
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
