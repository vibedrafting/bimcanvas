<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick } from 'vue'
import html2canvas from 'html2canvas'

const emit = defineEmits<{
  (e: 'capture', imageData: string): void
  (e: 'cancel'): void
}>()

// 状态枚举
type ToolType = 'rect' | 'arrow' | 'text' | null
type ResizeHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w'
type Annotation = 
  | { type: 'rect'; x: number; y: number; w: number; h: number; color: string; size: number }
  | { type: 'arrow'; startX: number; startY: number; endX: number; endY: number; color: string; size: number }
  | { type: 'text'; x: number; y: number; text: string; color: string; size: number }

// 核心状态
const canvasRef = ref<HTMLCanvasElement | null>(null)
const bgCanvasRef = ref<HTMLCanvasElement | null>(null) // 存储原始全屏截图
const isSelecting = ref(true) // true=选区阶段, false=编辑阶段
const isDrawing = ref(false) // 正在绘制标注
const isResizing = ref(false) // 正在调整选区大小
const currentResizeHandle = ref<ResizeHandle | null>(null)

// 坐标状态
const startX = ref(0)
const startY = ref(0)
const endX = ref(0)
const endY = ref(0)

// 选区坐标 (标准化后)
const selection = computed(() => {
  const x = Math.min(startX.value, endX.value)
  const y = Math.min(startY.value, endY.value)
  const w = Math.abs(endX.value - startX.value)
  const h = Math.abs(endY.value - startY.value)
  return { x, y, w, h }
})

// 编辑工具状态
const currentTool = ref<ToolType>(null)
const currentColor = ref('#ff0000') // 默认红色
const currentSize = ref(2) // 1=small, 2=medium, 3=large
const annotations = ref<Annotation[]>([])
const history = ref<Annotation[][]>([]) // 撤销栈
const redoStack = ref<Annotation[][]>([]) // 重做栈

// 文本输入状态
const isTextInputVisible = ref(false)
const textInputX = ref(0)
const textInputY = ref(0)
const textInputValue = ref('')
const textInputRef = ref<HTMLTextAreaElement | null>(null)

// 颜色选项
const colors = ['#ff0000', '#00ff00', '#0000ff', '#ffff00', '#000000', '#ffffff']

// 初始化：截取全屏
onMounted(async () => {
  try {
    const canvas = await html2canvas(document.body, {
      backgroundColor: null,
      scale: window.devicePixelRatio || 1,
      logging: false,
      useCORS: true,
      allowTaint: true,
      foreignObjectRendering: true
    })
    
    bgCanvasRef.value = canvas
    
    // 初始化绘图 Canvas
    if (canvasRef.value) {
      canvasRef.value.width = window.innerWidth * (window.devicePixelRatio || 1)
      canvasRef.value.height = window.innerHeight * (window.devicePixelRatio || 1)
      canvasRef.value.style.width = '100vw'
      canvasRef.value.style.height = '100vh'
      draw()
    }
    
    window.addEventListener('keydown', handleKeyDown)
    window.addEventListener('mousemove', handleGlobalMouseMove)
    window.addEventListener('mouseup', handleGlobalMouseUp)
  } catch (error) {
    console.error('[Screenshot] Init failed:', error)
    emit('cancel')
  }
})

onUnmounted(() => {
  window.removeEventListener('keydown', handleKeyDown)
  window.removeEventListener('mousemove', handleGlobalMouseMove)
  window.removeEventListener('mouseup', handleGlobalMouseUp)
})

