import type { Ref } from 'vue'
import type { BackgroundTaskRecord } from '../../types/agent'
import type { ChatMessage, ChatWindow } from '../../types/aiCommandCenter'
import { getBackgroundTaskService } from '../../services/BackgroundTaskService'
import { createTextBubble, completeBubble } from '../../utils/bubbleManager'
import { useWorkflowProgress } from './useWorkflowProgress'

const workflowProgress = useWorkflowProgress()

interface BackgroundTaskOptions {
  agentApiBase: string
  windows: Ref<ChatWindow[]>
  scrollToBottom: (options?: { windowId?: string }) => void
}

const createBackgroundHostMessage = (): ChatMessage => ({
  role: 'ai',
  bubbles: [],
  waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
  isStreaming: false,
  startTime: Date.now(),
  endTime: Date.now()
})

export const useBackgroundTask = (options: BackgroundTaskOptions) => {
  // 去重：仅防同一完成事件被重复实时投递。history 重建走独立路径（restoreHistoryForWindow
  // 每次 messages=[] 全量重建），与实时注入永不共存，故无需在此防 history 重复。
  const seenKeys = new Set<string>()

  const findTargetWindow = (windowId?: string | null): ChatWindow | undefined => {
    if (windowId) {
      const matched = options.windows.value.find(w => w.id === windowId)
      if (matched) {
        return matched
      }
    }
    // windowId 缺失或已不存在：兜底落到第一个窗口
    return options.windows.value[0]
  }

  const handleCompleted = (record: BackgroundTaskRecord) => {
    if (record.taskId) {
      const key = `${record.sessionId ?? ''}:${record.taskId}`
      if (seenKeys.has(key)) {
        return
      }
      seenKeys.add(key)
    }

    // Workflow 进度收口:有活跃 workflow 时标记完成,携带 sdkSessionId 供 Task 页 tier C 拉 transcript。
    workflowProgress.onWorkflowCompleted({
      taskId: record.taskId,
      status: record.status,
      sdkSessionId: record.sdkSessionId
    })

    const win = findTargetWindow(record.windowId)
    if (!win) {
      console.warn(`[useBackgroundTask] No window to host background task: ${record.taskId}`)
      return
    }

    // content 由 Agent 组装好（与 history 重建复用同一文本，保证两条渲染路径收敛）
    const text = record.content || record.summary || `后台任务已完成（${record.taskId}）`
    const message = createBackgroundHostMessage()
    const bubble = createTextBubble(text)
    completeBubble(bubble)
    message.bubbles.push(bubble)
    win.messages.push(message)

    options.scrollToBottom({ windowId: win.id })
  }

  const startListening = () => {
    getBackgroundTaskService(options.agentApiBase).startListening({
      onCompleted: handleCompleted,
      onProgress: (record) => {
        // 后台 Workflow 实时进度 → Task 页 workflow 视图（detach 后唯一实时来源）
        workflowProgress.onWorkflowProgress({
          taskId: record.taskId,
          sdkSessionId: record.sdkSessionId,
          description: record.description,
          lastToolName: record.lastToolName,
          usage: record.usage
        })
      }
    })
  }

  const stopListening = () => {
    getBackgroundTaskService(options.agentApiBase).stopListening()
  }

  return { startListening, stopListening }
}
