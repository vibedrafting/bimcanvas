import type { Ref } from 'vue'
import type { ChatBubble, QuestionRequestEvent } from '../../types/agent'
import type { ChatWindow } from '../../types/aiCommandCenter'
import { getQuestionService } from '../../services/QuestionService'
import { createQuestionBubble, completeBubble } from '../../utils/bubbleManager'

interface QuestionOptions {
  agentApiBase: string
  windows: Ref<ChatWindow[]>
  activeWindowId: Ref<string>
  scrollToBottom: (options?: { windowId?: string }) => void
}

export const useQuestion = (options: QuestionOptions) => {

  const startListening = () => {
    const service = getQuestionService(options.agentApiBase)
    service.startListening(handleQuestionRequest)
  }

  const stopListening = () => {
    const service = getQuestionService(options.agentApiBase)
    service.stopListening()
  }

  /** 收到 Agent 问题请求 -> 在最后一条 AI 消息中插入问题气泡 */
  const handleQuestionRequest = (event: QuestionRequestEvent) => {
    const win = options.windows.value.find(w => w.id === options.activeWindowId.value)
    if (!win) return

    const lastAiMsg = [...win.messages].reverse().find(m => m.role === 'ai')
    if (!lastAiMsg) return

    const bubble = createQuestionBubble(event.requestId, event.questions)
    lastAiMsg.bubbles.push(bubble)
    lastAiMsg.waitingState.isWaiting = false

    options.scrollToBottom({ windowId: win.id })
  }

  /** 用户提交答案 */
  const submitAnswer = async (bubble: ChatBubble) => {
    if (!bubble.questionRequestId || bubble.questionSubmitted) return
    const service = getQuestionService(options.agentApiBase)
    await service.submitAnswer(bubble.questionRequestId, bubble.questionAnswers || {})
    bubble.questionSubmitted = true
    completeBubble(bubble)
  }

  /** 用户点跳过 */
  const cancelQuestion = async (bubble: ChatBubble) => {
    if (!bubble.questionRequestId || bubble.questionSubmitted) return
    const service = getQuestionService(options.agentApiBase)
    await service.submitAnswer(bubble.questionRequestId, {}, true)
    bubble.questionSubmitted = true
    bubble.questionAnswers = {}
    completeBubble(bubble)
  }

  return { startListening, stopListening, submitAnswer, cancelQuestion }
}
