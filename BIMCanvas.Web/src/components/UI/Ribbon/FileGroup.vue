<script setup lang="ts">
import { ref } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import SaveConfirmDialog from '../SaveConfirmDialog.vue';
import ExportFormatDialog from '../ExportFormatDialog.vue';
import { useProjectFile } from '../../../composables/useProjectFile';
import { useSave } from '../../../composables/useSave';
import { useCanvasStore } from '../../../stores/canvasStore';
import { getWebRuntime } from '../../../runtime/runtimeRegistry';
import { supports } from '../../../runtime/WebRuntimeProtocol';
import { createLogger } from '../../../utils/logger';

const log = createLogger('USER');

const store = useCanvasStore();
const runtime = getWebRuntime();
const canServerPersistence = supports(runtime.capabilities.serverPersistence);
const fileInputRef = ref<HTMLInputElement | null>(null);
const { handleLoad, handleExportSnapshot, handleExportBcp, processFile, fileAccept, canExportBcp } = useProjectFile();

// 使用统一的保存逻辑
const { handleSave, canSave, isSaving } = useSave();

// 保存对话框状态
const showSaveDialog = ref(false);
const showExportDialog = ref(false);

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

const onImport = () => {
  log.debug('Import triggered');
  onOpen();
};

const onExportClick = async () => {
  if (!store.projectData) return;
  if (canExportBcp) {
    showExportDialog.value = true;
    return;
  }
  await handleExportSnapshot();
};

const onExportFormatSelected = async (format: 'snapshot' | 'bcp') => {
  showExportDialog.value = false;
  if (format === 'snapshot') {
    await handleExportSnapshot();
  } else {
    await handleExportBcp();
  }
};

// 点击保存按钮时显示对话框
const onSaveClick = () => {
  if (canSave.value && !isSaving.value) {
    showSaveDialog.value = true;
  }
};

// 确认保存
const onSaveConfirm = async (commitMessage: string) => {
  showSaveDialog.value = false;
  await handleSave(commitMessage);
};

// 取消保存
const onSaveCancel = () => {
  showSaveDialog.value = false;
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
      <GlassButton v-if="canServerPersistence" @click="onSaveClick" :disabled="!canSave || isSaving" variant="ghost" class="ribbon-btn">
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
      <GlassButton @click="onExportClick" :disabled="!store.projectData" variant="ghost" class="ribbon-btn" title="导出">
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
        :accept="fileAccept"
        style="display: none"
        @change="onFileSelected"
      />
    </div>

    <!-- 保存确认对话框 -->
    <SaveConfirmDialog
      :visible="showSaveDialog && canServerPersistence"
      @confirm="onSaveConfirm"
      @cancel="onSaveCancel"
    />

    <ExportFormatDialog
      :visible="showExportDialog"
      @select="onExportFormatSelected"
      @cancel="showExportDialog = false"
    />
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

