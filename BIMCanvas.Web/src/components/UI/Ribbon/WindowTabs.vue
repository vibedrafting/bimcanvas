<script setup lang="ts">
import { computed, ref } from 'vue';
import { useWindowStore, type VirtualWindow } from '../../../stores/windowStore';
import { useGitStore } from '../../../stores/gitStore';
import { useCanvasStore } from '../../../stores/canvasStore';
import { ChangeSource } from '../../../types/history';
import GlassButton from '../base/GlassButton.vue';

const windowStore = useWindowStore();
const gitStore = useGitStore();
const canvasStore = useCanvasStore();

// 是否显示新窗口下拉菜单
const showNewWindowMenu = ref(false);

// 当前窗口
const activeWindow = computed(() => windowStore.activeWindow);

// 所有窗口
const windows = computed(() => windowStore.windows);

// 可用于新窗口的分支列表（直接显示所有分支，和 DesignGroup 逻辑一致）
const availableBranches = computed(() => gitStore.branches);

// 切换窗口
const handleTabClick = async (window: VirtualWindow) => {
  if (window.id !== windowStore.activeWindowId) {
    // ⭐ 移除 checkout 调用：虚拟窗口已在 worktree 中绑定分支
    // 只需激活窗口，Server 端会自动路由到正确的 Worktree
    windowStore.activateWindow(window.id);

    // 重新加载项目数据（Server 端会根据 ActiveWindowId 自动路由）
    await canvasStore.loadProject({
      source: ChangeSource.GitCheckout,
      preserveView: true
    });
  }
};

// 关闭窗口
const handleCloseTab = async (window: VirtualWindow, event: MouseEvent) => {
  event.stopPropagation();

  if (window.isDirty) {
    const confirmed = confirm(`窗口 "${window.title}" 有未保存的更改，确定关闭吗？`);
    if (!confirmed) return;
  }

  await windowStore.closeVirtualWindow(window.id, true);
};

// 创建新窗口
const handleCreateWindow = async (branchName: string) => {
  showNewWindowMenu.value = false;
  const newWindow = await windowStore.createVirtualWindow(branchName);
  if (newWindow) {
    // 切换到新分支
    await gitStore.checkout(branchName);
  }
};

// 切换下拉菜单
const toggleNewWindowMenu = () => {
  if (availableBranches.value.length === 0) {
    alert('没有可用的分支（所有分支都已被窗口占用）');
    return;
  }
  showNewWindowMenu.value = !showNewWindowMenu.value;
};

// 点击外部关闭菜单
const closeMenu = () => {
  showNewWindowMenu.value = false;
};
</script>

<template>
  <div class="window-tabs" @click.self="closeMenu">
    <!-- 窗口标签 -->
    <div class="tabs-container">
      <div
        v-for="win in windows"
        :key="win.id"
        class="tab"
        :class="{ active: win.id === windowStore.activeWindowId, dirty: win.isDirty }"
        @click="handleTabClick(win)"
      >
        <span class="tab-title">{{ win.title }}</span>
        <span class="tab-branch">{{ win.branchName }}</span>
        <button class="close-btn" @click="handleCloseTab(win, $event)" title="Close Window">
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <line x1="18" y1="6" x2="6" y2="18"></line>
            <line x1="6" y1="6" x2="18" y2="18"></line>
          </svg>
        </button>
      </div>
    </div>

    <!-- 新建窗口按钮 -->
    <div class="new-window-container">
      <GlassButton
        variant="ghost"
        class="new-btn"
        @click="toggleNewWindowMenu"
        title="New Window"
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <line x1="12" y1="5" x2="12" y2="19"></line>
          <line x1="5" y1="12" x2="19" y2="12"></line>
        </svg>
      </GlassButton>

      <!-- 分支选择下拉菜单 -->
      <Transition name="fade">
        <div v-if="showNewWindowMenu" class="branch-dropdown">
          <div class="dropdown-header">Select Branch</div>
          <div
            v-for="branch in availableBranches"
            :key="branch.id"
            class="dropdown-item"
            @click="handleCreateWindow(branch.name)"
          >
            <span class="branch-name">{{ branch.name }}</span>
            <span class="branch-commit" v-if="branch.commit">
              {{ branch.commit.hash?.substring(0, 7) }}
            </span>
          </div>
          <div v-if="availableBranches.length === 0" class="dropdown-empty">
            No available branches
          </div>
        </div>
      </Transition>
    </div>
  </div>
</template>

<style scoped lang="scss">
.window-tabs {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 8px;
  background: var(--bg-secondary);
  border-bottom: 1px solid var(--border-subtle);
}

.tabs-container {
  display: flex;
  gap: 2px;
  overflow-x: auto;
  max-width: calc(100% - 40px);

  &::-webkit-scrollbar {
    height: 4px;
  }

  &::-webkit-scrollbar-thumb {
    background: var(--border-subtle);
    border-radius: 2px;
  }
}

.tab {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-radius: 6px 6px 0 0;
  background: var(--bg-tertiary);
  cursor: pointer;
  user-select: none;
  transition: all 0.2s ease;
  border: 1px solid transparent;
  border-bottom: none;
  min-width: 120px;
  max-width: 200px;

  &:hover {
    background: var(--bg-hover);
  }

  &.active {
    background: var(--bg-primary);
    border-color: var(--border-subtle);

    .tab-title {
      color: var(--text-primary);
    }
  }

  &.dirty {
    .tab-title::after {
      content: '*';
      color: var(--warning);
      margin-left: 2px;
    }
  }
}

.tab-title {
  font-size: 0.85rem;
  font-weight: 500;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  flex: 1;
  user-select: none;
  cursor: pointer !important;
}

.tab-branch {
  font-size: 0.7rem;
  color: var(--text-tertiary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  max-width: 60px;
  user-select: none;
  cursor: pointer !important;
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  border: none;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  opacity: 0.5;
  transition: all 0.2s ease;
  flex-shrink: 0;

  &:hover {
    opacity: 1;
    background: var(--bg-hover);
  }

  svg {
    stroke: var(--text-secondary);
  }
}

.new-window-container {
  position: relative;
  flex-shrink: 0;
}

.new-btn {
  width: 28px;
  height: 28px;
  padding: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}

.branch-dropdown {
  position: absolute;
  top: 100%;
  right: 0;
  margin-top: 4px;
  min-width: 180px;
  background: var(--bg-primary);
  border: 1px solid var(--border-subtle);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.15);
  z-index: 100;
  overflow: hidden;
}

.dropdown-header {
  padding: 8px 12px;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-secondary);
}

.dropdown-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  cursor: pointer;
  transition: background 0.15s ease;

  &:hover {
    background: var(--bg-hover);
  }
}

.branch-name {
  font-size: 0.85rem;
  color: var(--text-primary);
}

.branch-commit {
  font-size: 0.75rem;
  color: var(--text-tertiary);
  font-family: monospace;
}

.dropdown-empty {
  padding: 16px 12px;
  text-align: center;
  color: var(--text-tertiary);
  font-size: 0.85rem;
}

.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.15s ease, transform 0.15s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
