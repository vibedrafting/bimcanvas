<script setup lang="ts">
// Placeholder for Side Gallery
</script>

<template>
  <aside class="side-gallery">
    <!-- Gallery content will go here -->
    <div class="placeholder-text">Gallery</div>
  </aside>
</template>

<style scoped lang="scss">
.side-gallery {
  position: absolute;
  top: 80px; // Below toolbar
  left: 20px;
  width: 60px; // Collapsed state
  height: calc(100% - 100px);
  background: rgba(10, 10, 15, 0.6);
  backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.05);
  border-radius: 8px;
  display: flex;
  justify-content: center;
  padding-top: 20px;
  transition: width 0.3s ease;
  
  .placeholder-text {
    writing-mode: vertical-rl;
    text-orientation: mixed;
    color: rgba(255, 255, 255, 0.3);
    font-size: 0.8rem;
    letter-spacing: 2px;
  }
  
  &:hover {
    background: rgba(10, 10, 15, 0.8);
    border-color: rgba(255, 255, 255, 0.1);
  }
}
</style>
