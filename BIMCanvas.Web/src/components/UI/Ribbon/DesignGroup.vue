<script setup lang="ts">
import { ref, computed } from 'vue';
import GlassSelect from '../base/GlassSelect.vue';
import GlassButton from '../base/GlassButton.vue';
import BranchCreationDialog from './BranchCreationDialog.vue';

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
const currentBranch = ref('main');
const showBranchDialog = ref(false);

// Mock Data for Branches
const branches = ref([
  { 
    label: 'Main Branch', 
    value: 'main', 
    tags: ['Base'],
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="3" x2="6" y2="15"></line><circle cx="18" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M18 9a9 9 0 0 1-9 9"></path></svg>' 
  },
  { 
    label: 'Option A', 
    value: 'opt_a', 
    tags: ['Storage First'],
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><path d="M12 16v-4"></path><path d="M12 8h.01"></path></svg>' 
  },
  { 
    label: 'Option B', 
    value: 'opt_b', 
    tags: ['Flow First'],
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><path d="M12 8v8"></path><path d="M8 12h8"></path></svg>' 
  },
]);

// Computed options including "Create New..."
const branchOptions = computed(() => [
  ...branches.value,
  { 
    label: 'Create New Branch...', 
    value: '__create_new__', 
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>' 
  },
]);

const handleBranchChange = (val: string | number) => {
  if (val === '__create_new__') {
    // Revert selection to previous (or keep current) until created
    // For now, we just open dialog. Ideally we'd track previous value.
    showBranchDialog.value = true;
    // Reset selection to current valid branch to avoid showing "Create New..." as selected
    // In a real app, we might want to wait for dialog close.
  } else {
    currentBranch.value = val as string;
  }
};

const handleCreateBranch = (data: { name: string; baseBranch: string; tags: string[]; reason: string }) => {
  // Mock creation logic
  const newId = `feat/${data.name.toLowerCase().replace(/\s+/g, '-')}`;
  branches.value.push({
    label: data.name,
    value: newId,
    tags: data.tags,
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle></svg>'
  });
  
  currentBranch.value = newId;
  showBranchDialog.value = false;
  console.log('Created Branch:', data);
};

// Get current branch tags for dialog defaults
const currentBranchTags = computed(() => {
  const branch = branches.value.find(b => b.value === currentBranch.value);
  return branch?.tags || [];
});

// Simple branch list for dialog base selection
const simpleBranchList = computed(() => 
  branches.value.map(b => ({ label: b.label, value: b.value }))
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
            :model-value="currentBranch"
            @update:model-value="handleBranchChange"
            :options="branchOptions" 
            width="200px"
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
        :base-branch="currentBranch"
        :base-tags="currentBranchTags"
        :all-branches="simpleBranchList"
        @create="handleCreateBranch"
        @cancel="showBranchDialog = false"
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
