import { nextTick, ref } from 'vue';
import type { Ref } from 'vue';
import type { ChatMessage, ChatWindow, EffortLevel, ModelOption, ThinkingLevel } from '../../types/aiCommandCenter';
import type { WaitingState, ChatBubble } from '../../types/agent';
import { ProjectService } from '../../services/ProjectService';
import {
  createTextBubble,
  createToolCallBubble,
  createSubAgentBubble,
  createThinkingBubble,
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

interface ChatStreamOptions {
  agentApiBase: string;
  windows: Ref<ChatWindow[]>;
  activeWindowId: Ref<string>;
  activeWindow: Ref<ChatWindow | undefined>;
  addMessage: (message: ChatMessage) => number;
  addMessageToWindow: (windowId: string, message: ChatMessage) => number;
  getWindowMessage: (windowId: string, msgIndex: number) => ChatMessage | undefined;
  pendingImages: Ref<string[]>;
  currentModel: Ref<ModelOption | null>;
  currentEffort: Ref<EffortLevel>;
  currentThinking: Ref<ThinkingLevel>;
  scrollToBottom: (options?: { force?: boolean; windowId?: string }) => void;
  fetchAgentConfig: () => Promise<void>;
  buildContextPayload?: () => Record<string, any> | undefined;
}

// 用于中止请求的 AbortController 管理
let currentAbortController: AbortController | null = null;

export const useChatStream = (options: ChatStreamOptions) => {
  const agentStatus = ref<'connecting' | 'connected' | 'disconnected'>('disconnected');
  const currentProjectPath = ref('');
  const isPollingBackground = ref(false);

  const getRandomWaitingVerb = (): string =>
    WAITING_VERBS[Math.floor(Math.random() * WAITING_VERBS.length)];

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

      if (i < welcomeText.length) {
        msg.bubbles[0].content += welcomeText[i];
        i++;
        options.scrollToBottom({ windowId: targetWindowId });
      } else {
        clearInterval(interval);
        msg.bubbles[0].status = 'completed';
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

  const sendMessage = async () => {
    const win = options.activeWindow.value;
    if (!win) return;

    const message = win.inputMessage.trim();
    if (!message || win.isStreaming) return;

    // 每次发消息前刷新项目路径，确保项目切换后携带最新路径
    await fetchProjectPath();

    const targetWindowId = win.id;

    // 先提取待发送图片，再清空
    const imagesToSend = [...options.pendingImages.value];
    options.pendingImages.value = [];

    const userTextBubble = createTextBubble(message);
    userTextBubble.status = 'completed';
    // 如果有图片，存储到气泡中
    if (imagesToSend.length > 0) {
      userTextBubble.images = imagesToSend;
    }
    options.addMessageToWindow(targetWindowId, {
      role: 'user',
      bubbles: [userTextBubble],
      waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 }
    });
    win.inputMessage = '';
    win.isStreaming = true;

    win.shouldAutoScroll = true;
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

    try {
      const effectiveWindowId = options.activeWindowId.value || 'window-main';

      console.log('[sendMessage] Request:', {
        projectPath: currentProjectPath.value,
        windowId: effectiveWindowId,
        message: message.substring(0, 50) + (message.length > 50 ? '...' : ''),
        imagesCount: imagesToSend.length,
        model: options.currentModel.value?.id,
        effort: options.currentEffort.value.id,
        thinking: options.currentThinking.value.id
      });

      // 创建新的 AbortController 用于中止请求
      currentAbortController = new AbortController();

      const context = options.buildContextPayload?.();
      const response = await fetch(`${options.agentApiBase}/api/chat/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          projectPath: currentProjectPath.value,
          windowId: effectiveWindowId,
          worktreePath: options.activeWindow.value?.worktreePath,
          message,
          images: imagesToSend,
          model: options.currentModel.value?.id,
          effort: options.currentEffort.value.id,
          thinking: options.currentThinking.value.id,
          ...(context ? { context } : {})
        }),
        signal: currentAbortController.signal
      });

      if (!response.ok) {
        throw new Error(`HTTP error: ${response.status}`);
      }

      const reader = response.body?.getReader();
      const decoder = new TextDecoder();

      if (!reader) {
        throw new Error('No response body');
      }

      let buffer = '';
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (line.startsWith('data: ')) {
            const data = line.slice(6);
            if (data === '[DONE]') {
              break;
            }
            try {
              const parsed = JSON.parse(data);

              const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
              if (!currentMsg) continue;

              const targetWin = options.windows.value.find(w => w.id === targetWindowId);
              if (!targetWin) continue;

              if (parsed.type === 'thinking') {
                // 查找当前正在 streaming 的 thinking 气泡
                let activeThinking = getLastStreamingThinkingBubble(currentMsg.bubbles);
                if (!activeThinking) {
                  // 创建新的 thinking 气泡
                  activeThinking = createThinkingBubble(parsed.content || '');
                  currentMsg.bubbles.push(activeThinking);
                } else {
                  // 追加到现有 thinking 气泡
                  activeThinking.content = (activeThinking.content || '') + (parsed.content || '');
                }
                exitWaitingState(currentMsg.waitingState);
              } else if (parsed.type === 'thinking_complete') {
                let activeThinking = getLastStreamingThinkingBubble(currentMsg.bubbles);
                if (!activeThinking) {
                  // 边界情况：没有活跃的 thinking 气泡
                  activeThinking = createThinkingBubble(parsed.content || '');
                  currentMsg.bubbles.push(activeThinking);
                }
                // 用完整内容覆盖并标记完成
                if (parsed.content) {
                  activeThinking.content = parsed.content;
                }
                completeThinkingBubble(activeThinking);
              } else if (parsed.type === 'text') {
                exitWaitingState(currentMsg.waitingState);
                // 自动折叠最后一个 thinking 气泡
                collapseLastThinkingBubble(currentMsg.bubbles);

                if (parsed.errorType === 'recoverable') {
                  if (import.meta.env.DEV) {
                    console.log('[Recoverable error (hidden)]', parsed.errorContent || parsed.content);
                  }
                  continue;
                }

                if (parsed.errorType === 'blocking') {
                  if (import.meta.env.DEV) {
                    console.warn('[Blocking error (hidden from chat)]', parsed.errorContent || parsed.content);
                  }
                  continue;
                }

                // sdk_error 和 api_error 需要用户知晓 → 不跳过，走正常文本追加逻辑

                const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

                if (lastTextBubble) {
                  lastTextBubble.content = (lastTextBubble.content || '') + (parsed.content || '');
                } else {
                  const newTextBubble = createTextBubble(parsed.content || '');
                  currentMsg.bubbles.push(newTextBubble);
                }

                if (parsed.hiddenContent && import.meta.env.DEV) {
                  console.debug('[Hidden recoverable error]', parsed.hiddenContent);
                }
              } else if (parsed.type === 'text_complete') {
                const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

                if (lastTextBubble) {
                  completeBubble(lastTextBubble);
                } else if (parsed.content) {
                  const newTextBubble = createTextBubble(parsed.content);
                  newTextBubble.status = 'completed';
                  currentMsg.bubbles.push(newTextBubble);
                }

                if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                  enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
                }
              } else if (parsed.error) {
                console.error('[SSE Error]', parsed.error);
              } else if (parsed.type === 'subagent_start') {
                exitWaitingState(currentMsg.waitingState);
                collapseLastThinkingBubble(currentMsg.bubbles);

                const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
                if (lastTextBubble) {
                  completeBubble(lastTextBubble);
                }

                const subAgentBubble = createSubAgentBubble(
                  parsed.subAgentId,
                  parsed.subAgentName,
                  parsed.subAgentType
                );
                currentMsg.bubbles.push(subAgentBubble);
              } else if (parsed.type === 'subagent_complete') {
                const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.subAgentId);
                if (subAgentBubble) {
                  if (parsed.success === false) {
                    failBubble(subAgentBubble, parsed.error);
                  } else {
                    completeBubble(subAgentBubble);
                  }
                  if (parsed.content) {
                    updateSubAgentResult(subAgentBubble, parsed.content);
                  }
                }
                if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                  enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
                }
              } else if (parsed.type === 'tool_call_start') {
                exitWaitingState(currentMsg.waitingState);
                collapseLastThinkingBubble(currentMsg.bubbles);

                const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
                if (lastTextBubble) {
                  completeBubble(lastTextBubble);
                }

                const toolBubble = createToolCallBubble(
                  parsed.toolCallId,
                  parsed.toolName,
                  parsed.toolDescription,
                  parsed.toolParams
                );

                if (parsed.subAgentId) {
                  const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.subAgentId);
                  if (subAgentBubble && subAgentBubble.type === 'subagent') {
                    if (!subAgentBubble.childBubbles) {
                      subAgentBubble.childBubbles = [];
                    }
                    subAgentBubble.childBubbles.push(toolBubble);
                  }
                } else {
                  currentMsg.bubbles.push(toolBubble);
                }
              } else if (parsed.type === 'tool_call_output') {
                const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
                if (toolBubble && toolBubble.type === 'tool_call') {
                  appendToolCallOutput(toolBubble, parsed.toolOutput);
                }
              } else if (parsed.type === 'tool_call_complete') {
                const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
                if (toolBubble && toolBubble.type === 'tool_call') {
                  // 保存工具返回结果到 bubble
                  if (parsed.toolOutput) {
                    appendToolCallOutput(toolBubble, parsed.toolOutput);
                  }
                  if (parsed.success) {
                    completeBubble(toolBubble);
                  } else {
                    failBubble(toolBubble, parsed.error);
                  }
                }
                if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                  enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
                }
              } else if (parsed.type === 'task_output_polling') {
                isPollingBackground.value = true;
                const streamingSubAgents = findStreamingSubAgents(currentMsg.bubbles);
                for (const bubble of streamingSubAgents) {
                  markAsBackground(bubble);
                  bubble.subAgentResult = `正在获取结果... (timeout: ${parsed.timeout / 1000}s)`;
                }
              }

              await nextTick();
              options.scrollToBottom({ windowId: targetWindowId });
            } catch (error) {
              console.error('Parse error:', error, data);
            }
          }
        }
      }

      const finalMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (finalMsg) {
        finalMsg.isStreaming = false;
        finalMsg.waitingState.isWaiting = false;

        // 兜底清理：递归完成所有残留的 streaming 气泡（包括 tool_call、subagent、text、thinking）
        cleanupAllStreamingBubbles(finalMsg.bubbles);
      }

      agentStatus.value = 'connected';
    } catch (error) {
      // AbortError 是用户主动中止，不是真正的错误
      if (error instanceof Error && error.name === 'AbortError') {
        console.log('[sendMessage] Request aborted by user');
        // 正常结束，不显示错误
        const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
        if (currentMsg) {
          currentMsg.isStreaming = false;
          currentMsg.waitingState.isWaiting = false;
          // 兜底清理所有残留的 streaming 气泡
          cleanupAllStreamingBubbles(currentMsg.bubbles);
        }
        return;  // 提前返回，跳过错误处理
      }

      // 其他错误正常处理
      console.error('Chat error:', error);
      const currentMsg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (currentMsg) {
        if (currentMsg.bubbles.length === 0) {
          const errorBubble = createTextBubble('Sorry, I encountered an error. Please check if the Agent server is running.');
          errorBubble.status = 'failed';
          currentMsg.bubbles.push(errorBubble);
        }
        currentMsg.isStreaming = false;
        currentMsg.waitingState.isWaiting = false;
      }
      agentStatus.value = 'disconnected';
    } finally {
      const targetWin = options.windows.value.find(w => w.id === targetWindowId);
      if (targetWin) {
        targetWin.isStreaming = false;
      }
      isPollingBackground.value = false;
      currentAbortController = null;  // 清理 AbortController
      await nextTick();
      options.scrollToBottom({ windowId: targetWindowId });
    }
  };

  /**
   * 中止当前正在进行的 AI 对话
   * 通过调用后端 /api/interrupt 端点实现
   */
  const interruptMessage = async () => {
    const win = options.activeWindow.value;
    if (!win || !win.isStreaming) {
      console.log('[interruptMessage] No active streaming to interrupt');
      return;
    }

    const targetWindowId = win.id;
    const effectiveWindowId = options.activeWindowId.value || 'window-main';

    console.log('[interruptMessage] Interrupting conversation:', { windowId: effectiveWindowId });

    try {
      // 1. 取消前端 fetch 请求
      if (currentAbortController) {
        currentAbortController.abort();
        currentAbortController = null;
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

  return {
    agentStatus,
    currentProjectPath,
    isPollingBackground,
    streamWelcomeMessage,
    sendMessage,
    interruptMessage,
    checkAgentHealth,
    fetchProjectPath,
    cleanupHealthCheck
  };
};
