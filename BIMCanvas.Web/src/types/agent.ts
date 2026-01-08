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
