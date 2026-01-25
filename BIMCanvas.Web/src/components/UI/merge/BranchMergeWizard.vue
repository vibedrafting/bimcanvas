<script setup lang="ts">
import { computed, watch, onMounted, onUnmounted } from 'vue';
import { storeToRefs } from 'pinia';
import { useMergeStore } from '../../../stores/mergeStore';
import { useGitStore } from '../../../stores/gitStore';
import { useCanvasStore } from '../../../stores/canvasStore';
import { ChangeSource } from '../../../types/history';
import BranchSelectStep from './BranchSelectStep.vue';
import MergeConfirmStep from './MergeConfirmStep.vue';

console.log('[BranchMergeWizard] Component script setup executing');

const mergeStore = useMergeStore();
const gitStore = useGitStore();
const canvasStore = useCanvasStore();

const {
  isVisible, currentStep, targetBranch, sourceBranch, isMerging, error, canProceed,
  worktreeNames, selectedWorktree, cleanupAfterMerge,
  isWorktreeMode, worktreeOptions
} = storeToRefs(mergeStore);
const { branches, currentBranch } = storeToRefs(gitStore);

// 分支选项（排除"创建新分支"选项）
const branchOptions = computed(() => {
  console.log('[BranchMergeWizard] branchOptions computed, branches count:', branches.value.length);
  return branches.value.map(b => ({
    value: b.name,
    label: b.name,
    isCurrent: b.isCurrent
  }));
});

// 当向导打开时，获取分支列表并设置默认目标分支
watch(isVisible, async (visible, oldVisible) => {
  console.log('[BranchMergeWizard] watch isVisible triggered:', oldVisible, '->', visible);
  if (visible) {
    // 立即设置默认目标分支，避免等待 fetchBranches 导致的 UI 闪烁
    if (currentBranch.value && !targetBranch.value) {
      console.log('[BranchMergeWizard] Setting default targetBranch immediately to:', currentBranch.value);
      mergeStore.targetBranch = currentBranch.value;
    }

    console.log('[BranchMergeWizard] Wizard opened, fetching branches...');
    await gitStore.fetchBranches();
    console.log('[BranchMergeWizard] Branches fetched, currentBranch:', currentBranch.value);
    
    // fetch 后再次确认（以防 fetch 更新了 currentBranch）
    if (currentBranch.value && !targetBranch.value) {
      console.log('[BranchMergeWizard] Setting default targetBranch (post-fetch) to:', currentBranch.value);
      mergeStore.targetBranch = currentBranch.value;
    }
  } else {
    console.log('[BranchMergeWizard] Wizard closed');
  }
}, { immediate: false });

// 监控 isVisible 的变化（用于调试）
watch(() => mergeStore.isVisible, (val) => {
  console.log('[BranchMergeWizard] Direct store isVisible changed to:', val);
});

onMounted(() => {
  console.log('[BranchMergeWizard] Component mounted, isVisible:', isVisible.value);
});

onUnmounted(() => {
  console.log('[BranchMergeWizard] Component unmounted');
});

// 执行合并
const handleMerge = async () => {
  console.log('[BranchMergeWizard] handleMerge called');
  const result = await mergeStore.executeOverwriteMerge();
  if (result.success) {
    // 刷新 Canvas 显示合并后的结果
    await canvasStore.loadProject({ source: ChangeSource.ServerSync, preserveView: true });
    // 刷新分支列表
    await gitStore.fetchBranches();
  }
};

// 关闭向导
const handleClose = () => {
  console.log('[BranchMergeWizard] handleClose called');
  mergeStore.closeWizard();
};

// 暴露打开方法供外部调用
defineExpose({
  open: () => {
    console.log('[BranchMergeWizard] expose.open() called');
    mergeStore.openWizard();
  }
});
</script>

<template>
  <Teleport to="body">
    <Transition name="fade">
      <div v-if="isVisible" class="wizard-overlay">
        <div class="wizard-container">
          <!-- 标题栏 -->
          <div class="wizard-header">
            <h3>合并分支</h3>
            <button class="close-btn" @click="handleClose">
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <!-- 步骤指示器 -->
          <div class="step-indicator">
            <div class="step" :class="{ active: currentStep === 1, done: currentStep > 1 }">
              <span class="step-number">1</span>
              <span class="step-label">选择分支</span>
            </div>
            <div class="step-line" :class="{ active: currentStep > 1 }"></div>
            <div class="step" :class="{ active: currentStep === 2 }">
              <span class="step-number">2</span>
              <span class="step-label">确认合并</span>
            </div>
          </div>

          <!-- 步骤内容 -->
          <div class="wizard-content">
            <BranchSelectStep
              v-if="currentStep === 1"
              v-model:targetBranch="mergeStore.targetBranch"
              v-model:sourceBranch="mergeStore.sourceBranch"
              v-model:selectedWorktree="mergeStore.selectedWorktree"
              :branches="branchOptions"
              :canProceed="canProceed"
              :worktreeMode="isWorktreeMode"
              :worktreeOptions="worktreeOptions"
              @next="mergeStore.nextStep()"
            />
            <MergeConfirmStep
              v-else-if="currentStep === 2"
              :targetBranch="targetBranch"
              :sourceBranch="sourceBranch"
              :isMerging="isMerging"
              :error="error"
              :showCleanupOption="isWorktreeMode"
              :worktreeName="selectedWorktree"
              v-model:cleanupAfterMerge="mergeStore.cleanupAfterMerge"
              @back="mergeStore.prevStep()"
              @cancel="handleClose"
              @confirm="handleMerge"
            />
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.wizard-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  backdrop-filter: blur(4px);
}

.wizard-container {
  width: 480px;
  background: #1a1d24;
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.5);
  overflow: hidden;
}

.wizard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  background: #22262e;

  h3 {
    margin: 0;
    font-size: 1.1rem;
    font-weight: 600;
    color: #e0e0e0;
  }
}

.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  border: none;
  background: transparent;
  border-radius: 6px;
  cursor: pointer;
  color: #e0e0e0;
  transition: background 0.15s ease;

  &:hover {
    background: rgba(255, 255, 255, 0.1);
  }
}

.step-indicator {
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 20px;
  gap: 12px;
}

.step {
  display: flex;
  align-items: center;
  gap: 8px;
  opacity: 0.5;
  transition: opacity 0.2s ease;

  &.active,
  &.done {
    opacity: 1;
  }

  &.done .step-number {
    background: #22c55e;
  }

  &.active .step-number {
    background: #3b82f6;
  }
}

.step-number {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: #3a3f4a;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  font-weight: 600;
  color: white;
}

.step-label {
  font-size: 0.85rem;
  color: #a0a0a0;
}

.step-line {
  width: 40px;
  height: 2px;
  background: rgba(255, 255, 255, 0.1);
  transition: background 0.2s ease;

  &.active {
    background: #3b82f6;
  }
}

.wizard-content {
  padding: 0;
}

// 动画
.fade-enter-active,
.fade-leave-active {
  transition: opacity 0.2s ease;
}

.fade-enter-from,
.fade-leave-to {
  opacity: 0;
}
</style>
