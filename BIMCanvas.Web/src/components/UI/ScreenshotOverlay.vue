<script setup lang="ts">
import { ref, onMounted, onUnmounted } from 'vue'
import html2canvas from 'html2canvas'

const emit = defineEmits<{
  (e: 'capture', imageData: string): void
  (e: 'cancel'): void
}>()

// 选区状态
const isSelecting = ref(false)
const startX = ref(0)
const startY = ref(0)
const endX = ref(0)
const endY = ref(0)

// 计算选区矩形
const selectionStyle = () => {
  const left = Math.min(startX.value, endX.value)
  const top = Math.min(startY.value, endY.value)
  const width = Math.abs(endX.value - startX.value)
  const height = Math.abs(endY.value - startY.value)
  return {
    left: `${left}px`,
    top: `${top}px`,
    width: `${width}px`,
    height: `${height}px`
  }
}

const handleMouseDown = (e: MouseEvent) => {
  isSelecting.value = true
  startX.value = e.clientX
  startY.value = e.clientY
  endX.value = e.clientX
  endY.value = e.clientY
}

const handleMouseMove = (e: MouseEvent) => {
  if (!isSelecting.value) return
  endX.value = e.clientX
  endY.value = e.clientY
}

const handleMouseUp = async () => {
  if (!isSelecting.value) return
  isSelecting.value = false

  const left = Math.min(startX.value, endX.value)
  const top = Math.min(startY.value, endY.value)
  const width = Math.abs(endX.value - startX.value)
  const height = Math.abs(endY.value - startY.value)

  // 选区太小则取消
  if (width < 10 || height < 10) {
    emit('cancel')
    return
  }

  const overlay = document.querySelector('.screenshot-overlay') as HTMLElement

  try {
    // 使用 visibility 而不是 display，避免 DOM 重排
    if (overlay) overlay.style.visibility = 'hidden'

    // 截取整个文档
    const canvas = await html2canvas(document.body, {
      backgroundColor: null,
      scale: window.devicePixelRatio || 1,
      logging: false,
      useCORS: true,
      allowTaint: true,
      foreignObjectRendering: true
    })

    // 裁剪选中区域
    const scale = window.devicePixelRatio || 1
    const croppedCanvas = document.createElement('canvas')
    croppedCanvas.width = width * scale
    croppedCanvas.height = height * scale

    const ctx = croppedCanvas.getContext('2d')
    if (ctx) {
      ctx.drawImage(
        canvas,
        left * scale,
        top * scale,
        width * scale,
        height * scale,
        0,
        0,
        width * scale,
        height * scale
      )
    }

    const imageData = croppedCanvas.toDataURL('image/png')
    emit('capture', imageData)
  } catch (error) {
    console.error('[ScreenshotOverlay] Capture failed:', error)
    emit('cancel')
  } finally {
    // 确保恢复 overlay 可见性
    if (overlay) overlay.style.visibility = ''
  }
}

const handleKeyDown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    emit('cancel')
  }
}

onMounted(() => {
  window.addEventListener('keydown', handleKeyDown)
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown)
})
</script>

<template>
  <div
    class="screenshot-overlay"
    @mousedown="handleMouseDown"
    @mousemove="handleMouseMove"
    @mouseup="handleMouseUp"
  >
    <!-- 提示文字 -->
    <div class="hint" v-if="!isSelecting">
      <div class="hint-text">拖拽选择截图区域</div>
      <div class="hint-subtext">按 ESC 取消</div>
    </div>

    <!-- 选区框 -->
    <div
      v-if="isSelecting || (startX !== endX && startY !== endY)"
      class="selection-box"
      :style="selectionStyle()"
    >
      <div class="selection-size">
        {{ Math.abs(endX - startX) }} × {{ Math.abs(endY - startY) }}
      </div>
    </div>

    <!-- 遮罩（选区外的暗色区域） -->
    <div class="mask-top" :style="{ height: `${Math.min(startY, endY)}px` }"></div>
    <div class="mask-bottom" :style="{ top: `${Math.max(startY, endY)}px` }"></div>
    <div
      class="mask-left"
      :style="{
        top: `${Math.min(startY, endY)}px`,
        height: `${Math.abs(endY - startY)}px`,
        width: `${Math.min(startX, endX)}px`
      }"
    ></div>
    <div
      class="mask-right"
      :style="{
        top: `${Math.min(startY, endY)}px`,
        height: `${Math.abs(endY - startY)}px`,
        left: `${Math.max(startX, endX)}px`
      }"
    ></div>
  </div>
</template>

<style scoped lang="scss">
.screenshot-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  z-index: 99999;
  cursor: crosshair;
}

.hint {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  text-align: center;
  pointer-events: none;
  z-index: 10;

  .hint-text {
    font-size: 24px;
    color: white;
    text-shadow: 0 2px 8px rgba(0, 0, 0, 0.8);
    margin-bottom: 8px;
  }

  .hint-subtext {
    font-size: 14px;
    color: rgba(255, 255, 255, 0.7);
    text-shadow: 0 1px 4px rgba(0, 0, 0, 0.8);
  }
}

.selection-box {
  position: absolute;
  border: 2px solid #4a9eff;
  background: transparent;
  box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.5);
  z-index: 5;

  .selection-size {
    position: absolute;
    bottom: -24px;
    left: 50%;
    transform: translateX(-50%);
    background: rgba(0, 0, 0, 0.7);
    color: white;
    padding: 2px 8px;
    border-radius: 4px;
    font-size: 12px;
    white-space: nowrap;
  }
}

// 遮罩层（备用方案，如果 box-shadow 不够用）
.mask-top,
.mask-bottom,
.mask-left,
.mask-right {
  position: absolute;
  background: transparent;
  pointer-events: none;
}

.mask-top {
  top: 0;
  left: 0;
  width: 100%;
}

.mask-bottom {
  left: 0;
  width: 100%;
  bottom: 0;
}

.mask-left {
  left: 0;
}

.mask-right {
  right: 0;
}
</style>
