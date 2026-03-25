<script setup lang="ts">
import { ref, computed } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import { themeService } from '../../services/theme/ThemeService';
import ConflictDialog from './ConflictDialog.vue';
import { ProjectService } from '../../services/ProjectService';
import { ChangeSource } from '../../types/history';

import { storeToRefs } from 'pinia';

const store = useCanvasStore();
const { agentConnectionState, currentOperation } = storeToRefs(store);
const isExpanded = ref(false);
const fileInputRef = ref<HTMLInputElement | null>(null);

// 调试开关：设为 true 可保持灵动岛展开状态，方便截图调试
const DEBUG_KEEP_EXPANDED = false;

// 计算属性：结合调试开关和鼠标状态
const shouldExpand = computed(() => DEBUG_KEEP_EXPANDED || isExpanded.value);

// Theme Toggle
const isDarkTheme = computed(() => themeService.currentTheme.value.name === 'dark');

const toggleTheme = () => {
  themeService.toggleTheme();
};

// === 冲突对话框状态 ===
const showConflictDialog = ref(false);
const conflictProjectName = ref('');
const conflictExistingPath = ref('');
const pendingFile = ref<File | null>(null);

// Actions
const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror' | 'copy') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};

// 加载数据（支持 .bcp 和 .json）
const handleLoad = async () => {
  try {
    // 尝试使用 File System Access API
    if ('showOpenFilePicker' in window) {
      const [fileHandle] = await (window as any).showOpenFilePicker({
        types: [
          {
            description: 'BIMCanvas Project',
            accept: { 'application/octet-stream': ['.bcp'] }
          },
          {
            description: 'BIMCanvas JSON (Legacy)',
            accept: { 'application/json': ['.json'] }
          }
        ],
        multiple: false,
        startIn: 'desktop'
      });

      const file = await fileHandle.getFile();
      const fileName = file.name.toLowerCase();

      if (fileName.endsWith('.bcp')) {
        // 通过文件上传 API 打开项目
        const result = await ProjectService.uploadProject(file);

        if (result.status === 'Conflict') {
          // 显示冲突对话框，保存文件以便后续解决冲突
          pendingFile.value = file;
          conflictProjectName.value = result.projectName || '';
          conflictExistingPath.value = result.existingPath || '';
          showConflictDialog.value = true;
        } else if (result.status === 'Success') {
          // 重新加载项目数据
          await store.loadProject(ChangeSource.UserUpload);
        } else {
          // 错误
          alert(`打开项目失败：${result.message}`);
        }
      } else {
        // JSON 文件（已废弃）
        console.warn('Direct JSON loading is deprecated in v3.0');
      }
    } else {
      // Fallback
      fileInputRef.value?.click();
    }
  } catch (err: any) {
    if (err.name !== 'AbortError') {
      console.error('Failed to open file:', err);
    }
  }
};

// 处理冲突解决
const handleConflictResolve = async (resolution: 'Overwrite' | 'UseExisting' | 'Cancel') => {
  showConflictDialog.value = false;

  if (resolution === 'Cancel') {
    // 用户取消，不做任何操作
    pendingFile.value = null;
    return;
  }

  if (!pendingFile.value) {
    console.error('No pending file for conflict resolution');
    return;
  }

  try {
    const result = await ProjectService.uploadResolveConflict(pendingFile.value, resolution);

    if (result.status === 'Success') {
      // 重新加载项目数据
      await store.loadProject(ChangeSource.UserUpload);
    } else {
      alert(`解决冲突失败：${result.message}`);
    }
  } catch (err: any) {
    console.error('Failed to resolve conflict:', err);
    alert(`解决冲突失败：${err.message}`);
  } finally {
    pendingFile.value = null;
  }
};

const onFileSelected = (event: Event) => {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;

  const reader = new FileReader();
  reader.onload = (_e) => {
    // Note: Direct JSON loading removed in v3.0 - use loadProject with folder path
    console.warn('Direct JSON loading is deprecated in v3.0. Use ?project=path URL parameter.');
  };
  reader.readAsText(file);
  input.value = ''; // 重置以允许重复选择同一文件
};

