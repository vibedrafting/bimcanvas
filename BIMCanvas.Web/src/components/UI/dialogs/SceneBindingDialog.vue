<script setup lang="ts">
/**
 * Scene 绑定对话框 (主真理源 v1.1 §2.2 步骤 5 + §7.1 议题四)。
 *
 * 场景 2 (跨 plugin 接力) 与 legacy-unbound (.bcp 无任何 scene) 共用本组件。
 *
 * 触发条件:OpenProject 返回 openStatus="requiresSceneBinding";
 * payload 含 existingScenes (已有 scene 列表) + currentActivePlugin (当前 active)。
 *
 * 用户选择:
 *   [新增此场景] → 调 POST /api/project/scenes 追加新 scene
 *   [取消并切回 X plugin] → 关闭项目;由调用方提示用户去切换 active plugin
 */
import { computed } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import type { ProjectScene } from '../../../types/plugin';

interface Props {
  visible: boolean;
  /** 当前 active plugin id (server_config.agent.activePlugin) */
  currentActivePlugin: string | null;
  /** 项目内已有的 scenes;空数组表示 legacy-unbound */
  existingScenes: ProjectScene[];
  /** 建议的新 sceneId (派发方可基于 plugin manifest defaultSceneIdPattern 递增) */
  suggestedSceneId: string;
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'confirm', payload: { sceneId: string; pluginId: string }): void;
  (e: 'cancel'): void;
}>();

const handleConfirm = () => {
  if (!props.currentActivePlugin) return;
  emit('confirm', {
    sceneId: props.suggestedSceneId,
    pluginId: props.currentActivePlugin,
  });
};

const handleCancel = () => emit('cancel');

const isLegacyUnbound = computed(() => props.existingScenes.length === 0);
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay" @click.self="handleCancel">
        <div class="binding-dialog">
          <div class="dialog-header">
            <svg class="header-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M13 10V3L4 14h7v7l9-11h-7z" />
            </svg>
            <h3>{{ isLegacyUnbound ? '为本项目添加场景' : '此项目尚未绑定 active plugin 的场景' }}</h3>
          </div>

          <div class="dialog-content">
            <p v-if="isLegacyUnbound" class="hint">
              此项目还没有任何 scene。你当前激活的 plugin 是
              <strong v-if="currentActivePlugin">{{ currentActivePlugin }}</strong>
              <span v-else class="muted">(未激活任何 plugin)</span>,
              是否在此项目新增对应的场景?
            </p>
            <p v-else class="hint">
              此项目已有以下场景,但都不属于你当前激活的 plugin
              <strong>{{ currentActivePlugin }}</strong>:
            </p>

            <ul v-if="!isLegacyUnbound" class="scene-list">
              <li v-for="s in existingScenes" :key="s.sceneId" class="scene-item">
                <span class="scene-id">{{ s.sceneId }}</span>
                <span class="scene-meta">
                  <span class="badge">{{ s.scene }}</span>
                  <span class="plugin-tag">plugin: {{ s.plugin.id }}</span>
                </span>
              </li>
            </ul>

            <div v-if="currentActivePlugin" class="new-scene-preview">
              <span class="label">将新增:</span>
              <span class="value">
                sceneId = <code>{{ suggestedSceneId }}</code> · plugin = <code>{{ currentActivePlugin }}</code>
              </span>
            </div>

            <p v-else class="muted-note">
              请先在 [插件管理] 中激活一个 plugin,再回来打开本项目。
            </p>
          </div>

          <div class="dialog-actions">
            <GlassButton
              variant="primary"
              :disabled="!currentActivePlugin"
              @click="handleConfirm"
            >
              新增此场景
            </GlassButton>
            <GlassButton variant="ghost" @click="handleCancel">
              {{ isLegacyUnbound ? '取消' : '取消并切回原 plugin' }}
            </GlassButton>
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

.binding-dialog {
  background: var(--bg-surface, #1a1d24);
  border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
  border-radius: 12px;
  padding: 24px;
  min-width: 480px;
  max-width: 600px;
  box-shadow:
    0 8px 32px rgba(0, 0, 0, 0.35),
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
  font-size: 17px;
  font-weight: 600;
  color: var(--text-primary);
}

.dialog-content {
  margin-bottom: 20px;
}

.hint {
  margin: 0 0 14px;
  color: var(--text-secondary);
  font-size: 13px;
  line-height: 1.55;
}

.hint strong {
  color: var(--text-primary);
  font-weight: 600;
}

.muted {
  color: var(--text-tertiary);
}

.scene-list {
  list-style: none;
  margin: 0 0 14px;
  padding: 8px 0;
  background: rgba(0, 0, 0, 0.3);
  border-radius: 6px;
  max-height: 200px;
  overflow-y: auto;
}

.scene-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 14px;
  font-size: 12.5px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.04);
}

.scene-item:last-child {
  border-bottom: none;
}

.scene-id {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  color: var(--text-primary);
}

.scene-meta {
  display: flex;
  gap: 8px;
  align-items: center;
}

.badge {
  background: rgba(59, 130, 246, 0.18);
  color: #93c5fd;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 11px;
}

.plugin-tag {
  color: var(--text-tertiary);
  font-size: 11px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}

.new-scene-preview {
  padding: 10px 14px;
  background: rgba(34, 197, 94, 0.08);
  border-left: 3px solid rgba(34, 197, 94, 0.55);
  border-radius: 4px;
  margin-top: 10px;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--text-secondary);
}

.new-scene-preview .label {
  color: var(--text-tertiary);
  margin-right: 8px;
}

.new-scene-preview code {
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 5px;
  border-radius: 3px;
  font-size: 11.5px;
  color: var(--text-primary);
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
}

.muted-note {
  margin: 10px 0 0;
  padding: 10px 14px;
  background: rgba(234, 179, 8, 0.08);
  border-left: 3px solid rgba(234, 179, 8, 0.55);
  border-radius: 4px;
  color: var(--text-secondary);
  font-size: 12.5px;
  line-height: 1.55;
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

.dialog-enter-from .binding-dialog,
.dialog-leave-to .binding-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.dialog-enter-active .binding-dialog,
.dialog-leave-active .binding-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
