<script setup lang="ts">
import { ref, watch } from 'vue';
import GlassButton from '../base/GlassButton.vue';

interface Props {
  visible: boolean;
}

const props = defineProps<Props>();

// confirm payload 是一个判别联合:source='github' 走 repoUrl/ref,source='local' 走 path/link。
// 与后端 PluginsController.InstallRequest (sourceKind=github|local) 对齐。
type InstallPayload =
  | { source: 'github'; repoUrl: string; ref?: string | null }
  | { source: 'local'; path: string; link: boolean };

const emit = defineEmits<{
  (e: 'confirm', payload: InstallPayload): void;
  (e: 'cancel'): void;
}>();

type SourceTab = 'github' | 'local';
const sourceTab = ref<SourceTab>('github');

// github 输入
const repoUrl = ref('');
const gitRef = ref('');

// local 输入
const localPath = ref('');
const localLink = ref(true); // 默认软链(改源码即时生效),与后端 link 缺省一致

// 对话框打开时清空输入并复位到 github tab
watch(() => props.visible, (v) => {
  if (v) {
    sourceTab.value = 'github';
    repoUrl.value = '';
    gitRef.value = '';
    localPath.value = '';
    localLink.value = true;
  }
});

const canConfirm = () =>
  sourceTab.value === 'github' ? !!repoUrl.value.trim() : !!localPath.value.trim();

const handleConfirm = () => {
  if (!canConfirm()) return; // 按钮 disable 兜底
  if (sourceTab.value === 'github') {
    emit('confirm', {
      source: 'github',
      repoUrl: repoUrl.value.trim(),
      ref: gitRef.value.trim() || null,
    });
  } else {
    emit('confirm', {
      source: 'local',
      path: localPath.value.trim(),
      link: localLink.value,
    });
  }
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
            <!-- source tab 切换 -->
            <div class="source-tabs" role="tablist">
              <button
                type="button"
                class="source-tab"
                :class="{ active: sourceTab === 'github' }"
                role="tab"
                :aria-selected="sourceTab === 'github'"
                @click="sourceTab = 'github'"
              >
                GitHub
              </button>
              <button
                type="button"
                class="source-tab"
                :class="{ active: sourceTab === 'local' }"
                role="tab"
                :aria-selected="sourceTab === 'local'"
                @click="sourceTab = 'local'"
              >
                本地目录
              </button>
            </div>

            <!-- GitHub source -->
            <template v-if="sourceTab === 'github'">
              <p class="hint">从 GitHub 仓库 URL 安装 BIMCanvas plugin。安装后会显示在列表中,需要再点 [信任并激活] 才会执行其 Python 代码。</p>

              <label class="field">
                <span class="field-label">GitHub 仓库 URL <span class="required">*</span></span>
                <input
                  v-model="repoUrl"
                  type="text"
                  placeholder="https://github.com/vibedrafting/BIMCanvas-IndoorLayout"
                  spellcheck="false"
                  autocomplete="off"
                  @keydown.enter="handleConfirm"
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
                  @keydown.enter="handleConfirm"
                />
              </label>

              <p class="note">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: -2px; margin-right: 4px;">
                  <circle cx="12" cy="12" r="10" />
                  <path d="M12 8v4M12 16h.01" />
                </svg>
                安装阶段绝不执行该插件的代码 (R9 RCE 防御),只做 git clone + 纯文本校验。
              </p>
            </template>

            <!-- 本地 source -->
            <template v-else>
              <p class="hint">从本机目录安装 plugin (开发 / 离线 / 私有分发)。该目录须含 <code>bimcanvas-plugin.json</code>。安装后同样需要 [信任并激活] 才会执行其 Python 代码。</p>

              <label class="field">
                <span class="field-label">本地 plugin 目录绝对路径 <span class="required">*</span></span>
                <input
                  v-model="localPath"
                  type="text"
                  placeholder="C:/CodingProject/vibedrafting/bimcanvas-plugin-atlas"
                  spellcheck="false"
                  autocomplete="off"
                  @keydown.enter="handleConfirm"
                />
              </label>

              <label class="checkbox-field">
                <input v-model="localLink" type="checkbox" />
                <span class="checkbox-text">
                  <strong>软链接 (junction)</strong>
                  <span class="checkbox-hint">勾选:在 plugins/ 建 junction 指向源目录,改源码即时生效 (推荐开发用)。取消:复制目录快照,与源解耦。</span>
                </span>
              </label>

              <p class="note">
                <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align: -2px; margin-right: 4px;">
                  <circle cx="12" cy="12" r="10" />
                  <path d="M12 8v4M12 16h.01" />
                </svg>
                安装阶段绝不执行该插件的代码 (R9 RCE 防御),只做纯文本校验。
              </p>
            </template>
          </div>

          <div class="dialog-actions">
            <GlassButton variant="primary" :disabled="!canConfirm()" @click="handleConfirm">
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

/* ─── source tab 切换 (深色玻璃风, 对齐面板 badge-active 蓝调) ─── */
.source-tabs {
  display: flex;
  gap: 4px;
  padding: 4px;
  margin-bottom: 18px;
  background: rgba(0, 0, 0, 0.35);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 8px;
}

.source-tab {
  flex: 1;
  appearance: none;
  border: none;
  background: transparent;
  color: var(--text-secondary, #a1a1aa);
  font-size: 13px;
  font-weight: 500;
  padding: 7px 12px;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s, color 0.15s;
}

.source-tab:hover {
  color: var(--text-primary, #fafafa);
  background: rgba(255, 255, 255, 0.04);
}

.source-tab.active {
  background: rgba(59, 130, 246, 0.18);
  color: #93c5fd;
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

/* ─── 本地模式 junction 勾选框 ─── */
.checkbox-field {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  margin-bottom: 14px;
  cursor: pointer;
}

.checkbox-field input[type='checkbox'] {
  width: 15px;
  height: 15px;
  margin: 2px 0 0;
  flex-shrink: 0;
  accent-color: #3b82f6;
  cursor: pointer;
}

.checkbox-text {
  display: flex;
  flex-direction: column;
  gap: 3px;
}

.checkbox-text strong {
  font-size: 13px;
  font-weight: 500;
  color: var(--text-primary);
}

.checkbox-hint {
  font-size: 11.5px;
  color: var(--text-secondary);
  line-height: 1.5;
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
