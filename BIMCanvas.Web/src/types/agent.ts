// ==========================================
// BIMCanvas Agent Type Definitions
// SubAgent/Task 渲染相关类型
// ==========================================

// ========== 状态类型 ==========

export type ToolCallStatus = 'pending' | 'running' | 'completed' | 'failed';
export type SubAgentStatus = 'pending' | 'running' | 'completed' | 'failed';

// ========== SubAgent 类型标签 ==========

export const SubAgentType = {
  Explore: 'Explore',
  Plan: 'Plan',
  GeneralPurpose: 'general-purpose'
} as const;
export type SubAgentType = typeof SubAgentType[keyof typeof SubAgentType] | string;

// ========== 工具调用 ==========

export interface ToolCall {
  /** 工具调用唯一标识 */
  id: string;
  /** 工具名称 (Glob, Bash, Read, Grep, Edit, Write 等) */
  toolName: string;
  /** 工具调用描述 */
  description?: string;
  /** 工具调用参数 */
  params: Record<string, unknown>;
  /** 工具输出内容 */
  output?: string;
  /** 调用状态 */
  status: ToolCallStatus;
  /** 开始时间戳 (ms) */
  startTime?: number;
  /** 结束时间戳 (ms) */
  endTime?: number;
  /** 错误信息 */
  error?: string;
  /** UI 展开状态 */
  isExpanded?: boolean;
}

// ========== SubAgent/Task ==========

export interface SubAgent {
  /** SubAgent 唯一标识 */
  id: string;
  /** 任务名称 (如 "探索项目结构") */
  name: string;
  /** SubAgent 类型 (Explore, Plan, general-purpose) */
  type: SubAgentType;
  /** 任务输入/描述 */
  input?: string;
  /** 执行状态 */
  status: SubAgentStatus;
  /** 包含的工具调用列表 */
  toolCalls: ToolCall[];
  /** 执行结果 */
  result?: string;
  /** 开始时间戳 (ms) */
  startTime?: number;
  /** 结束时间戳 (ms) */
  endTime?: number;
  /** UI 展开状态 */
  isExpanded?: boolean;
}

// ========== SSE 事件类型 ==========

// 错误分类类型
export type ErrorType = 'recoverable' | 'blocking';

export interface TextStreamEvent {
  type: 'text' | 'text_complete';
  content: string;
  /** 错误分类：recoverable（可恢复）或 blocking（阻塞性）*/
  errorType?: ErrorType;
  /** 提取的错误内容（不含 XML 标签，blocking 类型）*/
  errorContent?: string;
  /** 被过滤的内容（调试用，recoverable 类型）*/
  hiddenContent?: string;
}

export interface SubAgentStartEvent {
  type: 'subagent_start';
  subAgentId: string;
  subAgentName: string;
  subAgentType: SubAgentType;
}

export interface SubAgentCompleteEvent {
  type: 'subagent_complete';
  subAgentId: string;
  content?: string;  // 执行结果摘要
  success?: boolean; // 是否成功
  error?: string;    // 失败时的错误信息
}

export interface ToolCallStartEvent {
  type: 'tool_call_start';
  subAgentId?: string;  // 可选：SubAgent 内的工具调用有此字段，主 Agent 工具调用无此字段
  toolCallId: string;
  toolName: string;
  toolDescription?: string;
  toolParams?: Record<string, unknown>;
}

export interface ToolCallOutputEvent {
  type: 'tool_call_output';
  toolCallId: string;
  toolOutput: string;
}

export interface ToolCallCompleteEvent {
  type: 'tool_call_complete';
  toolCallId: string;
  success: boolean;
  error?: string;
}

export type AgentSSEEvent =
  | SubAgentStartEvent
  | SubAgentCompleteEvent
  | ToolCallStartEvent
  | ToolCallOutputEvent
  | ToolCallCompleteEvent;

// ========== 时间线气泡模型（Timeline Bubble Model）==========

/** 气泡类型 */
export type BubbleType = 'text' | 'tool_call' | 'subagent';