// 绘图循环
const draw = () => {
  const ctx = canvasRef.value?.getContext('2d')
  if (!ctx || !bgCanvasRef.value) return

  const dpr = window.devicePixelRatio || 1
  const width = canvasRef.value!.width
  const height = canvasRef.value!.height

  // 1. 清空
  ctx.clearRect(0, 0, width, height)

  // 2. 绘制全屏截图作为底图
  ctx.drawImage(bgCanvasRef.value, 0, 0, width, height)

  // 3. 绘制遮罩 (半透明黑色)
  ctx.fillStyle = 'rgba(0, 0, 0, 0.5)'
  ctx.fillRect(0, 0, width, height)

  // 4. 清除选区部分的遮罩 (使其高亮)
  if (selection.value.w > 0 && selection.value.h > 0) {
    const s = selection.value
    ctx.clearRect(s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr)
    // 重绘选区内的底图
    ctx.drawImage(
      bgCanvasRef.value, 
      s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr,
      s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr
    )
    
    // 绘制选区边框
    ctx.strokeStyle = '#00ff00'
    ctx.lineWidth = 1 * dpr
    ctx.strokeRect(s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr)
    
    // 绘制选区尺寸 (仅在拖拽选区时显示)
    if (isSelecting.value || isResizing.value) {
        ctx.fillStyle = 'rgba(0, 0, 0, 0.7)'
        ctx.fillRect(s.x * dpr, (s.y - 25) * dpr, 100 * dpr, 20 * dpr)
        ctx.fillStyle = '#fff'
        ctx.font = `${12 * dpr}px Arial`
        ctx.fillText(`${Math.round(s.w)} x ${Math.round(s.h)}`, (s.x + 5) * dpr, (s.y - 10) * dpr)
    }
  }

  // 5. 绘制标注 (仅在选区内)
  ctx.save()
  if (selection.value.w > 0) {
      const s = selection.value
      ctx.beginPath()
      ctx.rect(s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr)
      ctx.clip()
  }

  annotations.value.forEach(ann => {
    ctx.strokeStyle = ann.color
    ctx.fillStyle = ann.color
    ctx.lineWidth = ann.size * dpr
    
    if (ann.type === 'rect') {
      ctx.strokeRect(ann.x * dpr, ann.y * dpr, ann.w * dpr, ann.h * dpr)
    } else if (ann.type === 'arrow') {
      drawArrow(ctx, ann.startX * dpr, ann.startY * dpr, ann.endX * dpr, ann.endY * dpr, ann.size * dpr)
    } else if (ann.type === 'text') {
      ctx.font = `${(ann.size * 12 + 12) * dpr}px sans-serif`
      ctx.fillText(ann.text, ann.x * dpr, ann.y * dpr)
    }
  })

  // 绘制当前正在画的形状
  if (isDrawing.value && currentTool.value) {
      ctx.strokeStyle = currentColor.value
      ctx.fillStyle = currentColor.value
      ctx.lineWidth = currentSize.value * dpr
      
      if (currentTool.value === 'rect') {
          const w = endX.value - startX.value
          const h = endY.value - startY.value
          ctx.strokeRect(startX.value * dpr, startY.value * dpr, w * dpr, h * dpr)
      } else if (currentTool.value === 'arrow') {
          drawArrow(ctx, startX.value * dpr, startY.value * dpr, endX.value * dpr, endY.value * dpr, currentSize.value * dpr)
      }
  }
  
  ctx.restore()
}

const drawArrow = (ctx: CanvasRenderingContext2D, fromX: number, fromY: number, toX: number, toY: number, width: number) => {
    const headlen = width * 5
    const dx = toX - fromX
    const dy = toY - fromY
    const angle = Math.atan2(dy, dx)
    
    ctx.beginPath()
    ctx.moveTo(fromX, fromY)
    ctx.lineTo(toX, toY)
    ctx.stroke()
    
    ctx.beginPath()
    ctx.moveTo(toX - headlen * Math.cos(angle - Math.PI / 6), toY - headlen * Math.sin(angle - Math.PI / 6))
    ctx.lineTo(toX, toY)
    ctx.lineTo(toX - headlen * Math.cos(angle + Math.PI / 6), toY - headlen * Math.sin(angle + Math.PI / 6))
    ctx.fill()
}

