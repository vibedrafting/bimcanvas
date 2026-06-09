import { nextTick, ref } from 'vue';
import type { Ref } from 'vue';
import type {
  ChatMessage,
  ChatWindow,
  EffortLevel,
  ModelOption,
  QueuedChatDraft,
  SpatialMark,
  ThinkingLevel,
  TodoProgressItem,
  TodoProgressPanelStatus,
  TodoProgressState
} from '../../types/aiCommandCenter';
import type { ChatAttachmentRef } from '../../types/chatAttachment';
import type { WaitingState, ChatBubble, ChatHistoryEntry, ChatHistoryResponse, InteractionRecord, SentContextSnapshot } from '../../types/agent';
import { ProjectService } from '../../services/ProjectService';
import { ChatAttachmentService, createDraftMessageId } from '../../services/ChatAttachmentService';
import { getChatHistoryService } from '../../services/ChatHistoryService';
import { useSystemStore } from '../../stores/systemStore';
import {
  createTextBubble,
  createToolCallBubble,
  createSubAgentBubble,
  createThinkingBubble,
  createQuestionBubble,
  getLastStreamingThinkingBubble,
  completeThinkingBubble,
  collapseLastThinkingBubble,
  enterWaitingState,
  exitWaitingState,
  hasStreamingSubAgent,
  findBubbleByIdDeep,
  getLastStreamingTextBubble,
  completeBubble,
  failBubble,
  appendToolCallOutput,
  updateSubAgentResult,
  markAsBackground,
  findStreamingSubAgents
} from '../../utils/bubbleManager';
import { WAITING_VERBS } from '../../constants/aiCommandCenter';
import { useWorkflowProgress } from './useWorkflowProgress';

// Workflow 进度单例：把已解析的 subtask/tool 事件同步喂给 Task 页 workflow 视图。
// 复用现有 case 的解析结果，不新增 SSE 解析。
const workflowProgress = useWorkflowProgress();

interface ChatStreamOptions {
  agentApiBase: string;
  windows: Ref<ChatWindow[]>;
  activeWindowId: Ref<string>;
  activeWindow: Ref<ChatWindow | undefined>;
  addMessage: (message: ChatMessage) => number;
  addMessageToWindow: (windowId: string, message: ChatMessage) => number;
  getWindowMessage: (windowId: string, msgIndex: number) => ChatMessage | undefined;
  pendingAttachments: Ref<ChatAttachmentRef[]>;
  currentModel: Ref<ModelOption | null>;
  currentEffort: Ref<EffortLevel>;
  currentThinking: Ref<ThinkingLevel>;
  scrollToBottom: (options?: { force?: boolean; windowId?: string }) => void;
  fetchAgentConfig: () => Promise<void>;
  hasFallback?: (key: string) => boolean;
  buildContextPayload?: (spatialMarks?: SpatialMark[]) => Record<string, any> | undefined;
  buildContextSnapshot?: (spatialMarks?: SpatialMark[]) => SentContextSnapshot | undefined;
}

// 用于中止请求的 AbortController 管理，每个窗口独立一条流。
const currentAbortControllers = new Map<string, AbortController>();
const preserveDeliveredStateOnAbort = new Set<string>();
const PLACEHOLDER_ASSISTANT_TEXTS = new Set(['(no content)', '[no content]']);
const LEGACY_EVENT_TYPE_MAP: Record<string, string> = {
  thinking: 'thinking.delta',
  thinking_complete: 'thinking.completed',
  text: 'text.delta',
  text_complete: 'text.completed',
  subagent_start: 'subtask.started',
  subagent_complete: 'subtask.completed',
  // WP-Web: SDK 0.2.87 TaskProgressMessage / RateLimitEvent
  subagent_progress: 'subtask.progress',
  rate_limit: 'runtime.rate_limit',
  tool_call_start: 'tool.started',
  tool_call_output: 'tool.output',
  tool_call_complete: 'tool.completed',
  // workflow 真后台脱离信号（agent 在回合脱离收尾时发）：标记本回合 workflow 已转后台，
  // 完成走 background_task.completed 旁路，前端据此跳过前台回合结束的内联收口
  workflow_detached: 'workflow_detached'
};
const ASSISTANT_EVENT_TYPES = new Set([
  'thinking.delta',
  'thinking.completed',
  'text.delta',
  'text.completed',
  'subtask.started',
  'subtask.completed',
  'subtask.progress',
  'tool.started',
  'tool.output',
  'tool.completed',
  'turn.completed',
  'turn.failed',
  'runtime.rate_limit'
]);
const STREAM_DELTA_EVENT_TYPES = new Set(['text.delta', 'thinking.delta']);
const HISTORY_POLL_INTERVAL_MS = 1000;
const HISTORY_POLL_MAX_ATTEMPTS = 30 * 60;

type StreamPayload = Record<string, any>;

interface NormalizedStreamEvent {
  eventType: string;
  payload: StreamPayload;
  raw: Record<string, any>;
}

type HistoryTimelineItem =
  | { kind: 'history'; entry: ChatHistoryEntry; timestamp: number }
  | { kind: 'question_pushed'; record: InteractionRecord; timestamp: number }
  | { kind: 'question_terminal'; record: InteractionRecord; timestamp: number };

class ChatHttpError extends Error {
  status: number;
  errorType?: string;
  rawMessage?: string;
  shouldMarkDisconnected: boolean;

  constructor(
    message: string,
    status: number,
    errorType?: string,
    rawMessage?: string,
    shouldMarkDisconnected: boolean = false
  ) {
    super(message);
    this.name = 'ChatHttpError';
    this.status = status;
    this.errorType = errorType;
    this.rawMessage = rawMessage;
    this.shouldMarkDisconnected = shouldMarkDisconnected;
  }
}

const isRecord = (value: unknown): value is Record<string, any> =>
  typeof value === 'object' && value !== null;

const getString = (value: unknown): string | undefined =>
  typeof value === 'string' ? value : undefined;

const getBoolean = (value: unknown): boolean | undefined =>
  typeof value === 'boolean' ? value : undefined;

const getObject = (value: unknown): Record<string, any> | undefined =>
  isRecord(value) ? value : undefined;

const getEventTurnId = (event: NormalizedStreamEvent): string | undefined =>
  getString(event.payload.turnId) ?? getString(event.raw.turnId);

const withFallbackTurnId = (event: NormalizedStreamEvent, fallbackTurnId: string): NormalizedStreamEvent => {
  if (getEventTurnId(event)) {
    return event;
  }

  return {
    ...event,
    payload: {
      ...event.payload,
      turnId: fallbackTurnId
    },
    raw: {
      ...event.raw,
      turnId: fallbackTurnId
    }
  };
};

const parseTodoProgressItems = (value: unknown): TodoProgressItem[] | null => {
  if (!Array.isArray(value)) {
    return null;
  }

  const todos: TodoProgressItem[] = [];
  for (const item of value) {
    if (!isRecord(item)) {
      return null;
    }

    const content = getString(item.content)?.trim();
    const rawStatus = getString(item.status);
    if (!content || !rawStatus) {
      return null;
    }

    if (rawStatus !== 'pending' && rawStatus !== 'in_progress' && rawStatus !== 'completed') {
      return null;
    }

    const activeForm = getString(item.activeForm)?.trim();
    todos.push({
      content,
      status: rawStatus,
      ...(activeForm ? { activeForm } : {})
    });
  }

  return todos;
};

const buildLegacyPayload = (raw: Record<string, any>, eventType: string): StreamPayload => {
  switch (eventType) {
    case 'thinking.delta':
    case 'thinking.completed':
    case 'text.delta':
    case 'text.completed':
      return { content: raw.content };
    case 'subtask.started':
      return {
        name: raw.subAgentName,
        type: raw.subAgentType
      };
    case 'subtask.completed':
      return {
        success: raw.success,
        error: raw.error,
        summary: raw.content
      };
    case 'tool.started':
      return {
        toolName: raw.toolName,
        toolDescription: raw.toolDescription,
        params: raw.toolParams
      };
    case 'tool.output':
      return {
        output: raw.toolOutput
      };
    case 'tool.completed':
      return {
        output: raw.toolOutput,
        success: raw.success,
        errorType: raw.errorType,
        error: raw.error
      };
    case 'subtask.progress':
      // WP-Web: legacy 降级路径(modern path 优先,后端 main_stream.py 已透传同名字段)
      return {
        taskId: raw.taskId,
        description: raw.content,
        lastToolName: raw.toolName,
        usage: raw.usage,
        parentSubtaskId: raw.parentSubtaskId
      };
    case 'runtime.rate_limit':
      // WP-Web: RateLimitEvent legacy fallback;raw.extra 在 caba1772 已加入 legacy 白名单
      return {
        status: raw.content,
        ...(raw.extra ?? {})
      };
    default:
      return {};
  }
};

