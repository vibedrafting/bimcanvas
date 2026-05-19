/**
 * RestartService — 触发 Server 优雅重启 + 轮询 /health + 自动 reload。
 *
 * 从 HomeSettingsPanel.handleRestart (行 644-702) 抽离,消除"实例设置 与 插件管理 各持一份重启逻辑"的散乱。
 * 调用方:systemStore.performRestart(...) 唯一入口,GlobalRestartButton 点击触发。
 *
 * 流程:
 *   POST /api/settings/restart  → Server 写 restart.flag + StopApplication()
 *   wait 2s
 *   轮询 GET /health(最多 20 次,间隔 1.5s,共 30s 上限)
 *   通过 → window.location.reload()
 *   超时 → throw Error
 *
 * 兜底:Server 重启时 POST 自身可能因连接断开抛 ECONNABORTED / Network Error,
 *       此时视为"重启已触发,等服务回来"继续轮询,与原 handleRestart 行为一致。
 */

import { SettingsService } from './SettingsService'

export class RestartService {
  /**
   * 触发服务重启 + 健康检查 + 自动 reload。成功路径会直接 reload 当前页面,不会返回。
   * 失败 / 超时抛 Error,调用方负责展示错误。
   */
  static async performRestart(runtimeServerBase: string): Promise<void> {
    try {
      await SettingsService.restartInstance()
    } catch (error: any) {
      // Server 重启时 POST 连接可能直接断 → 走 fallback 路径继续轮询
      const isConnAbort = error.code === 'ECONNABORTED'
        || (typeof error.message === 'string' && error.message.includes('Network Error'))
      if (!isConnAbort) {
        throw new Error(
          error.response?.data?.message
            || error.message
            || '触发重启失败'
        )
      }
    }

    // 给 Server 一点时间开始重启
    await new Promise(r => setTimeout(r, 2000))
    await pollHealth(runtimeServerBase)
  }
}

async function pollHealth(runtimeServerBase: string): Promise<void> {
  const maxRetries = 20
  const retryInterval = 1500

  for (let i = 0; i < maxRetries; i++) {
    try {
      const resp = await fetch(`${runtimeServerBase}/health`, { cache: 'no-store' })
      if (resp.ok) {
        await new Promise(r => setTimeout(r, 500))
        window.location.reload()
        return
      }
    } catch { /* 继续轮询 */ }
    await new Promise(r => setTimeout(r, retryInterval))
  }

  throw new Error('服务重启超时（30 秒），请手动刷新页面或检查服务状态。')
}
