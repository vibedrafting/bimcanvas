/**
 * 全局通知体系的类型与默认配置。
 *
 * 行为约定:
 *   - 时长严格由 type 决定,无 per-call override
 *   - error 类型默认持久(0 = 不自动消失,需用户点 × 关闭)
 *   - 其他类型按严重性递增:info/success 3s,warning 5s
 */

export type NotificationType = 'info' | 'success' | 'warning' | 'error'

/** Toast 自动消失时长(毫秒)。0 = 持久。 */
export const TOAST_DURATIONS: Record<NotificationType, number> = {
  info: 3000,
  success: 3000,
  warning: 5000,
  error: 0,
}

export interface ToastItem {
  id: number
  title: string
  message: string
  type: NotificationType
}

/** Worktree 列表通知(走全屏 Modal,不进 toast 队列)。 */
export interface WorktreeNotification {
  title: string
  worktreeNames: string[]
  type: NotificationType
}

/** 后端 SignalR 推送 AgentNotification 的 DTO。 */
export interface AgentNotificationDto {
  title: string
  message: string
  type: NotificationType
  timestamp: string
}
