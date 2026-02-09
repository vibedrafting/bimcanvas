import type { ChatBubble, WaitingState } from './agent';

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
  isStreaming: boolean;
  pendingImages: string[];
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
  zones: ContextOption[];
  regulations: ContextOption[];
  attachments: ContextOption[];
}

export interface DropdownPosition {
  top: number;
  left?: number;
  right?: number;
}
