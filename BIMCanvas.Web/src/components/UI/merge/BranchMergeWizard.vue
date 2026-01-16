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

const { isVisible, currentStep, targetBranch, sourceBranch, isMerging, error, canProceed } = storeToRefs(mergeStore);
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
    console.log('[BranchMergeWizard] Wizard opened, fetching branches...');
    await gitStore.fetchBranches();
    console.log('[BranchMergeWizard] Branches fetched, currentBranch:', currentBranch.value);
    console.log('[BranchMergeWizard] Current targetBranch:', targetBranch.value);
    // 默认目标分支为当前分支
    if (currentBranch.value && !targetBranch.value) {
      console.log('[BranchMergeWizard] Setting default targetBranch to:', currentBranch.value);
      mergeStore.targetBranch = currentBranch.value;
    }
    console.log('[BranchMergeWizard] After setup, isVisible still:', isVisible.value);
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
      <div v-if="isVisible" class="wizard-overlay" @click.self="handleClose">
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
              :branches="branchOptions"
              :canProceed="canProceed"
              @next="mergeStore.nextStep()"
            />
            <MergeConfirmStep
              v-else-if="currentStep === 2"
              :targetBranch="targetBranch"
              :sourceBranch="sourceBranch"
              :isMerging="isMerging"
              :error="error"
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
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.wizard-container {
  width: 480px;
  background: var(--bg-primary);
  border: 1px solid var(--border-subtle);
  border-radius: 12px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
  overflow: hidden;
}

.wizard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-subtle);
  background: var(--bg-secondary);

  h3 {
    margin: 0;
    font-size: 1.1rem;
    font-weight: 600;
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
  color: var(--text-primary);
  transition: background 0.15s ease;

  &:hover {
    background: var(--bg-hover);
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
    background: var(--accent-green);
  }

  &.active .step-number {
    background: var(--accent-blue);
  }
}

.step-number {
  width: 24px;
  height: 24px;
  border-radius: 50%;
  background: var(--bg-tertiary);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 0.75rem;
  font-weight: 600;
  color: white;
}

.step-label {
  font-size: 0.85rem;
  color: var(--text-secondary);
}

.step-line {
  width: 40px;
  height: 2px;
  background: var(--border-subtle);
  transition: background 0.2s ease;

  &.active {
    background: var(--accent-blue);
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
