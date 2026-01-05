<script setup lang="ts">
import { ref, watch } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import GlassSelect from '../base/GlassSelect.vue';

interface Props {
  visible: boolean;
  baseBranch: string;
  baseTags: string[];
  allBranches: { label: string; value: string }[];
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'create', data: { name: string; baseBranch: string; tags: string[]; reason: string }): void;
  (e: 'cancel'): void;
}>();

const newBranchName = ref('');
const selectedBaseBranch = ref(props.baseBranch);
const tags = ref<string[]>([...props.baseTags]);
const reason = ref('');
const newTagInput = ref('');

// Watch for visibility changes to reset form
watch(() => props.visible, (newVal) => {
  if (newVal) {
    newBranchName.value = '';
    selectedBaseBranch.value = props.baseBranch;
    tags.value = [...props.baseTags];
    reason.value = '';
    newTagInput.value = '';
  }
});

const handleCreate = () => {
  if (!newBranchName.value || !reason.value) return;
  
  emit('create', {
    name: newBranchName.value,
    baseBranch: selectedBaseBranch.value,
    tags: tags.value,
    reason: reason.value
  });
};

const addTag = () => {
  if (newTagInput.value && !tags.value.includes(newTagInput.value)) {
    tags.value.push(newTagInput.value);
    newTagInput.value = '';
  }
};

const removeTag = (tagToRemove: string) => {
  tags.value = tags.value.filter(tag => tag !== tagToRemove);
};
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay" @click.self="emit('cancel')">
        <div class="dialog-card">
          <div class="dialog-header">
            <h3>Create New Branch</h3>
            <button class="close-btn" @click="emit('cancel')">
              <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2">
                <line x1="18" y1="6" x2="6" y2="18"></line>
                <line x1="6" y1="6" x2="18" y2="18"></line>
              </svg>
            </button>
          </div>

          <div class="dialog-body">
            <!-- Base Branch -->
            <div class="form-group">
              <label>Base Branch</label>
              <GlassSelect 
                v-model="selectedBaseBranch" 
                :options="allBranches" 
                width="100%"
              />
            </div>

            <!-- New Branch Name -->
            <div class="form-group">
              <label>New Branch Name</label>
              <input 
                v-model="newBranchName" 
                type="text" 
                class="glass-input" 
                placeholder="e.g. feat/living-room-storage"
              />
            </div>

            <!-- Tags -->
            <div class="form-group">
              <label>Tags</label>
              <div class="tags-container">
                <span v-for="tag in tags" :key="tag" class="tag-badge">
                  {{ tag }}
                  <button class="remove-tag" @click="removeTag(tag)">×</button>
                </span>
                <div class="add-tag-wrapper">
                  <input 
                    v-model="newTagInput" 
                    type="text" 
                    class="tag-input" 
                    placeholder="+ Add tag"
                    @keydown.enter.prevent="addTag"
                    @blur="addTag"
                  />
                </div>
              </div>
            </div>

            <!-- Reason -->
            <div class="form-group">
              <label>Reason (Commit Message)</label>
              <textarea 
                v-model="reason" 
                class="glass-input textarea" 
                placeholder="Why are you creating this branch?"
                rows="3"
              ></textarea>
            </div>
          </div>

          <div class="dialog-footer">
            <GlassButton variant="ghost" @click="emit('cancel')">Cancel</GlassButton>
            <GlassButton 
              variant="primary" 
              @click="handleCreate"
              :disabled="!newBranchName || !reason"
            >
              Create Branch
            </GlassButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped lang="scss">
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 2000;
}

.dialog-card {
  background: var(--glass-bg-solid);
  border: var(--glass-border);
  border-radius: 12px;
  width: 400px;
  box-shadow: var(--shadow-modal);
  display: flex;
  flex-direction: column;
  
  /* Glare effect */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg-solid), var(--glass-bg-solid));
  background-origin: border-box;
  background-clip: padding-box, border-box;
}

.dialog-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border-subtle);
  display: flex;
  justify-content: space-between;
  align-items: center;

  h3 {
    margin: 0;
    font-size: 1rem;
    font-weight: 600;
    color: var(--text-primary);
  }

  .close-btn {
    background: none;
    border: none;
    color: var(--text-secondary);
    cursor: pointer;
    padding: 4px;
    border-radius: 4px;
    
    &:hover {
      background: rgba(255, 255, 255, 0.1);
      color: var(--text-primary);
    }
  }
}

.dialog-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;

  label {
    font-size: 0.8rem;
    color: var(--text-secondary);
    font-weight: 500;
  }
}

.glass-input {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--text-primary);
  font-family: inherit;
  font-size: 0.9rem;
  outline: none;
  transition: all 0.2s;

  &:focus {
    background: rgba(255, 255, 255, 0.08);
    border-color: var(--accent-blue);
  }

  &.textarea {
    resize: none;
  }
}

.tags-container {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  padding: 6px;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  min-height: 38px;
}

.tag-badge {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 8px;
  background: rgba(59, 130, 246, 0.15);
  color: var(--accent-blue);
  border-radius: 4px;
  font-size: 0.8rem;

  .remove-tag {
    background: none;
    border: none;
    color: currentColor;
    cursor: pointer;
    padding: 0;
    font-size: 1rem;
    line-height: 1;
    opacity: 0.7;

    &:hover {
      opacity: 1;
    }
  }
}

.add-tag-wrapper {
  flex: 1;
  min-width: 60px;
}

.tag-input {
  width: 100%;
  background: none;
  border: none;
  color: var(--text-primary);
  font-size: 0.85rem;
  outline: none;
  padding: 2px 4px;

  &::placeholder {
    color: var(--text-muted);
  }
}

.dialog-footer {
  padding: 16px 20px;
  border-top: 1px solid var(--border-subtle);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

/* Animation */
.dialog-enter-active,
.dialog-leave-active {
  transition: opacity 0.2s ease;
  
  .dialog-card {
    transition: transform 0.2s var(--ease-spring);
  }
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;
  
  .dialog-card {
    transform: scale(0.95) translateY(10px);
  }
}
</style>
