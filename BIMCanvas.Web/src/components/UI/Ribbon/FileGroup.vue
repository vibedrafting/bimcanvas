<script setup lang="ts">
import { ref } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import { useProjectFile } from '../../../composables/useProjectFile';
import { useCanvasStore } from '../../../stores/canvasStore';

const store = useCanvasStore();
const fileInputRef = ref<HTMLInputElement | null>(null);
const { handleLoad, handleExport, processFile } = useProjectFile();

const onOpen = async () => {
  const result = await handleLoad();
  if (result === 'fallback') {
    fileInputRef.value?.click();
  }
};

const onFileSelected = (event: Event) => {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  processFile(file);
  input.value = '';
};

// Placeholder for Import/Save (Save is usually Commit in v3)
const onSave = () => {
  console.log('Save/Commit triggered');
  // TODO: Implement Git Commit logic
};

const onImport = () => {
  console.log('Import triggered');
  // Reuse open logic for now or specific import logic
  onOpen();
};
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      <GlassButton @click="onOpen" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
        </svg>
        <span>Open</span>
      </GlassButton>
      <GlassButton @click="onSave" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"></path>
          <polyline points="17 21 17 13 7 13 7 21"></polyline>
          <polyline points="7 3 7 8 15 8"></polyline>
        </svg>
        <span>Save</span>
      </GlassButton>
      <GlassButton @click="onImport" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
        <span>Import</span>
      </GlassButton>
      <GlassButton @click="handleExport" :disabled="!store.projectData" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="17 8 12 3 7 8"></polyline>
          <line x1="12" y1="3" x2="12" y2="15"></line>
        </svg>
        <span>Export</span>
      </GlassButton>

      <!-- Hidden Input for Fallback -->
      <input
        ref="fileInputRef"
        type="file"
        accept=".json,.bcp"
        style="display: none"
        @change="onFileSelected"
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
  gap: 4px;
}

.ribbon-btn {
  flex-direction: column;
  align-items: center;
  height: 42px;
  min-width: 50px;
  gap: 2px;
  font-size: 0.7rem;
  padding: 4px 8px;
}
</style>