/** 气泡状态 */
export type BubbleStatus = 'pending' | 'streaming' | 'completed' | 'failed';

/**
 * 统一的消息气泡接口
 * 所有 AI 产出都是同一类型的元素，按时间戳排序显示
 */
export interface ChatBubble {
  /** 唯一标识 */
  id: string;
  /** 气泡类型 */
  type: BubbleType;
  /** 时间戳（排序依据，ms） */
  timestamp: number;
  /** 状态 */
  status: BubbleStatus;

  // ===== TextBubble 专有 =====
  /** 文本内容 */
  content?: string;

  // ===== ToolCallBubble 专有 =====
  /** 工具名称 */
  toolName?: string;
  /** 工具描述 */
  toolDescription?: string;
  /** 工具调用参数 */
  toolParams?: Record<string, unknown>;
  /** 工具输出内容 */
  toolOutput?: string;
  /** 工具错误信息 */
  toolError?: string;

  // ===== SubAgentBubble 专有 =====
  /** SubAgent 名称 */
  subAgentName?: string;
  /** SubAgent 类型 */
  subAgentType?: SubAgentType;
  /** SubAgent 执行结果 */
  subAgentResult?: string;
  /** SubAgent 内部的气泡（工具调用等） */
  childBubbles?: ChatBubble[];
}

/**
 * 等待状态
 * 用于在气泡之间显示等待提示词
 */
export interface WaitingState {
  /** 是否处于等待状态 */
  isWaiting: boolean;
  /** 等待提示词（如 "思考中"、"分析代码"） */
  waitingVerb: string;
  /** 等待开始时间（ms） */
  waitingSince: number;
}

// ========== 工具函数 ==========

/**
 * 在消息的 subAgents 中查找指定的 ToolCall
 */
export function findToolCallInSubAgents(
  subAgents: SubAgent[] | undefined,
  toolCallId: string
): ToolCall | undefined {
  if (!subAgents) return undefined;
  for (const sa of subAgents) {
    const tc = sa.toolCalls.find(t => t.id === toolCallId);
    if (tc) return tc;
  }
  return undefined;
}

/**
 * 计算 SubAgent 执行时长（秒）
 */
export function getSubAgentDuration(subAgent: SubAgent): string | undefined {
  if (!subAgent.startTime) return undefined;
  const endTime = subAgent.endTime || Date.now();
  const durationSec = Math.round((endTime - subAgent.startTime) / 1000);
  return `${durationSec}s`;
}

/**
 * 获取状态对应的颜色类名
 */
export function getStatusColorClass(status: SubAgentStatus | ToolCallStatus): string {
  switch (status) {
    case 'pending': return 'status-pending';
    case 'running': return 'status-running';
    case 'completed': return 'status-completed';
    case 'failed': return 'status-failed';
    default: return 'status-pending';
  }
}

// ========== 气泡工具函数 ==========

/**
 * 在气泡列表中查找指定 ID 的气泡（递归搜索 childBubbles）
 */
export function findBubbleById(
  bubbles: ChatBubble[],
  id: string
): ChatBubble | undefined {
  for (const bubble of bubbles) {
    if (bubble.id === id) return bubble;
    if (bubble.childBubbles) {
      const found = findBubbleById(bubble.childBubbles, id);
      if (found) return found;
    }
  }
  return undefined;
}

/**
 * 获取气泡列表中最后一个指定类型的气泡
 */
export function findLastBubbleByType(
  bubbles: ChatBubble[],
  type: BubbleType
): ChatBubble | undefined {
  for (let i = bubbles.length - 1; i >= 0; i--) {
    if (bubbles[i].type === type) return bubbles[i];
  }
  return undefined;
}

/**
 * 计算气泡执行时长
 */
export function getBubbleDuration(bubble: ChatBubble): string | undefined {
  if (!bubble.timestamp) return undefined;
  if (bubble.status === 'streaming') return undefined;
  return undefined;
}
