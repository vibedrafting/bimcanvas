<script setup lang="ts">
import { ref } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import { useProjectFile } from '../../composables/useProjectFile';
import { useCanvasStore } from '../../stores/canvasStore';

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
    <div class="group-title">File</div>
    <div class="group-content">
      <GlassButton @click="onOpen" variant="ghost" class="ribbon-btn">
        <span class="icon">📂</span> Open
      </GlassButton>
      <GlassButton @click="onSave" variant="ghost" class="ribbon-btn">
        <span class="icon">💾</span> Save
      </GlassButton>
      <GlassButton @click="onImport" variant="ghost" class="ribbon-btn">
        <span class="icon">⬇️</span> Import
      </GlassButton>
      <GlassButton @click="handleExport" :disabled="!store.projectData" variant="ghost" class="ribbon-btn">
        <span class="icon">⬆️</span> Export
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

.group-title {
  font-size: 0.75rem;
  color: var(--text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-align: center;
}

.group-content {
  display: flex;
  gap: 4px;
}

.ribbon-btn {
  flex-direction: column;
  height: 48px;
  min-width: 48px;
  gap: 4px;
  font-size: 0.8rem;
  
  .icon {
    font-size: 1.2rem;
  }
}
</style>
