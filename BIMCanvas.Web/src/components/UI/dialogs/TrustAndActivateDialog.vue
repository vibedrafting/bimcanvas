<script setup lang="ts">
/**
 * R9 RCE 防御的最后一道感知防线 (主真理源 v1.1 §8.2)。
 *
 * 首次激活按钮文案强制为 [信任并激活],按下后弹本对话框做二次确认:
 * 展示 pluginId / sourceUrl / resolvedCommit + 黄色警告条
 * "激活将执行该插件 <plugin-id> 的 Python 代码"。
 *
 * 后续切换 active (plugin 已 trusted) 用普通 [设为激活] 按钮,无二次确认。
 */
import { computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import type { PluginListItem } from '../../../types/plugin';

interface Props {
  visible: boolean;
  plugin: PluginListItem | null;
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'confirm'): void;
  (e: 'cancel'): void;
}>();

const handleConfirm = () => emit('confirm');
const handleCancel = () => emit('cancel');

const shortCommit = computed(() => {
  const c = props.plugin?.resolvedCommit;
  if (!c) return null;
  return c.length > 12 ? c.slice(0, 12) : c;
});

const sourceUrl = computed(() => props.plugin?.sourceUrl ?? '(本地 plugin,无远程来源)');
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible && plugin" class="dialog-overlay" @click.self="handleCancel">
        <div class="trust-dialog">
          <div class="dialog-header">
            <svg class="warning-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M12 9v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
            </svg>
            <h3>信任并激活 plugin</h3>
          </div>

          <div class="dialog-content">
            <!-- ⚠️ R9 警告条 - 最显著位置 -->
            <div class="rce-warning">
              <strong>⚠️ 激活将执行该插件的 Python 代码</strong>
              <p>
                确认激活意味着你信任 <code>{{ plugin.pluginId }}</code> 来自
                <code class="source-url">{{ sourceUrl }}</code> 的代码。
                平台将立即对该插件做一次 dry-run import + 后续设为 active。
              </p>
            </div>

            <!-- Plugin 元数据 -->
            <table class="meta">
              <tbody>
                <tr>
                  <td class="label">Plugin ID</td>
                  <td class="value mono">{{ plugin.pluginId }}</td>
                </tr>
                <tr>
                  <td class="label">名称</td>
                  <td class="value">{{ plugin.displayName }}</td>
                </tr>
                <tr v-if="plugin.description">
                  <td class="label">描述</td>
                  <td class="value">{{ plugin.description }}</td>
                </tr>
                <tr>
                  <td class="label">版本</td>
                  <td class="value mono">{{ plugin.version }}</td>
                </tr>
                <tr>
                  <td class="label">来源</td>
                  <td class="value">
                    <a v-if="plugin.sourceUrl" :href="plugin.sourceUrl" target="_blank" rel="noopener noreferrer" class="link">
                      {{ plugin.sourceUrl }}
                    </a>
                    <span v-else class="muted">本地 plugin · sourceKind={{ plugin.sourceKind }}</span>
                  </td>
                </tr>
                <tr v-if="shortCommit">
                  <td class="label">Commit</td>
                  <td class="value mono">{{ shortCommit }}</td>
                </tr>
                <tr>
                  <td class="label">MCP namespace</td>
                  <td class="value mono">{{ plugin.mcpNamespace ?? plugin.pluginId }}</td>
                </tr>
              </tbody>
            </table>

            <p class="footnote">
              如不确定,请先到 <a v-if="plugin.sourceUrl" :href="plugin.sourceUrl" target="_blank" rel="noopener noreferrer" class="link">仓库主页</a><span v-else>该插件来源</span>检查代码,或参阅 BIMCanvas 文档
              <code>docs/plugin-security-model.md</code>。
            </p>
          </div>

          <div class="dialog-actions">
            <GlassButton variant="danger" @click="handleConfirm">
              <template #icon>
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <path d="M5 13l4 4L19 7" />
                </svg>
              </template>
              确认信任并激活
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
  background: rgba(0, 0, 0, 0.65);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}

.trust-dialog {
  background: var(--bg-surface, #1a1d24);
  border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
  border-radius: 12px;
  padding: 24px;
  min-width: 520px;
  max-width: 620px;
  box-shadow:
    0 8px 32px rgba(0, 0, 0, 0.4),
    0 0 0 1px rgba(255, 255, 255, 0.05) inset;
}

.dialog-header {
  display: flex;
  align-items: center;
  gap: 12px;
  margin-bottom: 16px;
}

.warning-icon {
  width: 24px;
  height: 24px;
  color: #fbbf24;
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

/* R9 警告条 — 最显著 */
.rce-warning {
  background: rgba(234, 179, 8, 0.12);
  border: 1px solid rgba(234, 179, 8, 0.35);
  border-left-width: 4px;
  border-radius: 6px;
  padding: 12px 14px;
  margin-bottom: 18px;
}

.rce-warning strong {
  display: block;
  color: #fde047;
  font-size: 14px;
  margin-bottom: 6px;
}

.rce-warning p {
  margin: 0;
  color: var(--text-secondary);
  font-size: 12.5px;
  line-height: 1.55;
}

.rce-warning code {
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 5px;
  border-radius: 3px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 11.5px;
  color: var(--text-primary);
}

.rce-warning code.source-url {
  word-break: break-all;
}

/* Plugin 元数据表 */
.meta {
  width: 100%;
  border-collapse: collapse;
  margin-bottom: 14px;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 6px;
  overflow: hidden;
}

.meta tr {
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
}

.meta tr:last-child {
  border-bottom: none;
}

.meta td {
  padding: 7px 12px;
  font-size: 12.5px;
  line-height: 1.5;
  vertical-align: top;
}

.meta .label {
  color: var(--text-tertiary);
  width: 110px;
  white-space: nowrap;
}

.meta .value {
  color: var(--text-primary);
  word-break: break-word;
}

.meta .mono {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}

.meta .link {
  color: var(--accent-blue, #60a5fa);
  text-decoration: none;
}

.meta .link:hover {
  text-decoration: underline;
}

.meta .muted {
  color: var(--text-tertiary);
}

.footnote {
  margin: 12px 0 0;
  color: var(--text-tertiary);
  font-size: 11.5px;
  line-height: 1.5;
}

.footnote code {
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 4px;
  border-radius: 3px;
  font-size: 10.5px;
}

.footnote a.link {
  color: var(--accent-blue, #60a5fa);
  text-decoration: none;
}

.footnote a.link:hover {
  text-decoration: underline;
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

.dialog-enter-from .trust-dialog,
.dialog-leave-to .trust-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.dialog-enter-active .trust-dialog,
.dialog-leave-active .trust-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
