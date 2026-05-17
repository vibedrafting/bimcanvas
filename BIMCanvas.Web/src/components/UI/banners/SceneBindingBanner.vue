<script setup lang="ts">
/**
 * legacy-unbound .bcp 顶部非阻塞 banner (主真理源 v1.1 §7.1 议题四 + §2.2)。
 *
 * 触发场景:用户已经打开一个 legacy 项目(进入工作区),后续主动想绑定 active plugin。
 * 不打断当前工作流;两个按钮 [绑定为当前 active] / [稍后再说]。
 */
import GlassButton from '../base/GlassButton.vue';

interface Props {
  /** 当前 active plugin id (server_config.agent.activePlugin) */
  currentActivePlugin: string | null;
  /** 建议的新 sceneId */
  suggestedSceneId: string;
}

defineProps<Props>();

const emit = defineEmits<{
  (e: 'bind'): void;
  (e: 'dismiss'): void;
}>();
</script>

<template>
  <div class="scene-binding-banner">
    <div class="banner-icon">
      <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2">
        <circle cx="12" cy="12" r="10" />
        <path d="M12 16v-4M12 8h.01" />
      </svg>
    </div>
    <div class="banner-body">
      <span>
        本项目尚未绑定到当前 active plugin
        <strong v-if="currentActivePlugin">{{ currentActivePlugin }}</strong>
        <span v-else class="muted">(未激活任何 plugin)</span>。
        绑定后将新增 scene <code>{{ suggestedSceneId }}</code>,允许当前 plugin 写入项目数据。
      </span>
    </div>
    <div class="banner-actions">
      <GlassButton
        variant="primary"
        :disabled="!currentActivePlugin"
        @click="emit('bind')"
      >
        绑定为当前 active
      </GlassButton>
      <GlassButton variant="ghost" @click="emit('dismiss')">
        稍后再说
      </GlassButton>
    </div>
  </div>
</template>

<style scoped>
.scene-binding-banner {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 10px 16px;
  background: rgba(234, 179, 8, 0.1);
  border: 1px solid rgba(234, 179, 8, 0.25);
  border-radius: 8px;
  color: var(--text-secondary);
  font-size: 13px;
  line-height: 1.5;
}

.banner-icon {
  color: #fbbf24;
  flex-shrink: 0;
}

.banner-body {
  flex: 1;
  min-width: 0;
}

.banner-body strong {
  color: var(--text-primary);
  font-weight: 600;
}

.banner-body code {
  background: rgba(0, 0, 0, 0.4);
  padding: 1px 5px;
  border-radius: 3px;
  font-family: ui-monospace, SFMono-Regular, Consolas, monospace;
  font-size: 11.5px;
  color: var(--text-primary);
}

.muted {
  color: var(--text-tertiary);
}

.banner-actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
}
</style>
