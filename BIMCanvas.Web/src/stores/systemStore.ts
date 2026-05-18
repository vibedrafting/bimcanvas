/**
 * systemStore — 平台级系统状态与统一通知通道。
 *
 * 职责:
 *   1. restartRequired 全局状态(由多个来源累计:实例设置保存 / plugin 激活 / 未来其它场景)
 *   2. pushToast() 统一封装左下角 toast 通道(复用 AgentNotificationModal 的 'bimcanvas:agent-notification'
 *      事件总线,该容器对纯文本 message 走 toast 队列,对 worktree 列表分流到 modal)
 *   3. performRestart() 触发 RestartService + 错误时 push 一条 error toast
 *
 * 与 pluginStore / settings 状态的边界:
 *   - 业务状态(installedPlugins / settings drafts 等)各 store 自管
 *   - "需重启"语义跨业务,由本 store 统一(避免 pluginStore.restartRequired 与 settings 各持一份)
 */

import { defineStore } from 'pinia'
import { ref, computed } from 'vue'
import { RestartService } from '../services/RestartService'

export type ToastType = 'info' | 'success' | 'warning' | 'error'

export interface ToastPayload {
  title: string
  message: string
  type?: ToastType
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

  /**
   * 推送一条左下角 toast。复用 AgentNotificationModal 的全局事件通道。
   * 该容器最多同时显示 3 条,溢出显示"+N 条更多"徽章 + "全部清除"按钮。
   */
  const pushToast = (payload: ToastPayload) => {
    window.dispatchEvent(new CustomEvent('bimcanvas:agent-notification', {
      detail: {
        title: payload.title,
        message: payload.message,
        type: payload.type ?? 'info',
      },
    }))
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
    // computed
    restartRequired,
    // actions
    markRestartRequired,
    clearRestartFlag,
    pushToast,
    performRestart,
  }
})
