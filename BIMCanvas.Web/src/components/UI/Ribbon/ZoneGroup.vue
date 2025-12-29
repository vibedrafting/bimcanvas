<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      <GlassButton variant="ghost" class="ribbon-btn">
        <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M2 22h20"></path>
          <path d="M5 2h14"></path>
          <path d="M5 2v20"></path>
          <path d="M19 2v20"></path>
          <path d="M5 7h14"></path>
          <path d="M5 12h14"></path>
          <path d="M5 17h14"></path>
        </svg>
        Edit Zone
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
  gap: 4px;
}

.ribbon-btn {
  flex-direction: column;
  height: 42px;
  min-width: 42px;
  gap: 2px;
  font-size: 0.7rem;
  padding: 4px;
  
  .icon {
    width: 18px;
    height: 18px;
  }
}
</style>
