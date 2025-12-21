<script setup lang="ts">
/**
 * FloatingInput - 浮动数值输入框
 *
 * 在移动/旋转操作中，用户按下数字键时出现在鼠标位置附近。
 * 支持输入精确的距离（mm）或角度（°）值。
 */
import { computed } from 'vue';
import { NumericInputManager } from '../../services/interaction/NumericInputManager';

const manager = NumericInputManager.getInstance();

// 计算输入框位置（鼠标右下方偏移）
const style = computed(() => ({
    left: `${manager.position.value.x + 16}px`,
    top: `${manager.position.value.y - 40}px`,
}));

// 单位显示文本
const unitText = computed(() => {
    return manager.config.value?.unit === 'deg' ? '°' : 'mm';
});
</script>

<template>
  <Teleport to="body">
    <Transition name="fade-scale">
      <div
        v-if="manager.isActive.value"
        class="floating-input"
        :style="style"
      >
        <input
          :value="manager.inputValue.value"
          :placeholder="manager.config.value?.placeholder || '输入数值'"
          readonly
          class="input-field"
        />
        <span class="unit">{{ unitText }}</span>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.floating-input {
  position: fixed;
  z-index: 1000;
  display: flex;
  align-items: center;
  gap: 4px;

  background: var(--glass-bg, rgba(20, 20, 30, 0.85));
  backdrop-filter: var(--glass-blur, blur(12px));
  -webkit-backdrop-filter: var(--glass-blur, blur(12px));
  border: var(--glass-border, 1px solid rgba(255, 255, 255, 0.1));
  border-radius: 6px;
  padding: 6px 10px;
  box-shadow: var(--shadow-panel, 0 4px 12px rgba(0, 0, 0, 0.3));

  .input-field {
    width: 44px;
    background: transparent;
    border: none;
    outline: none;
    color: var(--text-primary, #fff);
    font-family: var(--font-mono, 'JetBrains Mono', monospace);
    font-size: 14px;
    text-align: right;
    caret-color: var(--accent-blue, #0a84ff);

    &::placeholder {
      color: var(--text-tertiary, rgba(255, 255, 255, 0.4));
      font-size: 12px;
    }
  }

  .unit {
    color: var(--text-secondary, rgba(255, 255, 255, 0.7));
    font-size: 12px;
    font-family: var(--font-mono, 'JetBrains Mono', monospace);
  }
}

// 入场/离场动画
.fade-scale-enter-active,
.fade-scale-leave-active {
  transition: all 0.15s cubic-bezier(0.4, 0, 0.2, 1);
}

.fade-scale-enter-from,
.fade-scale-leave-to {
  opacity: 0;
  transform: scale(0.95) translateY(4px);
}
</style>
