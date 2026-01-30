import { nextTick, ref } from 'vue';
import type { Ref } from 'vue';
import type { ChatMessage, ChatWindow, ModelOption, ThinkingLevel } from '../../types/aiCommandCenter';
import type { WaitingState } from '../../types/agent';
import { ProjectService } from '../../services/ProjectService';
import {
  createTextBubble,
  createToolCallBubble,
  createSubAgentBubble,
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
  currentThinking: Ref<ThinkingLevel>;
  scrollToBottom: (options?: { force?: boolean; windowId?: string }) => void;
  fetchAgentConfig: () => Promise<void>;
}

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

  const checkAgentHealth = async () => {
    agentStatus.value = 'connecting';
    try {
      const response = await fetch(`${options.agentApiBase}/health`);
      if (response.ok) {
        agentStatus.value = 'connected';
        await options.fetchAgentConfig();
      } else {
        agentStatus.value = 'disconnected';
      }
    } catch {
      agentStatus.value = 'disconnected';
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

  const sendMessage = async () => {
    const win = options.activeWindow.value;
    if (!win) return;

    const message = win.inputMessage.trim();
    if (!message || win.isStreaming) return;

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
      startTime: Date.now(),
      thinking: '',
      thinkingDuration: undefined
    });

    const timerInterval = setInterval(() => {
      const msg = options.getWindowMessage(targetWindowId, aiMessageIndex);
      if (msg && msg.isStreaming && msg.bubbles.length === 0 && msg.thinking) {
        const duration = Math.round((Date.now() - (msg.startTime || Date.now())) / 1000);
        msg.thinkingDuration = duration + 's';
      } else {
        clearInterval(timerInterval);
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
        thinkingLevel: options.currentThinking.value.id
      });

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
          thinkingLevel: options.currentThinking.value.id
        })
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

              if (parsed.type === 'thinking' || parsed.type === 'thinking_complete') {
                if (parsed.type === 'thinking_complete') {
                  currentMsg.thinking = parsed.content;
                } else {
                  currentMsg.thinking = (currentMsg.thinking || '') + parsed.content;
                }
                if (!targetWin.expandedThinking[aiMessageIndex]) {
                  targetWin.expandedThinking[aiMessageIndex] = true;
                }
              } else if (parsed.type === 'text') {
                exitWaitingState(currentMsg.waitingState);

                if (currentMsg.thinking && targetWin.expandedThinking[aiMessageIndex] === true) {
                  currentMsg.endTime = Date.now();
                  const duration = Math.round((currentMsg.endTime - (currentMsg.startTime || currentMsg.endTime)) / 1000);
                  currentMsg.thinkingDuration = duration + 's';
                  targetWin.expandedThinking = { ...targetWin.expandedThinking, [aiMessageIndex]: false };
                  nextTick(() => options.scrollToBottom({ windowId: targetWindowId }));
                }

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

                if (parsed.errorType === 'permission_required') {
                  console.warn('[Permission error]', parsed.errorContent || parsed.content);
                  continue;
                }

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

        const lastStreamingBubble = getLastStreamingTextBubble(finalMsg.bubbles);
        if (lastStreamingBubble) {
          completeBubble(lastStreamingBubble);
        }
      }

      agentStatus.value = 'connected';
    } catch (error) {
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
      await nextTick();
      options.scrollToBottom({ windowId: targetWindowId });
    }
  };

  return {
    agentStatus,
    currentProjectPath,
    isPollingBackground,
    streamWelcomeMessage,
    sendMessage,
    checkAgentHealth,
    fetchProjectPath
  };
};
