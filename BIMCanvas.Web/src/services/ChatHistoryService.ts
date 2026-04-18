import { AGENT_API } from '../config/api';
import type { ChatHistoryResponse } from '../types/agent';

export class ChatHistoryService {
  private agentApiBase: string;

  constructor(agentApiBase: string = AGENT_API) {
    this.agentApiBase = agentApiBase;
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