/**
 * 后端 SSE turn.failed event 的 error.code → 用户文案映射(WP-Web)。
 * SDK 0.2.87 ResultMessage.api_error_status 在后端 _build_recorded_runtime_failure
 * 分级为 RATE_LIMITED / CLIENT_ERROR / UPSTREAM_ERROR / PROVIDER_SDK_ERROR。
 * 注意:此函数仅用于 SSE 路径,HTTP 错误走 mapChatError(语义不同,不可混用)。
 */
const mapTurnFailedError = (code: string | undefined, fallbackMessage: string | undefined): string => {
  switch (code) {
    case 'RATE_LIMITED':
      return '请求过于频繁，请稍后重试。';
    case 'CLIENT_ERROR':
      return '请求格式有误，请检查输入后重试。';
    case 'UPSTREAM_ERROR':
      return 'AI 服务暂时不可用，请稍后重试。';
    case 'PROVIDER_SDK_ERROR':
    default:
      return fallbackMessage || '本轮对话失败，请稍后重试。';
  }
};

const normalizeStreamEvent = (value: unknown): NormalizedStreamEvent | null => {
  if (!isRecord(value)) {
    return null;
  }

  const legacyType = getString(value.type);
  const eventType = getString(value.eventType) ?? (legacyType ? LEGACY_EVENT_TYPE_MAP[legacyType] : undefined);
  if (!eventType && legacyType !== 'session_ready' && legacyType !== 'task_output_polling') {
    return null;
  }

  if (legacyType === 'session_ready') {
    return { eventType: 'session_ready', payload: value, raw: value };
  }

  if (legacyType === 'task_output_polling' && !eventType) {
    return { eventType: 'task_output_polling', payload: value, raw: value };
  }

  return {
    eventType: eventType || legacyType || '',
    payload: getObject(value.payload) ?? buildLegacyPayload(value, eventType || ''),
    raw: value
  };
};

