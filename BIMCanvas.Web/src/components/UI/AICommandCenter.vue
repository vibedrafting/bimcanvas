<script setup lang="ts">
import { ref, onMounted, nextTick, computed, watch } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { useGitStore } from '../../stores/gitStore';
import { ProjectService } from '../../services/ProjectService';
import { getScreenshotService } from '../../services/ScreenshotService';
import { storeToRefs } from 'pinia';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';
import type { SubAgent, ToolCall, ChatBubble, WaitingState } from '../../types/agent';
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
import ToolCallBubble from './ToolCallBubble.vue';
import SubAgentBubble from './SubAgentBubble.vue';
import WaitingIndicator from './WaitingIndicator.vue';

// Props from parent (MainLayout)
const props = defineProps<{
  panelReady?: boolean;
}>();

// API Configuration
const AGENT_API_BASE = 'http://127.0.0.1:8765';

const panelWidth = ref(480);
const isResizing = ref(false);
const isBranchDropdownOpen = ref(false);
const showCheckoutConfirmDialog = ref(false);
const pendingCheckoutBranch = ref('');
const mode = ref('chat'); // 'chat' | 'tasks'
const isTaskSummaryExpanded = ref(false);

// Git Store - 使用共享Store管理分支状态
const gitStore = useGitStore();
const { branches, currentBranch, isLoading: isBranchLoading } = storeToRefs(gitStore);

// Store Integration
const store = useCanvasStore();
const { selectedIds } = storeToRefs(store);

// Computed Selection State - 使用 selectedIds 作为选中数量的数据源
const selectedModuleCount = computed(() => {
  return selectedIds.value.length;
});

// Debug watcher


// Sticky Scope State
const activeScope = ref('Global');

// Watch selection to update scope (Sticky Logic)
watch(selectedModuleCount, (count) => {
  if (count > 0) {
    // In a real app, we would find the room of the selected item here.
    // For now, we simulate it being 'Living Room'.
    activeScope.value = 'Living Room';
  }
  // If count === 0, we DO NOT reset activeScope, keeping it "sticky".
});

// Agent connection state
const agentStatus = ref<'connecting' | 'connected' | 'disconnected'>('disconnected');
const currentProjectPath = ref('');

// Chat state - 使用时间线气泡模型
interface ChatMessage {
  role: 'user' | 'ai';
  /** 是否正在流式传输 */
  isStreaming?: boolean;
  /** 开始时间戳 */
  startTime?: number;
  /** 结束时间戳 */
  endTime?: number;
  /** 思考内容 */
  thinking?: string;
  /** 思考持续时间 */
  thinkingDuration?: string;
  /** 时间线气泡列表（核心数据结构） */
  bubbles: ChatBubble[];
  /** 等待状态 */
  waitingState: WaitingState;
}

// Claude Code 风格的拟人等待提示词 (169 个)
const WAITING_VERBS = [
  'Accomplishing', 'Actioning', 'Actualizing', 'Baking', 'Beaming', 'Beboppin',
  'Befuddling', 'Billowing', 'Blanching', 'Bloviating', 'Boogieing', 'Boondoggling',
  'Bootstrapping', 'Booping', 'Brewing', 'Burrowing', 'Calculating', 'Caramelizing',
  'Cascading', 'Capturing', 'Cerebrating', 'Channelling', 'Choreographing', 'Churning',
  'Clauding', 'Coalescing', 'Cogitating', 'Composing', 'Combobulating', 'Concocting',
  'Considering', 'Contemplating', 'Cooking', 'Crafting', 'Creating', 'Crunching',
  'Crystallizing', 'Cultivating', 'Deciphering', 'Deliberating', 'Determining',
  'Discombobulating', 'Distilling', 'Doing', 'Dilly-dallying', 'Doodling', 'Ebbing',
  'Effecting', 'Elucidating', 'Embellishing', 'Enchanting', 'Envisioning', 'Evaporating',
  'Fermenting', 'Fiddle-fadding', 'Finagling', 'Flambéing', 'Flibbertigibbeting',
  'Flowing', 'Flummoxing', 'Forging', 'Forming', 'Frosting', 'Frolicking', 'Gallivanting',
  'Generating', 'Germinating', 'Gitifying', 'Grooving', 'Gusting', 'Hatching', 'Herding',
  'Hibernating', 'Honking', 'Hullaballooing', 'Hyperspacing', 'Ideating', 'Imagining',
  'Incubating', 'Inferring', 'Infusing', 'Jitterbugging', 'Julienning', 'Kneading',
  'Leavening', 'Levitating', 'Lollygagging', 'Manifesting', 'Marinating', 'Meandering',
  'Misting', 'Moseying', 'Mulling', 'Mustering', 'Musing', 'Nebulizing', 'Noodling',
  'Nucleating', 'Orbiting', 'Perambulating', 'Percolating', 'Perusing', 'Philosophising',
  'Photosynthesizing', 'Pontificating', 'Pondering', 'Pollinating', 'Precipitating',
  'Processing', 'Proofing', 'Propagating', 'Puttering', 'Puzzling', 'Quantumizing',
  'Razzle-dazzling', 'Recombobulating', 'Reticulating', 'Ruminating', 'Scheming',
  'Schlepping', 'Scurrying', 'Scampering', 'Seasoning', 'Shenaniganing', 'Shimming',
  'Shimmying', 'Simmering', 'Skedaddling', 'Sketching', 'Slithering', 'Smooshing',
  'Spelunking', 'Spinning', 'Sprouting', 'Stewing', 'Sublimating', 'Sussing', 'Swooping',
  'Symbioting', 'Synthesizing', 'Tempering', 'Thinking', 'Thundering', 'Tinkering',
  'Topsy-turvying', 'Transfiguring', 'Transmuting', 'Trick-or-treating', 'Twisting',
  'Unfurling', 'Unravelling', 'Vibing', 'Waddling', 'Wandering', 'Warping',
  'Whatchamacalliting', 'Whirlpooling', 'Whirring', 'Whisking', 'Wibbling', 'Working',
  'Wrangling', 'Zesting', 'Zigzagging'
];

// 随机选择一个等待提示词
const getRandomWaitingVerb = (): string => {
  return WAITING_VERBS[Math.floor(Math.random() * WAITING_VERBS.length)];
};

const chatMessages = ref<ChatMessage[]>([]);
const inputMessage = ref('');
const pendingImages = ref<string[]>([]);  // 待发送的截图附件（base64）
const isLoading = ref(false);
const isPollingBackground = ref(false);  // 后台任务 polling 状态
const chatScrollRef = ref<HTMLElement | null>(null);
const chatBottomRef = ref<HTMLElement | null>(null);
const expandedThinking = ref<Record<number, boolean>>({});
const shouldAutoScroll = ref(true); // Track if we should auto-scroll to bottom

// Auto-resize Textarea
const textareaRef = ref<HTMLTextAreaElement | null>(null);

const adjustTextareaHeight = () => {
  const el = textareaRef.value;
  if (!el) return;
  el.style.height = 'auto';
  el.style.height = el.scrollHeight + 'px';
};

watch(inputMessage, (newVal) => {
    if (!newVal) {
        nextTick(() => {
            if (textareaRef.value) {
                textareaRef.value.style.height = 'auto';
            }
        });
    }
});

// Toggle thinking section visibility
const toggleThinking = (index: number) => {
  expandedThinking.value[index] = !expandedThinking.value[index];
};

// 辅助函数：将 ChatBubble (subagent) 转换为 SubAgent 格式
const bubbleToSubAgent = (bubble: ChatBubble): SubAgent => {
  // 将 childBubbles (tool_call bubbles) 转换为 ToolCall[]
  const toolCalls: ToolCall[] = (bubble.childBubbles || [])
    .filter(child => child.type === 'tool_call')
    .map(child => ({
      id: child.id,
      toolName: child.toolName || '',
      description: child.toolDescription,
      params: child.toolParams || {},
      output: child.toolOutput,
      status: child.status === 'streaming' ? 'running' : child.status as ToolCall['status'],
      startTime: child.timestamp,
      error: child.toolError
    }));

  return {
    id: bubble.id,
    name: bubble.subAgentName || '',
    type: bubble.subAgentType || 'general-purpose',
    status: bubble.status === 'streaming' ? 'running' : bubble.status as SubAgent['status'],
    toolCalls,
    result: bubble.subAgentResult,
    startTime: bubble.timestamp
  };
};

// Computed: Active or Recent SubAgents for the Task Monitor
// Logic:
// 1. If any agents are RUNNING, show them (Priority: High)
// 2. If no running agents, show agents from the LAST message (Priority: Low, represents "Completed" state)
// 3. Otherwise empty (Idle)
const activeSubAgents = computed(() => {
  // 1. Find all running agents globally (from bubbles)
  const runningAgents: SubAgent[] = [];
  chatMessages.value.forEach(msg => {
    if (msg.bubbles) {
      const runningBubbles = msg.bubbles.filter(
        b => b.type === 'subagent' && b.status === 'streaming'
      );
      runningAgents.push(...runningBubbles.map(bubbleToSubAgent));
    }
  });

  if (runningAgents.length > 0) {
    return runningAgents;
  }

  // 2. Fallback: Find the last message with subagent bubbles
  for (let i = chatMessages.value.length - 1; i >= 0; i--) {
    const msg = chatMessages.value[i];
    if (msg.role === 'ai' && msg.bubbles) {
      const subAgentBubbles = msg.bubbles.filter(b => b.type === 'subagent');
      if (subAgentBubbles.length > 0) {
        return subAgentBubbles.map(bubbleToSubAgent);
      }
    }
  }

  return [];
});

const handleStopAgent = (agentId: string) => {
    console.log('Request to stop agent:', agentId);
    // TODO: Implement backend interrupt call
};

// === Task Widget State Management ===
const taskWidgetExpanded = ref(false);

// Auto-expand logic for Task Widget
watch(activeSubAgents, (newAgents, oldAgents) => {
  const newRunning = newAgents.some(a => a.status === 'running');
  const oldRunning = oldAgents?.some(a => a.status === 'running') ?? false;
  
  // 1. From No Tasks -> Has Tasks: Auto expand
  if (newAgents.length > 0 && (!oldAgents || oldAgents.length === 0)) {
    taskWidgetExpanded.value = true;
  }
  // 2. From Completed/Idle -> Running: Auto expand
  if (newRunning && !oldRunning) {
    taskWidgetExpanded.value = true;
  }
}, { deep: true });

// Mock Data for Tasks - REMOVED (TaskSummaryWidget now uses subAgents)
// Proposals mock data is kept below for the Proposals carousel

