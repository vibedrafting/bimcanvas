import { AGENT_API } from '../config/api';
import type { BackgroundTaskRecord, InteractionEventListener } from '../types/agent';
import { getInteractionChannelService } from './InteractionChannelService';

export interface BackgroundTaskHandlers {
  /** 后台任务（Workflow）完成时触发，record 携带 summary / status 等 */
  onCompleted?: (record: BackgroundTaskRecord) => void;
}

/**
 * 后台任务完成事件订阅服务。
 *
 * 复用 interaction SSE 通道（与 QuestionService / ScreenshotService 同源），
 * 只关心 `background_task.completed` 事件。用于修复"后台 Workflow 完成后
 * 总结不主动显示、需用户再次发消息才刷新"的错位 bug：完成事件带外推送，
 * 由 useBackgroundTask 注入一条 AI 气泡，无需用户回合。
 */
export class BackgroundTaskService {
  private serverUrl: string;
  private handlers: BackgroundTaskHandlers = {};
  private listener: InteractionEventListener | null = null;

  constructor(serverUrl: string = AGENT_API) {
    this.serverUrl = serverUrl;
  }

  startListening(handlers: BackgroundTaskHandlers): void {
    this.handlers = handlers;
    if (this.listener) {
      return;
    }

    this.listener = ({ event, record }) => {
      if (event !== 'background_task.completed' || record.kind !== 'background_task') {
        return;
      }
      // 运行时已由 kind 守卫保证，但 InteractionRecord.kind 含 string 字面量无法被 TS
      // 自动排除，这里显式断言收窄到 BackgroundTaskRecord。
      this.handlers.onCompleted?.(record as BackgroundTaskRecord);
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

  /** 断线重连补发：拉取各窗口当前 session 已留存的后台任务完成事件 */
  async restorePending(windowIds: string[]): Promise<BackgroundTaskRecord[]> {
    const channel = getInteractionChannelService(this.serverUrl);
    const restored: BackgroundTaskRecord[] = [];

    for (const windowId of windowIds) {
      const tasks = await channel.queryBackgroundTasks(windowId);
      for (const record of tasks) {
        restored.push(record);
        this.handlers.onCompleted?.(record);
      }
    }

    return restored;
  }
}

const instances = new Map<string, BackgroundTaskService>();

export function getBackgroundTaskService(serverUrl?: string): BackgroundTaskService {
  const normalizedUrl = serverUrl || AGENT_API;
  let instance = instances.get(normalizedUrl);
  if (!instance) {
    instance = new BackgroundTaskService(normalizedUrl);
    instances.set(normalizedUrl, instance);
  }
  return instance;
}
