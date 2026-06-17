<script setup lang="ts">
import { ref, computed } from 'vue';
import { storeToRefs } from 'pinia';
import FileGroup from './Ribbon/FileGroup.vue';
import ProjectGroup from './Ribbon/ProjectGroup.vue';
import DesignGroup from './Ribbon/DesignGroup.vue';
import ZoneGroup from './Ribbon/ZoneGroup.vue';
import LibraryGroup from './Ribbon/LibraryGroup.vue';
import EditGroup from './Ribbon/EditGroup.vue';
import ViewGroup from './Ribbon/ViewGroup.vue';
import BranchCreationDialog from './Ribbon/BranchCreationDialog.vue';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';
import { useGitStore } from '../../stores/gitStore';
import { getWebRuntime } from '../../runtime/runtimeRegistry';
import { supports } from '../../runtime/WebRuntimeProtocol';
import { createLogger } from '../../utils/logger';

const log = createLogger('USER');

// --- Git Store (for branch dialog) ---
const gitStore = useGitStore();
const { branches, currentBranchId, currentBranch } = storeToRefs(gitStore);
const runtime = getWebRuntime();
const canGitBranching = supports(runtime.capabilities.gitBranching);
const canAgentChat = supports(runtime.capabilities.agentChat);
const canUseModuleLibrary = supports(runtime.capabilities.moduleLibrary);
const canEnvision = supports(runtime.capabilities.envisionPanel);

// Tabs definition
const baseTabs = [
  { id: 'file', label: 'File' },
  { id: 'project', label: 'Project' },
  { id: 'design', label: 'Design' },
  { id: 'zone', label: 'Zone' },
  { id: 'library', label: 'Library' },
  { id: 'edit', label: 'Edit' },
  { id: 'view', label: 'View' },
  { id: 'envision', label: 'Envision' },
];

const tabs = computed(() => baseTabs.filter((tab) => {
  if (tab.id === 'design') return canGitBranching || canAgentChat;
  if (tab.id === 'library') return canUseModuleLibrary;
  if (tab.id === 'envision') return canEnvision;
  return true;
}));

const activeTab = ref<string | null>(null);
const dropdownPos = ref({ top: 0, left: 0 });
let closeTimer: any = null;

const openTab = (id: string, event: MouseEvent) => {
  if (closeTimer) clearTimeout(closeTimer);

  const target = event.currentTarget as HTMLElement;
  const rect = target.getBoundingClientRect();

  dropdownPos.value = {
    top: rect.bottom + 4,
    left: rect.left
  };

  activeTab.value = id;
};

const closeTab = () => {
  closeTimer = setTimeout(() => {
    activeTab.value = null;
  }, 300);
};

const keepOpen = () => {
  if (closeTimer) clearTimeout(closeTimer);
};

// --- Branch Creation Dialog ---
const showBranchDialog = ref(false);

// --- Branch Conflict Confirm Dialog ---
const showConflictDialog = ref(false);
const pendingBranchData = ref<{ name: string; baseBranch: string; tags: string[]; reason: string } | null>(null);

const currentBranchTags = computed(() => {
  const branch = branches.value.find(b => b.id === currentBranchId.value);
  return branch?.commit ? [branch.commit.message.substring(0, 20)] : [];
});

const simpleBranchList = computed(() =>
  branches.value.map(b => ({ label: b.name, value: b.id }))
);

const handleOpenBranchDialog = () => {
  showBranchDialog.value = true;
};

const handleCreateBranch = async (data: { name: string; baseBranch: string; tags: string[]; reason: string; switchAfterCreate?: boolean }) => {
  log.debug('handleCreateBranch called', { data });

  const branchName = data.name.trim();

  // 直接创建分支，传递 baseBranch（无需先切换）
  // Git 支持 `git branch <new-branch> <base-branch>` 直接基于任意分支创建
  log.debug('Creating new branch', { branchName, baseBranch: data.baseBranch });
  const result = await gitStore.checkout(branchName, {
    createIfNotExist: true,
    commitMessage: data.reason,
    baseBranch: data.baseBranch,
    switchAfterCreate: data.switchAfterCreate ?? true
  });

  if (result.success) {
    showBranchDialog.value = false;
    log.info('Branch created', { branchName });
  } else if (result.hasUncommittedChanges) {
    // 有未提交的更改，弹出冲突确认对话框
    log.debug('Conflict detected: uncommitted changes');
    pendingBranchData.value = data;
    showConflictDialog.value = true;
  } else {
    log.error('Failed to create branch', { message: result.message });
  }
};

