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
import { AGENT_API } from '../config/api'
import type { InteractionEventListener, InteractionRecord } from '../types/agent'
import { getInteractionChannelService } from './InteractionChannelService'
import { createLogger } from '../utils/logger'

const log = createLogger('SYS')

export interface ClipRect {
  x: number
  y: number
  width: number
  height: number
}

export interface CaptureCanvasOptions {
  labelScale?: number
}

export interface ScreenshotInteractionHandlers {
  onPushed?: (record: InteractionRecord) => void
  onResolved?: (record: InteractionRecord) => void
  onCancelled?: (record: InteractionRecord) => void
  onExpired?: (record: InteractionRecord) => void
}

export class ScreenshotService {
  private serverUrl: string
  private handlers: ScreenshotInteractionHandlers = {}
  private listener: InteractionEventListener | null = null

  constructor(serverUrl: string = AGENT_API) {
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
  async captureCanvas(clipRect?: ClipRect, options: CaptureCanvasOptions = {}): Promise<string> {
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
          LabelRenderer.renderToCanvas(ctx, labels, scale, {
            labelScale: options.labelScale
          })

          log.info('labels rendered', { count: labels.length, labelScale: options.labelScale ?? 1 })
        } catch (e) {
          log.warn('failed to render labels', { error: e })
        }
      }
    } else {
      log.warn('ThreeSceneService not available, skipping labels')
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
  startListening(handlers: ScreenshotInteractionHandlers): void {
    this.handlers = handlers
    if (this.listener) {
      return
    }

    this.listener = ({ event, record }) => {
      if (record.kind !== 'screenshot') {
        return
      }

      switch (event) {
        case 'interaction.pushed':
          this.handlers.onPushed?.(record)
          break
        case 'interaction.resolved':
          this.handlers.onResolved?.(record)
          break
        case 'interaction.cancelled':
          this.handlers.onCancelled?.(record)
          break
        case 'interaction.expired':
          this.handlers.onExpired?.(record)
          break
      }
    }

    getInteractionChannelService(this.serverUrl).startListening(this.listener)
  }

  /**
   * 停止监听
   */
  stopListening(): void {
    if (!this.listener) return
    getInteractionChannelService(this.serverUrl).stopListening(this.listener)
    this.listener = null
  }

  /**
   * 恢复当前页面已存在窗口的 pending screenshot interactions
   */
  async restorePending(windowIds: string[]): Promise<InteractionRecord[]> {
    const channel = getInteractionChannelService(this.serverUrl)
    const restored: InteractionRecord[] = []

    for (const windowId of windowIds) {
      const interactions = await channel.queryPending(windowId)
      for (const record of interactions) {
        if (record.kind !== 'screenshot') {
          continue
        }
        restored.push(record)
        this.handlers.onPushed?.(record)
      }
    }

    return restored
  }

  /**
   * 提交截图结果给统一 InteractionChannel
   */
  async submitResult(
    interactionId: string,
    imageData: string | null,
    error?: string
  ): Promise<InteractionRecord> {
    return getInteractionChannelService(this.serverUrl).submitInteraction(interactionId, {
      imageData,
      ...(error ? { error } : {})
    })
  }

  async cancelRequest(
    interactionId: string,
    cancelReason: string = 'screenshot_cancelled'
  ): Promise<InteractionRecord> {
    return getInteractionChannelService(this.serverUrl).cancelInteraction(interactionId, cancelReason)
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
const instances = new Map<string, ScreenshotService>()

export function getScreenshotService(serverUrl?: string): ScreenshotService {
  const normalizedUrl = serverUrl || AGENT_API
  let instance = instances.get(normalizedUrl)
  if (!instance) {
    instance = new ScreenshotService(normalizedUrl)
    instances.set(normalizedUrl, instance)
  }
  return instance
}
