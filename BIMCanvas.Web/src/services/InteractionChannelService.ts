import { AGENT_API } from '../config/api';
import type {
  BackgroundTaskRecord,
  ChannelEventName,
  InteractionEventEnvelope,
  InteractionEventListener,
  InteractionQueryResponse,
  InteractionRecord
} from '../types/agent';

const INTERACTION_EVENT_NAMES: ChannelEventName[] = [
  'interaction.pushed',
  'interaction.resolved',
  'interaction.cancelled',
  'interaction.expired',
  'background_task.completed'
];

export class InteractionChannelService {
  private serverUrl: string;
  private eventSource: EventSource | null = null;
  private listeners = new Set<InteractionEventListener>();

  constructor(serverUrl: string = AGENT_API) {
    this.serverUrl = serverUrl;
  }

  startListening(listener: InteractionEventListener): void {
    this.listeners.add(listener);
    if (this.eventSource) {
      return;
    }

    this.eventSource = new EventSource(`${this.serverUrl}/api/interaction/events`);
    for (const eventName of INTERACTION_EVENT_NAMES) {
      this.eventSource.addEventListener(eventName, (event) => {
        this.dispatchEvent(eventName, event as MessageEvent);
      });
    }

    this.eventSource.onopen = () => {
      console.log('[InteractionChannelService] SSE connection opened');
    };

    this.eventSource.onerror = (error) => {
      console.error('[InteractionChannelService] SSE connection error:', error);
    };
  }

  stopListening(listener?: InteractionEventListener): void {
    if (listener) {
      this.listeners.delete(listener);
    } else {
      this.listeners.clear();
    }

    if (this.listeners.size === 0 && this.eventSource) {
      this.eventSource.close();
      this.eventSource = null;
      console.log('[InteractionChannelService] SSE connection closed');
    }
  }

  async queryPending(windowId: string): Promise<InteractionRecord[]> {
    const response = await fetch(`${this.serverUrl}/api/interaction?windowId=${encodeURIComponent(windowId)}`);
    if (!response.ok) {
      throw new Error(`Failed to query interactions for window ${windowId}: HTTP ${response.status}`);
    }

    const payload = await response.json() as InteractionQueryResponse;
    return Array.isArray(payload.interactions) ? payload.interactions : [];
  }

  async submitInteraction(
    interactionId: string,
    resolutionPayload: Record<string, unknown> = {}
  ): Promise<InteractionRecord> {
    const response = await fetch(`${this.serverUrl}/api/interaction/${interactionId}/submit`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ resolutionPayload })
    });

    if (!response.ok) {
      throw new Error(`Failed to submit interaction ${interactionId}: HTTP ${response.status}`);
    }

    const payload = await response.json() as { interaction?: InteractionRecord };
    if (!payload.interaction) {
      throw new Error(`Interaction submit response missing interaction payload: ${interactionId}`);
    }
    return payload.interaction;
  }

  async cancelInteraction(
    interactionId: string,
    cancelReason?: string
  ): Promise<InteractionRecord> {
    const response = await fetch(`${this.serverUrl}/api/interaction/${interactionId}/cancel`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(cancelReason ? { cancelReason } : {})
    });

    if (!response.ok) {
      throw new Error(`Failed to cancel interaction ${interactionId}: HTTP ${response.status}`);
    }

    const payload = await response.json() as { interaction?: InteractionRecord };
    if (!payload.interaction) {
      throw new Error(`Interaction cancel response missing interaction payload: ${interactionId}`);
    }
    return payload.interaction;
  }

  private dispatchEvent(eventName: ChannelEventName, event: MessageEvent): void {
    try {
      const record = JSON.parse(event.data) as InteractionRecord | BackgroundTaskRecord;
      const envelope: InteractionEventEnvelope = { event: eventName, record };
      for (const listener of this.listeners) {
        listener(envelope);
      }
    } catch (error) {
      console.error(`[InteractionChannelService] Failed to parse ${eventName}:`, error, event.data);
    }
  }
}

const instances = new Map<string, InteractionChannelService>();

export function getInteractionChannelService(serverUrl: string = AGENT_API): InteractionChannelService {
  const normalizedUrl = serverUrl || AGENT_API;
  let instance = instances.get(normalizedUrl);
  if (!instance) {
    instance = new InteractionChannelService(normalizedUrl);
    instances.set(normalizedUrl, instance);
  }
  return instance;
}
