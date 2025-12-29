<script setup lang="ts">
import { ref } from 'vue';
import GlassSelect from '../base/GlassSelect.vue';

const currentStrategy = ref('default');

const strategies = [
  { 
    label: 'Default Strategy', 
    value: 'default', 
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line><polyline points="10 9 9 9 8 9"></polyline></svg>' 
  },
  { 
    label: 'Minimalist', 
    value: 'minimal', 
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle></svg>' 
  },
  { 
    label: 'Create New...', 
    value: 'new', 
    icon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>' 
  },
];
</script>

<template>
  <div class="ribbon-group">
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

.group-content {
  display: flex;
  gap: 4px;
  align-items: center;
  height: 42px;
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
