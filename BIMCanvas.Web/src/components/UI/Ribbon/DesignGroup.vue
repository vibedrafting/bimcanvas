<script setup lang="ts">
import { ref, computed, onMounted, nextTick } from 'vue';
import { storeToRefs } from 'pinia';
import GlassSelect from '../base/GlassSelect.vue';
import GlassButton from '../base/GlassButton.vue';
import BranchCreationDialog from './BranchCreationDialog.vue';
import BranchCheckoutConfirmDialog from './BranchCheckoutConfirmDialog.vue';
import { useGitStore } from '../../../stores/gitStore';
import { useCanvasStore } from '../../../stores/canvasStore';

// --- Git Store ---
const gitStore = useGitStore();
const canvasStore = useCanvasStore();
const { branches, currentBranch, currentBranchId, isLoading, isOffline } = storeToRefs(gitStore);

// 组件挂载时获取分支列表
onMounted(() => {
  gitStore.fetchBranches();
});

// --- Strategy Section ---
const currentStrategy = ref('default');
const strategies = [
  {
    label: 'Default Strategy',
    value: 'default',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>'
  },
  {
    label: 'Minimalist',
    value: 'minimal',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle></svg>'
  },
  {
    label: 'Create New...',
    value: 'new',
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>'
  },
];

// --- Variant/Branch Section ---
const showBranchDialog = ref(false);
const showCheckoutConfirmDialog = ref(false);
const pendingCheckoutBranch = ref('');

// 分支图标
const branchIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="3" x2="6" y2="15"></line><circle cx="18" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M18 9a9 9 0 0 1-9 9"></path></svg>';
const createIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>';

// Computed options including "Create New..."
const branchOptions = computed(() => [
  ...branches.value.map(b => ({
    label: b.name,
    value: b.id,
    tags: b.commit ? [b.commit.message.substring(0, 25) + (b.commit.message.length > 25 ? '...' : '')] : [],
    icon: branchIcon
  })),
  {
    label: 'Create New Branch...',
    value: '__create_new__',
    icon: createIcon
  },
]);

// 分支切换处理
const handleBranchChange = async (val: string | number) => {
  if (val === '__create_new__') {
    // 延迟到下一个事件循环，避免与当前 click 事件冲突
    await nextTick();
    showBranchDialog.value = true;
    return;
  }

  const branchName = val as string;
  const result = await gitStore.checkout(branchName);

  if (result.success) {
    return;
  }

  // 如果有未提交的更改，显示确认弹窗
  if (result.hasUncommittedChanges) {
    pendingCheckoutBranch.value = branchName;
    showCheckoutConfirmDialog.value = true;
    return;
  }

  console.error('切换分支失败:', result.message);
};

// 确认弹窗回调
const handleCheckoutConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
  showCheckoutConfirmDialog.value = false;
  const branchName = pendingCheckoutBranch.value;
  if (!branchName) return;

  if (saveBeforeSwitch) {
    // 1. 先保存内存数据到文件系统
    const saved = await canvasStore.saveToServer();
    if (!saved) {
      console.error('保存数据失败，无法切换分支');
      pendingCheckoutBranch.value = '';
      return;
    }

    // 2. 再用 commitBeforeCheckout 提交并切换
    await gitStore.checkout(branchName, {
      commitBeforeCheckout: true,
      commitMessage
    });
  } else {
    // 放弃更改并切换：Server端原子操作
    await gitStore.checkout(branchName, { discardBeforeCheckout: true });
  }

  pendingCheckoutBranch.value = '';
};

const handleCheckoutCancel = () => {
  showCheckoutConfirmDialog.value = false;
  pendingCheckoutBranch.value = '';
};

