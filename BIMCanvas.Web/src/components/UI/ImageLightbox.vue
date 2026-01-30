<template>
  <Teleport to="body">
    <Transition name="lightbox">
      <div v-if="visible" class="lightbox-overlay" @click.self="close">
        <button class="lightbox-close" @click="close">&times;</button>
        <img :src="src" class="lightbox-image" alt="preview" />
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { onMounted, onUnmounted } from 'vue';

defineProps<{ visible: boolean; src: string }>();
const emit = defineEmits<{ close: [] }>();
const close = () => emit('close');

// ESC 键关闭
let handler: ((e: KeyboardEvent) => void) | null = null;

onMounted(() => {
  handler = (e: KeyboardEvent) => {
    if (e.key === 'Escape') close();
  };
  window.addEventListener('keydown', handler);
});

onUnmounted(() => {
  if (handler) {
    window.removeEventListener('keydown', handler);
  }
});
</script>

<style lang="scss" scoped>
.lightbox-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.85);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.lightbox-close {
  position: absolute;
  top: 16px;
  right: 16px;
  width: 36px;
  height: 36px;
  border: none;
  background: rgba(255, 255, 255, 0.1);
  color: white;
  font-size: 24px;
  border-radius: 50%;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  line-height: 1;

  &:hover {
    background: rgba(255, 255, 255, 0.2);
  }
}

.lightbox-image {
  max-width: 90vw;
  max-height: 90vh;
  object-fit: contain;
  border-radius: 8px;
}

// 动画
.lightbox-enter-active,
.lightbox-leave-active {
  transition: opacity 0.2s;
}
.lightbox-enter-from,
.lightbox-leave-to {
  opacity: 0;
}
</style>
