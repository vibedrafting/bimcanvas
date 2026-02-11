<script setup lang="ts">
import { ref, computed } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import { themeService } from '../../services/theme/ThemeService';
import { storeToRefs } from 'pinia';

const store = useCanvasStore();
const { agentConnectionState, currentOperation } = storeToRefs(store);
const isExpanded = ref(false);

// 调试开关：设为 true 可保持灵动岛展开状态，方便截图调试
const DEBUG_KEEP_EXPANDED = false;

// 计算属性：结合调试开关和鼠标状态
const shouldExpand = computed(() => DEBUG_KEEP_EXPANDED || isExpanded.value);

// Theme Toggle
const isDarkTheme = computed(() => themeService.currentTheme.value.name === 'dark');

const toggleTheme = () => {
  themeService.toggleTheme();
};

// Actions
const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror' | 'copy' | 'measure') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};

// 打开模块库
const openModuleLibrary = () => {
  window.dispatchEvent(new CustomEvent('bimcanvas:open-module-library'));
};

// Dynamic Status Text
const selectionCount = computed(() => store.selectedIds.length);

const dynamicStatusText = computed(() => {
  // Priority 1: Current Operation (Persistent)
  if (currentOperation.value) {
    const opMap: Record<string, string> = {
      'moving': 'Moving...',
      'rotating': 'Rotating...',
      'deleted': 'Deleted',
      'mirroring': 'Mirroring...',
      'copying': 'Copying...'
    };
    return opMap[currentOperation.value] || currentOperation.value;
  }

  // Priority 2: Selection with count
  if (selectionCount.value > 0) {
    return `Selecting (${selectionCount.value})...`;
  }

  // Priority 3: Default
  return 'BIMCanvas Ready';
});
</script>

<template>
  <!-- Dynamic Command Island -->
  <div
    class="command-island"
    :class="{ expanded: shouldExpand }"
    @mouseenter="isExpanded = true"
    @mouseleave="isExpanded = false"
  >

    <!-- Collapsed View -->
    <div class="island-collapsed" v-show="!shouldExpand">
      <div
        class="status-indicator"
        :class="{
          'connected': agentConnectionState === 'Connected',
          'disconnected': agentConnectionState === 'Disconnected',
          'reconnecting': agentConnectionState === 'Reconnecting'
        }"
        :title="`Agent Status: ${agentConnectionState}`"
      ></div>
      <span class="status-text">
        {{ dynamicStatusText }}
      </span>
    </div>

    <!-- Expanded View -->
    <div class="island-expanded" v-show="shouldExpand">
      <!-- BASIC Group -->
      <div class="group stagger-1">
        <GlassButton @click="openModuleLibrary" variant="ghost" class="compact-btn" title="Open Module Library">
          <svg class="icon" viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="7" height="7"></rect>
            <rect x="14" y="3" width="7" height="7"></rect>
            <rect x="14" y="14" width="7" height="7"></rect>
            <rect x="3" y="14" width="7" height="7"></rect>
          </svg>
          Library
        </GlassButton>
      </div>

      <div class="divider-vertical stagger-2"></div>

      <!-- TRANSFORM Group -->
      <div class="group stagger-3">
        <GlassButton @click="dispatchAction('move')" variant="ghost" class="compact-btn">
          <span class="icon">✥</span> Move
        </GlassButton>
        <GlassButton @click="dispatchAction('copy')" variant="ghost" class="compact-btn">
          <span class="icon">⧉</span> Copy
        </GlassButton>
        <GlassButton @click="dispatchAction('rotate')" variant="ghost" class="compact-btn">
          <span class="icon">↻</span> Rotate
        </GlassButton>
        <GlassButton @click="dispatchAction('measure')" variant="ghost" class="compact-btn">
          <svg class="icon" width="1.1em" height="1.1em" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M2 10h20"></path>
            <path d="M5 10v5"></path>
            <path d="M12 10v8"></path>
            <path d="M19 10v5"></path>
          </svg>
           Measure
        </GlassButton>
        <GlassButton @click="dispatchAction('delete')" :disabled="!store.selectedObject" variant="danger" class="compact-btn">
          <span class="icon">🗑</span> Delete
        </GlassButton>
      </div>

      <!-- Divider before Theme Toggle -->
      <div class="divider-vertical stagger-4"></div>

      <!-- THEME Group -->
      <div class="group stagger-5">
        <button
          @click="toggleTheme"
          class="theme-toggle-btn"
          :class="{ 'light-mode': !isDarkTheme }"
          :title="isDarkTheme ? '切换到亮色模式' : '切换到暗色模式'"
        >
          <!-- 太阳图标 (暗色模式下显示，提示切换到亮色) -->
          <svg v-if="isDarkTheme" class="theme-icon sun" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="5"></circle>
            <line x1="12" y1="1" x2="12" y2="3"></line>
            <line x1="12" y1="21" x2="12" y2="23"></line>
            <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"></line>
            <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"></line>
            <line x1="1" y1="12" x2="3" y2="12"></line>
            <line x1="21" y1="12" x2="23" y2="12"></line>
            <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"></line>
            <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"></line>
          </svg>
          <!-- 月亮图标 (亮色模式下显示，提示切换到暗色) -->
          <svg v-else class="theme-icon moon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"></path>
          </svg>
        </button>
      </div>

    </div>

  </div>