// 创建分支处理
const handleCreateBranch = async (data: { name: string; baseBranch: string; tags: string[]; reason: string }) => {
  const branchName = `scheme/${data.name.toLowerCase().replace(/\s+/g, '-')}`;

  // 使用checkout的createIfNotExist功能创建并切换到新分支
  const result = await gitStore.checkout(branchName, true);
  if (result.success) {
    showBranchDialog.value = false;
    console.log('Created Branch:', branchName);
  } else {
    console.error('创建分支失败:', result.message);
    // TODO: 可以添加UI提示
  }
};

// Get current branch tags for dialog defaults
const currentBranchTags = computed(() => {
  const branch = branches.value.find(b => b.id === currentBranchId.value);
  return branch?.commit ? [branch.commit.message.substring(0, 20)] : [];
});

// Simple branch list for dialog base selection
const simpleBranchList = computed(() =>
  branches.value.map(b => ({ label: b.name, value: b.id }))
);
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      
      <!-- 1. Strategy Input -->
      <div class="section">
        <div class="combo-box">
          <span class="label">Strategy</span>
          <GlassSelect 
            v-model="currentStrategy" 
            :options="strategies" 
            width="160px"
          />
        </div>
      </div>

      <div class="separator"></div>

      <!-- 2. AI Action -->
      <div class="section">
        <GlassButton variant="ghost" class="ribbon-btn primary-action">
          <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 2v4M12 18v4M4.93 4.93l2.83 2.83M16.24 16.24l2.83 2.83M2 12h4M18 12h4M4.93 19.07l2.83-2.83M16.24 7.76l2.83-2.83"></path>
          </svg>
          Parallel Run
        </GlassButton>
        
        <GlassButton variant="ghost" class="ribbon-btn">
          <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z"></path>
          </svg>
          AI Chat
        </GlassButton>
      </div>

      <div class="separator"></div>

      <!-- 3. Variant Output -->
      <div class="section">
        <div class="combo-box">
          <span class="label">Current Branch</span>
          <GlassSelect
            :model-value="currentBranchId"
            @update:model-value="handleBranchChange"
            :options="branchOptions"
            width="200px"
            :disabled="isLoading"
          />
        </div>
        
        <GlassButton variant="ghost" class="ribbon-btn">
          <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
            <line x1="12" y1="3" x2="12" y2="21"></line>
          </svg>
          Compare
        </GlassButton>

        <GlassButton variant="ghost" class="ribbon-btn">
          <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <polyline points="6 9 12 15 18 9"></polyline>
          </svg>
          Merge
        </GlassButton>
      </div>

      <!-- Branch Creation Dialog -->
      <BranchCreationDialog
        :visible="showBranchDialog"
        :base-branch="currentBranchId"
        :base-tags="currentBranchTags"
        :all-branches="simpleBranchList"
        @create="handleCreateBranch"
        @cancel="showBranchDialog = false"
      />

      <!-- Branch Checkout Confirm Dialog -->
      <BranchCheckoutConfirmDialog
        :visible="showCheckoutConfirmDialog"
        :target-branch="pendingCheckoutBranch"
        :current-branch="currentBranch"
        @confirm="handleCheckoutConfirm"
        @cancel="handleCheckoutCancel"
      />

    </div>
  </div>
</template>

<style scoped lang="scss">
.ribbon-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.group-content {
  display: flex;
  gap: 8px;
  align-items: center;
  height: 42px;
}

.section {
  display: flex;
  gap: 4px;
  align-items: center;
}

.separator {
  width: 1px;
  height: 24px;
  background: var(--border-dim);
  margin: 0 4px;
}

.combo-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
  justify-content: center;
}

.label {
  font-size: 0.7rem;
  color: var(--text-secondary);
  margin-left: 2px;
}

.ribbon-btn {
  flex-direction: column;
  height: 42px;
  min-width: 42px;
  gap: 2px;
  font-size: 0.7rem;
  padding: 4px 8px;
  
  .icon {
    width: 18px;
    height: 18px;
  }

  &.primary-action {
    color: var(--primary-light);
    
    &:hover {
      background: rgba(var(--primary-rgb), 0.1);
    }
  }
}
</style>
