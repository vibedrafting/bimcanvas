/**
 * 截图服务
 *
 * 提供两种能力：
 * 1. 主动截图：用户通过附件菜单截取画布/房间
 * 2. 响应截图请求：监听 Agent 的截图请求（通过 SSE）
 *
 * Labels 渲染策略：
 * - 不再依赖 html2canvas（对 writing-mode: vertical-rl 支持不可靠）
 * - 使用 LabelRenderer 手动绘制 + 投影计算
 */
import html2canvas from 'html2canvas'
import { LabelRenderer } from './screenshot/LabelRenderer'
import { getThreeSceneService } from './three/ThreeSceneService'
import { LayerManager } from './three/LayerManager'

export interface ClipRect {
  x: number
  y: number
  width: number
  height: number
}

export class ScreenshotService {
  private serverUrl: string
  private eventSource: EventSource | null = null

  constructor(serverUrl: string = 'http://localhost:8865') {
    this.serverUrl = serverUrl
  }

  /**
   * 截取整个画布（合成 WebGL + 手动绘制标签层）
   * @returns Base64 编码的图片数据
   *
   * 渲染策略：
   * 1. 直接获取 WebGL Canvas 内容（底层）
   * 2. 使用 LabelRenderer 手动绘制标签（上层）
   *    - 通过投影计算获取屏幕坐标
   *    - Canvas 2D API 绘制文字（含旋转）
   */
  async captureCanvas(clipRect?: ClipRect): Promise<string> {
    // 1. 获取 WebGL canvas
    const glCanvas = document.querySelector('.three-canvas canvas') as HTMLCanvasElement
    if (!glCanvas) {
      throw new Error('WebGL canvas not found. Please ensure the canvas is loaded.')
    }

    const scale = window.devicePixelRatio || 1

    const sourceRect = (() => {
      if (!clipRect) {
        return { x: 0, y: 0, width: glCanvas.width, height: glCanvas.height }
      }

      const x = Math.max(0, Math.floor(clipRect.x * scale))
      const y = Math.max(0, Math.floor(clipRect.y * scale))
      const maxWidth = Math.max(0, glCanvas.width - x)
      const maxHeight = Math.max(0, glCanvas.height - y)
      const width = Math.min(Math.floor(clipRect.width * scale), maxWidth)
      const height = Math.min(Math.floor(clipRect.height * scale), maxHeight)
      return { x, y, width, height }
    })()

    if (sourceRect.width <= 0 || sourceRect.height <= 0) {
      throw new Error('Clip rect out of canvas bounds')
    }

    // 2. 创建合成 canvas
    const finalCanvas = document.createElement('canvas')
    finalCanvas.width = sourceRect.width
    finalCanvas.height = sourceRect.height
    const ctx = finalCanvas.getContext('2d')
    if (!ctx) {
      throw new Error('Failed to create canvas context')
    }

    // 3. 绘制 WebGL 内容（底层）
    const sceneService = getThreeSceneService()
    const labelsEnabled = (() => {
      if (!sceneService) return false
      return sceneService.camera.layers.isEnabled(LayerManager.LAYER_LABELS)
        || sceneService.camera.layers.isEnabled(LayerManager.LAYER_GRID)
    })()

    if (sceneService) {
      sceneService.renderOnce(labelsEnabled)
    }

    ctx.drawImage(
      glCanvas,
      sourceRect.x,
      sourceRect.y,
      sourceRect.width,
      sourceRect.height,
      0,
      0,
      sourceRect.width,
      sourceRect.height
    )

    // 4. 手动绘制 Labels（上层）- 使用投影计算 + Canvas 2D API
    if (sceneService && labelsEnabled) {
      const scene = sceneService.scene
      const camera = sceneService.camera

      if (scene && camera) {
        try {
          // 提取所有标签数据（世界坐标 → 屏幕坐标）
          let labels = LabelRenderer.extractLabels(
            scene,
            camera,
            glCanvas.width / scale,
            glCanvas.height / scale
          )

          if (clipRect) {
            const minX = clipRect.x
            const minY = clipRect.y
            const maxX = minX + clipRect.width
            const maxY = minY + clipRect.height

            labels = labels
              .filter(label =>
                label.screenX >= minX &&
                label.screenX <= maxX &&
                label.screenY >= minY &&
                label.screenY <= maxY
              )
              .map(label => ({
                ...label,
                screenX: label.screenX - minX,
                screenY: label.screenY - minY
              }))
          }

          // 在 Canvas 上绘制标签（含旋转）
          LabelRenderer.renderToCanvas(ctx, labels, scale)

          console.log(`[ScreenshotService] Rendered ${labels.length} labels manually`)
        } catch (e) {
          console.warn('[ScreenshotService] Failed to render labels:', e)
        }
      }
    } else {
      console.warn('[ScreenshotService] ThreeSceneService not available, skipping labels')
    }

    return finalCanvas.toDataURL('image/png')
  }

