/**
 * AskUserQuestion 服务
 *
 * 监听 Agent 的用户问题请求（通过独立 SSE 通道），
 * 收到请求后通过回调通知 UI 层渲染问题气泡。
 *
 * 架构与 ScreenshotService 一致：
 * - SSE 监听 /api/question/events
 * - POST 回调 /api/question/answer
 */
import type { QuestionRequestEvent } from '../types/agent'

export class QuestionService {
  private serverUrl: string
  private eventSource: EventSource | null = null
  private onQuestionRequest: ((event: QuestionRequestEvent) => void) | null = null

  constructor(serverUrl: string = 'http://localhost:8865') {
    this.serverUrl = serverUrl
  }

  /**
   * 开始监听 Agent 问题请求（通过 SSE）
   */
  startListening(callback: (event: QuestionRequestEvent) => void): void {
    this.onQuestionRequest = callback  // 始终更新回调，即使 SSE 连接已存在
    if (this.eventSource) {
      return  // SSE 连接已建立，仅更新回调
    }
    this.eventSource = new EventSource(`${this.serverUrl}/api/question/events`)

    this.eventSource.addEventListener('question_request', (event) => {
      const data: QuestionRequestEvent = JSON.parse((event as MessageEvent).data)
      console.log(`[QuestionService] Question request: ${data.requestId}, ${data.questions.length} questions`)
      this.onQuestionRequest?.(data)
    })

    this.eventSource.onerror = () => {
      console.error('[QuestionService] SSE connection error')
    }

    this.eventSource.onopen = () => {
      console.log('[QuestionService] SSE connection opened')
    }
  }

  /**
   * 停止监听
   */
  stopListening(): void {
    if (this.eventSource) {
      this.eventSource.close()
      this.eventSource = null
      console.log('[QuestionService] SSE connection closed')
    }
  }

  /**
   * 提交用户答案
   */
  async submitAnswer(
    requestId: string,
    answers: Record<string, string>,
    cancelled: boolean = false
  ): Promise<void> {
    await fetch(`${this.serverUrl}/api/question/answer`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ requestId, answers, cancelled })
    })
  }
}

// 单例
let instance: QuestionService | null = null
export function getQuestionService(serverUrl?: string): QuestionService {
  if (!instance) {
    instance = new QuestionService(serverUrl)
  }
  return instance
}