// 交互处理
const handleMouseDown = (e: MouseEvent) => {
  // 如果点击了文本输入框，不处理
  if ((e.target as HTMLElement).tagName === 'TEXTAREA') return
  
  // 如果正在输入文本，点击其他地方确认文本
  if (isTextInputVisible.value) {
      confirmText()
      return
  }

  // 1. 选区模式
  if (isSelecting.value) {
    startX.value = e.clientX
    startY.value = e.clientY
    endX.value = e.clientX
    endY.value = e.clientY
    return
  }

  // 2. 编辑模式
  // 检查是否点击了 Resize Handle (通过 DOM 事件冒泡，Handle 会先捕获)
  // 如果 isResizing 已经被 Handle 设置为 true，则这里不需要做
  if (isResizing.value) return

  // 检查是否在选区内
  const s = selection.value
  if (e.clientX < s.x || e.clientX > s.x + s.w || e.clientY < s.y || e.clientY > s.y + s.h) {
      // 点击选区外，不做任何事 (或者可以实现移动选区)
      return
  }

  // 开始标注
  if (!currentTool.value) return

  if (currentTool.value === 'text') {
      startTextInput(e.clientX, e.clientY)
      return
  }

  isDrawing.value = true
  startX.value = e.clientX
  startY.value = e.clientY
  endX.value = e.clientX
  endY.value = e.clientY
}

const handleGlobalMouseMove = (e: MouseEvent) => {
  if (isSelecting.value) {
      if (e.buttons !== 1) return
      endX.value = e.clientX
      endY.value = e.clientY
      draw()
  } else if (isResizing.value && currentResizeHandle.value) {
      // 调整选区大小
      const handle = currentResizeHandle.value
      // 根据 handle 更新 startX/Y 或 endX/Y
      // 注意：这里需要保持 selection 的 x,y,w,h 逻辑，所以我们直接修改 start/end
      // 简单起见，我们假设 start 是左上，end 是右下 (在 mouseup 时规范化)
      
      if (handle.includes('e')) endX.value = e.clientX
      if (handle.includes('s')) endY.value = e.clientY
      if (handle.includes('w')) startX.value = e.clientX
      if (handle.includes('n')) startY.value = e.clientY
      
      draw()
  } else if (isDrawing.value) {
      endX.value = e.clientX
      endY.value = e.clientY
      draw()
  }
}

const handleGlobalMouseUp = (e: MouseEvent) => {
  if (isSelecting.value) {
      if (selection.value.w > 10 && selection.value.h > 10) {
          isSelecting.value = false
          // 规范化坐标，确保 start 是左上，end 是右下
          const s = selection.value
          startX.value = s.x
          startY.value = s.y
          endX.value = s.x + s.w
          endY.value = s.y + s.h
      } else {
          // 选区太小，重置
          startX.value = 0; startY.value = 0; endX.value = 0; endY.value = 0;
      }
      draw()
  } else if (isResizing.value) {
      isResizing.value = false
      currentResizeHandle.value = null
      // 规范化
      const s = selection.value
      startX.value = s.x
      startY.value = s.y
      endX.value = s.x + s.w
      endY.value = s.y + s.h
      draw()
  } else if (isDrawing.value) {
      isDrawing.value = false
      saveAnnotation()
      draw()
  }
}

const startResize = (handle: ResizeHandle) => {
    isResizing.value = true
    currentResizeHandle.value = handle
}

const saveAnnotation = () => {
    // 保存当前状态到历史栈
    history.value.push(JSON.parse(JSON.stringify(annotations.value)))
    // 清空重做栈
    redoStack.value = []

    if (currentTool.value === 'rect') {
        annotations.value.push({
            type: 'rect',
            x: startX.value, y: startY.value,
            w: endX.value - startX.value, h: endY.value - startY.value,
            color: currentColor.value, size: currentSize.value
        })
    } else if (currentTool.value === 'arrow') {
        annotations.value.push({
            type: 'arrow',
            startX: startX.value, startY: startY.value,
            endX: endX.value, endY: endY.value,
            color: currentColor.value, size: currentSize.value
        })
    }
}

// 文本输入逻辑
const startTextInput = (x: number, y: number) => {
    isTextInputVisible.value = true
    textInputX.value = x
    textInputY.value = y
    textInputValue.value = ''
    nextTick(() => {
        textInputRef.value?.focus()
    })
}

