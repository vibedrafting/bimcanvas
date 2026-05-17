<script setup lang="ts">
import { ref, watch } from 'vue';
import GlassButton from '../base/GlassButton.vue';

interface Props {
  visible: boolean;
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'confirm', payload: { repoUrl: string; ref?: string | null }): void;
  (e: 'cancel'): void;
}>();

const repoUrl = ref('');
const gitRef = ref('');

// 对话框打开时清空输入
watch(() => props.visible, (v) => {
  if (v) {
    repoUrl.value = '';
    gitRef.value = '';
  }
});

const handleConfirm = () => {
  const url = repoUrl.value.trim();
  if (!url) return; // 按钮 disable 兜底
  emit('confirm', {
    repoUrl: url,
    ref: gitRef.value.trim() || null,
  });
};

const handleCancel = () => emit('cancel');
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay" @click.self="handleCancel">
        <div class="install-dialog">
          <div class="dialog-header">
            <svg class="header-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 5v14M5 12h14" />
            </svg>
            <h3>安装新插件</h3>
          </div>

          <div class="dialog-content">
            <p class="hint">从 GitHub 仓库 URL 安装 BIMCanvas plugin。安装后会显示在列表中,需要再点 [信任并激活] 才会执行其 Python 代码。</p>

            <label class="field">
              <span class="field-label">GitHub 仓库 URL <span class="required">*</span></span>
              <input
                v-model="repoUrl"
                type="text"
                placeholder="https://github.com/vibedrafting/BIMCanvas-IndoorLayout"
                spellcheck="false"
                autocomplete="off"
              />
            </label>

            <label class="field">
              <span class="field-label">分支 / Tag <span class="optional">(可选)</span></span>
              <input
                v-model="gitRef"
                type="text"
                placeholder="留空使用默认分支;例如 v1.0.0 或 main"
                spellcheck="false"
                autocomplete="off"
              />
            </label>

            <p class="note">
              <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: -2px; margin-right: 4px;">
                <circle cx="12" cy="12" r="10" />
                <path d="M12 8v4M12 16h.01" />
              </svg>
              安装阶段绝不执行该插件的代码 (R9 RCE 防御),只做 git clone + 纯文本校验。
            </p>
          </div>

          <div class="dialog-actions">
            <GlassButton variant="primary" :disabled="!repoUrl.trim()" @click="handleConfirm">
              安装
            </GlassButton>
            <GlassButton variant="ghost" @click="handleCancel">取消</GlassButton>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.dialog-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.install-dialog {
  background: var(--bg-surface, #1a1d24);
  border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
  border-radius: 12px;
  padding: 24px;
  min-width: 480px;
  max-width: 560px;
  box-shadow:
    0 8px 32px rgba(0, 0, 0, 0.3),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.header-icon {
  width: 22px;
  height: 22px;
  color: var(--accent-blue, #3b82f6);
  flex-shrink: 0;
}

.dialog-header h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--text-primary);
}

.dialog-content {
  margin-bottom: 20px;
}

.hint {
  margin: 0 0 16px;
  color: var(--text-secondary);
  font-size: 13px;
  line-height: 1.5;
}

.field {
  display: block;
  margin-bottom: 14px;
}

.field-label {
  display: block;
  font-size: 12px;
  color: var(--text-secondary);
  margin-bottom: 6px;
  letter-spacing: 0.02em;
}

.required {
  color: #f87171;
  margin-left: 2px;
}

.optional {
  color: var(--text-tertiary);
  font-size: 11px;
}

.field input[type='text'] {
  width: 100%;
  height: 36px;
  padding: 0 12px;
  font-size: 13px;
  box-sizing: border-box;
  background-color: rgba(0, 0, 0, 0.45);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 6px;
  color: var(--text-primary);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  outline: none;
  transition: 0.15s;
  box-shadow: inset 0 2px 4px rgba(0, 0, 0, 0.5);
}

.field input[type='text']:focus {
  border-color: rgba(59, 130, 246, 0.5);
  box-shadow: 0 0 0 1px rgba(59, 130, 246, 0.5), inset 0 2px 4px rgba(0, 0, 0, 0.6);
  background-color: rgba(0, 0, 0, 0.6);
}

.note {
  margin: 16px 0 0;
  padding: 10px 12px;
  background: rgba(59, 130, 246, 0.08);
  border-left: 3px solid rgba(59, 130, 246, 0.6);
  border-radius: 4px;
  color: var(--text-secondary);
  font-size: 12px;
  line-height: 1.5;
}

.dialog-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
}

.dialog-enter-active,
.dialog-leave-active {
  transition: all 0.2s ease;
}

.dialog-enter-from,
.dialog-leave-to {
  opacity: 0;
}

.dialog-enter-from .install-dialog,
.dialog-leave-to .install-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.dialog-enter-active .install-dialog,
.dialog-leave-active .install-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
