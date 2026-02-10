<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';

const openLocalLibrary = () => {
  window.dispatchEvent(new CustomEvent('bimcanvas:open-module-library'));
};
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      <GlassButton variant="ghost" class="ribbon-btn" @click="openLocalLibrary">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"></path>
          <path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"></path>
        </svg>
        <span>Local</span>
      </GlassButton>
      <GlassButton variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M18 10h-1.26A8 8 0 1 0 9 20h9a5 5 0 0 0 0-10z"></path>
        </svg>
        <span>Cloud</span>
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
  align-items: center;
  height: 42px;
  min-width: 50px;
  gap: 2px;
  font-size: 0.7rem;
  padding: 4px 8px;
}
</style>

