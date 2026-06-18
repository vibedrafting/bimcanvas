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

  /** 按 sessionId 加载某历史会话事件流（只读回放），返回与 getHistory 同形。 */
  async loadSession(projectPath: string, sessionId: string): Promise<ChatHistoryResponse> {
    const url = `${this.agentApiBase}/api/history/session`
      + `?projectPath=${encodeURIComponent(projectPath)}&sessionId=${encodeURIComponent(sessionId)}`;
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Failed to load history session ${sessionId}: HTTP ${response.status}`);
    }
    return response.json() as Promise<ChatHistoryResponse>;
  }

  async getHistory(windowId: string, projectPath?: string): Promise<ChatHistoryResponse> {
    // projectPath 传给后端用于内存无活跃会话时(会话已关闭 / Agent 重启)回退磁盘 .history。
    let url = `${this.agentApiBase}/api/history?windowId=${encodeURIComponent(windowId)}`;
    if (projectPath) {
      url += `&projectPath=${encodeURIComponent(projectPath)}`;
    }
    const response = await fetch(url);
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
