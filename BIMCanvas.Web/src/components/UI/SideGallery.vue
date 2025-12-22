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
      <svg class="indicator" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="9 18 15 12 9 6"></polyline>
      </svg>
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
  height: 100%; /* Full height */
  margin: 0; /* Anchored to edges */
  padding-top: 0; /* Let internal elements handle spacing */
  
  /* Aurora Glass - Matching Dynamic Island */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  
  /* Anchored Borders */
  border: none;
  border-left: var(--glass-border);
  border-radius: 24px 0 0 24px; /* Round inner corners only */
  
  /* Glare Overlay */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  display: flex;
  flex-direction: column;
  transition: all 0.6s cubic-bezier(0.19, 1, 0.22, 1); /* Premium Spring Curve */
  overflow: hidden;
  z-index: 90;
  
  /* Right aligned behavior */
  margin-left: auto; 
  margin-right: 0; /* Anchored */
  
  &.expanded {
    width: 280px;
    /* Maintain same glass effect, just add shadow */
    box-shadow: var(--shadow-panel), var(--glass-inner-highlight);

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
    padding-top: 40vh; /* Position slightly up from center */
    cursor: pointer;
    flex-shrink: 0;
    
    /* Keep header on the right side when expanded */
    position: absolute;
    right: 0;
    top: 0;
    
    .indicator {
      width: 24px;
      height: 24px;
      color: var(--text-secondary);
      transition: transform 0.4s var(--ease-spring), color 0.2s;
      transform: rotate(180deg); /* Point left by default */
      opacity: 0.8;
    }
    
    &:hover .indicator {
      color: var(--text-primary);
      opacity: 1;
      /* Maintain rotation but add scale if needed, or just color change */
    }
  }

  .content {
    position: absolute;
    right: 48px; /* Content to the left of header */
    top: 100px; /* Clear the top toolbar buttons */
    width: calc(100% - 48px);
    height: calc(100% - 100px);
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