// 导出数据
const handleExport = async () => {
  if (!store.projectData) return;

  const timestamp = new Date().toISOString()
    .replace(/[-:]/g, '')
    .replace('T', '_')
    .slice(0, 15);
  const filename = `BIMCanvas_${timestamp}.json`;
  const jsonString = JSON.stringify(store.projectData, null, 2);

  try {
    // 尝试使用 File System Access API
    if ('showSaveFilePicker' in window) {
      const handle = await (window as any).showSaveFilePicker({
        suggestedName: filename,
        types: [{
          description: 'BIMCanvas JSON',
          accept: { 'application/json': ['.json'] }
        }],
        startIn: 'desktop' // 默认打开桌面
      });

      const writable = await handle.createWritable();
      await writable.write(jsonString);
      await writable.close();
    } else {
      // Fallback
      const blob = new Blob([jsonString], { type: 'application/json' });
      const url = URL.createObjectURL(blob);

      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      a.click();

      URL.revokeObjectURL(url);
    }
  } catch (err: any) {
    if (err.name !== 'AbortError') {
      console.error('Failed to save file:', err);
    }
  }
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
  <div class="toolbar-container">
    <!-- Top Bar: Branding & Quick Access (Transparent, Top-aligned) -->
    <div class="top-bar">
    <div class="brand-area">
        <span class="brand-text">BIMCanvas</span>
        <div class="divider"></div>
        <GlassButton @click="store.undo()" :disabled="!store.canUndo" variant="ghost" title="Undo" class="icon-btn">
          ↩
        </GlassButton>
        <GlassButton @click="store.redo()" :disabled="!store.canRedo" variant="ghost" title="Redo" class="icon-btn">
          ↪
        </GlassButton>
        <div class="divider"></div>
        <GlassButton @click="handleLoad" variant="ghost" title="Load Data" class="icon-btn">
          <!-- Load/Import Icon (Arrow Down) -->
          <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" y1="15" x2="12" y2="3"></line>
          </svg>
        </GlassButton>
        <GlassButton @click="handleExport" :disabled="!store.projectData" variant="ghost" title="Export Data" class="icon-btn">
          <!-- Export Icon (Arrow Up) -->
          <svg viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="17 8 12 3 7 8"></polyline>
            <line x1="12" y1="3" x2="12" y2="15"></line>
          </svg>
        </GlassButton>
        <!-- 隐藏的文件输入 -->
        <input
          ref="fileInputRef"
          type="file"
          accept=".json"
          style="display: none"
          @change="onFileSelected"
        />
      </div>
    </div>

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
          <GlassButton variant="ghost" title="Select" active class="compact-btn">
            <svg class="icon" viewBox="0 0 24 24" width="1.1em" height="1.1em" fill="currentColor">
              <path d="M7 2l12 11.2-5.8.5 3.3 7.3-2.2.9-3.2-7.4-4.4 4V2z"/>
            </svg>
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

    <!-- 冲突对话框 -->
    <ConflictDialog
      :visible="showConflictDialog"
      :project-name="conflictProjectName"
      :existing-path="conflictExistingPath"
      @resolve="handleConflictResolve"
    />

  </div>
</template>

<style scoped lang="scss">
.toolbar-container {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 0;
  z-index: 100;
  pointer-events: none;
}

.top-bar {
  display: flex;
  align-items: center;
  height: 40px;
  padding: 0 var(--spacing-md);
  /* Glass Header - Standardized */
  background: var(--glass-bg-header);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border-bottom: var(--glass-border);
  pointer-events: auto;

  .brand-area {
    display: flex;
    align-items: center;
    gap: var(--spacing-sm);

    .brand-text {
      font-weight: 600;
      font-size: 0.9rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-sm);
      color: var(--text-primary);
      /* text-shadow removed for cleaner look in light mode */
    }
  }
}

.command-island {
  position: absolute;
  top: 80px;
  left: 50%;
  transform: translateX(-50%);

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
  border: var(--glass-border);
  border-radius: 100px;
  box-shadow: var(--shadow-island), var(--glass-inner-highlight);

  /* Glare Overlay */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
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

.divider {
  width: 1px;
  height: 14px;
  background: var(--border-strong);
  margin: 0 var(--spacing-xs);
}

.divider-vertical {
  width: 1px;
  height: 20px;
  background: var(--border-strong);
  margin: 0 var(--spacing-xs);
}

.icon-btn {
  padding: 4px 8px;
  font-size: 1.1rem;
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

.vision-btn {
  min-width: 80px; /* Equal width for vision toggle buttons */
  justify-content: center;
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