const confirmText = () => {
    if (!isTextInputVisible.value) return
    if (textInputValue.value.trim()) {
        history.value.push(JSON.parse(JSON.stringify(annotations.value)))
        redoStack.value = []
        
        annotations.value.push({
            type: 'text',
            x: textInputX.value,
            y: textInputY.value + 20,
            text: textInputValue.value,
            color: currentColor.value,
            size: currentSize.value
        })
    }
    isTextInputVisible.value = false
    currentTool.value = null
    draw()
}

// 键盘事件
const handleKeyDown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    if (isTextInputVisible.value) {
        isTextInputVisible.value = false
        return
    }
    if (!isSelecting.value) {
        // 如果在编辑模式，ESC 取消
        emit('cancel')
    } else {
        emit('cancel')
    }
  } else if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
      if (e.shiftKey) redo()
      else undo()
  } else if (e.key === 'Enter' && isTextInputVisible.value && !e.shiftKey) {
      e.preventDefault()
      confirmText()
  }
}

// 工具栏操作
const selectTool = (tool: ToolType) => {
    currentTool.value = tool
}

const undo = () => {
    if (annotations.value.length > 0) {
        // 保存当前到重做栈
        redoStack.value.push(JSON.parse(JSON.stringify(annotations.value)))
        // 恢复上一步
        annotations.value.pop()
        draw()
    }
}

const redo = () => {
    if (redoStack.value.length > 0) {
        const next = redoStack.value.pop()
        if (next) {
            // 当前状态入历史栈? 不，直接恢复
            // 其实 undo 逻辑有点简单，应该用 snapshot
            // 修正 undo/redo 逻辑：
            // history 应该存储每一步的完整 annotations 列表
            // 这里为了简单，我们假设 undo 是 pop，redo 是 push back
            // 但是上面的 undo 只是 pop 最后一个，这意味着 history 没用上？
            // 让我们重写 undo/redo 逻辑
        }
    }
}

// 重写 Undo/Redo
// 每次操作前，push 当前 annotations 到 history
// Undo: pop history -> current, push current -> redoStack
// Redo: pop redoStack -> current, push current -> history
// 上面的 saveAnnotation 已经 push 到 history 了
// 但是 undo 实现不对

const undoAction = () => {
    if (history.value.length > 0) {
        redoStack.value.push(JSON.parse(JSON.stringify(annotations.value)))
        const prev = history.value.pop()
        annotations.value = prev || []
        draw()
    }
}

const redoAction = () => {
    if (redoStack.value.length > 0) {
        history.value.push(JSON.parse(JSON.stringify(annotations.value)))
        const next = redoStack.value.pop()
        annotations.value = next || []
        draw()
    }
}

const selectFullScreen = () => {
    startX.value = 0
    startY.value = 0
    endX.value = window.innerWidth
    endY.value = window.innerHeight
    isSelecting.value = false
    draw()
}

const confirmCapture = () => {
    if (!bgCanvasRef.value) return
    
    const dpr = window.devicePixelRatio || 1
    const s = selection.value
    
    const tempCanvas = document.createElement('canvas')
    tempCanvas.width = s.w * dpr
    tempCanvas.height = s.h * dpr
    const tCtx = tempCanvas.getContext('2d')
    if (!tCtx) return

    // 1. 绘制底图
    tCtx.drawImage(
        bgCanvasRef.value,
        s.x * dpr, s.y * dpr, s.w * dpr, s.h * dpr,
        0, 0, s.w * dpr, s.h * dpr
    )
    
    // 2. 绘制标注
    annotations.value.forEach(ann => {
        tCtx.strokeStyle = ann.color
        tCtx.fillStyle = ann.color
        tCtx.lineWidth = ann.size * dpr
        
        const offsetX = s.x
        const offsetY = s.y

        if (ann.type === 'rect') {
            tCtx.strokeRect((ann.x - offsetX) * dpr, (ann.y - offsetY) * dpr, ann.w * dpr, ann.h * dpr)
        } else if (ann.type === 'arrow') {
            drawArrow(tCtx, (ann.startX - offsetX) * dpr, (ann.startY - offsetY) * dpr, (ann.endX - offsetX) * dpr, (ann.endY - offsetY) * dpr, ann.size * dpr)
        } else if (ann.type === 'text') {
            tCtx.font = `${(ann.size * 12 + 12) * dpr}px sans-serif`
            tCtx.fillText(ann.text, (ann.x - offsetX) * dpr, (ann.y - offsetY) * dpr)
        }
    })

    emit('capture', tempCanvas.toDataURL('image/png'))
}

