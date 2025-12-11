<script setup lang="ts">
import { computed } from 'vue';
import { useCanvasStore } from '@/stores/canvasStore';

const store = useCanvasStore();

const selectedElement = computed(() => {
  if (!store.selectedElementId || !store.document) return null;
  
  // Search in modules (Phase 1 focus)
  const module = store.document.modules.find(m => m.id === store.selectedElementId);
  if (module) return { type: 'Module', data: module };
  
  // Search in zones
  const zone = store.document.zones.find(z => z.id === store.selectedElementId);
  if (zone) return { type: 'Zone', data: zone };
  
  return null;
});
</script>

<template>
  <transition name="slide">
    <aside v-if="selectedElement" class="property-panel cyber-panel">
      <div class="header">
        <h3>{{ selectedElement.type }} Details</h3>
        <button class="close-btn" @click="store.select(null)">×</button>
      </div>
      
      <div class="content">
        <div class="field">
          <label>ID</label>
          <div class="value">{{ selectedElement.data.id }}</div>
        </div>
        
        <template v-if="selectedElement.type === 'Module'">
          <div class="field">
            <label>Name</label>
            <div class="value">{{ (selectedElement.data as any).moduleName || 'Unknown' }}</div>
          </div>
          <div class="field">
            <label>Zone</label>
            <div class="value">{{ (selectedElement.data as any).zoneId }}</div>
          </div>
          <div class="field">
            <label>Facing</label>
            <div class="value">{{ (selectedElement.data as any).facing }}</div>
          </div>
        </template>

        <template v-if="selectedElement.type === 'Zone'">
          <div class="field">
            <label>Room ID</label>
            <div class="value">{{ (selectedElement.data as any).roomId }}</div>
          </div>
          <div class="field">
            <label>Tags</label>
            <div class="tags">
              <span v-for="tag in (selectedElement.data as any).tags" :key="tag" class="tag">
                {{ tag }}
              </span>
            </div>
          </div>
        </template>
      </div>
    </aside>
  </transition>
</template>

<style scoped lang="scss">
.property-panel {
  position: absolute;
  top: 80px;
  right: 20px;
  width: 300px;
  padding: 20px;
  z-index: 90;

  .header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 20px;
    border-bottom: 1px solid var(--glass-border);
    padding-bottom: 10px;

    h3 {
      margin: 0;
      color: var(--neon-cyan);
      text-transform: uppercase;
      letter-spacing: 1px;
    }

    .close-btn {
      background: none;
      border: none;
      color: var(--text-secondary);
      font-size: 1.5rem;
      cursor: pointer;
      &:hover { color: var(--neon-pink); }
    }
  }

  .field {
    margin-bottom: 15px;
    
    label {
      display: block;
      font-size: 0.8rem;
      color: var(--text-secondary);
      margin-bottom: 5px;
    }

    .value {
      font-size: 1rem;
      color: var(--text-primary);
    }

    .tags {
      display: flex;
      flex-wrap: wrap;
      gap: 5px;
      
      .tag {
        background: rgba(0, 255, 255, 0.1);
        border: 1px solid var(--neon-cyan);
        padding: 2px 6px;
        font-size: 0.8rem;
        border-radius: 2px;
      }
    }
  }
}

.slide-enter-active,
.slide-leave-active {
  transition: all 0.3s ease;
}

.slide-enter-from,
.slide-leave-to {
  transform: translateX(50px);
  opacity: 0;
}
</style>