// 处理冲突确认弹窗的响应
const handleConflictConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
  showConflictDialog.value = false;

  if (!pendingBranchData.value) return;

  const data = pendingBranchData.value;
  const branchName = data.name.trim();

  log.debug('Conflict resolved', { saveBeforeSwitch });

  // 直接创建分支，让 Server 处理存档/放弃操作
  // baseBranch 确保新分支基于正确的分支创建
  const result = await gitStore.checkout(branchName, {
    createIfNotExist: true,
    commitBeforeCheckout: saveBeforeSwitch,
    discardBeforeCheckout: !saveBeforeSwitch,
    commitMessage: saveBeforeSwitch ? commitMessage : data.reason,
    baseBranch: data.baseBranch  // 传递基础分支
  });

  if (result.success) {
    showBranchDialog.value = false;
    log.info('Branch created after conflict resolution', { branchName });
  } else {
    log.error('Failed to create branch after conflict resolution', { message: result.message });
  }

  pendingBranchData.value = null;
};

const handleConflictCancel = () => {
  showConflictDialog.value = false;
  pendingBranchData.value = null;
};

const dispatchEnvisionOpen = () => {
  window.dispatchEvent(new CustomEvent('bimcanvas:open-envision-panel'));
};
</script>

<template>
  <div class="ribbon-container">
    <div class="ribbon-tabs">
      <div
        v-for="tab in tabs"
        :key="tab.id"
        class="tab-wrapper"
        @mouseenter="tab.id !== 'envision' && openTab(tab.id, $event)"
        @mouseleave="closeTab"
      >
        <button
          class="tab-btn"
          :class="{ active: activeTab === tab.id }"
          @click="tab.id === 'envision' && dispatchEnvisionOpen()"
        >
          {{ tab.label }}
        </button>

        <Teleport to="body">
          <div
            class="ribbon-dropdown"
            v-if="activeTab === tab.id"
            :style="{ top: dropdownPos.top + 'px', left: dropdownPos.left + 'px' }"
            @mouseenter="keepOpen"
            @mouseleave="closeTab"
          >
            <div class="panel-content">
              <FileGroup v-if="tab.id === 'file'" />
              <ProjectGroup v-else-if="tab.id === 'project'" />
              <DesignGroup v-else-if="tab.id === 'design' && canGitBranching" @openBranchDialog="handleOpenBranchDialog" />
              <ZoneGroup v-else-if="tab.id === 'zone'" />
              <LibraryGroup v-else-if="tab.id === 'library'" />
              <EditGroup v-else-if="tab.id === 'edit'" />
              <ViewGroup v-else-if="tab.id === 'view'" />
            </div>
          </div>
        </Teleport>
      </div>
    </div>

    <BranchCreationDialog
      v-if="canGitBranching"
      :visible="showBranchDialog"
      :base-branch="currentBranchId"
      :base-tags="currentBranchTags"
      :all-branches="simpleBranchList"
      @create="handleCreateBranch"
      @cancel="showBranchDialog = false"
    />

    <BranchCheckoutConfirmDialog
      v-if="canGitBranching"
      :visible="showConflictDialog"
      :target-branch="pendingBranchData?.name ?? ''"
      :current-branch="currentBranch"
      :is-creating="true"
      @confirm="handleConflictConfirm"
      @cancel="handleConflictCancel"
    />
  </div>
</template>

<style scoped lang="scss">
.ribbon-container {
  position: relative;
  left: 0;
  width: 100%;
  height: 40px;
  z-index: 90;
  pointer-events: auto;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  padding: 0 4px;
  background: transparent;
}

.ribbon-tabs {
  display: flex;
  gap: 4px;
  height: 100%;
  align-items: center;
}

.tab-wrapper {
  position: relative;
  height: 100%;
  display: flex;
  align-items: center;
}

.tab-btn {
  background: transparent;
  border: none;
  color: var(--text-secondary);
  padding: 4px 12px;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
  border-radius: 4px;
  transition: background-color 0.2s, color 0.2s;
  position: relative;
  white-space: nowrap;

  &:hover {
    color: var(--text-primary);
    background: rgba(255, 255, 255, 0.05);
  }

  &.active {
    color: var(--tab-text-active);
    background: var(--tab-bg-active);
  }
}

.ribbon-dropdown {
  position: fixed;
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: var(--glass-border);
  border-radius: 8px;
  padding: 12px;
  box-shadow: var(--shadow-island), var(--glass-inner-highlight);
  pointer-events: auto;
  min-width: 200px;
  z-index: 1000;
  animation: dropdownSlideDown 0.2s var(--ease-spring);
  transform-origin: top left;
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  background-origin: border-box;
  background-clip: padding-box, border-box;
}

.panel-content {
  display: flex;
  gap: 12px;
  justify-content: flex-start;
}

@keyframes dropdownSlideDown {
  from {
    opacity: 0;
    transform: translateY(-8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}
</style>
