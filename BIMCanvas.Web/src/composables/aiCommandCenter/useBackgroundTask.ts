import type { Ref } from 'vue'
import type { BackgroundTaskRecord } from '../../types/agent'
import type { ChatMessage, ChatWindow } from '../../types/aiCommandCenter'
import { getBackgroundTaskService } from '../../services/BackgroundTaskService'
import { createTextBubble, completeBubble } from '../../utils/bubbleManager'

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

const buildSummaryContent = (record: BackgroundTaskRecord): string => {
  const body = (record.assistantText || record.summary || '').trim()
  if (record.status === 'completed') {
    return body || `后台任务已完成（${record.taskId}）`
  }
  const statusText = record.status === 'stopped' ? '已停止' : '执行失败'
  const prefix = `后台任务${statusText}（${record.taskId}）`
  return body ? `${prefix}\n\n${body}` : prefix
}

export const useBackgroundTask = (options: BackgroundTaskOptions) => {
  // 去重：restorePending 补发与实时推送可能投递同一 taskId，避免重复气泡
  const seenTaskIds = new Set<string>()

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
      if (seenTaskIds.has(record.taskId)) {
        return
      }
      seenTaskIds.add(record.taskId)
    }

    const win = findTargetWindow(record.windowId)
    if (!win) {
      console.warn(`[useBackgroundTask] No window to host background task: ${record.taskId}`)
      return
    }

    const message = createBackgroundHostMessage()
    const bubble = createTextBubble(buildSummaryContent(record))
    completeBubble(bubble)
    message.bubbles.push(bubble)
    win.messages.push(message)

    options.scrollToBottom({ windowId: win.id })
  }

  const startListening = async () => {
    const service = getBackgroundTaskService(options.agentApiBase)
    service.startListening({ onCompleted: handleCompleted })

    const windowIds = options.windows.value.map(window => window.id)
    if (windowIds.length === 0) {
      return
    }

    try {
      await service.restorePending(windowIds)
    } catch (error) {
      console.warn('[useBackgroundTask] Restore pending background tasks failed:', error)
    }
  }

  const stopListening = () => {
    const service = getBackgroundTaskService(options.agentApiBase)
    service.stopListening()
  }

  return { startListening, stopListening }
}
