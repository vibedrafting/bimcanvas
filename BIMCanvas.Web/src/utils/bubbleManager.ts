/**
 * BubbleManager - 气泡管理工具函数
 * 用于创建和管理时间线气泡模型中的各类气泡
 */

import type { ChatBubble, WaitingState, BubbleStatus, SubAgentType, UserQuestion } from '../types/agent';

// ========== ID 生成器 ==========

let bubbleIdCounter = 0;

/**
 * 生成唯一的气泡 ID
 */
export function generateBubbleId(prefix: string = 'bubble'): string {
  return `${prefix}_${Date.now()}_${++bubbleIdCounter}`;
}

// ========== 气泡创建函数 ==========

/**
 * 创建文本气泡
 */
export function createTextBubble(content: string = ''): ChatBubble {
  return {
    id: generateBubbleId('text'),
    type: 'text',
    timestamp: Date.now(),
    status: 'streaming',
    content
  };
}

/**
 * 创建工具调用气泡
 */
export function createToolCallBubble(
  toolCallId: string,
  toolName: string,
  toolDescription?: string,
  toolParams?: Record<string, unknown>
): ChatBubble {
  return {
    id: toolCallId,
    type: 'tool_call',
    timestamp: Date.now(),
    status: 'streaming',
    toolName,
    toolDescription,
    toolParams
  };
}

/**
 * 创建 SubAgent 气泡
 */
export function createSubAgentBubble(
  subAgentId: string,
  subAgentName: string,
  subAgentType: SubAgentType
): ChatBubble {
  return {
    id: subAgentId,
    type: 'subagent',
    timestamp: Date.now(),
    status: 'streaming',
    subAgentName,
    subAgentType,
    childBubbles: []
  };
}

/**
 * 创建问题气泡（AskUserQuestion）
 */
export function createQuestionBubble(
  requestId: string,
  questions: UserQuestion[]
): ChatBubble {
  return {
    id: generateBubbleId('question'),
    type: 'question',
    timestamp: Date.now(),
    status: 'streaming',  // streaming = 等待用户回答
    questionRequestId: requestId,
    questions,
    questionAnswers: {},
    questionSubmitted: false
  };
}

/**
 * 创建 Thinking 气泡
 */
export function createThinkingBubble(content: string = ''): ChatBubble {
  return {
    id: generateBubbleId('thinking'),
    type: 'thinking',
    timestamp: Date.now(),
    status: 'streaming',
    content,
    thinkingStartTime: Date.now(),
    isExpanded: true
  };
}

// ========== 等待状态管理 ==========

/**
 * 创建默认的等待状态
 */
export function createDefaultWaitingState(): WaitingState {
  return {
    isWaiting: true,
    waitingVerb: '',
    waitingSince: Date.now()
  };
}

/**
 * 进入等待状态
 */
export function enterWaitingState(
  waitingState: WaitingState,
  getRandomVerb: () => string
): void {
  waitingState.isWaiting = true;
  waitingState.waitingVerb = getRandomVerb();
  waitingState.waitingSince = Date.now();
}

/**
 * 退出等待状态
 */
export function exitWaitingState(waitingState: WaitingState): void {
  waitingState.isWaiting = false;
}

/**
 * 检查气泡列表中是否有正在运行的 SubAgent
 */
export function hasStreamingSubAgent(bubbles: ChatBubble[]): boolean {
  return bubbles.some(b => b.type === 'subagent' && b.status === 'streaming');
}

// ========== 气泡查找函数 ==========

/**
 * 在气泡列表中递归查找指定 ID 的气泡
 */
export function findBubbleByIdDeep(
  bubbles: ChatBubble[],
  id: string
): ChatBubble | undefined {
  for (const bubble of bubbles) {
    if (bubble.id === id) return bubble;
    if (bubble.childBubbles) {
      const found = findBubbleByIdDeep(bubble.childBubbles, id);
      if (found) return found;
    }
  }
  return undefined;
}

/**
 * 获取气泡列表中最后一个气泡
 */
export function getLastBubble(bubbles: ChatBubble[]): ChatBubble | undefined {
  return bubbles[bubbles.length - 1];
}

/**
 * 获取气泡列表中最后一个指定类型的气泡
 */
