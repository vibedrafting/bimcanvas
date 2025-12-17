<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import GlassButton from './base/GlassButton.vue';
import IconBadge from './base/IconBadge.vue';
import { LayerManager } from '../../services/three/LayerManager';

const store = useCanvasStore();

// Tabs
type Tab = 'home' | 'modify' | 'view';
const currentTab = ref<Tab>('home');

// Watch for selection to auto-switch to Modify tab
watch(() => store.selectedObject, (newVal) => {
  if (newVal) {
    currentTab.value = 'modify';
  } else {
    if (currentTab.value === 'modify') {
      currentTab.value = 'home';
    }
  }
});

// Load Demo
const handleLoadDemo = async () => {
  await store.loadDemoData('/demo/layout_proposal.json');
};

// Actions
const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};

// View Logic
const currentView = ref<'human' | 'ai'>('human');

const toggleView = (mode: 'human' | 'ai') => {
  currentView.value = mode;
  window.dispatchEvent(new CustomEvent('bimcanvas:view-mode-change', { detail: mode }));
};

</script>

<template>
  <div class="ribbon-toolbar">
    <!-- Top Bar: Branding & Quick Access -->
    <div class="top-bar">
      <div class="brand-area">
        <span class="brand-text">BIMCanvas</span>
        <div class="divider"></div>
        <GlassButton @click="store.undo()" :disabled="!store.canUndo" variant="ghost" title="Undo" class="icon-btn">
          ↩
        </GlassButton>
        <GlassButton @click="store.redo()" :disabled="!store.canRedo" variant="ghost" title="Redo" class="icon-btn">
          ↪
        </GlassButton>
        <div class="divider"></div>
        <GlassButton @click="handleLoadDemo" :disabled="store.isLoading" variant="ghost">
          Load Example
        </GlassButton>
      </div>
    </div>

    <!-- Tab Bar -->
    <div class="tab-bar">
      <button 
        v-for="tab in ['home', 'modify', 'view']" 
        :key="tab"
        :class="['tab-btn', { active: currentTab === tab }]"
        @click="currentTab = tab as Tab"
      >
        {{ tab.charAt(0).toUpperCase() + tab.slice(1) }}
      </button>
    </div>

    <!-- Tab Content -->
    <div class="ribbon-content">
      
      <!-- HOME TAB -->
      <div v-if="currentTab === 'home'" class="group">
        <div class="label">Basic</div>
        <div class="tools">
          <GlassButton variant="ghost" title="Select">
            ↖ Select
          </GlassButton>
        </div>
      </div>

      <!-- MODIFY TAB -->
      <div v-if="currentTab === 'modify'" class="group">
        <div class="label">Transform</div>
        <div class="tools">
          <GlassButton @click="dispatchAction('move')" :disabled="!store.selectedObject" variant="ghost">
            <span class="icon">✥</span> Move
          </GlassButton>
          <GlassButton @click="dispatchAction('rotate')" :disabled="!store.selectedObject" variant="ghost">
            <span class="icon">↻</span> Rotate
          </GlassButton>
          <GlassButton @click="dispatchAction('delete')" :disabled="!store.selectedObject" variant="danger">
            <span class="icon">🗑</span> Delete
          </GlassButton>
        </div>
      </div>

      <!-- VIEW TAB -->
      <div v-if="currentTab === 'view'" class="group">
        <div class="label">Vision Mode</div>
        <div class="tools">
          <GlassButton :active="currentView === 'human'" @click="toggleView('human')" variant="ghost">
            Human
          </GlassButton>
          <GlassButton :active="currentView === 'ai'" @click="toggleView('ai')" variant="ghost">
            AI Vision
          </GlassButton>
        </div>
      </div>

    </div>
  </div>
</template>

<style scoped lang="scss">
.ribbon-toolbar {
  display: flex;
  flex-direction: column;
  background: #0a0a0f;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
  color: #fff;
  width: 100%;
}

.top-bar {
  display: flex;
  align-items: center;
  height: 36px;
  padding: 0 var(--spacing-md);
  background: rgba(255, 255, 255, 0.02);
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);

  .brand-area {
    display: flex;
    align-items: center;
    gap: var(--spacing-sm);

    .brand-text {
      font-weight: 600;
      font-size: 0.85rem;
      letter-spacing: 0.5px;
      margin-right: var(--spacing-sm);
      color: var(--text-secondary);
    }
  }
}

.tab-bar {
  display: flex;
  padding: 0 var(--spacing-md);
  gap: var(--spacing-lg);
  background: transparent;
  margin-top: 4px;

  .tab-btn {
    background: none;
    border: none;
    color: var(--text-tertiary);
    padding: 6px 4px;
    font-size: 0.9rem;
    font-weight: 500;
    cursor: pointer;
    transition: all 0.2s;
    position: relative;

    &:hover {
      color: var(--text-primary);
    }

    &.active {
      color: #fff;
      font-weight: 600;
      
      &::after {
        content: '';
        position: absolute;
        bottom: -1px; /* Align with border-bottom of container if needed, or just below text */
        left: 0;
        width: 100%;
        height: 2px;
        background: var(--primary-color, #4a9eff);
        box-shadow: 0 0 8px rgba(74, 158, 255, 0.5);
      }
    }
  }
}

.ribbon-content {
  display: flex;
  align-items: center;
  padding: var(--spacing-sm) var(--spacing-md);
  height: 64px;
  gap: var(--spacing-xl);
  background: rgba(255, 255, 255, 0.015);

  .group {
    display: flex;
    flex-direction: column;
    gap: 6px;
    height: 100%;
    justify-content: center;

    .label {
      font-size: 0.65rem;
      color: var(--text-tertiary);
      text-transform: uppercase;
      letter-spacing: 0.8px;
      font-weight: 500;
    }

    .tools {
      display: flex;
      gap: var(--spacing-sm);
      align-items: center;
    }
  }
}

.divider {
  width: 1px;
  height: 14px;
  background: rgba(255, 255, 255, 0.1);
  margin: 0 var(--spacing-xs);
}

.icon-btn {
  padding: 4px 8px;
  font-size: 1.1rem;
}
</style>
