<script setup lang="ts">
/**
 * Scene 选择对话框 (主真理源 v1.1 §7.1 议题一:Manifest pattern + 选择对话框,非下拉)。
 *
 * 触发条件:OpenProject 返回 openStatus="sceneSelectRequired" + candidates (多个匹配 scene)。
 *
 * 「关键决策不藏在下拉里」—— 必须用对话框 + 卡片列表展示所有候选,让用户清楚比较。
 */
import { ref, watch } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import type { ProjectScene } from '../../../types/plugin';

interface Props {
  visible: boolean;
  candidates: ProjectScene[];
}

const props = defineProps<Props>();

const emit = defineEmits<{
  (e: 'select', sceneId: string): void;
  (e: 'cancel'): void;
}>();

const selectedSceneId = ref<string | null>(null);

watch(() => props.visible, (v) => {
  if (v) {
    // 默认选中最早创建的(列表头),便于直接确认
    selectedSceneId.value = props.candidates[0]?.sceneId ?? null;
  }
});

const handleSelect = () => {
  if (!selectedSceneId.value) return;
  emit('select', selectedSceneId.value);
};

const handleCancel = () => emit('cancel');

const formatTime = (iso: string) => {
  try {
    const d = new Date(iso);
    return d.toLocaleString('zh-CN', {
      year: 'numeric', month: '2-digit', day: '2-digit',
      hour: '2-digit', minute: '2-digit',
    });
  } catch {
    return iso;
  }
};
</script>

<template>
  <Teleport to="body">
    <Transition name="dialog">
      <div v-if="visible" class="dialog-overlay" @click.self="handleCancel">
        <div class="selector-dialog">
          <div class="dialog-header">
            <svg class="header-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
              <path d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2" />
            </svg>
            <h3>选择要打开的场景</h3>
          </div>

          <div class="dialog-content">
            <p class="hint">
              此项目中有多个 scene 都属于当前 active plugin,请选择要打开哪一个:
            </p>

            <ul class="scene-cards">
              <li
                v-for="s in candidates"
                :key="s.sceneId"
                class="scene-card"
                :class="{ selected: selectedSceneId === s.sceneId }"
                @click="selectedSceneId = s.sceneId"
              >
                <div class="card-radio">
                  <span class="radio-dot" :class="{ on: selectedSceneId === s.sceneId }"></span>
                </div>
                <div class="card-body">
                  <div class="card-title">
                    <code>{{ s.sceneId }}</code>
                    <span class="badge">{{ s.scene }}</span>
                  </div>
                  <div class="card-meta">
                    <span>plugin: <code>{{ s.plugin.id }}</code></span>
                    <span class="meta-dot">{{ formatTime(s.createdAt) }}</span>
                    <span class="meta-dot">status: {{ s.status }}</span>
                  </div>
                </div>
              </li>
            </ul>
          </div>

          <div class="dialog-actions">
            <GlassButton variant="primary" :disabled="!selectedSceneId" @click="handleSelect">
              选定此场景
            </GlassButton>
            <GlassButton variant="ghost" @click="handleCancel">取消打开</GlassButton>
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

.selector-dialog {
  background: var(--bg-surface, #1a1d24);
  border: 1px solid var(--border-subtle, rgba(255, 255, 255, 0.08));
  border-radius: 12px;
  padding: 24px;
  min-width: 520px;
  max-width: 640px;
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

.scene-cards {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: 320px;
  overflow-y: auto;
}

.scene-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 12px 14px;
  background: rgba(0, 0, 0, 0.3);
  border: 1px solid rgba(255, 255, 255, 0.06);
  border-radius: 8px;
  cursor: pointer;
  transition: 0.15s;
}

.scene-card:hover {
  background: rgba(0, 0, 0, 0.4);
  border-color: rgba(59, 130, 246, 0.3);
}

.scene-card.selected {
  background: rgba(59, 130, 246, 0.1);
  border-color: rgba(59, 130, 246, 0.55);
}

.card-radio {
  margin-top: 3px;
  flex-shrink: 0;
}

.radio-dot {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  border: 2px solid rgba(255, 255, 255, 0.3);
  box-sizing: border-box;
}

.radio-dot.on {
  border-color: var(--accent-blue, #3b82f6);
  background: var(--accent-blue, #3b82f6);
  box-shadow: inset 0 0 0 2px var(--bg-surface, #1a1d24);
}

.card-body {
  flex: 1;
  min-width: 0;
}

.card-title {
  display: flex;
  align-items: center;
  gap: 10px;
  margin-bottom: 6px;
  font-size: 13px;
  color: var(--text-primary);
}

.card-title code {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 12.5px;
}

.badge {
  background: rgba(59, 130, 246, 0.18);
  color: #93c5fd;
  padding: 2px 8px;
  border-radius: 10px;
  font-size: 11px;
}

.card-meta {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
  color: var(--text-tertiary);
  font-size: 11.5px;
}

.card-meta code {
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  color: var(--text-secondary);
}

.meta-dot::before {
  content: '·';
  margin: 0 6px;
  color: var(--text-tertiary);
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

.dialog-enter-from .selector-dialog,
.dialog-leave-to .selector-dialog {
  transform: scale(0.95) translateY(-10px);
  opacity: 0;
}

.dialog-enter-active .selector-dialog,
.dialog-leave-active .selector-dialog {
  transition: all 0.2s cubic-bezier(0.19, 1, 0.22, 1);
}
</style>
