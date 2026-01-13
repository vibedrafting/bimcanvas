/**
 * 截图服务
 *
 * 提供两种能力：
 * 1. 主动截图：用户通过附件菜单截取画布/房间
 * 2. 响应截图请求：监听 Agent 的截图请求（通过 SSE）
 */
import html2canvas from 'html2canvas'

export class ScreenshotService {
  private serverUrl: string
  private eventSource: EventSource | null = null

  constructor(serverUrl: string = 'http://localhost:8765') {
    this.serverUrl = serverUrl
  }

  /**
   * 截取整个画布（直接从 WebGL canvas 获取）
   * @returns Base64 编码的图片数据
   */
  async captureCanvas(): Promise<string> {
    // 直接获取 WebGL canvas 元素
    const glCanvas = document.querySelector('.three-canvas canvas') as HTMLCanvasElement
      || document.querySelector('.canvas-area canvas') as HTMLCanvasElement
    if (glCanvas) {
      // WebGL canvas 直接使用 toDataURL（需要 preserveDrawingBuffer: true）
      return glCanvas.toDataURL('image/png')
    }

    // 回退：使用 html2canvas（可能无法正确捕获 WebGL 内容）
    const element = document.querySelector('.canvas-area')
      || document.querySelector('.three-canvas')
    if (!element) {
      throw new Error('Canvas element not found. Please ensure the canvas is loaded.')
    }
    console.warn('[ScreenshotService] WebGL canvas not found, falling back to html2canvas')
    const canvas = await html2canvas(element as HTMLElement, {
      backgroundColor: null,
      scale: window.devicePixelRatio || 1,
      logging: false,
      useCORS: true,
      allowTaint: true
    })
    return canvas.toDataURL('image/png')
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
      console.warn('[ScreenshotService] Already listening')
      return
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
   * @returns 保存的文件路径
   */
  async saveToLocal(imageData: string, filename?: string): Promise<string> {
    const response = await fetch(`${this.serverUrl}/api/screenshot/save`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ imageData, filename })
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