</template>

<style scoped lang="scss">
.command-island {
  position: absolute;
  top: 120px; /* Adjusted to 120px to avoid Ribbon overlap */
  left: 50%;
  transform: translateX(-50%);
  z-index: 200; /* Ensure it's above ribbon */

  /* 折叠状态固定尺寸 */
  width: 180px;
  height: 36px;

  display: flex;
  align-items: center;
  justify-content: center;
  padding: 0 16px;

  /* Aurora Glass */
  background: var(--glass-bg); /* Base layer */
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  /* Enhanced Border & Glow */
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  border-radius: 100px;
  
  /* Glare & Deep Shadow */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow: 
    0 12px 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */

  background-origin: border-box;
  background-clip: padding-box, border-box;

  pointer-events: auto;
  /* Apple Dynamic Island Spring Physics */
  transition:
    width 0.6s cubic-bezier(0.19, 1, 0.22, 1),
    height 0.6s cubic-bezier(0.19, 1, 0.22, 1),
    padding 0.6s cubic-bezier(0.19, 1, 0.22, 1),
    background 0.3s ease,
    box-shadow 0.3s ease,
    border-radius 0.6s cubic-bezier(0.19, 1, 0.22, 1),
    backdrop-filter 0.3s ease;
  overflow: hidden;

  /* 展开状态 - 高度固定，宽度动态 */
  &.expanded {
    /* Expanded state inherits the same glass texture, just changes shape */
    width: auto;           /* 宽度动态 */
    min-width: 420px;      /* 最小宽度保证内容不压缩 */
    height: 52px;          /* 高度固定 */
    padding: 0 20px;
    border-radius: 26px;
    /* No change to background/shadow to ensure perfect consistency */
  }

  /* Collapsed Content */
  .island-collapsed {
    display: flex;
    align-items: center;
    gap: 10px;
    white-space: nowrap;

    .status-indicator {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--text-tertiary);
      box-shadow: 0 0 4px rgba(0, 0, 0, 0.1);
      transition: all 0.3s;

      &.connected {
        background: #4CAF50; /* Green */
        box-shadow: 0 0 8px rgba(76, 175, 80, 0.6);
      }

      &.disconnected {
        background: #F44336; /* Red */
        box-shadow: 0 0 8px rgba(244, 67, 54, 0.6);
      }

      &.reconnecting {
        background: #FFC107; /* Amber */
        box-shadow: 0 0 8px rgba(255, 193, 7, 0.6);
        animation: pulse 1.5s infinite;
      }
    }

    .status-text {
      font-size: 0.85rem;
      color: var(--text-primary);
      font-weight: 500;
      letter-spacing: 0.3px;
    }
  }

  /* Expanded Content */
  .island-expanded {
    display: flex;
    align-items: center;
    gap: var(--spacing-md);
    width: 100%;
    justify-content: center;
    animation: fadeIn 0.3s ease-out forwards;
  }
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateY(5px); }
  to { opacity: 1; transform: translateY(0); }
}

