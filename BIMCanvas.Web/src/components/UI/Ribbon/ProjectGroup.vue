<script setup lang="ts">
import { computed } from 'vue';
import { useCanvasStore } from '../../../stores/canvasStore';
import GlassButton from '../base/GlassButton.vue';

const store = useCanvasStore();
const projectInfo = computed(() => store.projectData?.project);
</script>

<template>
  <div class="ribbon-group">
    <div class="group-title">Project</div>
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
        <span class="icon">⚙️</span> Config
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

.group-title {
  font-size: 0.75rem;
  color: var(--text-tertiary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  text-align: center;
}

.group-content {
  display: flex;
  gap: 8px;
  align-items: center;
  height: 48px;
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
  height: 32px;
  background: var(--border-subtle);
  margin: 0 4px;
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
