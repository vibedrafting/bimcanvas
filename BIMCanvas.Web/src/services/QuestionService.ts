import { AGENT_API } from '../config/api';
import type { InteractionEventListener, InteractionRecord } from '../types/agent';
import { getInteractionChannelService } from './InteractionChannelService';

export interface QuestionInteractionHandlers {
  onPushed?: (record: InteractionRecord) => void;
  onResolved?: (record: InteractionRecord) => void;
  onCancelled?: (record: InteractionRecord) => void;
  onExpired?: (record: InteractionRecord) => void;
}

export class QuestionService {
  private serverUrl: string;
  private handlers: QuestionInteractionHandlers = {};
  private listener: InteractionEventListener | null = null;

  constructor(serverUrl: string = AGENT_API) {
    this.serverUrl = serverUrl;
  }

  startListening(handlers: QuestionInteractionHandlers): void {
    this.handlers = handlers;
    if (this.listener) {
      return;
    }

    this.listener = ({ event, record }) => {
      if (record.kind !== 'question') {
        return;
      }

      switch (event) {
        case 'interaction.pushed':
          this.handlers.onPushed?.(record);
          break;
        case 'interaction.resolved':
          this.handlers.onResolved?.(record);
          break;
        case 'interaction.cancelled':
          this.handlers.onCancelled?.(record);
          break;
        case 'interaction.expired':
          this.handlers.onExpired?.(record);
          break;
      }
    };

    getInteractionChannelService(this.serverUrl).startListening(this.listener);
  }

  stopListening(): void {
    if (!this.listener) {
      return;
    }

    getInteractionChannelService(this.serverUrl).stopListening(this.listener);
    this.listener = null;
  }

  async restorePending(windowIds: string[]): Promise<InteractionRecord[]> {
    const channel = getInteractionChannelService(this.serverUrl);
    const restored: InteractionRecord[] = [];

    for (const windowId of windowIds) {
      const interactions = await channel.queryPending(windowId);
      for (const record of interactions) {
        if (record.kind !== 'question') {
          continue;
        }
        restored.push(record);
        this.handlers.onPushed?.(record);
      }
    }

    return restored;
  }

  async submitAnswer(
    requestId: string,
    answers: Record<string, string>
  ): Promise<InteractionRecord> {
    return getInteractionChannelService(this.serverUrl).submitInteraction(requestId, { answers });
  }

  async cancelQuestion(
    requestId: string,
    cancelReason: string = 'question_cancelled'
  ): Promise<InteractionRecord> {
    return getInteractionChannelService(this.serverUrl).cancelInteraction(requestId, cancelReason);
  }
}

let instance: QuestionService | null = null;

export function getQuestionService(serverUrl?: string): QuestionService {
  if (!instance) {
    instance = new QuestionService(serverUrl);
  }
  return instance;
}
