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

// --- Git Store (for branch dialog) ---
const gitStore = useGitStore();
const { branches, currentBranchId, currentBranch } = storeToRefs(gitStore);

// Tabs definition
const tabs = [
  { id: 'file', label: 'File' },
  { id: 'project', label: 'Project' },
  { id: 'design', label: 'Design' },
  { id: 'zone', label: 'Zone' },
  { id: 'library', label: 'Library' },
  { id: 'edit', label: 'Edit' },
  { id: 'view', label: 'View' },
];

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

const handleCreateBranch = async (data: { name: string; baseBranch: string; tags: string[]; reason: string }) => {
  console.log('[RibbonToolbar] handleCreateBranch called:', data);

  // 使用用户输入的分支名称（不添加 scheme/ 前缀，让用户自己决定格式）
  const branchName = data.name.trim();

  // 如果基础分支不是当前分支，先切换到基础分支
  if (data.baseBranch && data.baseBranch !== currentBranchId.value) {
    console.log('[RibbonToolbar] Switching to base branch:', data.baseBranch);
    const switchResult = await gitStore.checkout(data.baseBranch);
    if (!switchResult.success) {
      // 切换基础分支时遇到冲突
      if (switchResult.hasUncommittedChanges) {
        console.log('[RibbonToolbar] Conflict detected when switching to base branch');
        pendingBranchData.value = data;
        showConflictDialog.value = true;
        return;
      }
      console.error('[RibbonToolbar] Failed to switch to base branch:', switchResult.message);
      return;
    }
  }

  // 创建新分支（基于当前分支）
  console.log('[RibbonToolbar] Creating new branch:', branchName);
  const result = await gitStore.checkout(branchName, {
    createIfNotExist: true,
    commitMessage: data.reason
  });

  if (result.success) {
    showBranchDialog.value = false;
    console.log('[RibbonToolbar] Branch created successfully:', branchName);
  } else {
    // 创建分支时遇到冲突
    if (result.hasUncommittedChanges) {
      console.log('[RibbonToolbar] Conflict detected when creating branch');
      pendingBranchData.value = data;
      showConflictDialog.value = true;
    } else {
      console.error('[RibbonToolbar] Failed to create branch:', result.message);
    }
  }
};

// 处理冲突确认弹窗的响应
const handleConflictConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
  showConflictDialog.value = false;

  if (!pendingBranchData.value) return;

  const data = pendingBranchData.value;
  const branchName = data.name.trim();

  console.log('[RibbonToolbar] Conflict resolved, saveBeforeSwitch:', saveBeforeSwitch);

  // 如果需要切换基础分支
  if (data.baseBranch && data.baseBranch !== currentBranchId.value) {
    const switchResult = await gitStore.checkout(data.baseBranch, {
      commitBeforeCheckout: saveBeforeSwitch,
      discardBeforeCheckout: !saveBeforeSwitch,
      commitMessage
    });

    if (!switchResult.success) {
      console.error('[RibbonToolbar] Failed to switch to base branch after conflict resolution:', switchResult.message);
      pendingBranchData.value = null;
      return;
    }
  } else {
    // 不需要切换基础分支，直接处理当前分支的未提交更改
    if (saveBeforeSwitch) {
      const commitResult = await gitStore.commit(commitMessage);
      if (!commitResult.success) {
        console.error('[RibbonToolbar] Failed to commit changes:', commitResult.message);
        pendingBranchData.value = null;
        return;
      }
    } else {
      const discardResult = await gitStore.discardChanges();
      if (!discardResult.success) {
        console.error('[RibbonToolbar] Failed to discard changes:', discardResult.message);
        pendingBranchData.value = null;
        return;
      }
    }
  }

  // 创建新分支
  const result = await gitStore.checkout(branchName, {
    createIfNotExist: true,
    commitMessage: data.reason
  });

  if (result.success) {
    showBranchDialog.value = false;
    console.log('[RibbonToolbar] Branch created successfully after conflict resolution:', branchName);
  } else {
    console.error('[RibbonToolbar] Failed to create branch after conflict resolution:', result.message);
  }

  pendingBranchData.value = null;
};

const handleConflictCancel = () => {
  showConflictDialog.value = false;
  pendingBranchData.value = null;
};
</script>

<template>
  <div class="ribbon-container">
    <div class="ribbon-tabs">
      <div
        v-for="tab in tabs"
        :key="tab.id"
        class="tab-wrapper"
        @mouseenter="openTab(tab.id, $event)"
        @mouseleave="closeTab"
      >
        <button
          class="tab-btn"
          :class="{ active: activeTab === tab.id }"
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
              <DesignGroup v-else-if="tab.id === 'design'" @openBranchDialog="handleOpenBranchDialog" />
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
      :visible="showBranchDialog"
      :base-branch="currentBranchId"
      :base-tags="currentBranchTags"
      :all-branches="simpleBranchList"
      @create="handleCreateBranch"
      @cancel="showBranchDialog = false"
    />

    <BranchCheckoutConfirmDialog
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
    transform: translateY(-8px) scale(0.98);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}
</style>