export function getLastBubbleOfType(
  bubbles: ChatBubble[],
  type: ChatBubble['type']
): ChatBubble | undefined {
  for (let i = bubbles.length - 1; i >= 0; i--) {
    const bubble = bubbles[i];
    if (bubble?.type === type) return bubble;
  }
  return undefined;
}

/**
 * 获取最后一个正在流式传输的 Thinking 气泡
 */
export function getLastStreamingThinkingBubble(
  bubbles: ChatBubble[]
): ChatBubble | undefined {
  for (let i = bubbles.length - 1; i >= 0; i--) {
    const bubble = bubbles[i];
    if (bubble?.type === 'thinking' && bubble.status === 'streaming') {
      return bubble;
    }
  }
  return undefined;
}

/**
 * 获取最后一个正在流式传输的文本气泡
 */
export function getLastStreamingTextBubble(
  bubbles: ChatBubble[]
): ChatBubble | undefined {
  for (let i = bubbles.length - 1; i >= 0; i--) {
    const bubble = bubbles[i];
    if (bubble?.type === 'text' && bubble.status === 'streaming') {
      return bubble;
    }
  }
  return undefined;
}

// ========== 气泡状态更新 ==========

/**
 * 更新气泡状态
 */
export function updateBubbleStatus(
  bubble: ChatBubble,
  status: BubbleStatus
): void {
  bubble.status = status;
}

/**
 * 标记气泡为完成
 */
export function completeBubble(bubble: ChatBubble): void {
  bubble.status = 'completed';
}

/**
 * 标记气泡为失败
 */
export function failBubble(bubble: ChatBubble, error?: string): void {
  bubble.status = 'failed';
  if (error && bubble.type === 'tool_call') {
    bubble.toolError = error;
  }
}

/**
 * 完成 Thinking 气泡并计算时长
 */
export function completeThinkingBubble(bubble: ChatBubble): void {
  if (bubble.type !== 'thinking') return;
  bubble.status = 'completed';
  if (bubble.thinkingStartTime) {
    const durationSec = Math.round((Date.now() - bubble.thinkingStartTime) / 1000);
    bubble.thinkingDuration = durationSec + 's';
  }
}

/**
 * 折叠最后一个展开的 Thinking 气泡
 */
export function collapseLastThinkingBubble(bubbles: ChatBubble[]): void {
  for (let i = bubbles.length - 1; i >= 0; i--) {
    const bubble = bubbles[i];
    if (bubble?.type === 'thinking' && bubble.isExpanded) {
      bubble.isExpanded = false;
      return;
    }
  }
}

// ========== 工具调用气泡更新 ==========

/**
 * 更新工具调用的输出
 */
export function updateToolCallOutput(
  bubble: ChatBubble,
  output: string
): void {
  if (bubble.type === 'tool_call') {
    bubble.toolOutput = output;
  }
}

/**
 * 追加工具调用的输出
 */
export function appendToolCallOutput(
  bubble: ChatBubble,
  output: string
): void {
  if (bubble.type === 'tool_call') {
    bubble.toolOutput = (bubble.toolOutput || '') + output;
  }
}

// ========== SubAgent 气泡更新 ==========

/**
 * 更新 SubAgent 的执行结果
 */
export function updateSubAgentResult(
  bubble: ChatBubble,
  result: string
): void {
  if (bubble.type === 'subagent') {
    bubble.subAgentResult = result;
  }
}

/**
 * 向 SubAgent 气泡添加子气泡
 */
export function addChildBubble(
  parentBubble: ChatBubble,
  childBubble: ChatBubble
): void {
  if (parentBubble.type === 'subagent' && parentBubble.childBubbles) {
    parentBubble.childBubbles.push(childBubble);
  }
}

// ========== 后台任务状态管理 ==========

/**
 * 将 SubAgent 气泡标记为后台执行状态
 */
export function markAsBackground(bubble: ChatBubble): void {
  if (bubble.type === 'subagent') {
    bubble.status = 'background';
  }
}

/**
 * 查找所有处于 streaming 状态的 SubAgent 气泡
 */
export function findStreamingSubAgents(bubbles: ChatBubble[]): ChatBubble[] {
  return bubbles.filter(b => b.type === 'subagent' && b.status === 'streaming');
}
