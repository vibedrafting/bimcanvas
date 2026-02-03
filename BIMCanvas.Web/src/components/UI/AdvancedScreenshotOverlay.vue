<script setup lang="ts">
import { ref, onMounted, onUnmounted, computed, nextTick, watch } from 'vue'
import html2canvas from 'html2canvas'

const emit = defineEmits<{
  (e: 'capture', imageData: string): void
  (e: 'cancel'): void
}>()

// --- 类型定义 ---
type ToolType = 'rect' | 'arrow' | 'text' | null
type ResizeHandle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w'
type AnnotationResizeHandle = 'start' | 'end' | 'nw' | 'ne' | 'se' | 'sw'

interface BaseAnnotation {
  id: string
  color: string
  size: number
}

interface RectAnnotation extends BaseAnnotation {
  type: 'rect'
  x: number; y: number; w: number; h: number
}

interface ArrowAnnotation extends BaseAnnotation {
  type: 'arrow'
  startX: number; startY: number; endX: number; endY: number
}

interface TextAnnotation extends BaseAnnotation {
  type: 'text'
  x: number; y: number; text: string
}

type Annotation = RectAnnotation | ArrowAnnotation | TextAnnotation

// --- 状态管理 ---
const canvasRef = ref<HTMLCanvasElement | null>(null)
const bgCanvasRef = ref<HTMLCanvasElement | null>(null)

const isSelecting = ref(true)
const interactionMode = ref<'none' | 'selecting' | 'moving_selection' | 'resizing_selection' | 'drawing' | 'moving_annotation' | 'resizing_annotation' | 'dragging_text_input'>('none')

// 选区状态
const selX = ref(0), selY = ref(0), selW = ref(0), selH = ref(0)

// 交互临时坐标
const dragStartX = ref(0), dragStartY = ref(0), dragCurrentX = ref(0), dragCurrentY = ref(0)
const initialSelX = ref(0), initialSelY = ref(0), initialSelW = ref(0), initialSelH = ref(0)
const initialAnnotationState = ref<Annotation | null>(null)

// 工具状态
const currentTool = ref<ToolType>(null)
const currentColor = ref('#ff0000')
const currentSize = ref(2)
const annotations = ref<Annotation[]>([])
const selectedAnnotationIndex = ref<number>(-1)
const history = ref<Annotation[][]>([])
const redoStack = ref<Annotation[][]>([])

// 文本输入状态
const isTextInputVisible = ref(false)
const textInputX = ref(0), textInputY = ref(0)
const textInputValue = ref('')
const textInputRef = ref<HTMLInputElement | null>(null)
const editingAnnotationIndex = ref<number>(-1)
// 用于拖拽文本输入框
const textInputDragStartX = ref(0), textInputDragStartY = ref(0)

// 计算文本输入框宽度（基于实际文本像素宽度）
const textInputWidth = computed(() => {
    const fontSize = currentSize.value * 12 + 12
    const text = textInputValue.value || '输入'
    // 使用 Canvas 测量文本宽度
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')!
    ctx.font = `${fontSize}px sans-serif`
    const textWidth = ctx.measureText(text).width
    // 最小宽度为 5 个字符，加上内边距
    const minWidth = ctx.measureText('输入文字').width
    return Math.max(minWidth, textWidth) + 16 // +16 为 padding
})

// 颜色选项（8种）
const colors = ['#ff0000', '#ff6600', '#ffff00', '#00ff00', '#00ffff', '#0000ff', '#000000', '#ffffff']

// --- Cursor restore ---
const previousBodyCursor = ref('')
const setBodyCursor = (value: string) => {
    document.body.style.cursor = value
}
const restoreBodyCursor = () => {
    document.body.style.cursor = previousBodyCursor.value
}

// --- 隐藏/恢复 UI 元素 ---
const hiddenElements: { el: HTMLElement; display: string }[] = []

const hideUIElements = () => {
    // 需要隐藏的 CSS 选择器
    const selectors = [
        '.header-area',           // 顶栏（AppHeader + RibbonToolbar）
        '.island-container',      // 灵动岛
        '.floating-tools',        // 浮动工具（图层管理按钮）
        '.properties-area',       // 属性面板
        '.gallery-area',          // AI 对话面板
        '.ai-command-center',     // AI 面板(内层,直接隐藏)
        '.prompt-bar',            // 底部提示栏
        '.floating-input',        // 浮动输入框
    ]
    
    selectors.forEach(selector => {
        const el = document.querySelector(selector) as HTMLElement
        if (el) {
            hiddenElements.push({ el, display: el.style.display })
            el.style.display = 'none'
        }
    })
}

const restoreUIElements = () => {
    hiddenElements.forEach(({ el, display }) => {
        el.style.display = display
    })
    hiddenElements.length = 0
}

