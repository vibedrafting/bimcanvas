import { AGENT_API } from '../config/api';
import type { BackgroundTaskRecord, InteractionEventListener, WorkflowProgressRecord, WorkflowPhasesRecord } from '../types/agent';
import { getInteractionChannelService } from './InteractionChannelService';

export interface BackgroundTaskHandlers {
  /** 后台任务（Workflow）完成时触发，record 携带 summary / status 等 */
  onCompleted?: (record: BackgroundTaskRecord) => void;
  /** 后台 Workflow 进度（detach 后实时心跳），record 携带 usage / lastToolName 等 */
  onProgress?: (record: WorkflowProgressRecord) => void;
  /** 后台 Workflow 阶段预声明（启动即推完整 meta.phases，供运行态全阶段渲染） */
  onPhases?: (record: WorkflowPhasesRecord) => void;
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
      if (event === 'background_task.progress' && record.kind === 'workflow_phases') {
        this.handlers.onPhases?.(record as WorkflowPhasesRecord);
        return;
      }
      if (event === 'background_task.progress' && record.kind === 'workflow_progress') {
        this.handlers.onProgress?.(record as WorkflowProgressRecord);
        return;
      }
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
