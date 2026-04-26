import type { ChatBubble, WaitingState } from './agent';
import type { ChatAttachmentRef } from './chatAttachment';

export interface ChatMessage {
  role: 'user' | 'ai';
  isStreaming?: boolean;
  startTime?: number;
  endTime?: number;
  /** @deprecated Thinking 现在存储在 type='thinking' 的气泡中 */
  thinking?: string;
  /** @deprecated Thinking 时长现在存储在各个 thinking 气泡的 thinkingDuration 中 */
  thinkingDuration?: string;
  bubbles: ChatBubble[];
  waitingState: WaitingState;
}

export type TodoProgressItemStatus = 'pending' | 'in_progress' | 'completed';
export type TodoProgressPanelStatus = 'running' | 'completed' | 'failed' | 'interrupted' | 'ended';

export interface TodoProgressItem {
  content: string;
  status: TodoProgressItemStatus;
  activeForm?: string;
}

export interface TodoProgressState {
  toolCallId?: string;
  turnId?: string;
  todos: TodoProgressItem[];
  status: TodoProgressPanelStatus;
  isCollapsed: boolean;
  updatedAt: number;
  message?: string;
}

export interface ChatWindow {
  id: string;
  name: string;
  branchId: string;
  messages: ChatMessage[];
  isPrimary: boolean;
  worktreeName?: string;
  worktreePath?: string;
  isLoading?: boolean;
  error?: string | null;
  inputMessage: string;
  draftMessageId: string;
  isStreaming: boolean;
  todoProgress?: TodoProgressState | null;
  pendingAttachments: ChatAttachmentRef[];
  scrollPosition: number;
  expandedThinking: Record<number, boolean>;
  shouldAutoScroll: boolean;
}

export interface ModelOption {
  id: string;
  label: string;
}

export interface ThinkingLevel {
  id: string;
  label: string;
}

export interface EffortLevel {
  id: string;
  label: string;
}

export interface ProposalMetric {
  storage: string;
  flow: string;
}

export interface Proposal {
  id: string;
  name: string;
  tags: string[];
  metrics: ProposalMetric;
  insight: string;
  color: string;
  thumbnailPattern: string;
}

export interface ContextOption {
  id: string;
  label: string;
}

export interface ContextOptions {
  regulations: ContextOption[];
  attachments: ContextOption[];
}

export interface DropdownPosition {
  top: number;
  left?: number;
  right?: number;
}