  /**
   * 截取指定房间
   * @param roomId 房间 ID
   * @returns Base64 编码的图片数据
   */
  async captureRoom(roomId: string): Promise<string> {
    const element = document.querySelector(`[data-room-id="${roomId}"]`)
    if (!element) {
      throw new Error(`Room ${roomId} not found`)
    }
    const canvas = await html2canvas(element as HTMLElement, {
      backgroundColor: null,
      scale: 1,
      logging: false
    })
    return canvas.toDataURL('image/png')
  }

  /**
   * 开始监听 Agent 截图请求（通过 SSE）
   *
   * 当 Agent 调用 request_screenshot MCP 工具时，Server 会通过 SSE 通知 Web 端，
   * Web 端执行截图后将结果提交给 Server。
   */
  startListening(): void {
    if (this.eventSource) {
      return  // 单例连接已存在，无需重建
    }

    this.eventSource = new EventSource(`${this.serverUrl}/api/screenshot/events`)

    this.eventSource.addEventListener('screenshot_request', async (event) => {
      const { requestId, roomId } = JSON.parse(event.data)
      console.log(`[ScreenshotService] Screenshot request received: ${requestId}, roomId=${roomId}`)

      try {
        const imageData = roomId
          ? await this.captureRoom(roomId)
          : await this.captureCanvas()

        await this.submitResult(requestId, imageData)
        console.log(`[ScreenshotService] Screenshot submitted: ${requestId}`)
      } catch (e) {
        console.error(`[ScreenshotService] Screenshot failed:`, e)
        await this.submitResult(requestId, null, String(e))
      }
    })

    this.eventSource.onerror = (error) => {
      console.error('[ScreenshotService] SSE connection error:', error)
    }

    this.eventSource.onopen = () => {
      console.log('[ScreenshotService] SSE connection opened')
    }
  }

  /**
   * 停止监听
   */
  stopListening(): void {
    if (this.eventSource) {
      this.eventSource.close()
      this.eventSource = null
      console.log('[ScreenshotService] SSE connection closed')
    }
  }

  /**
   * 提交截图结果给 Server
   */
  private async submitResult(
    requestId: string,
    imageData: string | null,
    error?: string
  ): Promise<void> {
    await fetch(`${this.serverUrl}/api/screenshot/result`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId, imageData, error })
    })
  }

  /**
   * 保存截图到本地临时目录
   * @param imageData Base64 编码的图片数据
   * @param filename 可选的文件名
   * @param projectPath 可选的项目路径（如提供，截图将保存到项目的 screenshots 子目录）
   * @returns 保存的文件路径
   */
  async saveToLocal(imageData: string, filename?: string, projectPath?: string): Promise<string> {
    const response = await fetch(`${this.serverUrl}/api/screenshot/save`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ imageData, filename, projectPath })
    })
    const result = await response.json()
    if (result.error) {
      throw new Error(result.error)
    }
    return result.path
  }
}

// 单例实例
let instance: ScreenshotService | null = null

export function getScreenshotService(serverUrl?: string): ScreenshotService {
  if (!instance) {
    instance = new ScreenshotService(serverUrl)
  }
  return instance
}
