<script setup lang="ts">
/**
 * 全局重启按钮 — Teleport 到 HomePage 顶栏 #global-header-actions。
 *
 * 仅当 systemStore.restartRequired === true 时显示;点击直接触发 systemStore.performRestart(),
 * 无二次确认对话框(此前 toast 已告知"需重启",按钮点击即视为用户主动确认)。
 *
 * 与 PluginsPanel / HomeSettingsPanel 顶栏自有 actions 区(#plugins-header-actions /
 * #settings-header-actions)解耦,跨 mode 共享。
 */
import { onMounted, ref, nextTick } from 'vue'
import GlassButton from './base/GlassButton.vue'
import { useSystemStore } from '../../stores/systemStore'
import { SERVER_BASE } from '../../config/api'

const systemStore = useSystemStore()
const isMounted = ref(false)

onMounted(async () => {
  await nextTick()
  isMounted.value = true
})

const onClick = () => {
  systemStore.performRestart(SERVER_BASE)
}
</script>

<template>
  <Teleport to="#global-header-actions" v-if="isMounted && systemStore.restartRequired">
    <GlassButton
      variant="warning"
      :disabled="systemStore.isRestarting"
      :title="`需要重启: ${[...systemStore.restartReasons].join(', ')}`"
      @click="onClick"
    >
      <svg
        viewBox="0 0 24 24"
        width="14"
        height="14"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        :class="{ spinning: systemStore.isRestarting }"
        style="margin-right: 6px;"
      >
        <polyline points="23 4 23 10 17 10" />
        <polyline points="1 20 1 14 7 14" />
        <path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" />
      </svg>
      {{ systemStore.isRestarting ? '重启中...' : '需要重启' }}
    </GlassButton>
  </Teleport>
</template>

<style scoped>
.spinning {
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  from { transform: rotate(0deg); }
  to { transform: rotate(360deg); }
}
</style>
