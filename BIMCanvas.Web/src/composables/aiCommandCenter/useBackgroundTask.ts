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
  // 去重：仅防同一完成事件被重复实时投递。
  const seenKeys = new Set<string>()

  const findTargetWindow = (windowId?: string | null): ChatWindow | undefined => {
    if (windowId) {
      const matched = options.windows.value.find(w => w.id === windowId)
      if (matched) return matched
    }
    return options.windows.value[0] // 兜底落第一个窗口
  }

  const handleCompleted = (record: BackgroundTaskRecord) => {
    if (record.taskId) {
      const key = `${record.sessionId ?? ''}:${record.taskId}`
      if (seenKeys.has(key)) {
        return
      }
      seenKeys.add(key)
    }

    // ① Task 面板收口（始终）——携带 sdkSessionId 供 tier C 拉 transcript。
    workflowProgress.onWorkflowCompleted({
      taskId: record.taskId,
      status: record.status,
      sdkSessionId: record.sdkSessionId
    })

    // ② Chat 气泡：仅当主控有原生总结(hasSummary)时注入富总结；generic 占位(hasSummary=false)
    //    不渲染（承接 Bug4，去掉 'Workflow ... completed' 噪声）。
    if (!record.hasSummary) return
    const text = (record.content || record.summary || '').trim()
    if (!text) return
    const win = findTargetWindow(record.windowId)
    if (!win) {
      console.warn(`[useBackgroundTask] No window to host background task: ${record.taskId}`)
      return
    }
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