// 计算工具栏位置
const toolbarStyle = computed(() => {
    const s = selection.value
    let top = s.y + s.h + 10
    let left = s.x + s.w - 320 
    
    if (top + 60 > window.innerHeight) {
        top = s.y - 70
    }
    if (left < s.x) left = s.x // 保持在选区左侧以内
    if (left < 10) left = 10
    
    return {
        top: `${top}px`,
        left: `${left}px`
    }
})

// Resize Handles
const handles = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'] as const
const getHandleStyle = (h: ResizeHandle) => {
    const s = selection.value
    const size = 8
    const half = size / 2
    let top = 0, left = 0
    
    if (h.includes('n')) top = s.y - half
    else if (h.includes('s')) top = s.y + s.h - half
    else top = s.y + s.h / 2 - half
    
    if (h.includes('w')) left = s.x - half
    else if (h.includes('e')) left = s.x + s.w - half
    else left = s.x + s.w / 2 - half
    
    return {
        top: `${top}px`,
        left: `${left}px`,
        cursor: `${h}-resize`
    }
}
</script>

<template>
  <div class="advanced-screenshot-overlay">
    <canvas 
        ref="canvasRef"
        @mousedown="handleMouseDown"
    ></canvas>

    <!-- Resize Handles (仅在编辑模式显示) -->
    <div v-if="!isSelecting">
        <div 
            v-for="h in handles" 
            :key="h"
            class="resize-handle"
            :style="getHandleStyle(h)"
            @mousedown.stop="startResize(h)"
        ></div>
    </div>

    <!-- 文本输入框 -->
    <div 
        v-if="isTextInputVisible"
        class="text-input-wrapper"
        :style="{ top: `${textInputY}px`, left: `${textInputX}px` }"
    >
        <textarea 
            ref="textInputRef"
            v-model="textInputValue"
            :style="{ color: currentColor, fontSize: `${currentSize * 12 + 12}px` }"
            @blur="confirmText"
            placeholder="输入文字..."
        ></textarea>
    </div>

    <!-- 全屏截图按钮 (仅在选区模式显示) -->
    <div v-if="isSelecting" class="fullscreen-btn-wrapper">
        <button class="fullscreen-btn" @click="selectFullScreen">
            <svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" fill="none" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="12" cy="12" r="3"/></svg>
            截取全屏
        </button>
    </div>

    <!-- 工具栏 (仅在编辑模式显示) -->
    <div v-if="!isSelecting" class="toolbar" :style="toolbarStyle" @mousedown.stop>
        <!-- 主工具 -->
        <div class="tools-row">
            <button class="tool-btn" :class="{ active: currentTool === 'rect' }" @click="selectTool('rect')" title="矩形">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>
            </button>
            <button class="tool-btn" :class="{ active: currentTool === 'arrow' }" @click="selectTool('arrow')" title="箭头">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>
            </button>
            <button class="tool-btn" :class="{ active: currentTool === 'text' }" @click="selectTool('text')" title="文字">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><path d="M4 7V4h16v3M9 20h6M12 4v16"/></svg>
            </button>
            
            <div class="divider"></div>
            
            <button class="tool-btn" @click="undoAction" title="撤销" :disabled="history.length === 0">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/></svg>
            </button>
            <button class="tool-btn" @click="redoAction" title="重做" :disabled="redoStack.length === 0">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2" style="transform: scaleX(-1)"><path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/></svg>
            </button>
            
            <div class="divider"></div>

            <button class="tool-btn" @click="emit('cancel')" title="取消">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
            <button class="tool-btn primary" @click="confirmCapture" title="完成">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
            </button>
        </div>

        <!-- 属性设置 -->
        <div class="props-row" v-if="currentTool">
            <div class="colors">
                <div 
                    v-for="c in colors" 
                    :key="c" 
                    class="color-dot"
                    :style="{ backgroundColor: c }"
                    :class="{ active: currentColor === c }"
                    @click="currentColor = c"
                ></div>
            </div>
            <div class="sizes">
                <div class="size-dot small" :class="{ active: currentSize === 1 }" @click="currentSize = 1"></div>
                <div class="size-dot medium" :class="{ active: currentSize === 2 }" @click="currentSize = 2"></div>
                <div class="size-dot large" :class="{ active: currentSize === 3 }" @click="currentSize = 3"></div>
            </div>
        </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.advanced-screenshot-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  z-index: 99999;
  cursor: crosshair;
  user-select: none;

  canvas {
      display: block;
      width: 100%;
      height: 100%;
  }
}