.group {
  display: flex;
  gap: 4px;
  align-items: center;
}

.divider-vertical {
  width: 1px;
  height: 20px;
  background: var(--border-strong);
  margin: 0 var(--spacing-xs);
}

.compact-btn {
  height: 32px; /* Enforce consistent height */
  padding: 0 12px; /* Adjust padding for fixed height */
  display: inline-flex;
  align-items: center;
  font-size: 0.9rem;
  border-radius: 20px !important;
  box-sizing: border-box;

  .icon {
    margin-right: 6px;
    font-size: 1.1em;
    line-height: 1; /* Prevent icon from affecting line height */
  }
}

/* Stagger Animation for Dynamic Island content */
.island-expanded {
  .stagger-1, .stagger-2, .stagger-3, .stagger-4, .stagger-5 {
    opacity: 0;
    animation: staggerFadeIn 0.5s var(--ease-spring) forwards;
  }

  .stagger-1 { animation-delay: 0.05s; }
  .stagger-2 { animation-delay: 0.1s; }
  .stagger-3 { animation-delay: 0.15s; }
  .stagger-4 { animation-delay: 0.2s; }
  .stagger-5 { animation-delay: 0.25s; }
}

/* Theme Toggle Button - 精美的明暗切换按钮 */
.theme-toggle-btn {
  width: 36px;
  height: 36px;
  border-radius: 50%;
  border: none;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.4s var(--ease-spring);
  position: relative;
  overflow: hidden;

  /* 暗色模式下的默认样式：亮色外观，金黄色调 */
  background: linear-gradient(135deg, #fef3c7 0%, #fcd34d 50%, #f59e0b 100%);
  color: #78350f;
  box-shadow:
    0 2px 8px rgba(251, 191, 36, 0.4),
    0 4px 16px rgba(245, 158, 11, 0.2),
    inset 0 1px 0 rgba(255, 255, 255, 0.5);

  &:hover {
    transform: scale(1.1) rotate(15deg);
    box-shadow:
      0 4px 12px rgba(251, 191, 36, 0.5),
      0 6px 24px rgba(245, 158, 11, 0.3),
      inset 0 1px 0 rgba(255, 255, 255, 0.6);
  }

  &:active {
    transform: scale(0.95);
  }

  /* 明亮模式下的样式：暗色外观，深蓝色调 */
  &.light-mode {
    background: linear-gradient(135deg, #1e3a5f 0%, #1e40af 50%, #3730a3 100%);
    color: #e0e7ff;
    box-shadow:
      0 2px 8px rgba(30, 64, 175, 0.4),
      0 4px 16px rgba(55, 48, 163, 0.2),
      inset 0 1px 0 rgba(255, 255, 255, 0.15);

    &:hover {
      transform: scale(1.1) rotate(-15deg);
      box-shadow:
        0 4px 12px rgba(30, 64, 175, 0.5),
        0 6px 24px rgba(55, 48, 163, 0.3),
        inset 0 1px 0 rgba(255, 255, 255, 0.2);
    }
  }

  .theme-icon {
    width: 20px;
    height: 20px;
    transition: transform 0.4s var(--ease-spring);

    &.sun {
      animation: sunPulse 2s ease-in-out infinite;
    }

    &.moon {
      animation: moonFloat 3s ease-in-out infinite;
    }
  }
}

@keyframes sunPulse {
  0%, 100% {
    transform: scale(1) rotate(0deg);
    filter: drop-shadow(0 0 2px rgba(251, 191, 36, 0.6));
  }
  50% {
    transform: scale(1.05) rotate(10deg);
    filter: drop-shadow(0 0 6px rgba(251, 191, 36, 0.8));
  }
}

@keyframes moonFloat {
  0%, 100% {
    transform: translateY(0) rotate(0deg);
  }
  50% {
    transform: translateY(-2px) rotate(-5deg);
  }
}

@keyframes staggerFadeIn {
  from {
    opacity: 0;
    transform: scale(0.8) translateY(4px);
  }
  to {
    opacity: 1;
    transform: scale(1) translateY(0);
  }
}

@keyframes pulse {
  0% { opacity: 1; }
  50% { opacity: 0.5; }
  100% { opacity: 1; }
}
</style>
