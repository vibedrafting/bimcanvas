import { AGENT_API } from '../config/api';
import type { ChatHistoryResponse } from '../types/agent';

export class ChatHistoryService {
  private agentApiBase: string;

  constructor(agentApiBase: string = AGENT_API) {
    this.agentApiBase = agentApiBase;
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