// --- 初始化 ---
onMounted(async () => {
  try {
    previousBodyCursor.value = document.body.style.cursor

    // 隐藏所有 UI 元素
    hideUIElements()
    
    // 等待 DOM 更新
    await nextTick()
    
    const canvas = await html2canvas(document.body, {
      backgroundColor: null,
      scale: Math.min(window.devicePixelRatio || 1, 2),
      logging: false,
      useCORS: true,
      allowTaint: true,
      foreignObjectRendering: false,
      removeContainer: true,
      ignoreElements: (el) => {
        return el.classList?.contains('advanced-screenshot-overlay')
      }
    })
    
    bgCanvasRef.value = canvas
    
    if (canvasRef.value) {
      const dpr = window.devicePixelRatio || 1
      canvasRef.value.width = window.innerWidth * dpr
      canvasRef.value.height = window.innerHeight * dpr
      canvasRef.value.style.width = '100vw'
      canvasRef.value.style.height = '100vh'
      draw()
    }
    
    // 全局事件监听
    window.addEventListener('keydown', handleKeyDown)
    window.addEventListener('mousemove', handleGlobalMouseMove)
    window.addEventListener('mouseup', handleGlobalMouseUp)
  } catch (error) {
    console.error('[Screenshot] Init failed:', error)
    restoreUIElements()
    restoreBodyCursor()
    emit('cancel')
  }
})

onUnmounted(() => {
  // 恢复 UI 元素
  restoreUIElements()
  restoreBodyCursor()

  // 移除全局事件监听
  window.removeEventListener('keydown', handleKeyDown)
  window.removeEventListener('mousemove', handleGlobalMouseMove)
  window.removeEventListener('mouseup', handleGlobalMouseUp)
})

// --- 辅助函数 ---
const generateId = () => Math.random().toString(36).substr(2, 9)

const isPointInRect = (x: number, y: number, rx: number, ry: number, rw: number, rh: number) => 
    x >= rx && x <= rx + rw && y >= ry && y <= ry + rh

const isPointNearLine = (px: number, py: number, x1: number, y1: number, x2: number, y2: number, threshold = 8) => {
    const A = px - x1, B = py - y1, C = x2 - x1, D = y2 - y1
    const dot = A * C + B * D, lenSq = C * C + D * D
    let param = lenSq !== 0 ? dot / lenSq : -1
    let xx, yy
    if (param < 0) { xx = x1; yy = y1 }
    else if (param > 1) { xx = x2; yy = y2 }
    else { xx = x1 + param * C; yy = y1 + param * D }
    return Math.sqrt((px - xx) ** 2 + (py - yy) ** 2) < threshold
}

const getHitAnnotation = (x: number, y: number): number => {
    for (let i = annotations.value.length - 1; i >= 0; i--) {
        const ann = annotations.value[i]
        if (ann.type === 'rect') {
            if (isPointInRect(x, y, ann.x - 5, ann.y - 5, ann.w + 10, ann.h + 10)) return i
        } else if (ann.type === 'arrow') {
            if (isPointNearLine(x, y, ann.startX, ann.startY, ann.endX, ann.endY, 10)) return i
        } else if (ann.type === 'text') {
            const ctx = canvasRef.value?.getContext('2d')
            if (ctx) {
                ctx.font = `${ann.size * 12 + 12}px sans-serif`
                const metrics = ctx.measureText(ann.text)
                const h = ann.size * 12 + 12
                if (x >= ann.x && x <= ann.x + metrics.width && y >= ann.y - h && y <= ann.y + 5) return i
            }
        }
    }
    return -1
}

