<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';

const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror' | 'copy') => {
  window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
};
</script>

<template>
  <div class="ribbon-group">
    <div class="group-title">Edit</div>
    <div class="group-content">
      <GlassButton variant="ghost" class="ribbon-btn" active>
        <span class="icon">↖️</span> Select
      </GlassButton>
      <GlassButton @click="dispatchAction('move')" variant="ghost" class="ribbon-btn">
        <span class="icon">✥</span> Move
      </GlassButton>
      <GlassButton @click="dispatchAction('rotate')" variant="ghost" class="ribbon-btn">
        <span class="icon">↻</span> Rotate
      </GlassButton>
      <GlassButton @click="dispatchAction('copy')" variant="ghost" class="ribbon-btn">
        <span class="icon">⧉</span> Copy
      </GlassButton>
      <GlassButton @click="dispatchAction('delete')" variant="ghost" class="ribbon-btn">
        <span class="icon">🗑</span> Delete
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
  gap: 4px;
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