export const useChatStream = (options: ChatStreamOptions) => {
  // WP-Web: RateLimitEvent 全局状态走 systemStore;按 Pinia 模式可在 composable 内直接 useStore
  const systemStore = useSystemStore();
  const agentStatus = ref<'connecting' | 'connected' | 'disconnected'>('disconnected');
  const currentProjectPath = ref('');
  const isPollingBackground = ref(false);
  const activeHistoryPollingWindows = new Set<string>();
  const todoDismissTimers = new Map<string, ReturnType<typeof setTimeout>>();

  const clearTodoDismissTimer = (windowId: string) => {
    const timer = todoDismissTimers.get(windowId);
    if (timer) {
      clearTimeout(timer);
      todoDismissTimers.delete(windowId);
    }
  };

  const scheduleTodoDismiss = (windowState: ChatWindow, delayMs: number) => {
    clearTodoDismissTimer(windowState.id);
    const updatedAt = windowState.todoProgress?.updatedAt;
    const timer = setTimeout(() => {
      if (windowState.todoProgress?.updatedAt === updatedAt) {
        windowState.todoProgress = null;
      }
      todoDismissTimers.delete(windowState.id);
    }, delayMs);
    todoDismissTimers.set(windowState.id, timer);
  };

  const finishTodoProgress = (
    windowState: ChatWindow | undefined,
    status: TodoProgressPanelStatus,
    message: string,
    delayMs: number
  ) => {
    if (!windowState?.todoProgress) {
      return;
    }

    windowState.todoProgress = {
      ...windowState.todoProgress,
      status,
      message,
      updatedAt: Date.now()
    };
    scheduleTodoDismiss(windowState, delayMs);
  };

  const updateTodoProgress = (
    windowState: ChatWindow | undefined,
    event: NormalizedStreamEvent,
    toolCallId: string,
    params: Record<string, any> | undefined
  ): boolean => {
    const todos = parseTodoProgressItems(params?.todos);
    if (!todos) {
      return false;
    }

    if (!windowState) {
      return true;
    }

    clearTodoDismissTimer(windowState.id);
    const turnId = getEventTurnId(event);
    const previous = windowState.todoProgress;
    const sameTurn = !!previous && !!turnId && previous.turnId === turnId;
    const allCompleted = todos.length > 0 && todos.every(todo => todo.status === 'completed');

    windowState.todoProgress = {
      toolCallId,
      todos,
      status: allCompleted ? 'completed' : 'running',
      isCollapsed: sameTurn ? previous.isCollapsed : false,
      updatedAt: Date.now(),
      ...(turnId ? { turnId } : {}),
      ...(allCompleted ? { message: '全部完成' } : {})
    };

    if (allCompleted) {
      scheduleTodoDismiss(windowState, 1500);
    }

    return true;
  };

  const getRandomWaitingVerb = (): string =>
    WAITING_VERBS[Math.floor(Math.random() * WAITING_VERBS.length)] ?? 'Processing';

  const isLiveSessionStatus = (status?: string | null): boolean =>
    status === 'running' || status === 'paused';

  const isPlaceholderAssistantText = (content?: string | null): boolean => {
    const trimmed = (content || '').trim();
    return PLACEHOLDER_ASSISTANT_TEXTS.has(trimmed.toLowerCase());
  };

  const isSuppressedAssistantText = (content?: string | null): boolean => {
    const trimmed = (content || '').trim();
    return trimmed.length === 0 || isPlaceholderAssistantText(content);
  };

  const pruneSuppressedTextBubbles = (bubbles: ChatBubble[]) => {
    for (let i = bubbles.length - 1; i >= 0; i--) {
      const bubble = bubbles[i];
      if (!bubble) continue;
      if (bubble.childBubbles) {
        pruneSuppressedTextBubbles(bubble.childBubbles);
      }
      if (bubble.type === 'text' && isSuppressedAssistantText(bubble.content)) {
        bubbles.splice(i, 1);
      }
    }
  };

  const streamWelcomeMessage = async () => {
    const win = options.activeWindow.value;
    if (!win) return;

    if (win.messages.length > 0) return;

    const welcomeText = '你好！我是 BIMCanvas 的布置助手。我可以帮助你分析房间功能、提供布置建议。有什么我能帮你的吗？';
    const targetWindowId = win.id;

    const welcomeBubble = createTextBubble('');
    const msgIndex = options.addMessage({
      role: 'ai',
      bubbles: [welcomeBubble],
      waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
      isStreaming: true
    });

    let i = 0;
    const interval = setInterval(() => {
      const msg = options.getWindowMessage(targetWindowId, msgIndex);
      if (!msg) {
        clearInterval(interval);
        return;
      }

      const firstBubble = msg.bubbles[0];
      if (!firstBubble) {
        clearInterval(interval);
        return;
      }

      if (i < welcomeText.length) {
        firstBubble.content += welcomeText[i] ?? '';
        i++;
        options.scrollToBottom({ windowId: targetWindowId });
      } else {
        clearInterval(interval);
        firstBubble.status = 'completed';
        msg.isStreaming = false;
      }
    }, 30);
  };

  // 健康检查重试定时器（用于组件卸载时清理）
  let healthCheckTimer: ReturnType<typeof setTimeout> | null = null;

  const checkAgentHealth = async (retries = 5, delay = 1000): Promise<void> => {
    agentStatus.value = 'connecting';
    try {
      const response = await fetch(`${options.agentApiBase}/health`);
      if (response.ok) {
        agentStatus.value = 'connected';
        await options.fetchAgentConfig();
        return;
      }
    } catch {
      // fetch 失败，下方重试
    }

    if (retries > 0) {
      await new Promise<void>((resolve) => {
        healthCheckTimer = setTimeout(() => {
          healthCheckTimer = null;
          resolve();
        }, delay);
      });
      return checkAgentHealth(retries - 1, delay * 2);
    }

    agentStatus.value = 'disconnected';
  };

  const cleanupHealthCheck = () => {
    if (healthCheckTimer) {
      clearTimeout(healthCheckTimer);
      healthCheckTimer = null;
    }
  };

  const fetchProjectPath = async () => {
    try {
      const status = await ProjectService.getStatus();
      if (status.isLoaded && status.projectPath) {
        currentProjectPath.value = status.projectPath;
        console.log('项目路径已设置:', status.projectPath);
      } else {
        console.warn('项目未加载或路径为空');
      }
    } catch (error) {
      console.error('获取项目路径失败:', error);
    }
  };

  // 兜底清理：递归完成所有残留的 streaming 气泡（tool_call、subagent、text、thinking）
  const cleanupAllStreamingBubbles = (bubbles: ChatBubble[]) => {
    for (const bubble of bubbles) {
      if (bubble.status === 'streaming') {
        if (bubble.type === 'thinking') {
          completeThinkingBubble(bubble);
          bubble.isExpanded = false;
        } else {
          completeBubble(bubble);
        }
      }
      if (bubble.childBubbles) {
        cleanupAllStreamingBubbles(bubble.childBubbles);
      }
    }
  };

  const hasFailedTextBubble = (bubbles: ChatBubble[]): boolean => {
    for (const bubble of bubbles) {
      if (bubble.type === 'text' && bubble.status === 'failed' && !isSuppressedAssistantText(bubble.content)) {
        return true;
      }
      if (bubble.childBubbles && hasFailedTextBubble(bubble.childBubbles)) {
        return true;
      }
    }
    return false;
  };

  const finalizeStreamingMessage = (message: ChatMessage) => {
    message.isStreaming = false;
    exitWaitingState(message.waitingState);
    cleanupAllStreamingBubbles(message.bubbles);
    pruneSuppressedTextBubbles(message.bubbles);
  };

  const appendTerminalFailure = (message: ChatMessage, errorMessage?: string) => {
    const normalizedError = (errorMessage || '').trim();
    if (!normalizedError || hasFailedTextBubble(message.bubbles)) {
      return;
    }

    const errorBubble = createTextBubble(normalizedError);
    errorBubble.status = 'failed';
    message.bubbles.push(errorBubble);
  };

  const hasPendingQuestionBubble = (bubbles: ChatBubble[]): boolean => {
    for (const bubble of bubbles) {
      if (bubble.type === 'question' && !bubble.questionSubmitted) {
        return true;
      }

      if (bubble.childBubbles && hasPendingQuestionBubble(bubble.childBubbles)) {
        return true;
      }
    }

    return false;
  };

  const shouldPreservePendingInteractionTool = (
    bubble: ChatBubble,
    preservePendingQuestionTools: boolean
  ): boolean => {
    return preservePendingQuestionTools
      && bubble.type === 'tool_call'
      && bubble.toolName === 'AskUserQuestion'
      && bubble.status === 'streaming';
  };

  const cleanupRestoredStreamingBubbles = (
    bubbles: ChatBubble[],
    preservePendingQuestionTools: boolean = false
  ) => {
    for (const bubble of bubbles) {
      if (
        bubble.status === 'streaming'
        && bubble.type !== 'question'
        && !shouldPreservePendingInteractionTool(bubble, preservePendingQuestionTools)
      ) {
        if (bubble.type === 'thinking') {
          completeThinkingBubble(bubble);
          bubble.isExpanded = false;
        } else {
          completeBubble(bubble);
        }
      }

      if (bubble.childBubbles) {
        cleanupRestoredStreamingBubbles(bubble.childBubbles, preservePendingQuestionTools);
      }
    }
  };

  const applyNormalizedEventToMessage = (
    currentMsg: ChatMessage,
    normalizedEvent: NormalizedStreamEvent,
    windowState?: ChatWindow
  ) => {
    const payload = normalizedEvent.payload;
    const raw = normalizedEvent.raw;

    switch (normalizedEvent.eventType) {
      case 'thinking.delta': {
        if (options.hasFallback?.('hide-thinking-panel')) {
          break;
        }
        const content = getString(payload.content) ?? getString(raw.content) ?? '';
        if (isPlaceholderAssistantText(content)) {
          break;
        }

        let activeThinking = getLastStreamingThinkingBubble(currentMsg.bubbles);
        if (!activeThinking) {
          activeThinking = createThinkingBubble(content);
          currentMsg.bubbles.push(activeThinking);
        } else {
          activeThinking.content = (activeThinking.content || '') + content;
        }
        exitWaitingState(currentMsg.waitingState);
        break;
      }
      case 'thinking.completed': {
        if (options.hasFallback?.('hide-thinking-panel')) {
          break;
        }
        const content = getString(payload.content) ?? getString(raw.content) ?? '';
        if (isSuppressedAssistantText(content)) {
          const activeThinking = getLastStreamingThinkingBubble(currentMsg.bubbles);
          if (activeThinking) {
            completeThinkingBubble(activeThinking);
          }
          break;
        }

        let activeThinking = getLastStreamingThinkingBubble(currentMsg.bubbles);
        if (!activeThinking) {
          activeThinking = createThinkingBubble(content);
          currentMsg.bubbles.push(activeThinking);
        } else if (content) {
          activeThinking.content = content;
        }
        completeThinkingBubble(activeThinking);
        break;
      }
      case 'text.delta': {
        const errorType = getString(raw.errorType);
        const content = getString(payload.content) ?? getString(raw.content) ?? '';

        if (errorType === 'recoverable' || errorType === 'blocking') {
          break;
        }

        if (isPlaceholderAssistantText(content)) {
          break;
        }

        exitWaitingState(currentMsg.waitingState);
        collapseLastThinkingBubble(currentMsg.bubbles);

        const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
        if (lastTextBubble) {
          lastTextBubble.content = (lastTextBubble.content || '') + content;
        } else {
          currentMsg.bubbles.push(createTextBubble(content));
        }
        break;
      }
      case 'text.completed': {
        const content = getString(payload.content) ?? getString(raw.content) ?? '';
        const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

        if (lastTextBubble) {
          if (isSuppressedAssistantText(lastTextBubble.content)) {
            const bubbleIndex = currentMsg.bubbles.lastIndexOf(lastTextBubble);
            if (bubbleIndex >= 0) {
              currentMsg.bubbles.splice(bubbleIndex, 1);
            }
          } else {
            completeBubble(lastTextBubble);
          }
        } else if (content && !isSuppressedAssistantText(content)) {
          const newTextBubble = createTextBubble(content);
          newTextBubble.status = 'completed';
          currentMsg.bubbles.push(newTextBubble);
        }

        if (!hasStreamingSubAgent(currentMsg.bubbles)) {
          enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
        }
        break;
      }
      case 'subtask.started': {
        exitWaitingState(currentMsg.waitingState);
        collapseLastThinkingBubble(currentMsg.bubbles);

        const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
        if (lastTextBubble) {
          completeBubble(lastTextBubble);
        }

        const subtaskId = getString(raw.subtaskId) ?? getString(raw.subAgentId);
        if (!subtaskId || findBubbleByIdDeep(currentMsg.bubbles, subtaskId)) {
          break;
        }

        const subtaskName = getString(payload.name) ?? getString(raw.subAgentName) ?? 'Subtask';
        const subtaskType = getString(payload.type) ?? getString(raw.subAgentType) ?? 'general-purpose';
        currentMsg.bubbles.push(createSubAgentBubble(subtaskId, subtaskName, subtaskType));
        workflowProgress.onSubtaskStarted(subtaskId, subtaskName, subtaskType);
        break;
      }
      case 'subtask.completed': {
        const subtaskId = getString(raw.subtaskId) ?? getString(raw.subAgentId);
        if (!subtaskId) {
          break;
        }

        const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, subtaskId);
        const completedSuccess = getBoolean(payload.success) ?? getBoolean(raw.success);
        const completedSummary = getString(payload.summary) ?? getString(raw.content);
        if (subAgentBubble) {
          if (completedSuccess === false) {
            failBubble(subAgentBubble, getString(payload.error) ?? getString(raw.error));
          } else {
            completeBubble(subAgentBubble);
          }

          if (completedSummary) {
            updateSubAgentResult(subAgentBubble, completedSummary);
          }
        }
        workflowProgress.onSubtaskCompleted(subtaskId, { success: completedSuccess, summary: completedSummary });

        if (!hasStreamingSubAgent(currentMsg.bubbles)) {
          enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
        }
        break;
      }
      case 'subtask.progress': {
        // WP-Web: SDK 0.2.87 TaskProgressMessage 进度更新,渲染到 SubAgentBubble 头部 meta-right
        const subtaskId = getString(raw.subtaskId) ?? getString(raw.subAgentId);
        const usageRaw = getObject(payload.usage) ?? getObject(raw.usage);
        const usage = usageRaw ? {
          totalTokens: typeof usageRaw.total_tokens === 'number' ? usageRaw.total_tokens : undefined,
          toolUses: typeof usageRaw.tool_uses === 'number' ? usageRaw.tool_uses : undefined,
          durationMs: typeof usageRaw.duration_ms === 'number' ? usageRaw.duration_ms : undefined
        } : undefined;
        const progressDescription = getString(payload.description) ?? getString(raw.content);
        const progressLastTool = getString(payload.lastToolName) ?? getString(raw.toolName);

        // Workflow 视图聚合:workflow 内 agent 实时流可能只给 taskId(无 subtaskId),用 subtaskId ?? taskId 作 key。
        const workflowKey = subtaskId ?? getString(payload.taskId) ?? getString(raw.taskId);
        if (workflowKey) {
          workflowProgress.onSubtaskProgress(workflowKey, {
            description: progressDescription,
            lastToolName: progressLastTool,
            usage
          });
        }

        if (!subtaskId) {
          break;
        }
        const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, subtaskId);
        if (!subAgentBubble || subAgentBubble.type !== 'subagent') {
          break;
        }
        subAgentBubble.subAgentProgress = {
          description: progressDescription,
          lastToolName: progressLastTool,
          usage
        };
        break;
      }
      case 'runtime.rate_limit': {
        // WP-Web: SDK 0.2.87 RateLimitEvent 全局状态徽章
        const status = getString(payload.status) ?? getString(raw.content);
        if (!status) {
          break;
        }
        systemStore.setRateLimitState({
          status,
          rateLimitType: getString(payload.rateLimitType) ?? getString(raw.rateLimitType),
          utilization: typeof payload.utilization === 'number' ? payload.utilization
            : typeof raw.utilization === 'number' ? raw.utilization : undefined,
          resetsAt: typeof payload.resetsAt === 'number' ? payload.resetsAt
            : typeof raw.resetsAt === 'number' ? raw.resetsAt : undefined
        });
        break;
      }
      case 'tool.started': {
        exitWaitingState(currentMsg.waitingState);
        collapseLastThinkingBubble(currentMsg.bubbles);

        const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
        if (lastTextBubble) {
          completeBubble(lastTextBubble);
        }

        const toolCallId = getString(raw.toolCallId);
        if (!toolCallId) {
          break;
        }

        const toolName = getString(payload.toolName) ?? getString(raw.toolName) ?? 'Tool';
        const toolParams = getObject(payload.params) ?? getObject(raw.toolParams);
        if (toolName === 'TodoWrite' && updateTodoProgress(windowState, normalizedEvent, toolCallId, toolParams)) {
          enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
          break;
        }

        // Workflow 触发信号(必修2):主控自身调用 workflow 工具(无 subtaskId,工具名命中触发信号)即开 workflow,
        // 让 Chat 气泡 + Task 页在"调用 workflow 时"就亮起,不等首个子 agent。
        const toolStartedSubtaskId = getString(raw.subtaskId) ?? getString(raw.subAgentId);
        if (!toolStartedSubtaskId && workflowProgress.isWorkflowTool(toolName)) {
          workflowProgress.startWorkflow({ toolCallId, label: toolName });
        } else if (toolStartedSubtaskId) {
          workflowProgress.onToolStarted(toolStartedSubtaskId, {
            toolCallId,
            toolName,
            description: getString(payload.toolDescription) ?? getString(raw.toolDescription)
          });
        }

        const existingBubble = findBubbleByIdDeep(currentMsg.bubbles, toolCallId);
        if (existingBubble) {
          break;
        }

        const toolBubble = createToolCallBubble(
          toolCallId,
          toolName,
          getString(payload.toolDescription) ?? getString(raw.toolDescription),
          toolParams
        );

        const subtaskId = getString(raw.subtaskId) ?? getString(raw.subAgentId);
        if (subtaskId) {
          const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, subtaskId);
          if (subAgentBubble && subAgentBubble.type === 'subagent') {
            if (!subAgentBubble.childBubbles) {
              subAgentBubble.childBubbles = [];
            }
            subAgentBubble.childBubbles.push(toolBubble);
          } else {
            currentMsg.bubbles.push(toolBubble);
          }
        } else {
          currentMsg.bubbles.push(toolBubble);
        }

        enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
        break;
      }
      case 'tool.output': {
        const toolCallId = getString(raw.toolCallId);
        const output = getString(payload.output) ?? getString(raw.toolOutput);
        if (!toolCallId || !output) {
          break;
        }

        const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, toolCallId);
        if (toolBubble && toolBubble.type === 'tool_call') {
          appendToolCallOutput(toolBubble, output);
        }
        break;
      }
      case 'tool.completed': {
        const toolCallId = getString(raw.toolCallId);
        if (!toolCallId) {
          break;
        }

        workflowProgress.onToolCompleted(toolCallId, {
          success: getBoolean(payload.success) ?? getBoolean(raw.success)
        });

        // Workflow 工具完成（async_launched）：从结果文本 "Task ID: <id>" 绑定 taskId。
        // 这是最可靠的 in-turn 来源——实时进度 SSE 可能只带 sdkSessionId 漏传 taskId，
        // 不绑定则完成后 loadTranscript 无 taskId、服务端只能回退实时态，Task 面板永久卡"运行中"。
        const wfTool = workflowProgress.workflow.value;
        if (wfTool && toolCallId === wfTool.toolCallId && !wfTool.taskId) {
          const wfOut = getString(payload.output) ?? getString(raw.toolOutput) ?? '';
          const wfMatch = wfOut.match(/Task ID:\s*(\S+)/i);
          if (wfMatch) {
            workflowProgress.bindWorkflowIdentity({ toolCallId, taskId: wfMatch[1] });
          }
        }

        if (windowState?.todoProgress?.toolCallId === toolCallId) {
          const success = getBoolean(payload.success) ?? getBoolean(raw.success);
          if (success === false) {
            finishTodoProgress(
              windowState,
              'failed',
              getString(payload.error) ?? getString(raw.error) ?? 'TodoWrite 更新失败',
              3000
            );
          }
          break;
        }

        const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, toolCallId);
        if (toolBubble && toolBubble.type === 'tool_call') {
          const output = getString(payload.output) ?? getString(raw.toolOutput);
          if (output && !toolBubble.toolOutput) {
            appendToolCallOutput(toolBubble, output);
          }

          const success = getBoolean(payload.success) ?? getBoolean(raw.success);
          if (success === false) {
            failBubble(toolBubble, getString(payload.error) ?? getString(raw.error));
          } else {
            completeBubble(toolBubble);
          }
        }

        if (!hasStreamingSubAgent(currentMsg.bubbles)) {
          enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
        }
        break;
      }
      case 'task_output_polling': {
        isPollingBackground.value = true;
        const timeout = Number(raw.timeout ?? payload.timeout ?? 0);
        const streamingSubAgents = findStreamingSubAgents(currentMsg.bubbles);
        for (const bubble of streamingSubAgents) {
          markAsBackground(bubble);
          bubble.subAgentResult = `正在获取结果... (timeout: ${timeout / 1000}s)`;
        }
        break;
      }
      case 'workflow_detached': {
        // workflow 已脱离到真后台：完成将经 background_task.completed 旁路到达。
        // 置此标记 → sendMessage 的 finally 跳过"前台回合结束即内联收口"（:1435 守卫），
        // 避免回合一结束就把仍在后台跑的 workflow 误标 completed。
        isPollingBackground.value = true;
        break;
      }
      case 'turn.completed': {
        finalizeStreamingMessage(currentMsg);
        const eventTurnId = getEventTurnId(normalizedEvent);
        const todoTurnId = windowState?.todoProgress?.turnId;
        if (
          windowState?.todoProgress?.status === 'running'
          && !!eventTurnId
          && todoTurnId === eventTurnId
        ) {
          const allCompleted = windowState.todoProgress.todos.length > 0
            && windowState.todoProgress.todos.every(todo => todo.status === 'completed');
          if (allCompleted) {
            finishTodoProgress(windowState, 'completed', '全部完成', 1500);
          }
        }
        break;
      }
      case 'turn.failed': {
        finalizeStreamingMessage(currentMsg);
        // WP-Web: 读后端 WP-1 新增的 error.code(RATE_LIMITED / CLIENT_ERROR / UPSTREAM_ERROR / PROVIDER_SDK_ERROR)
        // 与 error.details.httpStatus,按 code 给用户视角文案;httpStatus 仅供 console 诊断。
        const errorObj = getObject(payload.error);
        const errorCode = getString(errorObj?.code);
        const errorMessageRaw = getString(errorObj?.message) ?? getString(raw.error);
        const httpStatus = typeof errorObj?.details?.httpStatus === 'number' ? errorObj.details.httpStatus : undefined;
        if (errorCode || httpStatus !== undefined) {
          console.warn('[turn.failed]', { code: errorCode, httpStatus, message: errorMessageRaw });
        }
        const userMessage = mapTurnFailedError(errorCode, errorMessageRaw);

        const eventTurnId = getEventTurnId(normalizedEvent);
        const todoTurnId = windowState?.todoProgress?.turnId;
        if (!!eventTurnId && todoTurnId === eventTurnId) {
          finishTodoProgress(
            windowState,
            'failed',
            userMessage,
            3000
          );
        }
        appendTerminalFailure(currentMsg, userMessage);
        break;
      }
      default: {
        if (raw.error) {
          console.error('[SSE Error]', raw.error);
        }
        break;
      }
    }
  };

  const findWindow = (windowId: string): ChatWindow | undefined =>
    options.windows.value.find(w => w.id === windowId);

  const cloneSpatialMarks = (marks: SpatialMark[]): SpatialMark[] =>
    JSON.parse(JSON.stringify(marks)) as SpatialMark[];

  const hasDraftContent = (
    text: string,
    attachments: ChatAttachmentRef[],
    spatialMarks: SpatialMark[]
  ): boolean =>
    text.trim().length > 0 || attachments.length > 0 || spatialMarks.length > 0;

  const hasWindowDraftContent = (windowState: ChatWindow): boolean =>
    hasDraftContent(
      windowState.inputMessage,
      windowState.pendingAttachments,
      windowState.pendingSpatialMarks
    );

  const captureWindowDraft = (windowState: ChatWindow): QueuedChatDraft | null => {
    const text = windowState.inputMessage.trim();
    const attachments = [...windowState.pendingAttachments];
    const spatialMarks = cloneSpatialMarks(windowState.pendingSpatialMarks);

    if (!hasDraftContent(text, attachments, spatialMarks)) {
      return null;
    }

    return {
      id: createDraftMessageId(),
      text,
      clientMessageId: windowState.draftMessageId || createDraftMessageId(),
      attachments,
      spatialMarks,
      createdAt: Date.now()
    };
  };

  const clearCapturedWindowDraft = (windowState: ChatWindow) => {
    windowState.inputMessage = '';
    windowState.pendingAttachments = [];
    windowState.pendingSpatialMarks = [];
    windowState.draftMessageId = createDraftMessageId();
  };

  const getUserBubbleText = (draft: QueuedChatDraft): string => {
    if (draft.text) {
      return draft.text;
    }
    if (draft.spatialMarks.length > 0) {
      return `已附加 ${draft.spatialMarks.length} 个 Space Mark`;
    }
    return '';
  };

  const restoreDraftAfterUndeliveredSend = (
    targetWindowId: string,
    draft: QueuedChatDraft,
    source: 'input' | 'queued',
    userMessageIndex: number,
    aiMessageIndex: number,
    errorMessage?: string
  ) => {
    const targetWin = findWindow(targetWindowId);
    if (!targetWin) return;

    if (source === 'queued') {
      targetWin.queuedMessage ??= draft;
    } else {
      targetWin.inputMessage = draft.text;
      targetWin.pendingAttachments = draft.attachments;
      targetWin.pendingSpatialMarks = cloneSpatialMarks(draft.spatialMarks);
      targetWin.draftMessageId = draft.clientMessageId;
    }

    if (aiMessageIndex >= 0 && aiMessageIndex < targetWin.messages.length) {
      const aiMessage = targetWin.messages[aiMessageIndex];
      if (aiMessage?.role === 'ai') {
        targetWin.messages.splice(aiMessageIndex, 1);
      }
    }

    if (userMessageIndex >= 0 && userMessageIndex < targetWin.messages.length) {
      const userMessage = targetWin.messages[userMessageIndex];
      if (userMessage?.role === 'user') {
        targetWin.messages.splice(userMessageIndex, 1);
      }
    }

    if (errorMessage) {
      const errorBubble = createTextBubble(errorMessage);
      errorBubble.status = 'failed';
      options.addMessageToWindow(targetWindowId, {
        role: 'ai',
        bubbles: [errorBubble],
        waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 }
      });
    }
  };

  const sendQueuedDraftIfReady = (windowId: string) => {
    const targetWin = findWindow(windowId);
    const queuedDraft = targetWin?.queuedMessage;
    if (!targetWin || !queuedDraft || targetWin.isStreaming) {
      return;
    }

    targetWin.queuedMessage = null;
    void sendDraft(windowId, queuedDraft, 'queued');
  };

  const sendDraft = async (
    targetWindowId: string,
    draft: QueuedChatDraft,
    source: 'input' | 'queued'
  ) => {
    const targetWin = findWindow(targetWindowId);
    if (!targetWin || targetWin.isStreaming) return;

    const effectiveWindowId = targetWindowId || 'window-main';
    const clientMessageId = draft.clientMessageId || createDraftMessageId();
    const message = draft.text;
    const attachmentsToSend = [...draft.attachments];
    const attachmentIds = attachmentsToSend.map(item => item.attachmentId);

    const userTextBubble = createTextBubble(getUserBubbleText(draft));
    userTextBubble.status = 'completed';
    if (attachmentsToSend.length > 0) {
      userTextBubble.attachments = attachmentsToSend;
    }
    const sentContextSnapshot = options.buildContextSnapshot?.(draft.spatialMarks);
    if (sentContextSnapshot) {
      userTextBubble.sentContext = sentContextSnapshot;
    }
    const userMessageIndex = options.addMessageToWindow(targetWindowId, {
      role: 'user',
      bubbles: [userTextBubble],
      waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 }
    });
    targetWin.isStreaming = true;

    targetWin.shouldAutoScroll = true;
    await nextTick();
    options.scrollToBottom({ force: true, windowId: targetWindowId });
    requestAnimationFrame(() => options.scrollToBottom({ force: true, windowId: targetWindowId }));
    setTimeout(() => options.scrollToBottom({ force: true, windowId: targetWindowId }), 50);
    setTimeout(() => options.scrollToBottom({ force: true, windowId: targetWindowId }), 150);

    const initialWaitingState: WaitingState = {
      isWaiting: true,
      waitingVerb: getRandomWaitingVerb(),
      waitingSince: Date.now()
    };
    const aiMessageIndex = options.addMessageToWindow(targetWindowId, {
      role: 'ai',
      bubbles: [],
      waitingState: initialWaitingState,
      isStreaming: true,
      startTime: Date.now()
    });

    // 定时器：更新当前 streaming thinking 气泡的时长
    const timerInterval = setInterval(() => {
      const msg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (!msg || !msg.isStreaming) {
        clearInterval(timerInterval);
        return;
      }
      const activeThinking = getLastStreamingThinkingBubble(msg.bubbles);
      if (activeThinking && activeThinking.thinkingStartTime) {
        const duration = Math.round((Date.now() - activeThinking.thinkingStartTime) / 1000);
        activeThinking.thinkingDuration = duration + 's';
      }
    }, 1000);

    let shouldCommitAttachments = false;
    let completedSuccessfully = false;
    let didReceiveAssistantEvent = false;
    let receivedTurnFailed = false;
    let requestController: AbortController | null = null;
    let pendingDeltaEvent: NormalizedStreamEvent | null = null;
    let pendingDeltaFrame: number | null = null;

    const getDeltaEventKey = (event: NormalizedStreamEvent): string =>
      [
        event.eventType,
        getString(event.raw.subtaskId) ?? getString(event.raw.subAgentId) ?? ''
      ].join(':');

    const getDeltaEventContent = (event: NormalizedStreamEvent): string =>
      getString(event.payload.content) ?? getString(event.raw.content) ?? '';

    const applyEventToCurrentMessage = (event: NormalizedStreamEvent) => {
      const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (!currentMsg) return;
      const latestTargetWin = findWindow(targetWindowId);
      applyNormalizedEventToMessage(currentMsg, event, latestTargetWin);
      options.scrollToBottom({ windowId: targetWindowId });
    };

    const flushPendingDeltaEvent = () => {
      if (pendingDeltaFrame !== null) {
        cancelAnimationFrame(pendingDeltaFrame);
        pendingDeltaFrame = null;
      }

      const eventToApply = pendingDeltaEvent;
      pendingDeltaEvent = null;
      if (eventToApply) {
        applyEventToCurrentMessage(eventToApply);
      }
    };

    const enqueueDeltaEvent = (event: NormalizedStreamEvent) => {
      if (pendingDeltaEvent && getDeltaEventKey(pendingDeltaEvent) === getDeltaEventKey(event)) {
        const content = getDeltaEventContent(pendingDeltaEvent) + getDeltaEventContent(event);
        pendingDeltaEvent = {
          eventType: event.eventType,
          payload: { ...event.payload, content },
          raw: { ...event.raw, content }
        };
      } else {
        flushPendingDeltaEvent();
        pendingDeltaEvent = event;
      }

      if (pendingDeltaFrame === null) {
        pendingDeltaFrame = requestAnimationFrame(() => {
          pendingDeltaFrame = null;
          flushPendingDeltaEvent();
        });
      }
    };

    try {
      // 每次发消息前刷新项目路径，确保项目切换后携带最新路径
      await fetchProjectPath();

      console.log('[sendMessage] Request:', {
        projectPath: currentProjectPath.value,
        windowId: effectiveWindowId,
        message: message.substring(0, 50) + (message.length > 50 ? '...' : ''),
        attachmentCount: attachmentIds.length,
        spatialMarkCount: draft.spatialMarks.length,
        model: options.currentModel.value?.id,
        effort: options.currentEffort.value.id,
        thinking: options.currentThinking.value.id
      });

      requestController = new AbortController();
      currentAbortControllers.set(targetWindowId, requestController);

      const context = options.buildContextPayload?.(draft.spatialMarks);

      const response = await fetch(`${options.agentApiBase}/api/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          projectPath: currentProjectPath.value,
          windowId: effectiveWindowId,
          worktreePath: targetWin.worktreePath,
          clientMessageId,
          message,
          attachmentIds,
          attachments: attachmentsToSend,
          model: options.currentModel.value?.id,
          effort: options.currentEffort.value.id,
          thinking: options.currentThinking.value.id,
          ...(context ? { context } : {})
        }),
        signal: requestController.signal
      });

      if (!response.ok) {
        throw await createChatHttpError(response);
      }

      const reader = response.body?.getReader();
      const decoder = new TextDecoder();

      if (!reader) {
        throw new Error('No response body');
      }

      let buffer = '';
      let receivedTerminalEvent = false;
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (!line.startsWith('data: ')) {
            continue;
          }

          const data = line.slice(6);
          if (data === '[DONE]') {
            flushPendingDeltaEvent();
            if (!receivedTerminalEvent) {
              const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
              if (currentMsg) {
                finalizeStreamingMessage(currentMsg);
              }
            }
            continue;
          }

          try {
            const parsed = JSON.parse(data);
            const parsedEvent = normalizeStreamEvent(parsed);

            if (!parsedEvent) {
              if (parsed?.error) {
                console.error('[SSE Error]', parsed.error);
              }
              continue;
            }

            const normalizedEvent = withFallbackTurnId(parsedEvent, clientMessageId);

            if (ASSISTANT_EVENT_TYPES.has(normalizedEvent.eventType)) {
              didReceiveAssistantEvent = true;
            }

            if (normalizedEvent.eventType === 'turn.completed' || normalizedEvent.eventType === 'turn.failed') {
              receivedTerminalEvent = true;
              receivedTurnFailed = normalizedEvent.eventType === 'turn.failed';
            }

            if (normalizedEvent.eventType === 'session_ready') {
              agentStatus.value = 'connected';
              continue;
            }

            if (STREAM_DELTA_EVENT_TYPES.has(normalizedEvent.eventType)) {
              enqueueDeltaEvent(normalizedEvent);
              continue;
            }

            flushPendingDeltaEvent();
            applyEventToCurrentMessage(normalizedEvent);
          } catch (error) {
            console.error('Parse error:', error, data);
          }
        }
      }

      flushPendingDeltaEvent();
      const finalMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (finalMsg && !receivedTerminalEvent) {
        finalizeStreamingMessage(finalMsg);
      }

      agentStatus.value = 'connected';
      shouldCommitAttachments = true;
      completedSuccessfully = !receivedTurnFailed;
    } catch (error) {
      flushPendingDeltaEvent();
      // AbortError 是用户主动中止，不是真正的错误
      if (error instanceof Error && error.name === 'AbortError') {
        console.log('[sendMessage] Request aborted by user');
        const shouldPreserveDeliveredState = preserveDeliveredStateOnAbort.delete(targetWindowId);
        if (!didReceiveAssistantEvent && !shouldPreserveDeliveredState) {
          restoreDraftAfterUndeliveredSend(
            targetWindowId,
            draft,
            source,
            userMessageIndex,
            aiMessageIndex
          );
        }
        // 正常结束，不显示错误
        const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
        if (currentMsg) {
          currentMsg.isStreaming = false;
          currentMsg.waitingState.isWaiting = false;
          // 兜底清理所有残留的 streaming 气泡
          cleanupAllStreamingBubbles(currentMsg.bubbles);
          pruneSuppressedTextBubbles(currentMsg.bubbles);
        }
        return;  // 提前返回，跳过错误处理
      }

      // 其他错误正常处理
      console.error('Chat error:', error);
      const errorInfo = normalizeChatError(error);

      if (!didReceiveAssistantEvent) {
        restoreDraftAfterUndeliveredSend(
          targetWindowId,
          draft,
          source,
          userMessageIndex,
          aiMessageIndex,
          errorInfo.userMessage
        );
        agentStatus.value = errorInfo.shouldMarkDisconnected ? 'disconnected' : 'connected';
        return;
      }

      const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (currentMsg) {
        if (currentMsg.bubbles.length === 0) {
          const errorBubble = createTextBubble(errorInfo.userMessage);
          errorBubble.status = 'failed';
          currentMsg.bubbles.push(errorBubble);
        }
        currentMsg.isStreaming = false;
        currentMsg.waitingState.isWaiting = false;
        pruneSuppressedTextBubbles(currentMsg.bubbles);
      }
      agentStatus.value = errorInfo.shouldMarkDisconnected ? 'disconnected' : 'connected';
    } finally {
      flushPendingDeltaEvent();
      if (shouldCommitAttachments && attachmentIds.length > 0 && currentProjectPath.value) {
        try {
          await ChatAttachmentService.commitAttachments({
            projectPath: currentProjectPath.value,
            windowId: effectiveWindowId,
            clientMessageId,
            attachmentIds
          });
        } catch (commitError) {
          console.warn('[sendMessage] Commit attachments failed:', commitError);
        }
      }

      const targetWinAfterSend = findWindow(targetWindowId);
      const isLatestRequest = !requestController
        || currentAbortControllers.get(targetWindowId) === requestController;

      // 只有当前请求仍是这个窗口的最新请求时，才清理 streaming 状态；
      // 立即发送等待消息时，旧请求的 finally 不能覆盖新请求状态。
      if (targetWinAfterSend && isLatestRequest) {
        targetWinAfterSend.isStreaming = false;
      }
      if (isLatestRequest) {
        currentAbortControllers.delete(targetWindowId);
      }
      // 内联 workflow 收口（7.4）：未 detach 后台的 workflow 在主控回合结束时不会再有
      // background_task.completed（那是后台路径专属），在此统一收口，避免 Task 面板卡 Running。
      // detach 到后台的（isPollingBackground）留给 background_task.completed，不在此提前完成。
      if (isLatestRequest && !isPollingBackground.value
          && workflowProgress.workflow.value?.status === 'running') {
        workflowProgress.onWorkflowCompleted({ status: completedSuccessfully ? 'completed' : 'failed' });
      }
      isPollingBackground.value = false;
      await nextTick();
      options.scrollToBottom({ windowId: targetWindowId });

      if (completedSuccessfully) {
        sendQueuedDraftIfReady(targetWindowId);
      }
    }
  };

  const queueCurrentDraft = (windowState: ChatWindow): boolean => {
    if (windowState.queuedMessage) {
      return false;
    }

    const draft = captureWindowDraft(windowState);
    if (!draft) {
      return false;
    }

    windowState.queuedMessage = draft;
    clearCapturedWindowDraft(windowState);
    return true;
  };

  const sendMessage = async () => {
    const win = options.activeWindow.value;
    if (!win) return;

    if (win.isStreaming) {
      queueCurrentDraft(win);
      return;
    }

    const draft = captureWindowDraft(win);
    if (!draft) {
      return;
    }

    clearCapturedWindowDraft(win);
    await sendDraft(win.id, draft, 'input');
  };

  const parseHistoryTimestamp = (value?: string | null): number => {
    if (!value) {
      return Date.now();
    }

    const parsed = Date.parse(value);
    return Number.isNaN(parsed) ? Date.now() : parsed;
  };

  const findQuestionBubbleByInteractionId = (
    bubbleList: ChatBubble[],
    interactionId: string
  ): ChatBubble | undefined => {
    for (const bubble of bubbleList) {
      if (bubble.questionRequestId === interactionId) {
        return bubble;
      }
      if (bubble.childBubbles) {
        const nested = findQuestionBubbleByInteractionId(bubble.childBubbles, interactionId);
        if (nested) {
          return nested;
        }
      }
    }
    return undefined;
  };

  const createRestoredUserMessage = (
    message: string,
    attachments: ChatAttachmentRef[] | undefined,
    timestamp: number
  ): ChatMessage => {
    const bubble = createTextBubble(message);
    bubble.status = 'completed';
    bubble.timestamp = timestamp;
    if (attachments && attachments.length > 0) {
      bubble.attachments = attachments;
    }

    return {
      role: 'user',
      bubbles: [bubble],
      waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
      isStreaming: false,
      startTime: timestamp,
      endTime: timestamp
    };
  };

  const createRestoredAiMessage = (timestamp: number): ChatMessage => ({
    role: 'ai',
    bubbles: [],
    waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
    isStreaming: true,
    startTime: timestamp
  });

  const createHistoryTimeline = (response: ChatHistoryResponse): HistoryTimelineItem[] => {
    const timeline: HistoryTimelineItem[] = response.history.map(entry => ({
      kind: 'history',
      entry,
      timestamp: parseHistoryTimestamp(entry.createdAt)
    }));

    for (const record of response.interactions || []) {
      if (record.kind !== 'question') {
        continue;
      }

      timeline.push({
        kind: 'question_pushed',
        record,
        timestamp: parseHistoryTimestamp(record.createdAt)
      });

      if (record.status !== 'pending') {
        timeline.push({
          kind: 'question_terminal',
          record,
          timestamp: parseHistoryTimestamp(record.updatedAt || record.createdAt)
        });
      }
    }

    const kindPriority: Record<HistoryTimelineItem['kind'], number> = {
      history: 0,
      question_pushed: 1,
      question_terminal: 2
    };

    timeline.sort((left, right) => {
      if (left.timestamp !== right.timestamp) {
        return left.timestamp - right.timestamp;
      }
      return kindPriority[left.kind] - kindPriority[right.kind];
    });

    return timeline;
  };

  const restoreTodoProgressFromHistory = (response: ChatHistoryResponse): TodoProgressState | null => {
    let restored: TodoProgressState | null = null;

    for (const item of createHistoryTimeline(response)) {
      if (item.kind !== 'history' || item.entry.kind !== 'assistant_event') {
        continue;
      }

      const normalizedEvent = normalizeStreamEvent(item.entry.event);
      if (!normalizedEvent) {
        continue;
      }

      const payload = normalizedEvent.payload;
      const raw = normalizedEvent.raw;
      const eventTurnId = getEventTurnId(normalizedEvent) ?? item.entry.turnId;

      if (normalizedEvent.eventType === 'tool.started') {
        const toolName = getString(payload.toolName) ?? getString(raw.toolName);
        if (toolName !== 'TodoWrite') {
          continue;
        }

        const toolParams = getObject(payload.params) ?? getObject(raw.toolParams);
        const todos = parseTodoProgressItems(toolParams?.todos);
        if (!todos) {
          continue;
        }

        const allCompleted = todos.length > 0 && todos.every(todo => todo.status === 'completed');
        if (allCompleted) {
          restored = null;
          continue;
        }

        restored = {
          toolCallId: getString(raw.toolCallId),
          turnId: eventTurnId,
          todos,
          status: 'running',
          isCollapsed: false,
          updatedAt: item.timestamp
        };
        continue;
      }

      if (!restored) {
        continue;
      }

      if (normalizedEvent.eventType === 'tool.completed') {
        const toolCallId = getString(raw.toolCallId);
        const success = getBoolean(payload.success) ?? getBoolean(raw.success);
        if (toolCallId && restored.toolCallId === toolCallId && success === false) {
          restored = null;
        }
        continue;
      }

      if (normalizedEvent.eventType === 'turn.failed' && restored.turnId === eventTurnId) {
        restored = null;
      }
    }

    return restored;
  };

  const restoreHistoryForWindow = (windowState: ChatWindow, response: ChatHistoryResponse) => {
    const turnMessages = new Map<string, { user?: ChatMessage; ai?: ChatMessage }>();
    const sessionStatus = response.sessionStatus ?? response.session?.status ?? null;
    const activeTurnId = response.session?.activeTurnId ?? null;
    const shouldKeepActiveTurnStreaming = isLiveSessionStatus(sessionStatus) && !!activeTurnId;
    clearTodoDismissTimer(windowState.id);
    windowState.todoProgress = null;
    windowState.messages = [];
    windowState.isStreaming = shouldKeepActiveTurnStreaming;

    const ensureAiMessageForTurn = (turnId: string, timestamp: number): ChatMessage => {
      const existing = turnMessages.get(turnId);
      if (existing?.ai) {
        return existing.ai;
      }

      const aiMessage = createRestoredAiMessage(timestamp);
      windowState.messages.push(aiMessage);
      turnMessages.set(turnId, { ...(existing || {}), ai: aiMessage });
      return aiMessage;
    };

    const applyQuestionResolvedState = (bubble: ChatBubble, record: InteractionRecord) => {
      bubble.questionSubmitted = true;
      bubble.questionAnswers = record.status === 'resolved'
        ? (record.resolutionPayload?.answers as Record<string, string> | undefined) || {}
        : {};
      completeBubble(bubble);
    };

    for (const item of createHistoryTimeline(response)) {
      if (item.kind === 'history') {
        const entry = item.entry;
        if (entry.kind === 'user_message') {
          const userMessage = createRestoredUserMessage(
            entry.message || '',
            entry.attachments,
            item.timestamp
          );
          windowState.messages.push(userMessage);
          turnMessages.set(entry.turnId, { ...(turnMessages.get(entry.turnId) || {}), user: userMessage });
          continue;
        }

        const aiMessage = ensureAiMessageForTurn(entry.turnId, item.timestamp);
        const normalizedEvent = normalizeStreamEvent(entry.event);
        if (!normalizedEvent) {
          continue;
        }
        applyNormalizedEventToMessage(
          aiMessage,
          normalizedEvent,
          shouldKeepActiveTurnStreaming && entry.turnId === activeTurnId ? windowState : undefined
        );
        continue;
      }

      const record = item.record;
      const aiMessage = ensureAiMessageForTurn(record.turnId, item.timestamp);
      let bubble = findQuestionBubbleByInteractionId(aiMessage.bubbles, record.interactionId);

      if (!bubble) {
        bubble = createQuestionBubble(
          record.interactionId,
          Array.isArray(record.requestPayload?.questions) ? record.requestPayload.questions : []
        );
        bubble.timestamp = item.timestamp;
        aiMessage.bubbles.push(bubble);
      } else if (Array.isArray(record.requestPayload?.questions)) {
        bubble.questions = record.requestPayload.questions;
      }

      if (item.kind === 'question_terminal') {
        applyQuestionResolvedState(bubble, record);
      }
    }

    const activeAiMessage = activeTurnId ? turnMessages.get(activeTurnId)?.ai : undefined;
    windowState.isStreaming = shouldKeepActiveTurnStreaming && !!activeAiMessage;

    for (const message of windowState.messages) {
      if (message.role !== 'ai') {
        continue;
      }

      const keepStreaming = shouldKeepActiveTurnStreaming && message === activeAiMessage;
      message.isStreaming = keepStreaming;
      if (!keepStreaming) {
        exitWaitingState(message.waitingState);
        cleanupRestoredStreamingBubbles(message.bubbles, hasPendingQuestionBubble(message.bubbles));
      }
      pruneSuppressedTextBubbles(message.bubbles);
    }

    windowState.todoProgress = restoreTodoProgressFromHistory(response);
  };

  const syncHistoryForWindow = async (windowId: string): Promise<string | null> => {
    const windowState = options.windows.value.find(item => item.id === windowId);
    if (!windowState) {
      return null;
    }

    const historyService = getChatHistoryService(options.agentApiBase);
    const response = await historyService.getHistory(windowId);
    restoreHistoryForWindow(windowState, response);
    await nextTick();
    options.scrollToBottom({ windowId });
    return response.sessionStatus ?? response.session?.status ?? null;
  };

  /**
   * 注入「后台 Workflow 完成的主控原生总结」为一条 AI 气泡——走与 history 重建**完全相同**的渲染
   * 管线（createRestoredAiMessage + applyNormalizedEventToMessage），而非手搓 createTextBubble。
   * 折中重构：消除 useBackgroundTask 与 history 重建的双渲染路径，二者收敛到同一处。
   * turnId 语义上对应 store 落盘的 bgtask:<taskId>，重载后由 history 重建复现同一条气泡（不重复）。
   */
  const injectBackgroundSummary = (
    windowId: string | null | undefined,
    content: string,
    timestamp?: number
  ): void => {
    const windowState = (windowId ? options.windows.value.find(w => w.id === windowId) : undefined)
      ?? options.windows.value[0];
    if (!windowState) return;
    const text = (content ?? '').trim();
    if (!text) return;
    const ts = (typeof timestamp === 'number' && isFinite(timestamp)) ? timestamp : Date.now();

    const aiMessage = createRestoredAiMessage(ts);
    const normalized = normalizeStreamEvent({ eventType: 'text.completed', payload: { content: text } });
    if (!normalized) return;
    applyNormalizedEventToMessage(aiMessage, normalized, undefined);
    aiMessage.isStreaming = false;
    aiMessage.endTime = ts;
    exitWaitingState(aiMessage.waitingState);
    windowState.messages.push(aiMessage);

    void nextTick().then(() => options.scrollToBottom({ windowId: windowState.id }));
  };

  const wait = (ms: number): Promise<void> =>
    new Promise(resolve => setTimeout(resolve, ms));

  const pollHistoryForWindow = async (windowId: string) => {
    if (activeHistoryPollingWindows.has(windowId)) {
      return;
    }

    activeHistoryPollingWindows.add(windowId);
    try {
      for (let attempt = 0; attempt < HISTORY_POLL_MAX_ATTEMPTS; attempt++) {
        if (!activeHistoryPollingWindows.has(windowId)) {
          return;
        }

        if (!options.windows.value.some(item => item.id === windowId)) {
          return;
        }

        await wait(HISTORY_POLL_INTERVAL_MS);

        if (!activeHistoryPollingWindows.has(windowId)) {
          return;
        }

        const status = await syncHistoryForWindow(windowId);
        if (!isLiveSessionStatus(status)) {
          if (status === 'idle') {
            sendQueuedDraftIfReady(windowId);
          }
          return;
        }
      }
    } catch (error) {
      console.warn(`[useChatStream] History polling failed for window ${windowId}:`, error);
    } finally {
      activeHistoryPollingWindows.delete(windowId);
    }
  };

  const startHistoryPollingForWindow = (windowId: string, status?: string | null) => {
    if (!isLiveSessionStatus(status) || activeHistoryPollingWindows.has(windowId)) {
      return;
    }

    void pollHistoryForWindow(windowId);
  };

  const cleanupHistoryPolling = () => {
    activeHistoryPollingWindows.clear();
    for (const timer of todoDismissTimers.values()) {
      clearTimeout(timer);
    }
    todoDismissTimers.clear();
  };

  const waitForInteractionContinuation = async (windowId: string) => {
    const maxAttempts = 30;
    const intervalMs = 800;
    const windowState = options.windows.value.find(item => item.id === windowId);
    if (windowState) {
      windowState.isStreaming = true;
    }

    try {
      for (let attempt = 0; attempt < maxAttempts; attempt++) {
        const status = await syncHistoryForWindow(windowId);
        if (status !== 'running' && status !== 'paused') {
          if (status === 'idle') {
            sendQueuedDraftIfReady(windowId);
          }
          return;
        }

        await new Promise(resolve => setTimeout(resolve, intervalMs));
      }
    } finally {
      if (windowState) {
        windowState.isStreaming = false;
      }
    }
  };

  const restoreHistory = async (windowIds: string[]) => {
    if (windowIds.length === 0) {
      return;
    }

    await Promise.all(windowIds.map(async windowId => {
      try {
        const status = await syncHistoryForWindow(windowId);
        startHistoryPollingForWindow(windowId, status);
      } catch (error) {
        console.warn(`[useChatStream] Restore history failed for window ${windowId}:`, error);
      }
    }));
  };

  const createChatHttpError = async (response: Response): Promise<ChatHttpError> => {
    const payload = await readErrorPayload(response);
    const errorType = payload.errorType || inferErrorType(response.status, payload.message);
    const info = mapChatError(response.status, errorType, payload.message);
    return new ChatHttpError(
      info.userMessage,
      response.status,
      errorType,
      payload.message,
      info.shouldMarkDisconnected
    );
  };

  const normalizeChatError = (error: unknown): { userMessage: string; shouldMarkDisconnected: boolean } => {
    if (error instanceof ChatHttpError) {
      return {
        userMessage: error.message,
        shouldMarkDisconnected: error.shouldMarkDisconnected
      };
    }

    if (error instanceof TypeError) {
      return {
        userMessage: '无法连接到 Agent 服务，请检查本地服务是否可用。',
        shouldMarkDisconnected: true
      };
    }

    return {
      userMessage: '发送消息失败，请稍后重试。',
      shouldMarkDisconnected: false
    };
  };

  const readErrorPayload = async (response: Response): Promise<{ message?: string; errorType?: string }> => {
    try {
      const payload = await response.json();
      return {
        message: payload?.message || payload?.error,
        errorType: payload?.errorType
      };
    } catch {
      return {};
    }
  };

  const inferErrorType = (status: number, message?: string): string | undefined => {
    if (status === 413) {
      return 'request_too_large';
    }

    const normalizedMessage = (message || '').toLowerCase();
    if (normalizedMessage.includes('attachment_missing')) return 'attachment_missing';
    if (normalizedMessage.includes('attachment_invalid')) return 'attachment_invalid';
    if (normalizedMessage.includes('attachment_too_large')) return 'attachment_too_large';
    if (normalizedMessage.includes('request_too_large')) return 'request_too_large';
    return undefined;
  };

  const mapChatError = (
    status: number,
    errorType?: string,
    rawMessage?: string
  ): { userMessage: string; shouldMarkDisconnected: boolean } => {
    switch (errorType) {
      case 'attachment_missing':
        return { userMessage: '附件文件不存在，请重新添加图片后再试。', shouldMarkDisconnected: false };
      case 'attachment_invalid':
        return { userMessage: '附件无效，无法读取。请重新添加图片后再试。', shouldMarkDisconnected: false };
      case 'attachment_too_large':
        return { userMessage: '图片过大，超出可发送限制。请重新截图或压缩后再试。', shouldMarkDisconnected: false };
      case 'request_too_large':
        return { userMessage: '附件过大，无法发送。请减少图片数量或缩小图片后再试。', shouldMarkDisconnected: false };
      default:
        break;
    }

    if (status === 503) {
      return {
        userMessage: 'Agent 服务不可用，请检查本地 Agent 是否已启动。',
        shouldMarkDisconnected: true
      };
    }

    if (status >= 500) {
      return {
        userMessage: rawMessage || `请求失败（HTTP ${status}），请稍后重试。`,
        shouldMarkDisconnected: false
      };
    }

    return {
      userMessage: rawMessage || `请求失败（HTTP ${status}）。`,
      shouldMarkDisconnected: false
    };
  };

  const interruptWindow = async (windowId: string, keepDeliveredStateOnAbort = false) => {
    const win = findWindow(windowId);
    if (!win || !win.isStreaming) {
      console.log('[interruptMessage] No active streaming to interrupt');
      return;
    }

    const effectiveWindowId = windowId || 'window-main';

    console.log('[interruptMessage] Interrupting conversation:', { windowId: effectiveWindowId });

    try {
      // 1. 取消前端 fetch 请求
      const controller = currentAbortControllers.get(windowId);
      if (controller) {
        if (keepDeliveredStateOnAbort) {
          preserveDeliveredStateOnAbort.add(windowId);
        }
        controller.abort();
        currentAbortControllers.delete(windowId);
      }

      // 2. 通知后端中止 Agent
      const response = await fetch(`${options.agentApiBase}/api/interrupt`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          windowId: effectiveWindowId
        })
      });

      if (response.ok) {
        console.log('[interruptMessage] Successfully interrupted');
      } else {
        console.warn('[interruptMessage] Backend interrupt returned:', response.status);
      }

      // 3. 更新前端状态
      win.isStreaming = false;
      finishTodoProgress(win, 'interrupted', '已中止', 3000);

      // 4. 找到最后一条 AI 消息并标记为中止
      const lastAiMsgIndex = win.messages.length - 1;
      if (lastAiMsgIndex >= 0) {
        const lastMsg = win.messages[lastAiMsgIndex];
        if (lastMsg && lastMsg.role === 'ai') {
          lastMsg.isStreaming = false;
          lastMsg.waitingState.isWaiting = false;

          // 清理所有 streaming 状态的 bubble（包括并行工具调用和子气泡）
          cleanupAllStreamingBubbles(lastMsg.bubbles);
          // 在最后一个 text bubble 上追加中止标记
          const lastTextBubble = lastMsg.bubbles.filter(b => b.type === 'text').pop();
          if (lastTextBubble && lastTextBubble.status === 'completed') {
            lastTextBubble.content = lastTextBubble.content + '\n\n[已中止]';
          }
        }
      }

    } catch (error) {
      // AbortError 是正常的取消，不需要报错
      if (error instanceof Error && error.name !== 'AbortError') {
        console.error('[interruptMessage] Error:', error);
      }
    }
  };

  /**
   * 中止当前正在进行的 AI 对话
   * 通过调用后端 /api/interrupt 端点实现
   */
  const interruptMessage = async () => {
    const windowId = options.activeWindow.value?.id || options.activeWindowId.value || 'window-main';
    await interruptWindow(windowId);
  };

  const sendQueuedMessageNow = async () => {
    const win = options.activeWindow.value;
    const queuedDraft = win?.queuedMessage;
    if (!win || !queuedDraft) {
      return;
    }

    win.queuedMessage = null;
    if (win.isStreaming) {
      await interruptWindow(win.id, true);
    }

    await sendDraft(win.id, queuedDraft, 'queued');
  };

  const restoreQueuedMessage = (): boolean => {
    const win = options.activeWindow.value;
    const queuedDraft = win?.queuedMessage;
    if (!win || !queuedDraft || hasWindowDraftContent(win)) {
      return false;
    }

    win.inputMessage = queuedDraft.text;
    win.pendingAttachments = queuedDraft.attachments;
    win.pendingSpatialMarks = cloneSpatialMarks(queuedDraft.spatialMarks);
    win.draftMessageId = queuedDraft.clientMessageId;
    win.queuedMessage = null;
    return true;
  };

  const deleteQueuedMessage = async () => {
    const win = options.activeWindow.value;
    const queuedDraft = win?.queuedMessage;
    if (!win || !queuedDraft) {
      return;
    }

    win.queuedMessage = null;
    const draftAttachments = queuedDraft.attachments.filter(item => item.status !== 'submitted');
    if (draftAttachments.length === 0) {
      return;
    }

    try {
      if (!currentProjectPath.value) {
        await fetchProjectPath();
      }
      if (!currentProjectPath.value) {
        return;
      }

      await Promise.all(draftAttachments.map(attachment =>
        ChatAttachmentService
          .deleteAttachment(currentProjectPath.value, attachment.attachmentId)
          .catch(error => console.warn('[queuedMessage] Delete attachment failed:', error))
      ));
    } catch (error) {
      console.warn('[queuedMessage] Cleanup queued attachments failed:', error);
    }
  };

  return {
    agentStatus,
    currentProjectPath,
    isPollingBackground,
    streamWelcomeMessage,
    sendMessage,
    sendQueuedMessageNow,
    restoreQueuedMessage,
    deleteQueuedMessage,
    restoreHistory,
    waitForInteractionContinuation,
    interruptMessage,
    injectBackgroundSummary,
    checkAgentHealth,
    fetchProjectPath,
    cleanupHealthCheck,
    cleanupHistoryPolling
  };
};
