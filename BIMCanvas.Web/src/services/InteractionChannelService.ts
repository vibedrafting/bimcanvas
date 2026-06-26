import { AGENT_API } from '../config/api';
import { createLogger } from '../utils/logger';
import type {
  BackgroundTaskRecord,
  ChannelEventName,
  InteractionEventEnvelope,
  InteractionEventListener,
  InteractionQueryResponse,
  InteractionRecord,
  WorkflowProgressRecord
} from '../types/agent';

const INTERACTION_EVENT_NAMES: ChannelEventName[] = [
  'interaction.pushed',
  'interaction.resolved',
  'interaction.cancelled',
  'interaction.expired',
  'background_task.completed',
  'background_task.progress',
  'background_task.turn_started',
  'background_task.turn_chunk'
];

const log = createLogger('SYS');

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
      log.debug('SSE connection opened');
    };

    this.eventSource.onerror = (error) => {
      log.error('SSE connection error', { error });
    };
  }

  /** SSE 通道是否在线——后台任务心跳静默 sweeper 的守卫（断连期间心跳静默≠任务结束） */
  isConnected(): boolean {
    return this.eventSource?.readyState === EventSource.OPEN;
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
      log.debug('SSE connection closed');
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
      const record = JSON.parse(event.data) as InteractionRecord | BackgroundTaskRecord | WorkflowProgressRecord;
      const envelope: InteractionEventEnvelope = { event: eventName, record };
      for (const listener of this.listeners) {
        listener(envelope);
      }
    } catch (error) {
      log.error('failed to parse event', { eventName, error, data: event.data });
    }
  }
}

const instances = new Map<string, InteractionChannelService>();

/** 任一 interaction SSE 通道在线即 true（实践中单实例 AGENT_API）。无实例 = 从未监听 = 视为断连。 */
export function isInteractionChannelConnected(): boolean {
  for (const instance of instances.values()) {
    if (instance.isConnected()) return true;
  }
  return false;
}

export function getInteractionChannelService(serverUrl: string = AGENT_API): InteractionChannelService {
  const normalizedUrl = serverUrl || AGENT_API;
  let instance = instances.get(normalizedUrl);
  if (!instance) {
    instance = new InteractionChannelService(normalizedUrl);
    instances.set(normalizedUrl, instance);
  }
  return instance;
}
