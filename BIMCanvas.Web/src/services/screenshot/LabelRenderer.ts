/**
 * LabelRenderer - 截图时手动绘制标签
 *
 * 解决问题：html2canvas 对 writing-mode: vertical-rl 支持不可靠
 * 方案：用 Canvas 2D API 手动绘制标签，彻底绕过 html2canvas
 */
import * as THREE from 'three'
import { CSS2DObject } from 'three-stdlib'

export interface LabelData {
  text: string
  screenX: number
  screenY: number
  isVertical: boolean
  fontSize: number
  fontFamily: string
  fontWeight: string
  color: string
  textShadow: string
  labelType: 'component' | 'grid'
}

export class LabelRenderer {
  /**
   * 从场景中提取所有 Label 数据
   * 使用投影计算获取屏幕坐标，与 CSS2DRenderer 保持一致
   */
  static extractLabels(
    scene: THREE.Scene,
    camera: THREE.Camera,
    canvasWidth: number,
    canvasHeight: number
  ): LabelData[] {
    const labels: LabelData[] = []

    scene.traverse((obj) => {
      // 检查是否是 CSS2DObject
      if (!(obj instanceof CSS2DObject)) return

      const div = obj.element as HTMLDivElement
      if (!div) return
      const isAiLabel = div.classList.contains('ai-label')
      const isGridLabel = div.classList.contains('semantic-grid-label')
      if (!isAiLabel && !isGridLabel) return

      // 检查可见性与图层
      if (!obj.visible) return
      if (!camera.layers.test(obj.layers)) return

      // 投影计算：世界坐标 → 屏幕坐标
      const worldPos = new THREE.Vector3()
      obj.getWorldPosition(worldPos)

      // 使用相机投影
      const ndc = worldPos.clone().project(camera)

      // NDC 范围是 [-1, 1]，转换为屏幕坐标
      const screenX = (ndc.x + 1) / 2 * canvasWidth
      const screenY = (1 - ndc.y) / 2 * canvasHeight

      // 提取样式
      const style = window.getComputedStyle(div)
      const isVertical = style.writingMode === 'vertical-rl'
      const labelType: LabelData['labelType'] = isGridLabel ? 'grid' : 'component'

      labels.push({
        text: div.textContent || '',
        screenX,
        screenY,
        isVertical,
        fontSize: parseFloat(style.fontSize) || 10,
        fontFamily: style.fontFamily || 'monospace',
        fontWeight: style.fontWeight || 'normal',
        color: style.color || '#ffffff',
        textShadow: style.textShadow || '',
        labelType
      })
    })

    return labels
  }

  /**
   * 在 Canvas 上绘制所有 Labels
   */
  static renderToCanvas(
    ctx: CanvasRenderingContext2D,
    labels: LabelData[],
    scale: number = 1
  ): void {
    labels.forEach(label => {
      ctx.save()

      // 移动到文字位置
      ctx.translate(label.screenX * scale, label.screenY * scale)

      // 竖向文字：旋转 -90 度（整体旋转语义）
      if (label.isVertical) {
        ctx.rotate(-Math.PI / 2)
      }

      // 设置字体
      ctx.font = `${label.fontWeight} ${label.fontSize * scale}px ${label.fontFamily}`
      ctx.textAlign = 'center'
      ctx.textBaseline = 'middle'

      const hasShadow = label.textShadow && label.textShadow !== 'none'
      if (hasShadow) {
        if (label.labelType === 'grid') {
          const shadow = this.parseShadow(label.textShadow)
          if (shadow) {
            ctx.shadowColor = shadow.color
            ctx.shadowBlur = shadow.blur * scale
            ctx.shadowOffsetX = shadow.offsetX * scale
            ctx.shadowOffsetY = shadow.offsetY * scale
          }
        } else {
          // 解析 text-shadow 获取描边颜色
          // 格式: "-1px -1px 0 #000, 1px -1px 0 #000, ..."
          const shadowColor = this.parseShadowColor(label.textShadow)
          if (shadowColor) {
            ctx.strokeStyle = shadowColor
            ctx.lineWidth = 2 * scale
            ctx.lineJoin = 'round'
            ctx.strokeText(label.text, 0, 0)
          }
        }
      }

      // 绘制文字
      ctx.fillStyle = label.color
      ctx.fillText(label.text, 0, 0)

      ctx.restore()
    })
  }

  /**
   * 从 text-shadow CSS 值中解析描边颜色
   */
  private static parseShadowColor(textShadow: string): string | null {
    // text-shadow 格式: "-1px -1px 0 #000, 1px -1px 0 #000, ..."
    // 提取第一个颜色值
    const match = textShadow.match(/(#[0-9a-fA-F]{3,6}|rgb\([^)]+\)|rgba\([^)]+\))/)
    return match ? match[1] : null
  }

  /**
   * 解析单个 text-shadow（优先取第一个阴影）
   * 支持格式: "0 1px 2px rgba(...)" 或 "rgba(...) 0 1px 2px"
   */
  private static parseShadow(textShadow: string): { offsetX: number; offsetY: number; blur: number; color: string } | null {
    const shadowPart = textShadow.split(',')[0]?.trim()
    if (!shadowPart) return null

    const colorMatch = shadowPart.match(/(#[0-9a-fA-F]{3,6}|rgb\([^)]+\)|rgba\([^)]+\))/)
    const color = colorMatch ? colorMatch[1] : 'rgba(0,0,0,0.5)'
    const numberMatches = shadowPart.match(/-?\d*\.?\d+px/g) || []
    if (numberMatches.length < 2) return null

    const offsetX = parseFloat(numberMatches[0])
    const offsetY = parseFloat(numberMatches[1])
    const blur = numberMatches.length >= 3 ? parseFloat(numberMatches[2]) : 0
    return { offsetX, offsetY, blur, color }
  }
}
