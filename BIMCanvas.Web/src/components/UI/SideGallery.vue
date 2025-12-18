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
  /* Positioned by parent container, but we handle the visual expansion */
  width: 48px; /* Collapsed state */
  height: calc(100% - 80px); /* Fill grid area minus top and bottom margin */
  margin-top: 40px; /* Avoid overlap with top bar */
  margin-bottom: 40px; /* Symmetric bottom margin */
  background: var(--surface-glass);
  backdrop-filter: blur(10px);
  border-left: 1px solid var(--border-subtle); /* Changed to border-left */
  display: flex;
  flex-direction: column;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  overflow: hidden;
  z-index: 90;
  
  /* Right aligned behavior */
  margin-left: auto; 
  
  &.expanded {
    width: 280px;
    background: var(--surface-elevated);
    border-color: var(--border-subtle);
    box-shadow: -10px 0 30px rgba(0, 0, 0, 0.1); /* Shadow to left */

    .header .indicator {
      transform: rotate(0deg); /* Reset rotation or adjust for right side */
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
    
    /* Keep header on the right side when expanded */
    position: absolute;
    right: 0;
    top: 0;
    
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
      transform: rotate(180deg); /* Point left by default */
    }

    &:hover .label {
      color: var(--text-primary);
    }
  }

  .content {
    position: absolute;
    right: 48px; /* Content to the left of header */
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
  from { opacity: 0; transform: translateX(10px); }
  to { opacity: 1; transform: translateX(0); }
}
</style>
