<script setup lang="ts">
import { computed } from 'vue';
import { useCanvasStore } from '../../../stores/canvasStore';
import GlassButton from '../base/GlassButton.vue';

const store = useCanvasStore();
const projectInfo = computed(() => store.projectData?.project);
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      <div class="info-block" v-if="projectInfo">
        <div class="project-name">{{ projectInfo.name || 'Untitled' }}</div>
        <div class="project-meta">v{{ projectInfo.version || '1.0' }} • {{ projectInfo.id }}</div>
      </div>
      <div class="info-block placeholder" v-else>
        No Project Loaded
      </div>
      
      <div class="divider-vertical"></div>
      
      <GlassButton variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <circle cx="12" cy="12" r="3"></circle>
          <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path>
        </svg>
        <span>Config</span>
      </GlassButton>
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
  gap: 8px;
  align-items: center;
  height: 42px;
}

.info-block {
  display: flex;
  flex-direction: column;
  justify-content: center;
  padding: 0 8px;
  
  &.placeholder {
    color: var(--text-tertiary);
    font-style: italic;
    font-size: 0.85rem;
  }
}

.project-name {
  font-weight: 600;
  color: var(--text-primary);
  font-size: 0.9rem;
}

.project-meta {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.divider-vertical {
  width: 1px;
  height: 28px;
  background: var(--border-subtle);
  margin: 0 4px;
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