const proposals = ref([
  {
    id: 'A',
    name: 'Ultimate Storage',
    tags: ['Storage++', 'Flow-'],
    metrics: { storage: '12.5m³', flow: 'Compact' },
    insight: 'Sacrificed 10% open space for max storage.',
    color: '#4facfe',
    thumbnailPattern: 'radial-gradient(circle at 30% 30%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #4facfe 0%, #00f2fe 100%)'
  },
  {
    id: 'B',
    name: 'Flow Priority',
    tags: ['Flow++', 'Open'],
    metrics: { storage: '8.0m³', flow: 'Excellent' },
    insight: 'Optimized for 1200mm main walkways.',
    color: '#00f2fe',
    thumbnailPattern: 'radial-gradient(circle at 70% 70%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #43e97b 0%, #38f9d7 100%)'
  },
  {
    id: 'C',
    name: 'Minimalist',
    tags: ['Light++', 'Cost-'],
    metrics: { storage: '6.5m³', flow: 'Good' },
    insight: 'Removed non-essential partitions.',
    color: '#a18cd1',
    thumbnailPattern: 'radial-gradient(circle at 50% 50%, rgba(255,255,255,0.1) 0%, transparent 60%), linear-gradient(135deg, #fbc2eb 0%, #a6c1ee 100%)'
  },
]);

// Select branch - 使用Store方法
const selectBranch = async (branchId: string) => {
  isBranchDropdownOpen.value = false;
  const result = await gitStore.checkout(branchId);

  if (result.success) {
    return;
  }

  // 如果有未提交的更改，显示确认弹窗
  if (result.hasUncommittedChanges) {
    pendingCheckoutBranch.value = branchId;
    showCheckoutConfirmDialog.value = true;
    return;
  }

  console.error('切换分支失败:', result.message);
};

// 确认弹窗回调
const handleCheckoutConfirm = async (saveBeforeSwitch: boolean, commitMessage?: string) => {
  showCheckoutConfirmDialog.value = false;
  const branchName = pendingCheckoutBranch.value;
  if (!branchName) return;

  if (saveBeforeSwitch) {
    // 1. 先保存内存数据到文件系统
    const saved = await store.saveToServer();
    if (!saved) {
      console.error('保存数据失败，无法切换分支');
      pendingCheckoutBranch.value = '';
      return;
    }

    // 2. 再用 commitBeforeCheckout 提交并切换
    await gitStore.checkout(branchName, {
      commitBeforeCheckout: true,
      commitMessage
    });
  } else {
    // 放弃更改并切换：Server端原子操作
    await gitStore.checkout(branchName, { discardBeforeCheckout: true });
  }

  pendingCheckoutBranch.value = '';
};

const handleCheckoutCancel = () => {
  showCheckoutConfirmDialog.value = false;
  pendingCheckoutBranch.value = '';
};

// Clear selection
const clearSelection = () => {
  store.clearSelection();
};

// Check Agent health on mount
onMounted(async () => {
  await checkAgentHealth();
  await gitStore.fetchBranches();  // 使用Store获取分支列表
  await fetchProjectPath();  // 获取当前项目路径
  // 启动截图服务 SSE 监听（响应 Agent 截图请求）
  const screenshotService = getScreenshotService(AGENT_API_BASE);
  screenshotService.startListening();
  // Note: Welcome message is now triggered by panelReady prop
});

// Watch for panelReady to trigger welcome message streaming
watch(() => props.panelReady, (newVal) => {
  if (newVal) {
    streamWelcomeMessage();
  }
});

const streamWelcomeMessage = async () => {
    // Prevent duplicate welcome messages if one already exists
    if (chatMessages.value.length > 0) return;

    const welcomeText = '你好！我是 BIMCanvas 的布置助手。我可以帮助你分析房间功能、提供布置建议。有什么我能帮你的吗？';

    // 创建欢迎消息，使用气泡模型
    const welcomeBubble = createTextBubble('');
    chatMessages.value.push({
        role: 'ai',
        bubbles: [welcomeBubble],
        waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 },
        isStreaming: true
    });

    const msgIndex = chatMessages.value.length - 1;

    // Simulate typing effect
    let i = 0;
    const interval = setInterval(() => {
        if (i < welcomeText.length) {
            // 更新气泡内容
            chatMessages.value[msgIndex].bubbles[0].content += welcomeText[i];
            i++;
            scrollToBottom();
        } else {
            clearInterval(interval);
            // 标记完成
            chatMessages.value[msgIndex].bubbles[0].status = 'completed';
            chatMessages.value[msgIndex].isStreaming = false;
        }
    }, 30);
};

// Watch for chat messages to auto-scroll (watch already imported at top)
watch(() => chatMessages.value, () => {
    // Only auto-scroll if user is already near bottom
    if (shouldAutoScroll.value) {
        nextTick(() => {
            scrollToBottom();
        });
    }
}, { deep: true });

// Check if user is near bottom (within 100px threshold)
const isNearBottom = () => {
    const el = chatScrollRef.value;
    if (!el) return true;
    const threshold = 100;
    return el.scrollHeight - el.scrollTop - el.clientHeight < threshold;
};

// Handle scroll event to track if user scrolled up
const handleChatScroll = () => {
    if (mode.value !== 'chat') return;
    shouldAutoScroll.value = isNearBottom();
};

// Agent API functions
const checkAgentHealth = async () => {
  agentStatus.value = 'connecting';
  try {
    const response = await fetch(`${AGENT_API_BASE}/health`);
    if (response.ok) {
      agentStatus.value = 'connected';
      // 连接成功后获取服务端配置
      await fetchAgentConfig();
    } else {
      agentStatus.value = 'disconnected';
    }
  } catch {
    agentStatus.value = 'disconnected';
  }
};

// 获取 Agent 服务端配置并初始化模型/思考强度选择
const fetchAgentConfig = async () => {
  try {
    // 并行获取默认配置和 Web 配置
    const [configRes, webConfigRes] = await Promise.all([
      fetch(`${AGENT_API_BASE}/api/config`),
      fetch(`${AGENT_API_BASE}/api/web-config`)
    ]);

    // 加载模型列表（完全由配置文件控制）
    if (webConfigRes.ok) {
      const webConfig = await webConfigRes.json();
      models.value = webConfig.customModels || [];
    }

    // 加载默认配置
    if (configRes.ok) {
      const config = await configRes.json();
      const { model: defaultModel, thinkingLevel: defaultThinking } = config;

      // 初始化模型选择
      if (defaultModel) {
        let found = models.value.find(m => m.id === defaultModel);
        if (!found) {
          // 默认模型不在列表中，添加到列表
          found = { id: defaultModel, label: defaultModel };
          models.value.push(found);
        }
        currentModel.value = found;
      } else if (models.value.length > 0) {
        // 没有默认模型配置，选择列表中的第一个
        currentModel.value = models.value[0];
      }

      // 初始化思考强度选择
      if (defaultThinking) {
        const foundThinking = thinkingLevels.find(t => t.id === defaultThinking);
        if (foundThinking) {
          currentThinking.value = foundThinking;
        }
      }
    }

    console.log('Agent 配置已加载:', { model: currentModel.value?.id, thinking: currentThinking.value.id });
  } catch (error) {
    console.warn('获取 Agent 配置失败:', error);
  }
};

