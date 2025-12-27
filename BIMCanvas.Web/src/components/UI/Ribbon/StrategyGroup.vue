<script setup lang="ts">
import { ref } from 'vue';
import GlassSelect from '../base/GlassSelect.vue';

const currentStrategy = ref('default');

const strategies = [
  { label: 'Default Strategy', value: 'default', icon: '📝' },
  { label: 'Minimalist', value: 'minimal', icon: '⚪' },
  { label: 'Create New...', value: 'new', icon: '➕' },
];
</script>

<template>
  <div class="ribbon-group">
    <div class="group-title">Strategy</div>
    <div class="group-content">
      <div class="combo-box">
        <span class="label">Current Strategy</span>
        <GlassSelect 
          v-model="currentStrategy" 
          :options="strategies" 
          width="180px"
        />
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
  justify-content: center;
}

.label {
  font-size: 0.7rem;
  color: var(--text-secondary);
  margin-left: 2px;
}
</style>