// --- 绘图逻辑 ---
const draw = () => {
  const ctx = canvasRef.value?.getContext('2d')
  if (!ctx || !bgCanvasRef.value) return

  const dpr = window.devicePixelRatio || 1
  const width = canvasRef.value!.width, height = canvasRef.value!.height

  ctx.clearRect(0, 0, width, height)
  ctx.drawImage(bgCanvasRef.value, 0, 0, width, height)

  // 遮罩
  ctx.fillStyle = 'rgba(0, 0, 0, 0.5)'
  ctx.fillRect(0, 0, width, height)

  // 选区
  let sx = selX.value, sy = selY.value, sw = selW.value, sh = selH.value
  if (interactionMode.value === 'selecting') {
      sx = Math.min(dragStartX.value, dragCurrentX.value)
      sy = Math.min(dragStartY.value, dragCurrentY.value)
      sw = Math.abs(dragCurrentX.value - dragStartX.value)
      sh = Math.abs(dragCurrentY.value - dragStartY.value)
  }

  if (sw > 0 && sh > 0) {
      ctx.clearRect(sx * dpr, sy * dpr, sw * dpr, sh * dpr)
      ctx.drawImage(bgCanvasRef.value, sx * dpr, sy * dpr, sw * dpr, sh * dpr, sx * dpr, sy * dpr, sw * dpr, sh * dpr)
      ctx.strokeStyle = '#07c160'
      ctx.lineWidth = 1 * dpr
      ctx.strokeRect(sx * dpr, sy * dpr, sw * dpr, sh * dpr)
      
      if (isSelecting.value || interactionMode.value === 'resizing_selection') {
          ctx.fillStyle = 'rgba(0, 0, 0, 0.7)'
          ctx.fillRect(sx * dpr, (sy - 25) * dpr, 100 * dpr, 20 * dpr)
          ctx.fillStyle = '#fff'
          ctx.font = `${12 * dpr}px Arial`
          ctx.fillText(`${Math.round(sw)} x ${Math.round(sh)}`, (sx + 5) * dpr, (sy - 10) * dpr)
      }
  }

  // 标注
  ctx.save()
  if (sw > 0) {
      ctx.beginPath()
      ctx.rect(sx * dpr, sy * dpr, sw * dpr, sh * dpr)
      ctx.clip()
  }

  annotations.value.forEach((ann, index) => {
      if (ann.type === 'text' && editingAnnotationIndex.value === index) return
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

  // 正在绘制的标注
  if (interactionMode.value === 'drawing' && currentTool.value) {
      ctx.strokeStyle = currentColor.value
      ctx.fillStyle = currentColor.value
      ctx.lineWidth = currentSize.value * dpr
      
      if (currentTool.value === 'rect') {
          const w = dragCurrentX.value - dragStartX.value
          const h = dragCurrentY.value - dragStartY.value
          ctx.strokeRect(dragStartX.value * dpr, dragStartY.value * dpr, w * dpr, h * dpr)
      } else if (currentTool.value === 'arrow') {
          drawArrow(ctx, dragStartX.value * dpr, dragStartY.value * dpr, dragCurrentX.value * dpr, dragCurrentY.value * dpr, currentSize.value * dpr)
      }
  }


  // 选中标注的控制点（仅显示控制点，不显示虚线框）
  if (selectedAnnotationIndex.value !== -1 && !isSelecting.value) {
      const ann = annotations.value[selectedAnnotationIndex.value]
      if (ann) drawAnnotationHandles(ctx, ann, dpr)
  }


  ctx.restore()
}

const drawArrow = (ctx: CanvasRenderingContext2D, fromX: number, fromY: number, toX: number, toY: number, lineWidth: number) => {
    const dx = toX - fromX, dy = toY - fromY
    const angle = Math.atan2(dy, dx)
    const headLength = Math.max(lineWidth * 6, 16)
    const headWidth = headLength * 0.6
    const baseX = toX - headLength * Math.cos(angle)
    const baseY = toY - headLength * Math.sin(angle)
    
    ctx.lineWidth = lineWidth
    ctx.lineCap = 'round'
    ctx.beginPath()
    ctx.moveTo(fromX, fromY)
    ctx.lineTo(baseX, baseY)
    ctx.stroke()
    
    const perpAngle = angle + Math.PI / 2
    const leftX = baseX + headWidth / 2 * Math.cos(perpAngle)
    const leftY = baseY + headWidth / 2 * Math.sin(perpAngle)
    const rightX = baseX - headWidth / 2 * Math.cos(perpAngle)
    const rightY = baseY - headWidth / 2 * Math.sin(perpAngle)
    
    ctx.beginPath()
    ctx.moveTo(toX, toY)
    ctx.lineTo(leftX, leftY)
    ctx.lineTo(rightX, rightY)
    ctx.closePath()
    ctx.fill()
}

const drawAnnotationHandles = (ctx: CanvasRenderingContext2D, ann: Annotation, dpr: number) => {
    ctx.fillStyle = '#fff'
    ctx.strokeStyle = '#07c160'
    ctx.lineWidth = 1 * dpr
    
    const drawHandle = (x: number, y: number) => {
        ctx.beginPath()
        ctx.arc(x * dpr, y * dpr, 4 * dpr, 0, Math.PI * 2)
        ctx.fill()
        ctx.stroke()
    }

    if (ann.type === 'rect') {
        // 只绘制四个角的控制点，不绘制边框
        drawHandle(ann.x, ann.y)
        drawHandle(ann.x + ann.w, ann.y)
        drawHandle(ann.x + ann.w, ann.y + ann.h)
        drawHandle(ann.x, ann.y + ann.h)
    } else if (ann.type === 'arrow') {
        drawHandle(ann.startX, ann.startY)
        drawHandle(ann.endX, ann.endY)
    }
    // 文字标注不绘制控制点和虚线框
}

// --- 交互处理 ---
const handleMouseDown = (e: MouseEvent) => {
    if ((e.target as HTMLElement).tagName === 'INPUT') return
    
    const x = e.clientX, y = e.clientY
    
    // 如果文本输入可见，点击外部确认文本
    if (isTextInputVisible.value) {
        confirmText()
        return
    }

    if (isSelecting.value) {
        interactionMode.value = 'selecting'
        dragStartX.value = x; dragStartY.value = y
        dragCurrentX.value = x; dragCurrentY.value = y
        return
    }

    if (interactionMode.value === 'resizing_selection') return
    if (!isPointInRect(x, y, selX.value, selY.value, selW.value, selH.value)) return

    // 检查标注控制点
    if (selectedAnnotationIndex.value !== -1) {
        const ann = annotations.value[selectedAnnotationIndex.value]
        const handle = getAnnotationResizeHandle(x, y, ann)
        if (handle) {
            interactionMode.value = 'resizing_annotation'
            currentAnnotationResizeHandle.value = handle
            saveHistory()
            return
        }
    }

    // 绘制新标注
    if (currentTool.value) {
        if (currentTool.value === 'text') {
            startTextInput(x, y)
        } else {
            interactionMode.value = 'drawing'
            dragStartX.value = x; dragStartY.value = y
            dragCurrentX.value = x; dragCurrentY.value = y
            selectedAnnotationIndex.value = -1
        }
        return
    }

    // 选中/移动标注 (优先级高于移动选区)
    const hitIndex = getHitAnnotation(x, y)
    if (hitIndex !== -1) {
        const ann = annotations.value[hitIndex]
        // 如果已选中同一个文字标注，进入编辑模式
        if (ann.type === 'text' && selectedAnnotationIndex.value === hitIndex) {
            startTextInput(ann.x, ann.y - 10, hitIndex)
            return
        }
        
        selectedAnnotationIndex.value = hitIndex
        interactionMode.value = 'moving_annotation'
        dragStartX.value = x; dragStartY.value = y
        initialAnnotationState.value = JSON.parse(JSON.stringify(ann))
        saveHistory()
        currentColor.value = ann.color
        currentSize.value = ann.size
        draw()
        return
    }

    // 点击空白处，取消选中并移动选区
    selectedAnnotationIndex.value = -1
    interactionMode.value = 'moving_selection'
    dragStartX.value = x; dragStartY.value = y
    initialSelX.value = selX.value; initialSelY.value = selY.value
    draw()
}

const handleGlobalMouseMove = (e: MouseEvent) => {
    const x = e.clientX, y = e.clientY
    dragCurrentX.value = x; dragCurrentY.value = y

    if (interactionMode.value === 'none') {
        updateCursor(x, y)
        return
    }

    if (interactionMode.value === 'selecting') {
        draw()
    } else if (interactionMode.value === 'moving_selection') {
        selX.value = initialSelX.value + (x - dragStartX.value)
        selY.value = initialSelY.value + (y - dragStartY.value)
        draw()
    } else if (interactionMode.value === 'resizing_selection') {
        handleSelectionResize(x, y)
        draw()
    } else if (interactionMode.value === 'drawing') {
        draw()
    } else if (interactionMode.value === 'moving_annotation') {
        if (selectedAnnotationIndex.value !== -1 && initialAnnotationState.value) {
            const dx = x - dragStartX.value, dy = y - dragStartY.value
            const ann = annotations.value[selectedAnnotationIndex.value]
            const init = initialAnnotationState.value
            
            if (ann.type === 'rect' && init.type === 'rect') {
                ann.x = init.x + dx; ann.y = init.y + dy
            } else if (ann.type === 'arrow' && init.type === 'arrow') {
                ann.startX = init.startX + dx; ann.startY = init.startY + dy
                ann.endX = init.endX + dx; ann.endY = init.endY + dy
            } else if (ann.type === 'text' && init.type === 'text') {
                ann.x = init.x + dx; ann.y = init.y + dy
            }
            draw()
        }
    } else if (interactionMode.value === 'resizing_annotation') {
        handleAnnotationResize(x, y)
        draw()
    } else if (interactionMode.value === 'dragging_text_input') {
        textInputX.value = textInputDragStartX.value + (x - dragStartX.value)
        textInputY.value = textInputDragStartY.value + (y - dragStartY.value)
        draw() // 更新预览框位置
    }
}

const handleGlobalMouseUp = () => {
    if (interactionMode.value === 'selecting') {
        const sx = Math.min(dragStartX.value, dragCurrentX.value)
        const sy = Math.min(dragStartY.value, dragCurrentY.value)
        const sw = Math.abs(dragCurrentX.value - dragStartX.value)
        const sh = Math.abs(dragCurrentY.value - dragStartY.value)
        
        if (sw > 10 && sh > 10) {
            selX.value = sx; selY.value = sy; selW.value = sw; selH.value = sh
            isSelecting.value = false
        }
    } else if (interactionMode.value === 'drawing') {
        saveHistory()
        if (currentTool.value === 'rect') {
            annotations.value.push({
                id: generateId(), type: 'rect',
                x: Math.min(dragStartX.value, dragCurrentX.value),
                y: Math.min(dragStartY.value, dragCurrentY.value),
                w: Math.abs(dragCurrentX.value - dragStartX.value),
                h: Math.abs(dragCurrentY.value - dragStartY.value),
                color: currentColor.value, size: currentSize.value
            })
        } else if (currentTool.value === 'arrow') {
            annotations.value.push({
                id: generateId(), type: 'arrow',
                startX: dragStartX.value, startY: dragStartY.value,
                endX: dragCurrentX.value, endY: dragCurrentY.value,
                color: currentColor.value, size: currentSize.value
            })
        }
        selectedAnnotationIndex.value = annotations.value.length - 1
        currentTool.value = null
    }

    if (interactionMode.value !== 'dragging_text_input') {
        interactionMode.value = 'none'
    } else {
        interactionMode.value = 'none'
    }
    currentResizeHandle.value = null
    currentAnnotationResizeHandle.value = null
    draw()
}

// --- 选区调整 ---
const currentResizeHandle = ref<ResizeHandle | null>(null)
const startSelectionResize = (handle: ResizeHandle) => {
    interactionMode.value = 'resizing_selection'
    currentResizeHandle.value = handle
    initialSelX.value = selX.value; initialSelY.value = selY.value
    initialSelW.value = selW.value; initialSelH.value = selH.value
}

const handleSelectionResize = (x: number, y: number) => {
    if (!currentResizeHandle.value) return
    const h = currentResizeHandle.value
    let sx = initialSelX.value, sy = initialSelY.value
    let ex = initialSelX.value + initialSelW.value, ey = initialSelY.value + initialSelH.value

    if (h.includes('w')) sx = x
    if (h.includes('e')) ex = x
    if (h.includes('n')) sy = y
    if (h.includes('s')) ey = y

    selX.value = Math.min(sx, ex); selY.value = Math.min(sy, ey)
    selW.value = Math.abs(ex - sx); selH.value = Math.abs(ey - sy)
}

// --- 标注调整 ---
const currentAnnotationResizeHandle = ref<AnnotationResizeHandle | null>(null)
const getAnnotationResizeHandle = (x: number, y: number, ann: Annotation): AnnotationResizeHandle | null => {
    const dist = (x1: number, y1: number) => Math.sqrt((x - x1) ** 2 + (y - y1) ** 2)
    const threshold = 8

    if (ann.type === 'rect') {
        if (dist(ann.x, ann.y) < threshold) return 'nw'
        if (dist(ann.x + ann.w, ann.y) < threshold) return 'ne'
        if (dist(ann.x + ann.w, ann.y + ann.h) < threshold) return 'se'
        if (dist(ann.x, ann.y + ann.h) < threshold) return 'sw'
    } else if (ann.type === 'arrow') {
        if (dist(ann.startX, ann.startY) < threshold) return 'start'
        if (dist(ann.endX, ann.endY) < threshold) return 'end'
    }
    return null
}

const handleAnnotationResize = (x: number, y: number) => {
    if (selectedAnnotationIndex.value === -1 || !currentAnnotationResizeHandle.value) return
    const ann = annotations.value[selectedAnnotationIndex.value]
    const h = currentAnnotationResizeHandle.value

    if (ann.type === 'rect') {
        if (h === 'nw') { ann.w += ann.x - x; ann.h += ann.y - y; ann.x = x; ann.y = y }
        else if (h === 'ne') { ann.w = x - ann.x; ann.h += ann.y - y; ann.y = y }
        else if (h === 'se') { ann.w = x - ann.x; ann.h = y - ann.y }
        else if (h === 'sw') { ann.w += ann.x - x; ann.h = y - ann.y; ann.x = x }
    } else if (ann.type === 'arrow') {
        if (h === 'start') { ann.startX = x; ann.startY = y }
        else if (h === 'end') { ann.endX = x; ann.endY = y }
    }
}

// --- 文本工具 ---
// 文字基线偏移量计算
const getTextBaseline = (size: number) => size * 12 + 12
// 输入框内边距（与 CSS padding 保持一致）
const TEXT_INPUT_PADDING_X = 4
const TEXT_INPUT_PADDING_Y = 2

const startTextInput = (x: number, y: number, existingIndex = -1) => {
    isTextInputVisible.value = true
    
    if (existingIndex !== -1) {
        const ann = annotations.value[existingIndex] as TextAnnotation
        // 编辑已有：考虑输入框内边距
        textInputX.value = ann.x - TEXT_INPUT_PADDING_X
        textInputY.value = ann.y - getTextBaseline(ann.size) - TEXT_INPUT_PADDING_Y
        textInputValue.value = ann.text
        currentColor.value = ann.color
        currentSize.value = ann.size
        editingAnnotationIndex.value = existingIndex
        selectedAnnotationIndex.value = existingIndex
    } else {
        saveHistory()
        // 新建：点击位置就是文字左上角，输入框需要减去 padding
        textInputX.value = x - TEXT_INPUT_PADDING_X
        textInputY.value = y - TEXT_INPUT_PADDING_Y
        textInputValue.value = ''
        editingAnnotationIndex.value = -1
    }

    setTimeout(() => textInputRef.value?.focus(), 50)
}

const confirmText = () => {
    if (!isTextInputVisible.value) return
    
    const fontSize = getTextBaseline(currentSize.value)
    // 文字实际位置 = 输入框位置 + padding
    const textX = textInputX.value + TEXT_INPUT_PADDING_X
    const textY = textInputY.value + TEXT_INPUT_PADDING_Y + fontSize
    
    if (textInputValue.value.trim()) {
        if (editingAnnotationIndex.value !== -1) {
            const ann = annotations.value[editingAnnotationIndex.value] as TextAnnotation
            ann.text = textInputValue.value
            ann.x = textX
            ann.y = textY
            ann.color = currentColor.value
            ann.size = currentSize.value
        } else {
            annotations.value.push({
                id: generateId(), type: 'text',
                x: textX,
                y: textY,
                text: textInputValue.value,
                color: currentColor.value, size: currentSize.value
            })
            selectedAnnotationIndex.value = annotations.value.length - 1
        }
    } else if (editingAnnotationIndex.value !== -1) {
        annotations.value.splice(editingAnnotationIndex.value, 1)
        selectedAnnotationIndex.value = -1
    }

    isTextInputVisible.value = false
    editingAnnotationIndex.value = -1
    currentTool.value = null
    draw()
}

const startDragTextInput = (e: MouseEvent) => {
    e.preventDefault()
    interactionMode.value = 'dragging_text_input'
    dragStartX.value = e.clientX
    dragStartY.value = e.clientY
    textInputDragStartX.value = textInputX.value
    textInputDragStartY.value = textInputY.value
}

// --- 历史记录 ---
const saveHistory = () => {
    history.value.push(JSON.parse(JSON.stringify(annotations.value)))
    redoStack.value = []
}

const undoAction = () => {
    if (history.value.length > 0) {
        redoStack.value.push(JSON.parse(JSON.stringify(annotations.value)))
        annotations.value = history.value.pop() || []
        selectedAnnotationIndex.value = -1
        draw()
    }
}

const redoAction = () => {
    if (redoStack.value.length > 0) {
        history.value.push(JSON.parse(JSON.stringify(annotations.value)))
        annotations.value = redoStack.value.pop() || []
        selectedAnnotationIndex.value = -1
        draw()
    }
}

// --- 键盘 ---
const handleKeyDown = (e: KeyboardEvent) => {
    if (isTextInputVisible.value) {
        if (e.key === 'Enter') {
            e.preventDefault()
            confirmText()
        } else if (e.key === 'Escape') {
            isTextInputVisible.value = false
            editingAnnotationIndex.value = -1
            currentTool.value = null
        }
        return
    }

    if (e.key === 'Escape') {
        if (currentTool.value) currentTool.value = null
        else if (selectedAnnotationIndex.value !== -1) selectedAnnotationIndex.value = -1
        else emit('cancel')
        draw()
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
        if (selectedAnnotationIndex.value !== -1) {
            saveHistory()
            annotations.value.splice(selectedAnnotationIndex.value, 1)
            selectedAnnotationIndex.value = -1
            draw()
        }
    } else if ((e.ctrlKey || e.metaKey) && e.key === 'z') {
        if (e.shiftKey) redoAction()
        else undoAction()
    }
}

watch([currentColor, currentSize], ([newColor, newSize]) => {
    if (selectedAnnotationIndex.value !== -1 && !isTextInputVisible.value) {
        const ann = annotations.value[selectedAnnotationIndex.value]
        ann.color = newColor; ann.size = newSize
        draw()
    }
})

// 获取鼠标悬停在选区边缘的 resize 方向
const getSelectionEdgeHandle = (x: number, y: number): ResizeHandle | null => {
    const threshold = 8
    const sx = selX.value, sy = selY.value, sw = selW.value, sh = selH.value
    
    const nearLeft = Math.abs(x - sx) < threshold
    const nearRight = Math.abs(x - (sx + sw)) < threshold
    const nearTop = Math.abs(y - sy) < threshold
    const nearBottom = Math.abs(y - (sy + sh)) < threshold
    const inXRange = x >= sx - threshold && x <= sx + sw + threshold
    const inYRange = y >= sy - threshold && y <= sy + sh + threshold
    
    if (nearTop && nearLeft && inXRange && inYRange) return 'nw'
    if (nearTop && nearRight && inXRange && inYRange) return 'ne'
    if (nearBottom && nearLeft && inXRange && inYRange) return 'sw'
    if (nearBottom && nearRight && inXRange && inYRange) return 'se'
    if (nearTop && inXRange) return 'n'
    if (nearBottom && inXRange) return 's'
    if (nearLeft && inYRange) return 'w'
    if (nearRight && inYRange) return 'e'
    
    return null
}

const updateCursor = (x: number, y: number) => {
    // 选区阶段：十字光标
    if (isSelecting.value) {
        setBodyCursor('crosshair')
        return
    }
    
    // 检查是否在选区边缘 (resize)
    const edgeHandle = getSelectionEdgeHandle(x, y)
    if (edgeHandle) {
        const cursorMap: Record<ResizeHandle, string> = {
            'nw': 'nwse-resize', 'se': 'nwse-resize',
            'ne': 'nesw-resize', 'sw': 'nesw-resize',
            'n': 'ns-resize', 's': 'ns-resize',
            'e': 'ew-resize', 'w': 'ew-resize'
        }
        setBodyCursor(cursorMap[edgeHandle])
        return
    }
    
    // 检查是否在选区外 (禁止)
    if (!isPointInRect(x, y, selX.value, selY.value, selW.value, selH.value)) {
        setBodyCursor('not-allowed')
        return
    }
    
    // 在选区内
    
    // 1. 如果选中了工具，显示十字
    if (currentTool.value) {
        setBodyCursor('crosshair')
        return
    }
    
    // 2. 检查是否在已选中标注的 resize 手柄上
    if (selectedAnnotationIndex.value !== -1) {
        const ann = annotations.value[selectedAnnotationIndex.value]
        const handle = getAnnotationResizeHandle(x, y, ann)
        if (handle) {
            if (ann.type === 'rect') {
                const cursorMap: Record<string, string> = {
                    'nw': 'nwse-resize', 'se': 'nwse-resize',
                    'ne': 'nesw-resize', 'sw': 'nesw-resize'
                }
                setBodyCursor(cursorMap[handle] || 'pointer')
            } else {
                setBodyCursor('pointer')
            }
            return
        }
    }
    
    // 3. 检查是否悬停在某个标注上 (移动光标)
    const hitIndex = getHitAnnotation(x, y)
    if (hitIndex !== -1) {
        setBodyCursor('move')
        return
    }
    
    // 4. 在选区内空白区域 (移动整个选区)
    setBodyCursor('move')
}

const selectTool = (tool: ToolType) => {
    currentTool.value = tool
    selectedAnnotationIndex.value = -1
    draw()
}

const selectFullScreen = () => {
    selX.value = 0; selY.value = 0
    selW.value = window.innerWidth; selH.value = window.innerHeight
    confirmCapture()
}

const confirmCapture = () => {
    // 先提交未确认的文字标注
    if (isTextInputVisible.value) {
        confirmText()
    }

    if (!bgCanvasRef.value) return
    const dpr = window.devicePixelRatio || 1
    const tempCanvas = document.createElement('canvas')
    tempCanvas.width = selW.value * dpr
    tempCanvas.height = selH.value * dpr
    const tCtx = tempCanvas.getContext('2d')
    if (!tCtx) return

    tCtx.drawImage(bgCanvasRef.value, selX.value * dpr, selY.value * dpr, selW.value * dpr, selH.value * dpr, 0, 0, selW.value * dpr, selH.value * dpr)
    
    annotations.value.forEach(ann => {
        tCtx.strokeStyle = ann.color; tCtx.fillStyle = ann.color; tCtx.lineWidth = ann.size * dpr
        if (ann.type === 'rect') {
            tCtx.strokeRect((ann.x - selX.value) * dpr, (ann.y - selY.value) * dpr, ann.w * dpr, ann.h * dpr)
        } else if (ann.type === 'arrow') {
            drawArrow(tCtx, (ann.startX - selX.value) * dpr, (ann.startY - selY.value) * dpr, (ann.endX - selX.value) * dpr, (ann.endY - selY.value) * dpr, ann.size * dpr)
        } else if (ann.type === 'text') {
            tCtx.font = `${(ann.size * 12 + 12) * dpr}px sans-serif`
            tCtx.fillText(ann.text, (ann.x - selX.value) * dpr, (ann.y - selY.value) * dpr)
        }
    })
    restoreBodyCursor()
    emit('capture', tempCanvas.toDataURL('image/png'))
}

const toolbarStyle = computed(() => {
    let top = selY.value + selH.value + 10
    let left = selX.value + selW.value - 320 
    if (top + 60 > window.innerHeight) top = selY.value - 70
    if (left < selX.value) left = selX.value
    if (left < 10) left = 10
    return { top: `${top}px`, left: `${left}px` }
})

const handles = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w'] as const
const getHandleStyle = (h: ResizeHandle) => {
    const size = 8, half = size / 2
    let top = 0, left = 0
    if (h.includes('n')) top = selY.value - half
    else if (h.includes('s')) top = selY.value + selH.value - half
    else top = selY.value + selH.value / 2 - half
    if (h.includes('w')) left = selX.value - half
    else if (h.includes('e')) left = selX.value + selW.value - half
    else left = selX.value + selW.value / 2 - half
    return { top: `${top}px`, left: `${left}px`, cursor: `${h}-resize` }
}
</script>

<template>
  <div class="advanced-screenshot-overlay">
    <canvas ref="canvasRef" @mousedown="handleMouseDown"></canvas>

    <!-- 选区 Resize Handles -->
    <template v-if="!isSelecting">
        <div v-for="h in handles" :key="h" class="resize-handle" :style="getHandleStyle(h)" @mousedown.stop.prevent="startSelectionResize(h)"></div>
    </template>

    <!-- 文本输入框 (单行紧凑样式，可拖拽) -->
    <div v-if="isTextInputVisible" class="text-input-wrapper" :style="{ top: `${textInputY}px`, left: `${textInputX}px` }" @mousedown.stop>
        <div class="text-input-drag-handle" @mousedown="startDragTextInput">⋮⋮</div>
        <input 
            ref="textInputRef"
            type="text"
            v-model="textInputValue"
            :style="{ color: currentColor, fontSize: `${currentSize * 12 + 12}px`, width: `${textInputWidth}px` }"
            placeholder="输入"
            @keydown.enter.prevent="confirmText"
            @keydown.esc.prevent="isTextInputVisible = false"
        />
    </div>

    <!-- 全屏截图按钮 -->
    <div v-if="isSelecting" class="fullscreen-btn-wrapper">
        <button class="fullscreen-btn" @click.stop="selectFullScreen">
            <svg viewBox="0 0 24 24" width="20" height="20" stroke="currentColor" fill="none" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="12" cy="12" r="3"/></svg>
            截取全屏
        </button>
    </div>

    <!-- 工具栏 -->
    <div v-if="!isSelecting" class="toolbar" :style="toolbarStyle" @mousedown.stop>
        <div class="tools-row">
            <button class="tool-btn" :class="{ active: currentTool === 'rect' }" @click.stop="selectTool('rect')" title="矩形">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/></svg>
            </button>
            <button class="tool-btn" :class="{ active: currentTool === 'arrow' }" @click.stop="selectTool('arrow')" title="箭头">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/></svg>
            </button>
            <button class="tool-btn" :class="{ active: currentTool === 'text' }" @click.stop="selectTool('text')" title="文字">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><path d="M4 7V4h16v3M9 20h6M12 4v16"/></svg>
            </button>
            
            <div class="divider"></div>
            
            <button class="tool-btn" @click.stop="undoAction" title="撤销" :disabled="history.length === 0">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/></svg>
            </button>
            <button class="tool-btn" @click.stop="redoAction" title="重做" :disabled="redoStack.length === 0">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2" style="transform: scaleX(-1)"><path d="M3 7v6h6"/><path d="M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13"/></svg>
            </button>
            
            <div class="divider"></div>

            <button class="tool-btn" @click.stop="emit('cancel')" title="取消">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
            </button>
            <button class="tool-btn primary" @click.stop="confirmCapture" title="完成">
                <svg viewBox="0 0 24 24" width="18" height="18" stroke="currentColor" fill="none" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>
            </button>
        </div>

        <div class="props-row" v-if="currentTool || selectedAnnotationIndex !== -1">
            <div class="colors">
                <div v-for="c in colors" :key="c" class="color-dot" :style="{ backgroundColor: c }" :class="{ active: currentColor === c }" @click.stop="currentColor = c"></div>
            </div>
            <div class="sizes">
                <div class="size-dot-wrapper" @click.stop="currentSize = 1" title="细"><div class="size-dot xs" :class="{ active: currentSize === 1 }"></div></div>
                <div class="size-dot-wrapper" @click.stop="currentSize = 2" title="中"><div class="size-dot sm" :class="{ active: currentSize === 2 }"></div></div>
                <div class="size-dot-wrapper" @click.stop="currentSize = 3" title="粗"><div class="size-dot md" :class="{ active: currentSize === 3 }"></div></div>
                <div class="size-dot-wrapper" @click.stop="currentSize = 4" title="特粗"><div class="size-dot lg" :class="{ active: currentSize === 4 }"></div></div>
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
    background: #07c160;
    border: 1px solid white;
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

    .tools-row { display: flex; align-items: center; gap: 4px; }
    .props-row { display: flex; align-items: center; justify-content: space-between; padding-top: 8px; border-top: 1px solid #f0f0f0; }
}

.tool-btn {
    width: 28px; height: 28px;
    border: none; background: transparent;
    border-radius: 4px; cursor: pointer;
    color: #666; display: flex; align-items: center; justify-content: center;
    transition: all 0.2s;

    &:hover { background: #f5f5f5; color: #333; }
    &.active { background: #e6f7ff; color: #1890ff; }
    &.primary { color: #52c41a; &:hover { background: #f6ffed; } }
    &:disabled { opacity: 0.3; cursor: not-allowed; }
}

.divider { width: 1px; height: 16px; background: #eee; margin: 0 6px; }

.colors { display: flex; gap: 8px; }

.color-dot {
    width: 14px; height: 14px; border-radius: 50%; cursor: pointer;
    border: 2px solid transparent; box-shadow: 0 0 2px rgba(0,0,0,0.1);
    &.active { border-color: #1890ff; transform: scale(1.2); }
}

.sizes { display: flex; align-items: center; gap: 2px; }

.size-dot-wrapper {
    width: 24px; height: 24px; display: flex; align-items: center; justify-content: center;
    cursor: pointer; border-radius: 4px;
    &:hover { background: #f5f5f5; }
}

.size-dot {
    background: #bbb; border-radius: 50%;
    &.xs { width: 3px; height: 3px; }
    &.sm { width: 6px; height: 6px; }
    &.md { width: 10px; height: 10px; }
    &.lg { width: 14px; height: 14px; }
    &.active { background: #1890ff; }
}

.text-input-wrapper {
    position: absolute;
    z-index: 100001;
    display: flex;
    align-items: center;
    gap: 4px;
    background: rgba(255,255,255,0.95);
    border: 1px solid #07c160;
    border-radius: 4px;
    padding: 2px 4px;
    box-shadow: 0 2px 8px rgba(0,0,0,0.15);
    
    .text-input-drag-handle {
        cursor: move;
        color: #999;
        font-size: 12px;
        padding: 0 4px;
        user-select: none;
        
        &:hover { color: #666; }
    }
    
    input {
        background: transparent;
        border: none;
        outline: none;
        font-family: sans-serif;
        min-width: 40px;
        width: auto;
        padding: 2px 4px;
        line-height: 1.2;
    }
}
</style>
