<script setup lang="ts">
import GlassButton from '../base/GlassButton.vue';
</script>

<template>
  <div class="ribbon-group">
    <div class="group-title">Variant</div>
    <div class="group-content">
      <div class="combo-box">
        <span class="label">Current Variant</span>
        <select class="glass-select">
          <option>Main Branch</option>
          <option>Variant A</option>
          <option>New Variant...</option>
        </select>
      </div>
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
  align-items: center;
  height: 48px;
}

.combo-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.label {
  font-size: 0.75rem;
  color: var(--text-secondary);
}

.glass-select {
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: var(--text-primary);
  padding: 4px 8px;
  border-radius: 4px;
  outline: none;
  
  &:focus {
    border-color: var(--accent-primary);
  }
}
</style>
