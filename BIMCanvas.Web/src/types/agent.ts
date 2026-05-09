// ==========================================
// BIMCanvas Agent Type Definitions
// SubAgent/Task 渲染相关类型
// ==========================================

import type { ChatAttachmentRef } from './chatAttachment';

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
export type ErrorType = 'recoverable' | 'blocking' | 'api_error' | 'sdk_error';
export type MainStreamEventType =
  | 'thinking.delta'
  | 'thinking.completed'
  | 'text.delta'
  | 'text.completed'
  | 'subtask.started'
  | 'subtask.completed'
  | 'tool.started'
  | 'tool.output'
  | 'tool.completed'
  | 'turn.completed'
  | 'turn.failed';

export interface MainStreamEnvelope<TPayload = Record<string, unknown>> {
  eventId: string;
  sessionId: string;
  turnId: string;
  eventType: MainStreamEventType | string;
  payload: TPayload;
  timestamp?: string;
  subtaskId?: string;
  toolCallId?: string;
}

export type CapabilityLevel = 'required' | 'optional' | 'unsupported';

export interface RuntimeCapabilityMatrixRow {
  capabilityKey: string;
  level: CapabilityLevel;
  providerMapping?: string | null;
  frontendFallback?: string | null;
  notes?: string | null;
}

export type RuntimeCapabilityMap = Record<string, RuntimeCapabilityMatrixRow | undefined>;

export type InteractionKind = 'question' | 'screenshot' | 'permission' | string;
export type InteractionStatus = 'pending' | 'resolved' | 'cancelled' | 'expired';
export type InteractionEventName =
  | 'interaction.pushed'
  | 'interaction.resolved'
  | 'interaction.cancelled'
  | 'interaction.expired';

export interface InteractionRecord {
  interactionId: string;
  sessionId: string;
  turnId: string;
  windowId: string;
  kind: InteractionKind;
  blocking: boolean;
  status: InteractionStatus;
  resumeToken: string;
  requestPayload: Record<string, any>;
  resolutionPayload?: Record<string, any> | null;
  createdAt?: string | null;
  updatedAt?: string | null;
  expiresAt?: string | null;
  cancelReason?: string | null;
}

export interface InteractionEventEnvelope {
  event: InteractionEventName;
  record: InteractionRecord;
}

export type InteractionEventListener = (event: InteractionEventEnvelope) => void;

export interface InteractionQueryResponse {
  windowId: string;
  sessionId?: string | null;
  includeTerminal?: boolean;
  interactions: InteractionRecord[];
}

export interface ChatHistorySessionSnapshot {
  sessionId: string;
  runtimeId: string;
  runtimeVersion: string;
  windowId: string;
  projectPath: string;
  worktreePath?: string | null;
  status: string;
  activeTurnId?: string | null;
  createdAt?: string | null;
  lastActiveAt?: string | null;
  closedAt?: string | null;
}

export interface UserMessageHistoryEntry {
  entryId: string;
  sessionId: string;
  turnId: string;
  windowId: string;
  kind: 'user_message';
  createdAt?: string | null;
  clientMessageId?: string | null;
  message?: string | null;
  attachments?: ChatAttachmentRef[];
}

export interface AssistantEventHistoryEntry {
  entryId: string;
  sessionId: string;
  turnId: string;
  windowId: string;
  kind: 'assistant_event';
  createdAt?: string | null;
  event: Record<string, any>;
}

export type ChatHistoryEntry = UserMessageHistoryEntry | AssistantEventHistoryEntry;

export interface ChatHistoryResponse {
  windowId: string;
  session?: ChatHistorySessionSnapshot | null;
  sessionId?: string | null;
  sessionStatus?: string | null;
  history: ChatHistoryEntry[];
  interactions: InteractionRecord[];
}

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
  toolOutput?: string;
  success: boolean;
  error?: string;
}

export interface TaskOutputPollingEvent {
  type: 'task_output_polling';
  taskId: string;
  timeout: number;
}

export type LegacyAgentSSEEvent =
  | SubAgentStartEvent
  | SubAgentCompleteEvent
  | ToolCallStartEvent
  | ToolCallOutputEvent
  | ToolCallCompleteEvent
  | TaskOutputPollingEvent;

export type AgentSSEEvent = LegacyAgentSSEEvent | MainStreamEnvelope;

// ========== 时间线气泡模型（Timeline Bubble Model）==========

/** 气泡类型 */
export type BubbleType = 'text' | 'tool_call' | 'subagent' | 'thinking' | 'question';

/** 气泡状态 */
export type BubbleStatus = 'pending' | 'streaming' | 'completed' | 'failed' | 'background';

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
  /** 附带的图片（Base64 格式，用户消息专有） */
  images?: string[];
  /** 资源化附件引用（用户消息专有） */
  attachments?: ChatAttachmentRef[];
  /** 发送时刻的用户选择上下文快照（用户消息专有，只读） */
  sentContext?: SentContextSnapshot;

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

  // ===== ThinkingBubble 专有 =====
  /** Thinking 时长显示 (如 "3s") */
  thinkingDuration?: string;
  /** Thinking 开始时间 (ms) */
  thinkingStartTime?: number;
  /** 展开/折叠状态（Thinking 气泡专用） */
  isExpanded?: boolean;

  // ===== SubAgentBubble 专有 =====
  /** SubAgent 名称 */
  subAgentName?: string;
  /** SubAgent 类型 */
  subAgentType?: SubAgentType;
  /** SubAgent 执行结果 */
  subAgentResult?: string;
  /** SubAgent 内部的气泡（工具调用等） */
  childBubbles?: ChatBubble[];

  // ===== QuestionBubble 专有 =====
  /** 问题请求 ID（用于提交答案） */
  questionRequestId?: string;
  /** 问题列表 */
  questions?: UserQuestion[];
  /** 用户已选答案（key=问题文本，value=选中label） */
  questionAnswers?: Record<string, string>;
  /** 是否已提交答案 */
  questionSubmitted?: boolean;
}

/**
 * 用户消息发送瞬间的上下文快照（仅 UI 渲染，不参与 server payload）。
 * 字段全部 optional，老历史消息缺字段时模板 v-if 安全降级。
 *
 * 设计：存"已折叠的展示文本 + 计数"而非对象 id/zoneId/geometry。
 * 跨 session 重载时即使原对象已删/改，气泡仍能稳定显示当时的语境。
 */
export interface SentContextSnapshot {
  /** scope chip：推断区域名 / "全局"。isGlobal 让模板省字符串比对 */
  scope: { text: string; isGlobal: boolean };
  /** selection chip：复用 selectionDisplayText 的折叠文本（>3 个按类型汇总） */
  selection?: { text: string; count: number };
  /** spatial marks：count + 前 3 个 label。模板按 count<=3 决定列 label 还是折叠 */
  spatialMarks?: { count: number; labels: string[] };
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

// ========== AskUserQuestion 类型 ==========

/** AskUserQuestion 问题选项 */
export interface UserQuestionOption {
  label: string;
  description?: string;
}

/** AskUserQuestion 单个问题 */
export interface UserQuestion {
  question: string;
  header: string;
  options: UserQuestionOption[];
  multiSelect: boolean;
}

/** Agent SSE 问题请求事件（兼容别名，内部实际对应 question interaction） */
export interface QuestionRequestEvent {
  requestId: string;
  questions: UserQuestion[];
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
    const bubble = bubbles[i];
    if (bubble?.type === type) return bubble;
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
