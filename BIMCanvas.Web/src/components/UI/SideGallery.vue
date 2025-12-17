<script setup lang="ts">
import { ref } from 'vue';

const isExpanded = ref(false);

const toggleExpand = () => {
  isExpanded.value = !isExpanded.value;
};
</script>

<template>
  <aside 
    class="side-gallery" 
    :class="{ expanded: isExpanded }"
    @mouseenter="isExpanded = true"
    @mouseleave="isExpanded = false"
  >
    <div class="header" @click="toggleExpand">
      <span class="label">Gallery</span>
      <span class="indicator">›</span>
    </div>
    
    <div class="content" v-if="isExpanded">
      <!-- Placeholder for future gallery items -->
      <div class="empty-state">
        No proposals yet
      </div>
    </div>
  </aside>
</template>

<style scoped lang="scss">
.side-gallery {
  position: absolute;
  top: 80px; /* Below toolbar */
  left: var(--spacing-lg);
  width: 48px; /* Collapsed state */
  height: calc(100% - 100px);
  background: var(--surface-glass);
  backdrop-filter: blur(10px);
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  display: flex;
  flex-direction: column;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  overflow: hidden;
  z-index: 90;
  
  &.expanded {
    width: 280px;
    background: rgba(10, 10, 15, 0.9);
    border-color: rgba(255, 255, 255, 0.1);
    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.3);

    .header .indicator {
      transform: rotate(180deg);
    }
  }

  .header {
    height: 100%;
    width: 48px;
    display: flex;
    flex-direction: column;
    align-items: center;
    padding-top: var(--spacing-lg);
    cursor: pointer;
    flex-shrink: 0;
    
    .label {
      writing-mode: vertical-rl;
      text-orientation: mixed;
      color: var(--text-secondary);
      font-size: 0.8rem;
      letter-spacing: 2px;
      font-family: var(--font-sans);
      text-transform: uppercase;
      white-space: nowrap;
      transition: color 0.2s;
    }

    .indicator {
      margin-top: var(--spacing-sm);
      color: var(--text-secondary);
      font-size: 1.2rem;
      transition: transform 0.3s;
    }

    &:hover .label {
      color: var(--text-primary);
    }
  }

  .content {
    position: absolute;
    left: 48px;
    top: 0;
    width: calc(100% - 48px);
    height: 100%;
    padding: var(--spacing-md);
    opacity: 0;
    animation: fadeIn 0.3s forwards 0.1s;
    
    .empty-state {
      color: var(--text-secondary);
      font-size: 0.9rem;
      text-align: center;
      margin-top: var(--spacing-xl);
      font-family: var(--font-sans);
    }
  }
}

@keyframes fadeIn {
  from { opacity: 0; transform: translateX(-10px); }
  to { opacity: 1; transform: translateX(0); }
}
</style>
