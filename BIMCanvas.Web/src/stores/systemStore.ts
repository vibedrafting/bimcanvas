/**
 * systemStore — 平台级系统状态与统一通知中枢。
 *
 * 职责:
 *   1. restartRequired 全局状态(由多个来源累计:实例设置保存 / plugin 激活 / 未来其它场景)
 *   2. 通知中枢:toast 队列(自动消失) + worktree 全屏通知,所有调用方统一走本 store
 *   3. performRestart() 触发 RestartService + 错误时 push 一条 error toast
 *
 * 通知约定(types/notification.ts):
 *   - pushToast 行为严格由 type 决定时长:info/success 3s,warning 5s,error 持久
 *   - 不暴露 persistent/duration override,UI 一致性优先
 *   - worktree 列表(后端 SignalR 推 JSON 数组 message)走独立 API,分流由生产者完成
 *
 * 与 pluginStore / settings 状态的边界:
 *   - 业务状态(installedPlugins / settings drafts 等)各 store 自管
 *   - "需重启"语义跨业务,由本 store 统一(避免 pluginStore.restartRequired 与 settings 各持一份)
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { RestartService } from '../services/RestartService'
import {
  TOAST_DURATIONS,
  type NotificationType,
  type ToastItem,
  type WorktreeNotification,
} from '../types/notification'

export interface ToastPayload {
  title: string
  message: string
  type?: NotificationType
}

export const useSystemStore = defineStore('system', () => {
  // 累计需要重启的原因(如 'settings:server' / 'plugin:indoor-layout'),用于按钮 hover 调试 + 去重
  const restartReasons = ref<Set<string>>(new Set())
  const isRestarting = ref(false)
  const lastRestartError = ref<string | null>(null)

  const restartRequired = computed(() => restartReasons.value.size > 0)

  const markRestartRequired = (reason: string) => {
    if (restartReasons.value.has(reason)) return
    const next = new Set(restartReasons.value)
    next.add(reason)
    restartReasons.value = next
  }

  const clearRestartFlag = () => {
    restartReasons.value = new Set()
  }

  // ============== 通知中枢 ==============

  const toasts = ref<ToastItem[]>([])
  const worktreeNotification = ref<WorktreeNotification | null>(null)
  const timers = new Map<number, ReturnType<typeof setTimeout>>()
  let nextId = 0

  /**
   * 推送一条左下角 toast。时长严格由 type 决定:
   *   info/success 3s · warning 5s · error 持久(需用户点 × 关闭)
   */
  const pushToast = (payload: ToastPayload) => {
    const type = payload.type ?? 'info'
    const id = ++nextId
    toasts.value.push({ id, title: payload.title, message: payload.message, type })
    const duration = TOAST_DURATIONS[type]
    if (duration > 0) {
      timers.set(id, setTimeout(() => removeToast(id), duration))
    }
  }

  const removeToast = (id: number) => {
    const t = timers.get(id)
    if (t) {
      clearTimeout(t)
      timers.delete(id)
    }
    const idx = toasts.value.findIndex(x => x.id === id)
    if (idx !== -1) toasts.value.splice(idx, 1)
  }

  const clearAllToasts = () => {
    timers.forEach(t => clearTimeout(t))
    timers.clear()
    toasts.value = []
  }

  const pushWorktreeNotification = (n: WorktreeNotification) => {
    worktreeNotification.value = n
  }

  const dismissWorktreeNotification = () => {
    worktreeNotification.value = null
  }

  /**
   * 立即重启。无二次确认 — 调用方应已在 UI 层告知"需重启"。
   * 成功路径直接 reload 当前页面;失败 push 一条 error toast。
   */
  const performRestart = async (runtimeServerBase: string) => {
    if (isRestarting.value) return
    isRestarting.value = true
    lastRestartError.value = null
    pushToast({ title: '正在重启', message: '服务正在重启，请稍候...', type: 'info' })

    try {
      await RestartService.performRestart(runtimeServerBase)
      // 成功路径 reload,不会执行到这
    } catch (error: any) {
      const msg = error?.message || '重启失败'
      lastRestartError.value = msg
      pushToast({ title: '重启失败', message: msg, type: 'error' })
    } finally {
      isRestarting.value = false
    }
  }

  return {
    // state
    restartReasons,
    isRestarting,
    lastRestartError,
    toasts,
    worktreeNotification,
    // computed
    restartRequired,
    // actions
    markRestartRequired,
    clearRestartFlag,
    pushToast,
    removeToast,
    clearAllToasts,
    pushWorktreeNotification,
    dismissWorktreeNotification,
    performRestart,
  }
})

// 保持旧 type 别名导出(向后兼容,如有外部 import 'ToastType')
export type ToastType = NotificationType
