import type { Ref } from 'vue'
import type { ChatBubble, InteractionRecord } from '../../types/agent'
import type { ChatMessage, ChatWindow } from '../../types/aiCommandCenter'
import { getQuestionService } from '../../services/QuestionService'
import { createQuestionBubble, completeBubble } from '../../utils/bubbleManager'
import { createLogger } from '../../utils/logger'

const log = createLogger('STREAM')

interface QuestionOptions {
  agentApiBase: string
  windows: Ref<ChatWindow[]>
  scrollToBottom: (options?: { windowId?: string }) => void
  waitForInteractionContinuation?: (windowId: string) => Promise<void>
}

const createQuestionHostMessage = (): ChatMessage => ({
  role: 'ai',
  bubbles: [],
  waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
  isStreaming: false,
  startTime: Date.now()
})

const findQuestionBubble = (bubbleList: ChatBubble[], interactionId: string): ChatBubble | undefined => {
  for (const bubble of bubbleList) {
    if (bubble.questionRequestId === interactionId) {
      return bubble
    }
    if (bubble.childBubbles) {
      const nested = findQuestionBubble(bubble.childBubbles, interactionId)
      if (nested) {
        return nested
      }
    }
  }
  return undefined
}

export const useQuestion = (options: QuestionOptions) => {
  const findTargetWindow = (windowId: string): ChatWindow | undefined => {
    return options.windows.value.find(w => w.id === windowId)
  }

  const ensureTargetMessage = (window: ChatWindow): ChatMessage => {
    const lastAiMsg = [...window.messages].reverse().find(message => message.role === 'ai')
    if (lastAiMsg) {
      return lastAiMsg
    }

    const hostMessage = createQuestionHostMessage()
    window.messages.push(hostMessage)
    return hostMessage
  }

  const applyResolvedQuestion = (
    bubble: ChatBubble,
    answers?: Record<string, string>
  ) => {
    bubble.questionSubmitted = true
    bubble.questionAnswers = answers || {}
    completeBubble(bubble)
  }

  const handleQuestionPushed = (record: InteractionRecord) => {
    const win = findTargetWindow(record.windowId)
    if (!win) {
      log.warn('pending question points to missing window', { windowId: record.windowId })
      return
    }

    const existingBubble = win.messages
      .flatMap(message => message.bubbles)
      .map(bubble => findQuestionBubble([bubble], record.interactionId))
      .find(Boolean)

    if (existingBubble) {
      existingBubble.questions = record.requestPayload?.questions || existingBubble.questions
      options.scrollToBottom({ windowId: win.id })
      return
    }

    const targetMessage = ensureTargetMessage(win)
    const bubble = createQuestionBubble(
      record.interactionId,
      Array.isArray(record.requestPayload?.questions) ? record.requestPayload.questions : []
    )
    targetMessage.bubbles.push(bubble)
    targetMessage.waitingState.isWaiting = false
    targetMessage.isStreaming = false

    options.scrollToBottom({ windowId: win.id })
  }

  const handleQuestionTerminal = (record: InteractionRecord) => {
    const win = findTargetWindow(record.windowId)
    if (!win) {
      log.warn('terminal question points to missing window', { windowId: record.windowId })
      return
    }

    const bubble = win.messages
      .flatMap(message => message.bubbles)
      .map(item => findQuestionBubble([item], record.interactionId))
      .find(Boolean)

    if (!bubble) {
      return
    }

    const answers = record.status === 'resolved'
      ? (record.resolutionPayload?.answers as Record<string, string> | undefined)
      : {}

    applyResolvedQuestion(bubble, answers)
    options.scrollToBottom({ windowId: win.id })
  }

  const startListening = async () => {
    const service = getQuestionService(options.agentApiBase)
    service.startListening({
      onPushed: handleQuestionPushed,
      onResolved: handleQuestionTerminal,
      onCancelled: handleQuestionTerminal,
      onExpired: handleQuestionTerminal
    })

    const windowIds = options.windows.value.map(window => window.id)
    if (windowIds.length === 0) {
      return
    }

    try {
      await service.restorePending(windowIds)
    } catch (error) {
      log.warn('restore pending questions failed', { err: error })
    }
  }

  const stopListening = () => {
    const service = getQuestionService(options.agentApiBase)
    service.stopListening()
  }

  const submitAnswer = async (bubble: ChatBubble) => {
    if (!bubble.questionRequestId || bubble.questionSubmitted) return
    const service = getQuestionService(options.agentApiBase)
    const resolved = await service.submitAnswer(bubble.questionRequestId, bubble.questionAnswers || {})
    applyResolvedQuestion(bubble, bubble.questionAnswers || {})
    const targetWindow = resolved.windowId ? findTargetWindow(resolved.windowId) : undefined
    if (resolved.windowId && !targetWindow?.isStreaming) {
      await options.waitForInteractionContinuation?.(resolved.windowId)
    }
  }

  const cancelQuestion = async (bubble: ChatBubble) => {
    if (!bubble.questionRequestId || bubble.questionSubmitted) return
    const service = getQuestionService(options.agentApiBase)
    const resolved = await service.cancelQuestion(bubble.questionRequestId)
    applyResolvedQuestion(bubble, {})
    const targetWindow = resolved.windowId ? findTargetWindow(resolved.windowId) : undefined
    if (resolved.windowId && !targetWindow?.isStreaming) {
      await options.waitForInteractionContinuation?.(resolved.windowId)
    }
  }

  return { startListening, stopListening, submitAnswer, cancelQuestion }
}
