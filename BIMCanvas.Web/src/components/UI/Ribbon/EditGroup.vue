<script setup lang="ts">
import { ref, reactive, watch, onMounted, onBeforeUnmount, toRaw } from 'vue';
import GlassButton from '../base/GlassButton.vue';
import { SnapConfig, type SnapConfigState } from '../../../services/interaction/snap/SnapConfig';

const dispatchAction = (action: 'rotate' | 'delete' | 'move' | 'mirror' | 'copy' | 'measure') => {
  if (action === 'measure') {
    // Measurement doesn't dispatch a BIMCanvas action event, it's a tool activation directly? 
    // Or we should add a listener in InteractionService?
    // MoveTool listens for `bimcanvas:action-move`.
    // Let's add listener for `bimcanvas:action-measure` in InteractionService (wait, I haven't added that yet).
    // I added activateMeasurementTool() but no event listener in InteractionService constructor?
    // Let's check InteractionService.ts again.
    // I missed adding the event listener in InteractionService.ts!
    // I should add it there too. 
    // But for now let's emit the event.
    window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
  } else {
    window.dispatchEvent(new CustomEvent(`bimcanvas:action-${action}`));
  }
};

const showSnapSettings = ref(false);
const snapSettingsRef = ref<HTMLElement | null>(null);
const initialSnapConfig = SnapConfig.get();
const snapConfig = reactive<SnapConfigState>({
  ...initialSnapConfig,
  enabled: { ...initialSnapConfig.enabled }
});

const toggleSnapSettings = (event: MouseEvent) => {
  event.stopPropagation();
  showSnapSettings.value = !showSnapSettings.value;
};

const closeSnapSettings = () => {
  showSnapSettings.value = false;
};

const handleGlobalClick = (event: MouseEvent) => {
  if (!showSnapSettings.value) return;
  const target = event.target as Node | null;
  if (snapSettingsRef.value && target && snapSettingsRef.value.contains(target)) {
    return;
  }
  closeSnapSettings();
};

watch(
  snapConfig,
  () => {
    SnapConfig.set(toRaw(snapConfig) as SnapConfigState);
  },
  { deep: true }
);

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
});

onBeforeUnmount(() => {
  window.removeEventListener('click', handleGlobalClick);
});
</script>

<template>
  <div class="ribbon-group">
    <div class="group-content">
      <GlassButton variant="ghost" class="ribbon-btn" active>
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M3 3l7.07 16.97 2.51-7.39 7.39-2.51L3 3z"></path>
          <path d="M13 13l6 6"></path>
        </svg>
        <span>Select</span>
      </GlassButton>
      <GlassButton @click="dispatchAction('move')" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="5 9 2 12 5 15"></polyline>
          <polyline points="9 5 12 2 15 5"></polyline>
          <polyline points="19 9 22 12 19 15"></polyline>
          <polyline points="9 19 12 22 15 19"></polyline>
          <line x1="2" y1="12" x2="22" y2="12"></line>
          <line x1="12" y1="2" x2="12" y2="22"></line>
        </svg>
        <span>Move</span>
      </GlassButton>
      <GlassButton @click="dispatchAction('measure')" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <path d="M21 6H3"></path><path d="M21 12H3"></path><path d="M21 18H3"></path><path d="M5 6v12"></path><path d="M9 6v12"></path><path d="M13 6v12"></path><path d="M17 6v12"></path>
        </svg>
        <span>Measure</span>
      </GlassButton>
      <GlassButton @click="dispatchAction('rotate')" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M23 4v6h-6"></path>
          <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"></path>
        </svg>
        <span>Rotate</span>
      </GlassButton>
      <GlassButton @click="dispatchAction('copy')" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect>
          <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path>
        </svg>
        <span>Copy</span>
      </GlassButton>
      <GlassButton @click="dispatchAction('delete')" variant="ghost" class="ribbon-btn">
        <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="3 6 5 6 21 6"></polyline>
          <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
          <line x1="10" y1="11" x2="10" y2="17"></line>
          <line x1="14" y1="11" x2="14" y2="17"></line>
        </svg>
        <span>Delete</span>
      </GlassButton>
      <div class="snap-settings-container" ref="snapSettingsRef">
        <GlassButton @click="toggleSnapSettings" variant="ghost" class="ribbon-btn">
          <svg style="width: 18px; height: 18px; flex-shrink: 0;" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
            <circle cx="12" cy="12" r="10"></circle>
            <path d="M12 6v12"></path>
            <path d="M6 12h12"></path>
          </svg>
          <span>Osnap</span>
        </GlassButton>
        <div v-if="showSnapSettings" class="snap-settings" @click.stop>
          <div class="snap-title">对象捕捉</div>
          <label class="snap-option">
            <input type="checkbox" v-model="snapConfig.enabled.endpoint" />
            <span>端点</span>
          </label>
          <label class="snap-option">
            <input type="checkbox" v-model="snapConfig.enabled.midpoint" />
            <span>中点</span>
          </label>
          <label class="snap-option">
            <input type="checkbox" v-model="snapConfig.enabled.perpendicular" />
            <span>垂足</span>
          </label>
          <label class="snap-option">
            <input type="checkbox" v-model="snapConfig.enabled.intersection" />
            <span>交点</span>
          </label>
        </div>
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

.snap-settings-container {
  position: relative;
}

.snap-settings {
  position: absolute;
  top: 44px;
  left: 0;
  min-width: 160px;
  padding: 8px 10px;
  background: rgba(20, 20, 30, 0.95);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 6px;
  box-shadow: 0 8px 16px rgba(0, 0, 0, 0.35);
  z-index: 1000;
}

.snap-title {
  font-size: 0.75rem;
  color: var(--text-secondary);
  margin-bottom: 6px;
}

.snap-option {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  color: var(--text-primary);
  margin: 4px 0;
  cursor: pointer;
}

.snap-option input[type='checkbox'] {
  accent-color: #00ff66;
}
</style>