// 获取当前项目路径
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
  const message = inputMessage.value.trim();
  if (!message || isLoading.value) return;

  // Add user message to chat - 使用气泡模型
  const userTextBubble = createTextBubble(message);
  userTextBubble.status = 'completed';
  chatMessages.value.push({
    role: 'user',
    bubbles: [userTextBubble],
    waitingState: { isWaiting: false, waitingVerb: '', waitingSince: 0 }
  });
  inputMessage.value = '';
  isLoading.value = true;

  // Force scroll to bottom when user sends message
  shouldAutoScroll.value = true;
  await nextTick();
  scrollToBottom({ force: true });
  requestAnimationFrame(() => scrollToBottom({ force: true }));
  setTimeout(() => scrollToBottom({ force: true }), 50);
  setTimeout(() => scrollToBottom({ force: true }), 150);

  // Add placeholder AI message for streaming - 使用气泡模型
  const aiMessageIndex = chatMessages.value.length;
  const initialWaitingState: WaitingState = {
    isWaiting: true,
    waitingVerb: getRandomWaitingVerb(),
    waitingSince: Date.now()
  };
  chatMessages.value.push({
    role: 'ai',
    bubbles: [],
    waitingState: initialWaitingState,
    isStreaming: true,
    startTime: Date.now(),
    thinking: '',
    thinkingDuration: undefined
  });

  // Start thinking timer - updates every second while streaming
  const timerInterval = setInterval(() => {
    const msg = chatMessages.value[aiMessageIndex];
    // Only update if still streaming and no bubbles yet (still in thinking phase)
    if (msg && msg.isStreaming && msg.bubbles.length === 0 && msg.thinking) {
      const duration = Math.round((Date.now() - (msg.startTime || Date.now())) / 1000);
      msg.thinkingDuration = duration + 's';
    } else {
      clearInterval(timerInterval);
    }
  }, 1000);

  try {
    // 获取并清空待发送图片
    const imagesToSend = [...pendingImages.value];
    pendingImages.value = [];

    const response = await fetch(`${AGENT_API_BASE}/api/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        projectPath: currentProjectPath.value,
        message: message,
        images: imagesToSend,  // 新增：图片附件
        model: currentModel.value?.id,
        thinkingLevel: currentThinking.value.id
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
            const currentMsg = chatMessages.value[aiMessageIndex];

            // ===== Thinking Events =====
            if (parsed.type === 'thinking' || parsed.type === 'thinking_complete') {
              if (parsed.type === 'thinking_complete') {
                currentMsg.thinking = parsed.content;
              } else {
                currentMsg.thinking = (currentMsg.thinking || '') + parsed.content;
              }
              // Auto-expand thinking on first chunk
              if (!expandedThinking.value[aiMessageIndex]) {
                expandedThinking.value[aiMessageIndex] = true;
              }
            }

            // ===== Text Events (使用气泡模型) =====
            else if (parsed.type === 'text') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // Auto-collapse thinking when text starts
              if (currentMsg.thinking && expandedThinking.value[aiMessageIndex] === true) {
                currentMsg.endTime = Date.now();
                const duration = Math.round((currentMsg.endTime - (currentMsg.startTime || currentMsg.endTime)) / 1000);
                currentMsg.thinkingDuration = duration + 's';
                expandedThinking.value = { ...expandedThinking.value, [aiMessageIndex]: false };
                nextTick(() => scrollToBottom());
              }

              // ✅ 如果是 recoverable 错误，跳过显示
              if (parsed.errorType === 'recoverable') {
                if (import.meta.env.DEV) {
                  console.log('[Recoverable error (hidden)]', parsed.errorContent || parsed.content);
                }
                continue;  // 跳过这个事件，不添加到气泡
              }

              // ✅ 如果是 blocking 错误，也不显示在对话面板
              if (parsed.errorType === 'blocking') {
                if (import.meta.env.DEV) {
                  console.warn('[Blocking error (hidden from chat)]', parsed.errorContent || parsed.content);
                }
                continue;  // 跳过显示（Server 控制台已经打印了）
              }

              // ✅ 处理权限错误（打印日志但不添加到气泡）
              if (parsed.errorType === 'permission_required') {
                console.warn('[Permission error]', parsed.errorContent || parsed.content);
                continue;  // 跳过显示
              }

              // 找到最后一个正在流式传输的文本气泡
              let lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);

              if (lastTextBubble) {
                // 追加到现有文本气泡
                lastTextBubble.content = (lastTextBubble.content || '') + (parsed.content || '');
              } else {
                // 创建新的文本气泡
                const newTextBubble = createTextBubble(parsed.content || '');
                currentMsg.bubbles.push(newTextBubble);
              }

              // 调试模式：记录被隐藏的 recoverable 错误
              if (parsed.hiddenContent && import.meta.env.DEV) {
                console.debug('[Hidden recoverable error]', parsed.hiddenContent);
              }
            }

            else if (parsed.type === 'text_complete') {
              // 标记最后一个文本气泡为完成
              const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
              if (lastTextBubble) {
                completeBubble(lastTextBubble);
              }
              // 只有当没有 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== Error Event =====
            else if (parsed.error) {
              // ✅ 只打印到控制台，不创建气泡（错误已在 Server 控制台显示）
              console.error('[SSE Error]', parsed.error);
              // 不添加到气泡列表
            }

            // ===== SubAgent Events (使用气泡模型) =====
            else if (parsed.type === 'subagent_start') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // 如果有正在流式传输的文本气泡，先标记为完成
              const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
              if (lastTextBubble) {
                completeBubble(lastTextBubble);
              }

              // 创建 SubAgent 气泡
              const subAgentBubble = createSubAgentBubble(
                parsed.subAgentId,
                parsed.subAgentName,
                parsed.subAgentType
              );
              currentMsg.bubbles.push(subAgentBubble);
            }

            else if (parsed.type === 'subagent_complete') {
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
              // 只有当没有其他 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== Tool Call Events (使用气泡模型) =====
            else if (parsed.type === 'tool_call_start') {
              // 退出等待状态
              exitWaitingState(currentMsg.waitingState);

              // 如果有正在流式传输的文本气泡，先标记为完成
              const lastTextBubble = getLastStreamingTextBubble(currentMsg.bubbles);
              if (lastTextBubble) {
                completeBubble(lastTextBubble);
              }

              // 创建工具调用气泡
              const toolBubble = createToolCallBubble(
                parsed.toolCallId,
                parsed.toolName,
                parsed.toolDescription,
                parsed.toolParams
              );

              if (parsed.subAgentId) {
                // SubAgent 内的工具调用 - 添加到 childBubbles
                const subAgentBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.subAgentId);
                if (subAgentBubble && subAgentBubble.type === 'subagent') {
                  if (!subAgentBubble.childBubbles) {
                    subAgentBubble.childBubbles = [];
                  }
                  subAgentBubble.childBubbles.push(toolBubble);
                }
              } else {
                // 主 Agent 的工具调用 - 添加到主时间线
                currentMsg.bubbles.push(toolBubble);
              }
            }

            else if (parsed.type === 'tool_call_output') {
              // 在所有气泡中查找工具调用气泡（包括 childBubbles）
              const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
              if (toolBubble && toolBubble.type === 'tool_call') {
                appendToolCallOutput(toolBubble, parsed.toolOutput);
              }
            }

            else if (parsed.type === 'tool_call_complete') {
              const toolBubble = findBubbleByIdDeep(currentMsg.bubbles, parsed.toolCallId);
              if (toolBubble && toolBubble.type === 'tool_call') {
                if (parsed.success) {
                  completeBubble(toolBubble);
                } else {
                  failBubble(toolBubble, parsed.error);
                }
              }
              // 只有当没有 SubAgent 在运行时，才进入等待状态
              if (!hasStreamingSubAgent(currentMsg.bubbles)) {
                enterWaitingState(currentMsg.waitingState, getRandomWaitingVerb);
              }
            }

            // ===== TaskOutput Polling Event (后台任务轮询) =====
            else if (parsed.type === 'task_output_polling') {
              // 设置全局 polling 状态（用于 UI 提示）
              isPollingBackground.value = true;

              // 将所有 streaming 状态的 SubAgent 标记为后台执行
              const streamingSubAgents = findStreamingSubAgents(currentMsg.bubbles);
              for (const bubble of streamingSubAgents) {
                markAsBackground(bubble);
                // 更新结果显示轮询状态
                bubble.subAgentResult = `正在获取结果... (timeout: ${parsed.timeout / 1000}s)`;
              }
            }

            await nextTick();
            scrollToBottom();
          } catch (e) {
            console.error('Parse error:', e, data);
          }
        }
      }
    }

    // Mark streaming as complete
    const finalMsg = chatMessages.value[aiMessageIndex];
    finalMsg.isStreaming = false;
    finalMsg.waitingState.isWaiting = false;

    // 将最后一个 streaming 状态的气泡标记为 completed
    const lastStreamingBubble = getLastStreamingTextBubble(finalMsg.bubbles);
    if (lastStreamingBubble) {
      completeBubble(lastStreamingBubble);
    }

    agentStatus.value = 'connected';

  } catch (error) {
    console.error('Chat error:', error);
    const currentMsg = chatMessages.value[aiMessageIndex];
    if (currentMsg) {
      // 如果没有任何气泡，创建错误文本气泡
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
    isLoading.value = false;
    isPollingBackground.value = false;  // 重置 polling 状态
    await nextTick();
    scrollToBottom();
  }
};



const scrollToBottom = (options?: { force?: boolean }) => {
  if (!options?.force && mode.value !== 'chat') return;
  if (!options?.force && !shouldAutoScroll.value) return;

  if (chatBottomRef.value) {
    chatBottomRef.value.scrollIntoView({ block: 'end' });
    return;
  }

  const el = chatScrollRef.value;
  if (el) {
    el.scrollTop = el.scrollHeight;
  }
};

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    sendMessage();
  }
};

const startResize = () => {
  isResizing.value = true;
  window.addEventListener('mousemove', handleResize);
  window.addEventListener('mouseup', stopResize);
  document.body.style.cursor = 'ew-resize';
  document.body.style.userSelect = 'none';
};

const handleResize = (e: MouseEvent) => {
  const newWidth = window.innerWidth - e.clientX;
  if (newWidth >= 300 && newWidth <= 600) {
    panelWidth.value = newWidth;
  }
};

const stopResize = () => {
  isResizing.value = false;
  window.removeEventListener('mousemove', handleResize);
  window.removeEventListener('mouseup', stopResize);
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
};

const carouselTrackRef = ref<HTMLElement | null>(null);

const handleWheel = (e: WheelEvent) => {
  if (carouselTrackRef.value && e.deltaY !== 0) {
    e.preventDefault();
    carouselTrackRef.value.scrollLeft += e.deltaY;
  }
};

const removeContext = (type: 'scope' | 'selection', item?: string) => {
    if (type === 'scope') {
        contextScope.value = '';
    } else if (item) {
        contextSelection.value = contextSelection.value.filter(i => i !== item);
    }
}

// Context Menu State
const isContextMenuOpen = ref(false);
const activeSubmenu = ref<string | null>(null);
const submenuDirection = ref<'left' | 'right'>('left');
const isAttachmentMenuOpen = ref(false);

// Toggle functions
const toggleContextMenu = () => {
  isContextMenuOpen.value = !isContextMenuOpen.value;
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
  isAttachmentMenuOpen.value = false;
  if (!isContextMenuOpen.value) activeSubmenu.value = null;
};

const toggleAttachmentMenu = () => {
  isAttachmentMenuOpen.value = !isAttachmentMenuOpen.value;
  isContextMenuOpen.value = false;
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
};

// Model & Thinking State
// 存储完整对象 { id, label }，发送时使用 id，显示时使用 label
// 模型列表完全由配置文件控制，启动时从 web-config.json 加载
const models = ref<{ id: string; label: string }[]>([]);

const thinkingLevels = [
  { id: 'off', label: 'Off' },
  { id: 'low', label: 'Low' },
  { id: 'medium', label: 'Medium' },
  { id: 'high', label: 'High' }
];

const currentModel = ref<{ id: string; label: string } | null>(null);
const currentThinking = ref(thinkingLevels[2]);  // 默认 medium
const isModelMenuOpen = ref(false);
const isThinkingMenuOpen = ref(false);

// 添加模型输入状态
const isAddingModel = ref(false);
const newModelId = ref('');
const newModelInputRef = ref<HTMLInputElement | null>(null);

// 保存模型列表到服务端
const saveCustomModels = async () => {
  try {
    await fetch(`${AGENT_API_BASE}/api/web-config`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ customModels: models.value })
    });
  } catch (error) {
    console.warn('保存模型列表失败:', error);
  }
};

const selectModel = (model: { id: string; label: string }) => {
  currentModel.value = model;
  isModelMenuOpen.value = false;
  isAddingModel.value = false;
};

// 开始添加模型
const startAddModel = () => {
  isAddingModel.value = true;
  newModelId.value = '';
  nextTick(() => {
    newModelInputRef.value?.focus();
  });
};

// 确认添加模型
const confirmAddModel = async () => {
  const id = newModelId.value.trim();
  if (id && !models.value.some(m => m.id === id)) {
    const newModel = { id, label: id };  // label 使用 id
    models.value.push(newModel);
    selectModel(newModel);
    await saveCustomModels();
  }
  cancelAddModel();
};

// 取消添加模型
const cancelAddModel = () => {
  isAddingModel.value = false;
  newModelId.value = '';
};

const selectThinking = (level: { id: string; label: string }) => {
  currentThinking.value = level;
  isThinkingMenuOpen.value = false;
};

const contextOptions = {
  zones: [
    { id: 'living-room', label: 'Living Room' },
    { id: 'kitchen', label: 'Kitchen' },
    { id: 'master-bedroom', label: 'Master Bedroom' },
    { id: 'bathroom', label: 'Bathroom' },
    { id: 'balcony', label: 'Balcony' }
  ],
  regulations: [
    { id: 'wheelchair', label: 'Wheelchair Access (ADA)' },
    { id: 'feng-shui', label: 'Feng Shui Principles' },
    { id: 'fire-code', label: 'Fire Safety Code' }
  ],
  attachments: [
    { id: 'screenshot-select', label: 'Select Area Screenshot' },
    { id: 'screenshot-canvas', label: 'Screenshot Canvas' },
    { id: 'screenshot-room', label: 'Screenshot Current Room' },
    { id: 'upload', label: 'Upload Image...' },
    { id: 'docs', label: 'Project Requirements.pdf' }
  ]
};

const openSubmenu = (id: string, event: MouseEvent) => {
    activeSubmenu.value = id;
    
    // Smart positioning logic
    const target = event.currentTarget as HTMLElement;
    const rect = target.getBoundingClientRect();
    const submenuWidth = 220; // Matches CSS width
    const windowWidth = window.innerWidth;
    
    // Check if there is space on the right
    if (rect.right + submenuWidth + 20 < windowWidth) {
        submenuDirection.value = 'right';
    } else {
        submenuDirection.value = 'left';
    }
};

const handleContextSelect = async (type: string, item: any) => {
  console.log('Selected context:', type, item);

  // Handle select area screenshot
  if (type === 'attachments' && item.id === 'screenshot-select') {
    isAttachmentMenuOpen.value = false;
    // 延迟显示覆盖层，让菜单先关闭
    setTimeout(() => {
      showScreenshotOverlay.value = true;
    }, 100);
    return;
  }

  // Handle screenshot attachments
  if (type === 'attachments' && (item.id === 'screenshot-canvas' || item.id === 'screenshot-room')) {
    isAttachmentMenuOpen.value = false;
    try {
      const screenshotService = getScreenshotService(AGENT_API_BASE);
      let imageData: string;

      if (item.id === 'screenshot-canvas') {
        imageData = await screenshotService.captureCanvas();
      } else {
        // TODO: Get current room ID from selection or active scope
        const roomId = 'room_001'; // Placeholder
        imageData = await screenshotService.captureRoom(roomId);
      }

      pendingImages.value.push(imageData);
      console.log(`[Screenshot] Added ${item.id}, total pending: ${pendingImages.value.length}`);
    } catch (e) {
      console.error('[Screenshot] Failed:', e);
    }
    return;
  }

  // Logic to add context
  if (type === 'zones') {
    activeScope.value = item.label; // Update scope for demo
  } else {
    // For other types, maybe add a chip to the input or a temporary toast
    // For now, let's just simulate adding it to the input for visibility
    inputMessage.value += ` [Context: ${item.label}] `;
  }

  isContextMenuOpen.value = false;
  activeSubmenu.value = null;
};

// Close menus when clicking outside
const handleGlobalClick = (e: MouseEvent) => {
  const target = e.target as HTMLElement;
  
  // Close Context Menu
  if (!target.closest('.add-context-wrapper')) {
    isContextMenuOpen.value = false;
    isAttachmentMenuOpen.value = false; // Also close attachment menu
    activeSubmenu.value = null;
  }

  // Close Branch Dropdown
  if (!target.closest('.branch-dropdown')) {
    isBranchDropdownOpen.value = false;
  }

  // Close Model Menu
  if (!target.closest('.control-pill-wrapper.model')) {
    isModelMenuOpen.value = false;
  }

  // Close Thinking Menu
  if (!target.closest('.control-pill-wrapper.thinking')) {
    isThinkingMenuOpen.value = false;
  }
};

// 处理表格区域的滚轮事件：禁止垂直滚轮触发表格水平滚动
const handleTableWheel = (e: WheelEvent) => {
  const target = e.target as HTMLElement;
  const tableWrapper = target.closest('.table-node-wrapper');

  if (tableWrapper) {
    // 如果主要是垂直滚动，阻止表格的水平滚动
    // 让事件冒泡到父容器进行垂直滚动
    if (Math.abs(e.deltaY) > Math.abs(e.deltaX)) {
      e.preventDefault();
      // 手动触发父容器的垂直滚动
      const scrollContainer = chatScrollRef.value;
      if (scrollContainer) {
        scrollContainer.scrollTop += e.deltaY;
      }
    }
  }
};

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
  // 在聊天区域监听 wheel 事件，需要 passive: false 才能 preventDefault
  chatScrollRef.value?.addEventListener('wheel', handleTableWheel, { passive: false });
});

import { onUnmounted } from 'vue';
onUnmounted(() => {
  window.removeEventListener('click', handleGlobalClick);
  chatScrollRef.value?.removeEventListener('wheel', handleTableWheel);
  // 停止截图服务 SSE 监听
  const screenshotService = getScreenshotService(AGENT_API_BASE);
  screenshotService.stopListening();
});

import TaskSummaryWidget from './TaskSummaryWidget.vue';
import MarkdownText from './base/MarkdownText.vue';
import ScreenshotOverlay from './ScreenshotOverlay.vue';

// 框选截图状态
const showScreenshotOverlay = ref(false);

const handleScreenshotCapture = async (imageData: string) => {
  showScreenshotOverlay.value = false;
  try {
    const screenshotService = getScreenshotService(AGENT_API_BASE);
    // 保存到本地
    const filePath = await screenshotService.saveToLocal(imageData);
    console.log(`[Screenshot] Saved to: ${filePath}`);
    // 添加到待发送附件
    pendingImages.value.push(imageData);
    console.log(`[Screenshot] Added to pending, total: ${pendingImages.value.length}`);
  } catch (e) {
    console.error('[Screenshot] Save failed:', e);
  }
};

const handleScreenshotCancel = () => {
  showScreenshotOverlay.value = false;
};
</script>

<template>
  <aside 
    class="ai-command-center" 
    :style="{ width: panelWidth + 'px' }"
  >
    <!-- Resize Handle -->
    <div class="resize-handle" @mousedown="startResize">
        <div class="handle-bar"></div>
    </div>
    
    <div class="main-content">
      
      <!-- Layer 1: Context Header -->
      <div class="layer-context">
        <div class="context-row">
          <!-- Branch Dropdown -->
          <div class="branch-dropdown" :class="{ open: isBranchDropdownOpen }">
            <button class="dropdown-trigger" @click="isBranchDropdownOpen = !isBranchDropdownOpen">
              <span class="icon">🌿</span>
              <span class="text">{{ currentBranch }}</span>
              <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <polyline points="6 9 12 15 18 9"></polyline>
              </svg>
            </button>
            <div class="dropdown-menu" v-if="isBranchDropdownOpen">
              <div class="branch-tree">
                <div 
                  v-for="branch in branches" 
                  :key="branch.id" 
                  class="branch-item"
                  :class="{ current: branch.isCurrent }"
                  @click="selectBranch(branch.id)"
                >
                  <div class="branch-main">
                    <span class="branch-icon">🌿</span>
                    <span class="branch-name">{{ branch.name }}</span>
                    <span v-if="branch.isCurrent" class="current-indicator">
                      <svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="3">
                        <polyline points="20 6 9 17 4 12"></polyline>
                      </svg>
                    </span>
                  </div>
                  <div class="branch-meta" v-if="branch.commit">
                    <span class="commit-msg">{{ branch.commit.message }}</span>
                    <span class="commit-time">{{ branch.commit.time }}</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="mode-switch">
          <button :class="{ active: mode === 'chat' }" @click="mode = 'chat'">Chat</button>
          <button :class="{ active: mode === 'tasks' }" @click="mode = 'tasks'">Task</button>
        </div>
      </div>

      <!-- Layer 2: Intelligence Stream -->
      <div class="layer-stream">
         
         <!-- View: Chat -->
        <div v-show="mode === 'chat'" class="view-chat" ref="chatScrollRef" @scroll="handleChatScroll">



            <!-- Actual Chat History -->
            <template v-for="(msg, index) in chatMessages" :key="index">
                <div class="chat-message" :class="[msg.role === 'user' ? 'user' : 'ai', { streaming: msg.isStreaming }]">
                    <!-- Avatar Removed -->
                    <div class="message-wrapper">
                        <!-- Thinking Section (for AI messages only) -->
                        <div v-if="msg.role === 'ai' && msg.thinking" class="thinking-section">
                            <div class="thinking-header" @click="toggleThinking(index)">
                                <svg
                                    class="thinking-chevron"
                                    :class="{ expanded: expandedThinking[index] }"
                                    viewBox="0 0 24 24"
                                    fill="none"
                                    stroke="currentColor"
                                    stroke-width="2"
                                >
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                                <span class="thinking-label">
                                    <template v-if="msg.isStreaming && msg.bubbles.length === 0">
                                        Thinking for {{ msg.thinkingDuration || '0s' }}<span class="dot">.</span><span class="dot">.</span><span class="dot">.</span>
                                    </template>
                                    <template v-else>
                                        Thought for {{ msg.thinkingDuration || '0s' }}
                                    </template>
                                </span>
                            </div>
                            <transition name="thinking-expand">
                                <div v-show="expandedThinking[index]" class="thinking-content">
                                    <MarkdownText :content="msg.thinking" />
                                </div>
                            </transition>
                        </div>
                        <!-- 时间线气泡列表渲染 -->
                        <template v-for="bubble in msg.bubbles" :key="bubble.id">
                            <!-- 文本气泡 - 用户消息用纯文本，AI 消息用 Markdown -->
                            <div class="bubble" v-if="bubble.type === 'text' && bubble.content">
                                <template v-if="msg.role === 'user'">{{ bubble.content }}</template>
                                <MarkdownText v-else :content="bubble.content" />
                            </div>

                            <!-- 工具调用气泡 -->
                            <ToolCallBubble
                                v-else-if="bubble.type === 'tool_call'"
                                :bubble="bubble"
                            />

                            <!-- SubAgent 气泡 -->
                            <SubAgentBubble
                                v-else-if="bubble.type === 'subagent'"
                                :bubble="bubble"
                            />
                        </template>

                        <!-- 等待提示词（在气泡列表末尾） -->
                        <WaitingIndicator
                            v-if="msg.waitingState?.isWaiting"
                            :state="msg.waitingState"
                        />
                    </div>
                </div>
            </template>

             <!-- Note: Loading state now handled within streaming messages -->
            <div ref="chatBottomRef" class="chat-bottom-anchor"></div>
         </div>

        <!-- View: Tasks (formerly Review) -->
        <div v-show="mode === 'tasks'" class="view-tasks">
            <!-- Agent Activity Monitor (SubAgent tracking) -->
            <TaskSummaryWidget 
                :sub-agents="activeSubAgents"
                v-model:expanded="taskWidgetExpanded"
            />

            <!-- Proposal Carousel -->
            <div class="carousel-section">
                <div class="section-title">Proposals</div>
                <div class="carousel-track" ref="carouselTrackRef" @wheel="handleWheel">
                    <div class="proposal-card" v-for="p in proposals" :key="p.id">
                        <!-- 1. Visual Thumbnail -->
                        <div class="card-thumbnail" :style="{ background: p.thumbnailPattern }">
                            <div class="thumbnail-overlay">
                                <span class="preview-badge">Preview {{ p.id }}</span>
                            </div>
                        </div>
                        
                        <!-- 2. Identity & Strategy -->
                        <div class="card-content">
                            <div class="card-header-row">
                                <div class="title">{{ p.name }}</div>
                                <div class="tags">
                                    <span v-for="tag in p.tags" :key="tag" class="tag">{{ tag }}</span>
                                </div>
                            </div>
                            
                            <!-- 3. Key Metrics -->
                            <div class="metrics-row">
                                <div class="metric">
                                    <span class="label">Storage</span>
                                    <span class="value">{{ p.metrics.storage }}</span>
                                </div>
                                <div class="metric">
                                    <span class="label">Flow</span>
                                    <span class="value">{{ p.metrics.flow }}</span>
                                </div>
                            </div>
                            
                            <!-- 4. AI Insight -->
                            <div class="insight-row">
                                <span class="icon">✨</span>
                                <span class="text">{{ p.insight }}</span>
                            </div>
                        </div>

                        <!-- Hover Actions -->
                        <div class="hover-actions">
                            <button class="action-btn">Refine</button>
                            <button class="action-btn primary">Apply</button>
                        </div>
                    </div>
                </div>
            </div>

            <!-- Alert Card (Mock) -->
            <div class="card alert-card">
                <div class="alert-header">
                    <span class="icon">⚠️</span>
                    <span>Conflict Detected</span>
                </div>
                <div class="alert-body">
                    Wall move caused collision in Master Bedroom.
                </div>
                <div class="alert-actions">
                    <button class="action-btn small">Auto-Fix</button>
                    <button class="action-btn small outline">Undo</button>
                </div>
            </div>
        </div>

      </div>

      <!-- Layer 3: Command Footer -->
      <div class="layer-footer" v-if="mode === 'chat'">
        
        <!-- Context Bar (Replaces Selection Status Bar) -->
        <div class="context-bar">
            <!-- 1. Scope Chip (Always visible, defaults to Global/Room) -->
            <div class="context-chip scope">
                <span class="chip-icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
                        <polyline points="9 22 9 12 15 12 15 22"></polyline>
                    </svg>
                </span>
                <span class="chip-text">{{ activeScope }}</span>
            </div>

            <!-- 2. Selection Chip (Visible only when items selected) -->
            <transition name="chip-fade">
                <div class="context-chip selection" v-if="selectedModuleCount > 0">
                    <span class="chip-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                        </svg>
                    </span>
                    <span class="chip-text">Selected ({{ selectedModuleCount }})</span>
                    <button class="chip-close" @click.stop="clearSelection">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="18" y1="6" x2="6" y2="18"></line>
                            <line x1="6" y1="6" x2="18" y2="18"></line>
                        </svg>
                    </button>
                </div>
            </transition>

            <!-- 3. Add Context Button -->
            <div class="add-context-wrapper">
                <button 
                    class="add-context-btn" 
                    title="Add Context"
                    @click.stop="toggleContextMenu"
                    :class="{ active: isContextMenuOpen }"
                >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <line x1="12" y1="5" x2="12" y2="19"></line>
                        <line x1="5" y1="12" x2="19" y2="12"></line>
                    </svg>
                </button>

                <!-- Context Menu -->
                <transition name="scale-up">
                    <div class="context-menu" v-if="isContextMenuOpen">
                        
                        <!-- Main Menu -->
                        <div class="menu-section">
                            <div class="menu-header">Add Context</div>
                            
                            <!-- Zones Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('zones', $event)"
                                :class="{ active: activeSubmenu === 'zones' }"
                            >
                                <span class="item-text">Reference Zone</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>

                            <!-- Regulations Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('regulations', $event)"
                                :class="{ active: activeSubmenu === 'regulations' }"
                            >
                                <span class="item-text">Regulations</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>

                            <!-- Attachments Item (Expandable) -->
                            <div 
                                class="menu-item has-submenu"
                                @mouseenter="openSubmenu('attachments', $event)"
                                :class="{ active: activeSubmenu === 'attachments' }"
                            >
                                <span class="item-text">Attachments</span>
                                <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <polyline points="9 18 15 12 9 6"></polyline>
                                </svg>
                            </div>
                        </div>

                        <!-- Submenus (Flyout) -->
                        <div 
                            class="submenu-container" 
                            v-if="activeSubmenu"
                            :class="submenuDirection"
                        >
                            
                            <!-- Zones Submenu -->
                            <div class="submenu" v-if="activeSubmenu === 'zones'">
                                <div class="menu-header">Select Zone</div>
                                <div 
                                    class="menu-item" 
                                    v-for="zone in contextOptions.zones" 
                                    :key="zone.id"
                                    @click="handleContextSelect('zones', zone)"
                                >
                                    <span class="item-text">{{ zone.label }}</span>
                                </div>
                            </div>

                            <!-- Regulations Submenu -->
                            <div class="submenu" v-if="activeSubmenu === 'regulations'">
                                <div class="menu-header">Apply Regulation</div>
                                <div 
                                    class="menu-item" 
                                    v-for="reg in contextOptions.regulations" 
                                    :key="reg.id"
                                    @click="handleContextSelect('regulations', reg)"
                                >
                                    <span class="item-text">{{ reg.label }}</span>
                                </div>
                            </div>

                             <!-- Attachments Submenu -->
                             <div class="submenu" v-if="activeSubmenu === 'attachments'">
                                <div class="menu-header">Attach File</div>
                                <div 
                                    class="menu-item" 
                                    v-for="att in contextOptions.attachments" 
                                    :key="att.id"
                                    @click="handleContextSelect('attachments', att)"
                                >
                                    <span class="item-text">{{ att.label }}</span>
                                </div>
                            </div>

                        </div>

                    </div>
                </transition>
            </div>
        </div>

        <!-- Polling Background Status Indicator -->
        <transition name="slide-down">
          <div v-if="isPollingBackground" class="polling-indicator">
            <span class="polling-dot"></span>
            <span class="polling-text">正在等待后台任务...</span>
          </div>
        </transition>

        <!-- Antigravity Input Box -->
        <div class="antigravity-input-box">
            <textarea
              ref="textareaRef"
              v-model="inputMessage"
              placeholder="你好"
              @keydown="handleKeydown"
              @input="adjustTextareaHeight"
              :disabled="isLoading || agentStatus !== 'connected'"
              rows="1"
            ></textarea>
            
            <div class="input-footer">
                <div class="left-controls">
                    <!-- Attachment Button (Paperclip) -->
                     <div class="add-context-wrapper">
                        <button 
                            class="icon-btn" 
                            title="Add Attachment"
                            @click.stop="toggleAttachmentMenu"
                            :class="{ active: isAttachmentMenuOpen }"
                        >
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
                            </svg>
                        </button>

                        <!-- Attachment Menu -->
                        <transition name="scale-up">
                            <div class="context-menu" v-if="isAttachmentMenuOpen">
                                <div class="menu-header">Attachments</div>
                                <!-- Screenshot Options -->
                                <div
                                    class="menu-item"
                                    @click="handleContextSelect('attachments', { id: 'screenshot-select' })"
                                >
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path>
                                            <rect x="6" y="10" width="12" height="8" rx="1"></rect>
                                        </svg>
                                    </span>
                                    <span class="item-text">框选截图</span>
                                </div>
                                <div
                                    class="menu-item"
                                    @click="handleContextSelect('attachments', { id: 'screenshot-canvas' })"
                                >
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path>
                                            <circle cx="12" cy="13" r="4"></circle>
                                        </svg>
                                    </span>
                                    <span class="item-text">截取画布</span>
                                </div>
                                <div class="menu-divider"></div>
                                <!-- Upload Options -->
                                <div class="menu-item">
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                                            <circle cx="8.5" cy="8.5" r="1.5"></circle>
                                            <polyline points="21 15 16 10 5 21"></polyline>
                                        </svg>
                                    </span>
                                    <span class="item-text">上传图片</span>
                                </div>
                                <div class="menu-item">
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path>
                                            <polyline points="14 2 14 8 20 8"></polyline>
                                            <line x1="16" y1="13" x2="8" y2="13"></line>
                                            <line x1="16" y1="17" x2="8" y2="17"></line>
                                            <polyline points="10 9 9 9 8 9"></polyline>
                                        </svg>
                                    </span>
                                    <span class="item-text">上传文件</span>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Model Pill -->
                    <div class="control-pill-wrapper model" :class="{ open: isModelMenuOpen }">
                        <button class="control-pill" @click="isModelMenuOpen = !isModelMenuOpen">
                            <span class="prefix-icon">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M12 2L2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5"/>
                                </svg>
                            </span>
                            <span class="text">{{ currentModel?.label || 'Select Model' }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu model-menu" v-if="isModelMenuOpen">
                                <div class="menu-header">Model</div>
                                <div
                                    v-for="m in models"
                                    :key="m.id"
                                    class="menu-item"
                                    :class="{ active: currentModel?.id === m.id }"
                                    @click="selectModel(m)"
                                >
                                    <span class="item-text">{{ m.label }}</span>
                                    <svg v-if="currentModel?.id === m.id" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                                <!-- 分隔线 -->
                                <div class="menu-divider"></div>
                                <!-- 添加模型按钮或输入框 -->
                                <div v-if="!isAddingModel" class="menu-item add-model" @click.stop="startAddModel">
                                    <svg class="add-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <line x1="12" y1="5" x2="12" y2="19"></line>
                                        <line x1="5" y1="12" x2="19" y2="12"></line>
                                    </svg>
                                    <span class="item-text">Add Model...</span>
                                </div>
                                <div v-else class="add-model-input" @click.stop>
                                    <input
                                        ref="newModelInputRef"
                                        v-model="newModelId"
                                        type="text"
                                        placeholder="Model ID"
                                        @keyup.enter="confirmAddModel"
                                        @keyup.escape="cancelAddModel"
                                    />
                                    <div class="input-actions">
                                        <button class="confirm-btn" @click="confirmAddModel" :disabled="!newModelId.trim()">
                                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                                <polyline points="20 6 9 17 4 12"></polyline>
                                            </svg>
                                        </button>
                                        <button class="cancel-btn" @click="cancelAddModel">
                                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                                <line x1="18" y1="6" x2="6" y2="18"></line>
                                                <line x1="6" y1="6" x2="18" y2="18"></line>
                                            </svg>
                                        </button>
                                    </div>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Thinking Pill -->
                     <div class="control-pill-wrapper thinking" :class="{ open: isThinkingMenuOpen }">
                        <button class="control-pill" @click="isThinkingMenuOpen = !isThinkingMenuOpen">
                            <span class="text">{{ currentThinking.label }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isThinkingMenuOpen">
                                <div class="menu-header">Thinking Intensity</div>
                                <div
                                    v-for="t in thinkingLevels"
                                    :key="t.id"
                                    class="menu-item"
                                    :class="{ active: currentThinking.id === t.id }"
                                    @click="selectThinking(t)"
                                >
                                    <span class="item-text">{{ t.label }}</span>
                                    <svg v-if="currentThinking.id === t.id" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                            </div>
                        </transition>
                    </div>
                </div>

                <div class="right-controls">
                    <button
                      class="send-btn-round"
                      @click="sendMessage"
                      :disabled="isLoading || !inputMessage.trim() || agentStatus !== 'connected'"
                    >
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round">
                            <line x1="12" y1="19" x2="12" y2="5"></line>
                            <polyline points="5 12 12 5 19 12"></polyline>
                        </svg>
                    </button>
                </div>
            </div>
        </div>
      </div>

    </div>

    <!-- Branch Checkout Confirm Dialog -->
    <BranchCheckoutConfirmDialog
      :visible="showCheckoutConfirmDialog"
      :target-branch="pendingCheckoutBranch"
      :current-branch="currentBranch"
      @confirm="handleCheckoutConfirm"
      @cancel="handleCheckoutCancel"
    />

    <!-- Screenshot Overlay for select area screenshot -->
    <Teleport to="body">
      <ScreenshotOverlay
        v-if="showScreenshotOverlay"
        @capture="handleScreenshotCapture"
        @cancel="handleScreenshotCancel"
      />
    </Teleport>
  </aside>
</template>

<style scoped lang="scss">
.ai-command-center {
  /* Layout & Positioning */
  height: 100%;
  margin-left: auto;
  margin-right: 0;
  
  /* Aurora Glass Effect */
  background: var(--glass-bg);
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  border-right: none;
  border-radius: 24px 0 0 24px;
  
  /* Glare & Shadow */
  background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
  box-shadow: 
    -12px 0 40px rgba(0, 0, 0, 0.4), /* Deep drop shadow to the left */
    0 0 0 1px rgba(255, 255, 255, 0.1) inset, /* Inner rim */
    0 0 20px rgba(255, 255, 255, 0.15); /* Outer glow */
  
  /* Animation */
  /* transition: width 0.1s;  Removed for smoother dragging */
  /* overflow: hidden;  Removed to allow context menu flyout */
  z-index: 90;
  display: flex;
  flex-direction: row; /* Changed to row to include resize handle */
  position: relative;
}

/* --- Resize Handle --- */
.resize-handle {
    width: 12px;
    height: 100%;
    cursor: ew-resize;
    display: flex;
    align-items: center;
    justify-content: center;
    position: absolute;
    left: 0;
    top: 0;
    z-index: 100;
    
    &:hover .handle-bar {
        background: var(--accent-primary);
        opacity: 0.8;
    }
}

.handle-bar {
    width: 4px;
    height: 48px;
    background: var(--text-tertiary);
    border-radius: 2px;
    opacity: 0.2;
    transition: all 0.2s;
}

/* --- Main Content --- */
.main-content {
    flex: 1;
    display: flex;
    flex-direction: column;
    height: 100%;
    padding-left: 12px; /* Space for resize handle */
    min-width: 0; /* Allow flex shrinking */
}

/* --- Layer 1: Context Header --- */
/* --- Layer 1: Context Header --- */
.layer-context {
    padding: 12px 16px; /* Compact padding */
    border-bottom: 1px solid var(--border-dim);

    .context-row {
        display: flex;
        align-items: center;
        width: 100%;
        margin-bottom: 8px; /* Reduced margin */
    }

    /* Branch Dropdown Redesign */
    .branch-dropdown {
        position: relative;
        width: 100%;
        z-index: 10;

        .dropdown-trigger {
            box-sizing: border-box; /* Ensure padding doesn't affect width */
            width: 100%;
            display: flex;
            align-items: center;
            gap: 6px; /* Reduced gap */
            padding: 6px 10px; /* Compact padding */
            background: var(--surface-dim);
            border: 1px solid var(--border-dim);
            border-radius: 8px;
            color: var(--text-primary);
            font-size: 0.8rem; /* Smaller font */
            cursor: pointer;
            transition: all 0.2s ease;

            &:hover {
                background: var(--surface-highlight);
                border-color: var(--border-subtle);
            }

            .icon { font-size: 1rem; }
            .text { 
                flex: 1; 
                text-align: left; 
                font-weight: 500;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .chevron {
                width: 16px;
                height: 16px;
                color: var(--text-tertiary);
                transition: transform 0.2s;
            }
        }

        &.open .dropdown-trigger {
            background: var(--surface-elevated); /* Match dropdown menu bg */
            border-color: var(--border-subtle); /* Match dropdown menu border */
            border-bottom-left-radius: 0; /* Connect to menu */
            border-bottom-right-radius: 0;
            .chevron { transform: rotate(180deg); }
        }

        .dropdown-menu {
            box-sizing: border-box; /* Ensure padding doesn't affect width */
            position: absolute;
            top: 100%; /* Connect directly */
            left: 0;
            width: 100%; /* Consistent width */
            background: var(--surface-elevated);
            border: 1px solid var(--border-subtle);
            border-top: none; /* Remove top border to merge */
            border-radius: 0 0 12px 12px; /* Only round bottom */
            box-shadow: 0 8px 32px rgba(0,0,0,0.2);
            padding: 6px; /* Compact padding */
            backdrop-filter: blur(16px);
            animation: slideDown 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);

            .branch-tree {
                display: flex;
                flex-direction: column;
                gap: 4px;
            }

            .branch-item {
                display: flex;
                flex-direction: column;
                gap: 0; /* No gap for compactness */
                padding: 6px 8px; /* Even more compact padding */
                border-radius: 8px;
                cursor: pointer;
                border: 1px solid transparent;
                transition: all 0.2s;

                &:hover {
                    background: var(--surface-highlight);
                    border-color: var(--border-subtle);
                }

                &.current {
                    background: rgba(var(--accent-primary-rgb), 0.05); /* Very subtle tint */
                    border-color: transparent; /* Remove border to avoid boxy look */
                    position: relative;
                    
                    &::before {
                        content: '';
                        position: absolute;
                        left: 0;
                        top: 6px;
                        bottom: 6px;
                        width: 3px;
                        background: var(--accent-primary);
                        border-radius: 0 2px 2px 0;
                    }
                    
                    .branch-main .branch-name {
                        color: var(--accent-primary);
                        font-weight: 600;
                    }
                }

                .branch-main {
                    display: flex;
                    align-items: center;
                    gap: 8px;
                    
                    .branch-icon { font-size: 0.9rem; opacity: 0.7; }
                    
                    .branch-name {
                        flex: 1;
                        font-size: 0.85rem; /* Slightly smaller */
                        color: var(--text-primary);
                        font-weight: 500;
                    }

                    .current-indicator {
                        color: var(--accent-primary);
                        display: flex;
                        align-items: center;
                    }
                }

                .branch-meta {
                    display: flex;
                    justify-content: space-between;
                    align-items: center;
                    font-size: 0.7rem; /* Smaller meta text */
                    color: var(--text-tertiary);
                    padding-left: 22px; /* Align with text */

                    .commit-msg {
                        flex: 1;
                        white-space: nowrap;
                        overflow: hidden;
                        text-overflow: ellipsis;
                        margin-right: 12px;
                        max-width: 180px;
                    }

                    .commit-time {
                        white-space: nowrap;
                        font-feature-settings: "tnum";
                    }
                }
            }
        }
    }

    @keyframes slideDown {
        from { opacity: 0; transform: translateY(-8px); }
        to { opacity: 1; transform: translateY(0); }
    }

    .mode-switch {
        display: flex;
        background: var(--btn-ghost-bg-hover); /* Adaptive background */
        border-radius: 8px; /* Smaller radius */
        padding: 3px; /* Compact padding */
        border: 1px solid var(--border-subtle); /* Standardized border */
        
        button {
            flex: 1;
            border: 1px solid transparent;
            background: none;
            color: var(--text-secondary);
            font-size: 0.8rem; /* Smaller font */
            font-weight: 500;
            padding: 4px 10px; /* Compact padding */
            border-radius: 6px; /* Smaller radius */
            cursor: pointer;
            transition: all 0.2s ease;
            white-space: nowrap;
            
            &:hover:not(.active) {
                color: var(--text-primary);
                background: var(--btn-ghost-bg-active);
            }

            &.active {
                background: var(--surface-elevated); /* White in light, Dark Grey in dark */
                color: var(--text-primary);
                border-color: var(--border-subtle);
                box-shadow: 
                    0 2px 8px rgba(0, 0, 0, 0.12), /* Softer shadow */
                    0 0 0 1px var(--border-subtle) inset;
                /* Removed text-shadow for cleaner look in light mode */
            }
        }
    }
}

/* --- Layer 2: Intelligence Stream --- */
.layer-stream {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    position: relative;
    min-height: 0; /* 允许 flex 子元素收缩 */
}

/* 共享滚动容器样式 */
.view-chat, .view-tasks {
    flex: 1;
    overflow-y: auto;
    overflow-x: hidden;
    
    /* Scrollbar styling */
    &::-webkit-scrollbar {
        width: 6px;
    }
    &::-webkit-scrollbar-track {
        background: transparent;
    }
    &::-webkit-scrollbar-thumb {
        background: rgba(255, 255, 255, 0.1);
        border-radius: 3px;
    }
    &:hover::-webkit-scrollbar-thumb {
        background: rgba(255, 255, 255, 0.2);
    }
}

/* --- Context Menu Styles --- */
.add-context-wrapper {
    position: relative;
}

/* --- Unified Menu Styles (Antigravity) --- */
.context-menu, .pill-menu, .submenu-container {
    position: absolute;
    bottom: 100%;
    left: 0;
    right: auto;
    margin-bottom: 6px;
    background: rgba(20, 20, 20, 0.95); /* Deep dark background */
    backdrop-filter: blur(0); /* No blur needed for opaque dark */
    border: 1px solid rgba(255, 255, 255, 0.08);
    border-radius: 8px; /* Sharper radius */
    padding: 4px;
    box-shadow: 0 12px 40px rgba(0,0,0,0.6);
    z-index: 1000;
    min-width: 160px;
    display: flex;
    flex-direction: column;
    gap: 1px;
    transform-origin: bottom left;
    animation: scale-up 0.1s cubic-bezier(0.2, 0, 0.13, 1.5);
}

.submenu-container {
    bottom: 0;
    left: 100%; /* Flyout to the right */
    margin-bottom: 0;
    margin-left: 8px;
}

.submenu-container.left {
    left: auto;
    right: 100%;
    margin-left: 0;
    margin-right: 8px;
}

.menu-header {
    padding: 6px 8px 4px;
    font-size: 10px; /* Micro label */
    color: rgba(255, 255, 255, 0.3);
    font-weight: 600;
    letter-spacing: 0.5px;
    text-transform: uppercase;
}

.menu-item {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 4px 8px;
    border-radius: 4px;
    font-size: 11px; /* Micro font */
    color: rgba(255, 255, 255, 0.7);
    cursor: pointer;
    transition: all 0.1s;
    min-height: 24px;
    position: relative;

    &:hover, &.active {
        background: rgba(255, 255, 255, 0.08);
        color: white;
    }
    
    .icon, .chevron {
        color: rgba(255, 255, 255, 0.4);
        width: 14px;
        height: 14px;
        display: flex;
        align-items: center;
        justify-content: center;
        svg { width: 100%; height: 100%; }
    }

    .check-icon {
        margin-left: auto;
        width: 12px;
        height: 12px;
        color: #007AFF;
    }

    .item-text {
        flex: 1;
    }
}

/* --- Add Model Styles --- */
.menu-divider {
    height: 1px;
    background: rgba(255, 255, 255, 0.1);
    margin: 4px 0;
}

.menu-item.add-model {
    color: rgba(255, 255, 255, 0.5);

    .add-icon {
        width: 14px;
        height: 14px;
        color: rgba(255, 255, 255, 0.4);
    }

    &:hover {
        color: #007AFF;
        .add-icon {
            color: #007AFF;
        }
    }
}

.add-model-input {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 4px 8px;

    input {
        flex: 1;
        background: rgba(255, 255, 255, 0.1);
        border: 1px solid rgba(255, 255, 255, 0.2);
        border-radius: 4px;
        padding: 4px 8px;
        font-size: 11px;
        color: white;
        outline: none;

        &::placeholder {
            color: rgba(255, 255, 255, 0.4);
        }

        &:focus {
            border-color: #007AFF;
            background: rgba(0, 122, 255, 0.1);
        }
    }

    .input-actions {
        display: flex;
        gap: 4px;

        button {
            width: 22px;
            height: 22px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.1s;

            svg {
                width: 12px;
                height: 12px;
            }
        }

        .confirm-btn {
            background: #007AFF;
            color: white;

            &:hover:not(:disabled) {
                background: #0066DD;
            }

            &:disabled {
                opacity: 0.4;
                cursor: not-allowed;
            }
        }

        .cancel-btn {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.6);

            &:hover {
                background: rgba(255, 255, 255, 0.15);
                color: white;
            }
        }
    }
}

/* 扩展模型菜单宽度以适应输入框 */
.pill-menu.model-menu {
    min-width: 200px;
}

.submenu-container.left {
    right: 100%; /* Fly out to the left */
    left: auto;
    margin-right: 8px;
    animation: slideLeft 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.submenu-container.right {
    left: 100%; /* Fly out to the right */
    right: auto;
    margin-left: 8px;
    animation: slideRight 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.submenu {
    display: flex;
    flex-direction: column;
    gap: 2px;
}

@keyframes slideLeft {
    from { opacity: 0; transform: translateX(10px); }
    to { opacity: 1; transform: translateX(0); }
}

@keyframes slideRight {
    from { opacity: 0; transform: translateX(-10px); }
    to { opacity: 1; transform: translateX(0); }
}

/* Transition for main menu */
.scale-up-enter-active,
.scale-up-leave-active {
    transition: all 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
}

.scale-up-enter-from,
.scale-up-leave-to {
    opacity: 0;
    transform: scale(0.95) translateY(10px);
}
.layer-stream {
    flex: 1;
    overflow-y: auto;
    scrollbar-gutter: stable; /* 预留滚动条空间，避免布局抖动 */
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;

    /* Scrollbar styling */
    &::-webkit-scrollbar { width: 4px; }
    &::-webkit-scrollbar-thumb { background: var(--border-dim); border-radius: 2px; }
}

.view-tasks {
    display: flex;
    flex-direction: column;
    gap: 16px;
}

.view-chat {
    display: flex;
    flex-direction: column;
    gap: 16px;

    .chat-bottom-anchor {
        height: 1px;
        width: 100%;
        flex-shrink: 0;
    }

    .chat-message {
        display: flex;
        gap: 8px;
        align-items: flex-start;

        &.user {
            flex-direction: row-reverse;
            .bubble {
                background: var(--accent-primary);
                color: white;
                border: none;
                white-space: pre-wrap; // 用户消息保留换行
            }
        }

        &.ai {
            .bubble {
                background: var(--surface-card);
                border: 1px solid var(--border-dim);
                color: var(--text-primary);
            }
        }



        .message-wrapper {
            display: flex;
            flex-direction: column;
            gap: 8px;
            max-width: 92%;
        }

        .bubble {
            padding: 6px 10px; /* Reduced padding */
            border-radius: 12px;
            font-size: 0.8rem; /* Reduced from 0.85rem */
            line-height: 1.35; /* Tighter line height */
            word-wrap: break-word; /* Enable word wrapping */
            word-break: break-word; /* Break long words */
            overflow-wrap: break-word; /* Fallback for older browsers */
            /* Note: white-space: pre-wrap removed - causes bottom padding from trailing newlines */

            &.empty {
                min-height: 20px;
            }

            // === 覆盖 markstream-vue 库的默认样式（极致紧凑） ===

            // 顶层容器：清除所有默认间距
            :deep(.markdown-renderer) {
                margin: 0;
                padding: 0;
            }
            :deep(.node-slot),
            :deep(.node-content),
            :deep(.node-space) {
                margin: 0;
                padding: 0;
            }

            // 段落：适度间距
            :deep(p) {
                margin: 0;
            }
            :deep(.paragraph-node) {
                margin: 0.3em 0;
            }

            // 标题：舒适层次感
            :deep(.heading-1) {
                font-size: 1.05rem;
                margin: 0.7em 0 0.35em 0;
            }
            :deep(.heading-2) {
                font-size: 0.95rem;
                margin: 0.6em 0 0.3em 0;
            }
            :deep(.heading-3) {
                font-size: 0.88rem;
                margin: 0.5em 0 0.25em 0;
            }
            :deep(.heading-4),
            :deep(.heading-5),
            :deep(.heading-6) {
                font-size: 0.82rem;
                margin: 0.45em 0 0.2em 0;
            }

            // 列表：适度间距
            :deep(.list-node) {
                margin: 0.25em 0;
                padding-left: 1.1em;
            }
            :deep(.list-item) {
                margin: 0;
                padding: 0;
            }

            // 表格容器：舒适间距
            :deep(.table-node-wrapper) {
                margin: 0.5em 0;
            }

            // 表格：边框可见 + 舒适单元格
            :deep(.table-node) {
                --table-border: rgba(255, 255, 255, 0.25);
                margin: 0;
            }
            :deep(.table-node th),
            :deep(.table-node td) {
                border: 1px solid rgba(255, 255, 255, 0.2);
                padding: 0.3em 0.5em;
            }
            :deep(.table-node th) {
                background: rgba(255, 255, 255, 0.05);
            }

            // 引用块：适度间距
            :deep(.blockquote) {
                margin: 0.3em 0;
                padding-left: 0.6em;
            }

            // 代码块：适度间距
            :deep(.code-block-container) {
                margin: 0.3em 0;
            }

            // 分割线：适度间距
            :deep(.hr-node) {
                margin: 0.5em 0;
            }
        }

        @keyframes dot-fade {
            0% { opacity: 0; }
            50% { opacity: 1; }
            100% { opacity: 0; }
        }

        @keyframes spin {
            to { transform: rotate(360deg); }
        }

        @keyframes text-breathe {
            0%, 100% { opacity: 0.4; }
            50% { opacity: 1; }
        }

        /* Thinking Section Styles */
        .thinking-section {
            margin-bottom: 4px;

            .thinking-header {
                display: flex;
                align-items: center;
                gap: 6px;
                padding: 4px 0;
                cursor: pointer;
                user-select: none;
                opacity: 0.7;
                transition: opacity 0.2s;

                &:hover {
                    opacity: 1;
                }

                .thinking-label {
                    font-size: 0.8rem;
                    color: var(--text-tertiary);
                    font-style: italic;
                    /* font-family: 'SF Mono', 'Roboto Mono', monospace; Removed to match Generating... */
                    
                    .dot {
                        animation: dot-fade 1.5s infinite;
                        opacity: 0;
                    }
                    .dot:nth-child(1) { animation-delay: 0.0s; }
                    .dot:nth-child(2) { animation-delay: 0.5s; }
                    .dot:nth-child(3) { animation-delay: 1.0s; }
                }

                .thinking-chevron {
                    width: 14px;
                    height: 14px;
                    color: var(--text-tertiary);
                    transition: transform 0.2s;

                    &.expanded {
                        transform: rotate(90deg);
                    }
                }
            }

            .thinking-content {
                padding: 6px 10px; /* Reduced padding */
                color: var(--text-secondary);
                line-height: 1.4; /* Tighter line height */
                font-size: 0.8rem;
                border-left: 2px solid var(--border-dim);
                margin-left: 6px; /* Align with chevron center approx */
                margin-top: 2px;
                margin-bottom: 6px;
                /* white-space: pre-wrap;  Removed because MarkdownText handles it */
                background: rgba(0, 0, 0, 0.2); /* Very subtle background */
                border-radius: 0 8px 8px 0;
            }
        }

        /* SubAgents Section */
        .subagents-section {
            margin: 8px 0;
        }

        /* Universal vacuum period waiting indicator */
        .vacuum-generating {
            font-size: 0.8rem;
            color: var(--text-tertiary);
            font-style: italic;
            padding: 4px 0;
            margin-top: 4px;
            
            .generating-text .char {
                animation: text-breathe 1.5s ease-in-out infinite;
            }

            .dot {
                animation: dot-fade 1.5s infinite;
                opacity: 0;
            }
            .dot:nth-child(1) { animation-delay: 0.0s; }
            .dot:nth-child(2) { animation-delay: 0.5s; }
            .dot:nth-child(3) { animation-delay: 1.0s; }
        }
    }
}

/* Thinking expand transition */
.thinking-expand-enter-active,
.thinking-expand-leave-active {
    transition: all 0.2s ease;
    max-height: 200px;
    overflow: hidden;
}

.thinking-expand-enter-from,
.thinking-expand-leave-to {
    opacity: 0;
    max-height: 0;
}


.card {
    background: var(--surface-card);
    border: 1px solid var(--border-dim);
    border-radius: 12px;
    padding: 12px;
    
    &.task-card {
        .card-header {
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 0.85rem;
            color: var(--text-primary);
            margin-bottom: 8px;
            
            .spinner {
                width: 12px;
                height: 12px;
                border: 2px solid var(--accent-primary);
                border-top-color: transparent;
                border-radius: 50%;
                animation: spin 1s linear infinite;
            }
        }
        .progress-track {
            height: 4px;
            background: var(--border-dim);
            border-radius: 2px;
            overflow: hidden;
            margin-bottom: 8px;
            .progress-fill {
                height: 100%;
                background: var(--accent-primary);
                border-radius: 2px;
                transition: width 0.3s ease;
            }
        }
        .status {
            font-size: 0.75rem;
            color: var(--text-tertiary);
        }
        .card-actions {
            display: flex;
            justify-content: flex-end;
            gap: 8px;
            .text-btn {
                background: none;
                border: none;
                color: var(--text-secondary);
                font-size: 0.7rem;
                cursor: pointer;
                &:hover { color: var(--text-primary); }
            }
        }
    }

    &.alert-card {
        border-color: rgba(255, 165, 0, 0.3);
        background: rgba(255, 165, 0, 0.05);
        
        .alert-header {
            color: #ffb74d;
            font-weight: 600;
            font-size: 0.85rem;
            margin-bottom: 6px;
            display: flex;
            gap: 6px;
        }
        .alert-body {
            font-size: 0.8rem;
            color: var(--text-secondary);
            margin-bottom: 10px;
            line-height: 1.4;
        }
        .alert-actions {
            display: flex;
            gap: 8px;
            .action-btn {
                padding: 4px 8px;
                font-size: 0.75rem;
            }
        }
    }
}

.carousel-section {
    .section-title {
        font-size: 0.75rem;
        text-transform: uppercase;
        letter-spacing: 1px;
        color: var(--text-tertiary);
        margin-bottom: 8px;
    }
    .carousel-track {
        display: flex;
        gap: 10px;
        overflow-x: auto;
        padding-top: 6px;  /* Space for hover translateY(-4px) effect */
        padding-bottom: 4px;
        
        &::-webkit-scrollbar { height: 6px; }
        &::-webkit-scrollbar-thumb { background: var(--border-dim); border-radius: 3px; }
        &:hover::-webkit-scrollbar-thumb { background: var(--text-tertiary); }
    }
}

.proposal-card {
    min-width: 200px; /* Wider card */
    width: 200px;
    background: var(--surface-card);
    border-radius: 12px;
    overflow: hidden;
    border: 1px solid var(--border-dim);
    cursor: pointer;
    transition: all 0.2s;
    position: relative;
    display: flex;
    flex-direction: column;

    &:hover {
        transform: translateY(-4px);
        box-shadow: 0 8px 20px rgba(0,0,0,0.15);
        .hover-actions { opacity: 1; }
    }

    /* 1. Visual Thumbnail */
    .card-thumbnail {
        height: 110px; /* Dominant visual area */
        position: relative;
        
        .thumbnail-overlay {
            position: absolute;
            top: 8px;
            left: 8px;
            
            .preview-badge {
                background: rgba(0,0,0,0.4);
                backdrop-filter: blur(4px);
                color: white;
                font-size: 0.6rem;
                padding: 2px 6px;
                border-radius: 4px;
                font-weight: 500;
            }
        }
    }

    /* 2. Content Area */
    .card-content {
        padding: 10px;
        display: flex;
        flex-direction: column;
        gap: 8px;
        background: var(--surface-elevated);

        .card-header-row {
            .title {
                font-size: 0.85rem;
                font-weight: 600;
                color: var(--text-primary);
                margin-bottom: 4px;
            }
            .tags {
                display: flex;
                gap: 4px;
                flex-wrap: wrap;
                
                .tag {
                    font-size: 0.65rem;
                    padding: 1px 5px;
                    border-radius: 3px;
                    background: var(--surface-highlight);
                    color: var(--text-secondary);
                    border: 1px solid var(--border-subtle);
                }
            }
        }

        /* 3. Metrics */
        .metrics-row {
            display: flex;
            justify-content: space-between;
            padding: 6px 0;
            border-top: 1px solid var(--border-subtle);
            border-bottom: 1px solid var(--border-subtle);
            
            .metric {
                display: flex;
                flex-direction: column;
                gap: 1px;
                
                .label { font-size: 0.6rem; color: var(--text-tertiary); text-transform: uppercase; }
                .value { font-size: 0.75rem; font-weight: 500; color: var(--text-primary); }
            }
        }

        /* 4. AI Insight */
        .insight-row {
            display: flex;
            gap: 4px;
            align-items: flex-start;
            
            .icon { font-size: 0.7rem; margin-top: 1px; }
            .text {
                font-size: 0.65rem;
                color: var(--text-secondary);
                line-height: 1.3;
                display: -webkit-box;
                -webkit-line-clamp: 2;
                -webkit-box-orient: vertical;
                overflow: hidden;
            }
        }
    }

    .hover-actions {
        position: absolute;
        inset: 0;
        background: rgba(0,0,0,0.3); /* Lighter overlay to see content */
        backdrop-filter: blur(2px);
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 8px;
        opacity: 0;
        transition: opacity 0.2s;
        
        .action-btn {
            font-size: 0.75rem;
            padding: 6px 16px;
            border-radius: 16px;
            border: 1px solid rgba(255,255,255,0.3);
            background: rgba(0,0,0,0.6);
            color: white;
            cursor: pointer;
            font-weight: 500;
            width: 80%;
            
            &.primary {
                background: var(--accent-primary);
                border-color: var(--accent-primary);
                box-shadow: 0 4px 12px rgba(0,0,0,0.2);
            }
            
            &:hover {
                transform: scale(1.05);
            }
        }
    }
}

/* --- Layer 3: Command Footer --- */
.layer-footer {
    padding: 16px 20px;
    background: transparent; /* Force transparent to let glass bg show through */
    border-top: 1px solid var(--border-dim);
}

.context-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 12px;
    flex-wrap: wrap;

    .context-chip {
        display: flex;
        align-items: center;
        gap: 6px;
        padding: 4px 8px;
        background: var(--surface-dim);
        border: 1px solid var(--border-dim);
        border-radius: 6px;
        font-size: 0.75rem;
        color: var(--text-secondary);
        transition: all 0.2s;
        cursor: default;
        user-select: none;

        .chip-icon {
            display: flex;
            align-items: center;
            color: var(--text-tertiary);
            svg { width: 14px; height: 14px; }
        }

        .chip-text {
            font-weight: 500;
        }

        &.scope {
            background: rgba(10, 132, 255, 0.1); /* Fallback blue tint */
            border-color: transparent;
            .chip-icon { color: var(--accent-blue); }
            .chip-text { color: var(--accent-blue); }

            &:hover {
                border-color: var(--accent-blue);
                background: rgba(10, 132, 255, 0.15);
            }
        }

        &.selection {
            background: var(--surface-highlight);
            border-color: var(--border-subtle);
            .chip-text { color: var(--text-primary); }
            
            .chip-close {
                display: flex;
                align-items: center;
                justify-content: center;
                background: none;
                border: none;
                padding: 2px;
                margin-left: 2px;
                cursor: pointer;
                color: var(--text-tertiary);
                border-radius: 4px;
                
                svg { width: 12px; height: 12px; }

                &:hover {
                    background: var(--surface-dim);
                    color: var(--text-primary);
                }
            }
        }

        &:hover {
            border-color: var(--border-subtle);
            box-shadow: 0 2px 4px rgba(0,0,0,0.05);
        }
    }

    .add-context-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 6px;
        border: 1px dashed var(--border-dim);
        background: transparent;
        color: var(--text-tertiary);
        cursor: pointer;
        transition: all 0.2s;

        svg { width: 14px; height: 14px; }

        &:hover {
            border-color: var(--text-secondary);
            color: var(--text-secondary);
            background: var(--surface-dim);
        }
    }
}

/* Chip Animation */
.chip-fade-enter-active,
.chip-fade-leave-active {
    transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
    max-width: 200px;
    opacity: 1;
    overflow: hidden;
    white-space: nowrap;
}

.chip-fade-enter-from,
.chip-fade-leave-to {
    opacity: 0;
    max-width: 0;
    padding-left: 0;
    padding-right: 0;
    margin-left: 0;
    margin-right: 0;
    border-width: 0;
    transform: scale(0.95);
}

/* --- Polling Indicator --- */
.polling-indicator {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 16px;
    margin: 0 4px 8px;
    background: rgba(251, 191, 36, 0.1);  /* amber/warning color */
    border-radius: 8px;
    border: 1px solid rgba(251, 191, 36, 0.3);
    color: rgba(251, 191, 36, 0.9);
    font-size: 0.8rem;

    .polling-dot {
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background: rgba(251, 191, 36, 0.9);
        animation: pulse-polling 1.5s ease-in-out infinite;
    }

    .polling-text {
        font-weight: 500;
    }
}

@keyframes pulse-polling {
    0%, 100% { opacity: 1; transform: scale(1); }
    50% { opacity: 0.4; transform: scale(1.2); }
}

.slide-down-enter-active,
.slide-down-leave-active {
    transition: all 0.3s ease;
}

.slide-down-enter-from,
.slide-down-leave-to {
    opacity: 0;
    transform: translateY(-10px);
}

/* --- Antigravity Input Box --- */
/* --- Antigravity Input Box --- */
.antigravity-input-box {
    margin: 0 4px 16px; /* Widen the box */
    background: rgba(255, 255, 255, 0.03); /* Lighter, more distinct background */
    backdrop-filter: blur(20px);
    -webkit-backdrop-filter: blur(20px);
    border-radius: 16px;
    border: none; /* Remove solid border */
    /* Optical border + Top highlight + Deep shadow */
    box-shadow: 
        inset 0 0 0 0.5px rgba(255, 255, 255, 0.1),
        inset 0 1px 0 rgba(255, 255, 255, 0.05),
        0 4px 24px rgba(0, 0, 0, 0.2);
    display: flex;
    flex-direction: column;
    position: relative;
    transition: all 0.2s ease;

    &:focus-within {
        background: rgba(255, 255, 255, 0.05); /* Keep it bright/glassy, slightly more opaque */
        box-shadow: 
            inset 0 0 0 0.5px rgba(255, 255, 255, 0.2), /* Brighter border */
            inset 0 1px 0 rgba(255, 255, 255, 0.1),
            0 8px 32px rgba(0, 0, 0, 0.4);
    }

    textarea {
        display: block; /* Ensure block layout */
        width: 100%;
        box-sizing: border-box; /* Critical for correct width with padding */
        background: transparent;
        border: none;
        color: #E0E0E0;
        font-size: 0.9rem;
        line-height: 1.5;
        padding: 12px 16px 4px; /* Top padding */
        resize: none;
        outline: none;
        font-family: inherit;
        max-height: 200px;
        overflow-y: auto !important; /* Force scroll capability */
        
        /* Custom Scrollbar */
        &::-webkit-scrollbar { 
            width: 6px; 
            background: transparent;
        }
        &::-webkit-scrollbar-track { 
            background: rgba(255, 255, 255, 0.02); /* Faint track */
            border-radius: 3px;
            margin: 8px 0;
        }
        &::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.2); /* Subtle visibility */
            border-radius: 3px; 
        }
        &:hover::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.4); /* Visible on hover */
        }

        &::placeholder {
            color: rgba(255, 255, 255, 0.3);
        }
    }

    .input-footer {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 4px 8px 4px; /* Extremely compact padding */
    }

    .left-controls {
        display: flex;
        align-items: center;
        gap: 4px; /* Compact gap */
    }

    .right-controls {
        display: flex;
        align-items: center;
    }

    /* Buttons & Pills */
    .icon-btn {
        width: 28px;
        height: 28px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: transparent;
        border: none;
        border-radius: 6px;
        color: rgba(255, 255, 255, 0.4);
        cursor: pointer;
        transition: all 0.2s;

        &:hover, &.active {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.8);
        }
        
        svg { width: 16px; height: 16px; }
    }

    .control-pill-wrapper {
        position: relative;
    }

    .control-pill {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 0 6px;
        background: transparent;
        border: 1px solid transparent;
        border-radius: 4px;
        color: rgba(255, 255, 255, 0.5);
        font-size: 11px; /* Micro typography */
        font-weight: 500;
        cursor: pointer;
        transition: all 0.2s;
        height: 24px; /* Precision scale height */
        letter-spacing: 0.3px;

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            border-color: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.9);
        }
        
        /* Active state when menu is open */
        .open & {
            background: rgba(255, 255, 255, 0.1);
            border-color: rgba(255, 255, 255, 0.15);
            color: rgba(255, 255, 255, 1);
        }

        .prefix-icon {
            display: flex;
            align-items: center;
            opacity: 0.8;
            svg { width: 12px; height: 12px; }
        }
    }

    .send-btn-round {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        background: #007AFF; /* Antigravity Blue */
        border: none;
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        cursor: pointer;
        transition: transform 0.1s, background 0.2s;
        
        svg { width: 16px; height: 16px; transform: rotate(90deg); /* Point up */ }

        &:hover {
            background: #0062cc;
        }
        &:active {
            transform: scale(0.95);
        }
        &:disabled {
            background: rgba(255, 255, 255, 0.1);
            color: rgba(255, 255, 255, 0.3);
            cursor: not-allowed;
        }
    }

    /* Menus */

}
</style>