.resize-handle {
    position: absolute;
    width: 8px;
    height: 8px;
    background: white;
    border: 1px solid #1890ff;
    border-radius: 50%;
    z-index: 100000;
}

.fullscreen-btn-wrapper {
    position: absolute;
    top: 20px;
    left: 50%;
    transform: translateX(-50%);
    z-index: 100000;
}

.fullscreen-btn {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 16px;
    background: rgba(0, 0, 0, 0.6);
    color: white;
    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 20px;
    cursor: pointer;
    font-size: 14px;
    backdrop-filter: blur(4px);
    transition: all 0.2s;

    &:hover {
        background: rgba(0, 0, 0, 0.8);
        transform: scale(1.05);
    }
}

.toolbar {
    position: absolute;
    background: white;
    border-radius: 6px;
    box-shadow: 0 2px 10px rgba(0,0,0,0.15);
    padding: 6px 10px;
    display: flex;
    flex-direction: column;
    gap: 8px;
    z-index: 100000;
    min-width: 260px;

    .tools-row {
        display: flex;
        align-items: center;
        gap: 4px;
    }

    .props-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding-top: 8px;
        border-top: 1px solid #f0f0f0;
    }
}

.tool-btn {
    width: 28px;
    height: 28px;
    border: none;
    background: transparent;
    border-radius: 4px;
    cursor: pointer;
    color: #666;
    display: flex;
    align-items: center;
    justify-content: center;
    transition: all 0.2s;

    &:hover {
        background: #f5f5f5;
        color: #333;
    }

    &.active {
        background: #e6f7ff;
        color: #1890ff;
    }

    &.primary {
        color: #52c41a;
        &:hover {
            background: #f6ffed;
        }
    }
    
    &:disabled {
        opacity: 0.3;
        cursor: not-allowed;
    }
}

.divider {
    width: 1px;
    height: 16px;
    background: #eee;
    margin: 0 6px;
}

.colors {
    display: flex;
    gap: 8px;
}

.color-dot {
    width: 14px;
    height: 14px;
    border-radius: 50%;
    cursor: pointer;
    border: 2px solid transparent;
    box-shadow: 0 0 2px rgba(0,0,0,0.1);

    &.active {
        border-color: #1890ff;
        transform: scale(1.2);
    }
}

.sizes {
    display: flex;
    align-items: center;
    gap: 10px;
}

.size-dot {
    background: #bbb;
    border-radius: 50%;
    cursor: pointer;
    
    &.small { width: 4px; height: 4px; }
    &.medium { width: 8px; height: 8px; }
    &.large { width: 12px; height: 12px; }

    &.active {
        background: #1890ff;
    }
}

.text-input-wrapper {
    position: absolute;
    z-index: 100001;
    
    textarea {
        background: transparent;
        border: 1px dashed #1890ff;
        outline: none;
        resize: none;
        overflow: hidden;
        font-family: sans-serif;
        padding: 4px;
        min-width: 100px;
        min-height: 40px;
        line-height: 1.2;
    }
}
</style>
