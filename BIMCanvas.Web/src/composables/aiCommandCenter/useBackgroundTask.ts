import type { Ref } from 'vue'
import type { BackgroundTaskRecord } from '../../types/agent'
import type { ChatWindow } from '../../types/aiCommandCenter'
import { getBackgroundTaskService } from '../../services/BackgroundTaskService'
import { useWorkflowProgress } from './useWorkflowProgress'

const workflowProgress = useWorkflowProgress()

interface BackgroundTaskOptions {
  agentApiBase: string
  windows: Ref<ChatWindow[]>
  scrollToBottom: (options?: { windowId?: string }) => void
  /** 注入后台总结单文本气泡（events 为空时的兜底；走 history 重建同款渲染管线） */
  injectBackgroundSummary: (windowId: string | null | undefined, content: string, timestamp?: number) => void
  /** T2：注入后台总结完整回合（thinking/tool/text envelope 序列），走前台同款 applyNormalizedEventToMessage */
  injectBackgroundTurn: (windowId: string | null | undefined, events: Record<string, unknown>[], timestamp?: number) => void
}

export const useBackgroundTask = (options: BackgroundTaskOptions) => {
  // 去重：仅防同一完成事件被重复实时投递。
  const seenKeys = new Set<string>()

  const handleCompleted = (record: BackgroundTaskRecord) => {
    if (record.taskId) {
      const key = `${record.sessionId ?? ''}:${record.taskId}`
      if (seenKeys.has(key)) {
        return
      }
      seenKeys.add(key)
      // 普通后台 Task 收口：标完成态保留条目（Task 页卡片显示）；对 workflow 的 taskId 调用无害（集合里本来没有）
      workflowProgress.completeBackgroundTask(
        record.taskId,
        record.status === 'failed' ? 'failed' : 'completed'
      )
    }

    // ① Task 面板收口（始终）——携带 sdkSessionId 供 tier C 拉 transcript。
    workflowProgress.onWorkflowCompleted({
      taskId: record.taskId,
      status: record.status,
      sdkSessionId: record.sdkSessionId
    })

    // ② Chat 气泡。generic 占位(hasSummary=false 且无 events)不渲染（承接 Bug4）。
    const ts = record.timestamp ? Date.parse(record.timestamp) : Date.now()
    const stamp = isNaN(ts) ? Date.now() : ts
    // T2：有完整 envelope 序列 → 渲染成"思考+工具+文本"完整一条回合（走前台同款管线）。
    if (Array.isArray(record.events) && record.events.length > 0) {
      options.injectBackgroundTurn(record.windowId, record.events, stamp)
      return
    }
    // 兜底：无 events（旧 Agent / 异常）但有原生总结 → 单文本气泡。
    if (!record.hasSummary) return
    const text = (record.content || record.summary || '').trim()
    if (!text) return
    options.injectBackgroundSummary(record.windowId, text, stamp)
  }

  const startListening = () => {
    getBackgroundTaskService(options.agentApiBase).startListening({
      onCompleted: handleCompleted,
      onProgress: (record) => {
        // 后台任务实时进度：isWorkflow=false 在 onWorkflowProgress 内分流进普通任务集合，
        // 其余 → Task 页 workflow 视图（detach 后唯一实时来源）
        workflowProgress.onWorkflowProgress({
          taskId: record.taskId,
          isWorkflow: record.isWorkflow,
          sdkSessionId: record.sdkSessionId,
          description: record.description,
          lastToolName: record.lastToolName,
          usage: record.usage
        })
      },
      onPhases: (record) => {
        // 后台 Workflow 阶段预声明 → 运行态立即渲染全部阶段（不依赖脆弱的脚本文件读取）
        workflowProgress.onWorkflowPhases({
          taskId: record.taskId,
          sdkSessionId: record.sdkSessionId,
          workflowName: record.workflowName,
          phases: record.phases
        })
      }
    })
  }

  const stopListening = () => {
    getBackgroundTaskService(options.agentApiBase).stopListening()
  }

  return { startListening, stopListening }
}
