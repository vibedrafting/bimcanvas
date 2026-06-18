import { AGENT_API } from '../config/api';
import type { ChatHistoryResponse } from '../types/agent';

/** .history/index.json 里一条会话摘要（历史面板列表项）。 */
export interface ChatHistorySessionListItem {
  sessionId: string;
  windowId?: string;
  projectPath?: string;
  worktreePath?: string | null;
  title?: string;
  createdAt?: string | null;
  lastActiveAt?: string | null;
  closedAt?: string | null;
  status?: string;
  turnCount?: number;
  sdkSessionId?: string | null;
}

/** 激活历史对话的响应：getHistory 同形 + AI 上下文状态。 */
export interface ConversationActivateResponse extends ChatHistoryResponse {
  /** 'live'=SDK 上下文已 resume(模型记得);'expired'=transcript 缺失,仅显示历史。 */
  contextStatus?: 'live' | 'expired';
}

export class ChatHistoryService {
  private agentApiBase: string;

  constructor(agentApiBase: string = AGENT_API) {
    this.agentApiBase = agentApiBase;
  }

  /** 列出项目历史会话（按 lastActiveAt 倒序）。 */
  async listSessions(projectPath: string): Promise<ChatHistorySessionListItem[]> {
    const url = `${this.agentApiBase}/api/history/sessions?projectPath=${encodeURIComponent(projectPath)}`;
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Failed to list history sessions: HTTP ${response.status}`);
    }
    const data = await response.json() as { sessions?: ChatHistorySessionListItem[] };
    return data.sessions ?? [];
  }

  /**
   * 激活(切换/恢复)一段历史对话:后端拆当前 agent → resume 该对话的 SDK 会话 → 返回显示历史。
   * 激活后该窗口的输入即打进这段对话(隔离 + 记忆)。contextStatus=expired 表示 AI 上下文已不可恢复(仅显示历史)。
   */
  async activateConversation(params: {
    windowId: string;
    conversationId: string;
    projectPath: string;
    model: string;
    effort?: string;
    thinking?: string;
  }): Promise<ConversationActivateResponse> {
    const response = await fetch(`${this.agentApiBase}/api/conversation/activate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(params)
    });
    if (!response.ok) {
      const detail = await response.json().catch(() => ({}));
      throw new Error((detail as any)?.error || `Failed to activate conversation: HTTP ${response.status}`);
    }
    return response.json() as Promise<ConversationActivateResponse>;
  }

  /** 开始新对话:后端拆掉窗口当前 agent/session,下一条消息创建全新会话。 */
  async newConversation(windowId: string): Promise<void> {
    const response = await fetch(`${this.agentApiBase}/api/conversation/new`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ windowId })
    });
    if (!response.ok) {
      throw new Error(`Failed to start new conversation: HTTP ${response.status}`);
    }
  }

  async getHistory(windowId: string): Promise<ChatHistoryResponse> {
    const response = await fetch(`${this.agentApiBase}/api/history?windowId=${encodeURIComponent(windowId)}`);
    if (!response.ok) {
      throw new Error(`Failed to fetch history for window ${windowId}: HTTP ${response.status}`);
    }

    return response.json() as Promise<ChatHistoryResponse>;
  }
}

const instances = new Map<string, ChatHistoryService>();

export function getChatHistoryService(agentApiBase: string = AGENT_API): ChatHistoryService {
  const normalizedBase = agentApiBase || AGENT_API;
  let instance = instances.get(normalizedBase);
  if (!instance) {
    instance = new ChatHistoryService(normalizedBase);
    instances.set(normalizedBase, instance);
  }
  return instance;
}
