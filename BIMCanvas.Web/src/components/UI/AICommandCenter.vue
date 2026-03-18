<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';
import { useGitStore } from '../../stores/gitStore';
import type { SubAgent, ToolCall, ChatBubble } from '../../types/agent';
import { proposalMocks } from '../../constants/aiCommandCenter';
import { useAgentConfig } from '../../composables/aiCommandCenter/useAgentConfig';
import { useChatScroll } from '../../composables/aiCommandCenter/useChatScroll';
import { useChatStream } from '../../composables/aiCommandCenter/useChatStream';
import { useContextMenu } from '../../composables/aiCommandCenter/useContextMenu';
import { usePanelUI } from '../../composables/aiCommandCenter/usePanelUI';
import { useScreenshot } from '../../composables/aiCommandCenter/useScreenshot';
import { useQuestion } from '../../composables/aiCommandCenter/useQuestion';
import { useSelectionContext } from '../../composables/aiCommandCenter/useSelectionContext';
import { useWindowManager } from '../../composables/aiCommandCenter/useWindowManager';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';
import BranchCreationDialog from './Ribbon/BranchCreationDialog.vue';
import ThinkingBubble from './ThinkingBubble.vue';
import ToolCallBubble from './ToolCallBubble.vue';
import SubAgentBubble from './SubAgentBubble.vue';
import QuestionBubble from './QuestionBubble.vue';
import WaitingIndicator from './WaitingIndicator.vue';
import TaskSummaryWidget from './TaskSummaryWidget.vue';
import MarkdownText from './base/MarkdownText.vue';
import AdvancedScreenshotOverlay from './AdvancedScreenshotOverlay.vue';
import ImageLightbox from './ImageLightbox.vue';

// === Lightbox 状态 ===
const lightbox = ref({ visible: false, src: '' });
const openLightbox = (src: string) => {
  lightbox.value = { visible: true, src };
};
const closeLightbox = () => {
  lightbox.value.visible = false;
};

const props = defineProps<{
  panelReady?: boolean;
}>();

const AGENT_API_BASE = 'http://127.0.0.1:8865';
const SERVER_API_BASE = 'http://localhost:5000';

const { panelWidth, windowTabsRef, carouselTrackRef, startResize, handleTabsWheel, handleWheel } = usePanelUI();

const mode = ref<'chat' | 'tasks'>('chat');

const gitStore = useGitStore();
const { branches, currentBranch } = storeToRefs(gitStore);

const store = useCanvasStore();

const {
  selectedModuleCount,
  selectedCount,
  selectionDisplayText,
  scopeDisplayText,
  availableZones,
  buildContextPayload
} = useSelectionContext();
const isSelectionExpanded = ref(false);

const {
  windows,
  activeWindowId,
  activeWindow,
  showNewWindowDropdown,
  showBranchCreationDialog,
  showCheckoutConfirmDialog,
  pendingCheckoutBranch,
  isBranchDropdownOpen,
  newWindowDropdownPosition,
  branchOptionsForDialog,
  initDefaultWindow,
  addMessage,
  addMessageToWindow,
  getWindowMessage,
  handleCreateNewBranchForPrimary,
  handleBranchCreated,
  selectBranch,
  handleCheckoutConfirm,
  handleCheckoutCancel,
  isBranchOccupiedByOther,
  isBranchOccupied,
  closeWindow,
  handleNewWindowClick,
  toggleBranchDropdown,
  handleWindowTabClick,
  addWindow,
  setChatScrollRefs,
  setStreamWelcomeMessage
} = useWindowManager({
  branches,
  currentBranch,
  gitStore,
  store,
  agentApiBase: AGENT_API_BASE
});

const chatMessages = computed({
  get: () => activeWindow.value?.messages || [],
  set: (val) => { if (activeWindow.value) activeWindow.value.messages = val; }
});

const inputMessage = computed({
  get: () => activeWindow.value?.inputMessage || '',
  set: (val) => { if (activeWindow.value) activeWindow.value.inputMessage = val; }
});

const pendingImages = computed({
  get: () => activeWindow.value?.pendingImages || [],
  set: (val) => { if (activeWindow.value) activeWindow.value.pendingImages = val; }
});

const isLoading = computed({
  get: () => activeWindow.value?.isStreaming || false,
  set: (val) => { if (activeWindow.value) activeWindow.value.isStreaming = val; }
});

const shouldAutoScroll = computed({
  get: () => activeWindow.value?.shouldAutoScroll ?? true,
  set: (val) => { if (activeWindow.value) activeWindow.value.shouldAutoScroll = val; }
});

const isConfigLocked = computed(() => chatMessages.value.some(m => m.role === 'user'));

const {
  models,
  currentModel,
  currentThinking,
  currentEffort,
  thinkingLevels,
  effortLevels,
  isModelMenuOpen,
  isThinkingMenuOpen,
  isEffortMenuOpen,
  isAddingModel,
  newModelId,
  newModelInputRef,
  fetchAgentConfig,
  selectModel,
  startAddModel,
  confirmAddModel,
  cancelAddModel,
  selectThinking,
  selectEffort
} = useAgentConfig(AGENT_API_BASE, SERVER_API_BASE);

const {
  chatScrollRefs,
  chatScrollRef,
  setChatScrollRef,
  setChatBottomRef,
  handleChatScroll,
  scrollToBottom,
  handleTableWheel
} = useChatScroll({
  mode,
  windows,
  activeWindowId
});

setChatScrollRefs(chatScrollRefs);

const {
  contextOptions,
  isContextMenuOpen,
  activeSubmenu,
  submenuDirection,
  isAttachmentMenuOpen,
  toggleContextMenu: baseToggleContextMenu,
  toggleAttachmentMenu: baseToggleAttachmentMenu,
  openSubmenu,
  handleContextSelect
} = useContextMenu({
  inputMessage,
  availableZones
});

// === Image Upload ===
const imageUploadInputRef = ref<HTMLInputElement | null>(null);
const imageExtPattern = /\.(png|jpe?g|gif|webp|bmp|tiff)$/i;

const readFileAsDataUrl = (file: File) =>
  new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error || new Error('Failed to read file'));
    reader.readAsDataURL(file);
  });

const appendImageFiles = async (files: File[]) => {
  const imageFiles = files.filter(file =>
    file.type.startsWith('image/') || imageExtPattern.test(file.name)
  );
  if (imageFiles.length === 0) return;

  const images = await Promise.all(imageFiles.map(readFileAsDataUrl));
  pendingImages.value.push(...images);
};

const openImagePicker = async () => {
  isAttachmentMenuOpen.value = false;
  activeSubmenu.value = null;

  const picker = (window as unknown as {
    showOpenFilePicker?: (options?: unknown) => Promise<Array<{ getFile: () => Promise<File> }>>;
  }).showOpenFilePicker;

  if (picker) {
    try {
      const handles = await picker({
        multiple: true,
        startIn: 'desktop',
        types: [
          {
            description: 'Images',
            accept: {
              'image/*': ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.tiff']
            }
          }
        ]
      });
      const files = await Promise.all(handles.map(handle => handle.getFile()));
      await appendImageFiles(files);
      return;
    } catch (error) {
      const err = error as DOMException;
      if (err?.name !== 'AbortError') {
        console.error('[ImageUpload] File picker failed:', error);
      }
    }
  }

  imageUploadInputRef.value?.click();
};

const handleImageInputChange = async (event: Event) => {
  const input = event.target as HTMLInputElement;
  const files = input.files ? Array.from(input.files) : [];
  input.value = '';
  if (files.length === 0) return;
  await appendImageFiles(files);
};

const handleImagePaste = async (event: ClipboardEvent) => {
  const clipboard = event.clipboardData;
  if (!clipboard) return;

  const items = Array.from(clipboard.items);
  const imageFiles = items
    .filter(item => item.kind === 'file' && item.type.startsWith('image/'))
    .map(item => item.getAsFile())
    .filter((file): file is File => Boolean(file));

  if (imageFiles.length === 0) return;

  event.preventDefault();
  await appendImageFiles(imageFiles);

  const text = clipboard.getData('text/plain');
  if (text) {
    inputMessage.value += text;
    nextTick(() => adjustTextareaHeight());
  }
};

const handleAttachmentSelect = (att: { id: string; label: string }) => {
  isAttachmentMenuOpen.value = false;
  activeSubmenu.value = null;
  if (att.id === 'upload') {
    openImagePicker();
    return;
  }
  handleContextSelect('attachments', att);
};

const toggleContextMenu = () => {
  baseToggleContextMenu();
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
};

const toggleAttachmentMenu = () => {
  baseToggleAttachmentMenu();
  isModelMenuOpen.value = false;
  isThinkingMenuOpen.value = false;
};

const {
  agentStatus,
  currentProjectPath,
  isPollingBackground,
  streamWelcomeMessage,
  sendMessage,
  interruptMessage,
  checkAgentHealth,
  fetchProjectPath,
  cleanupHealthCheck
} = useChatStream({
  agentApiBase: AGENT_API_BASE,
  windows,
  activeWindowId,
  activeWindow,
  addMessage,
  addMessageToWindow,
  getWindowMessage,
  pendingImages,
  currentModel,
  currentEffort,
  currentThinking,
  scrollToBottom,
  fetchAgentConfig,
  buildContextPayload
});

const {
  showScreenshotOverlay,
  startListening,
  stopListening,
  handleScreenshotCapture,
  handleScreenshotCancel,
  removePendingImage
} = useScreenshot({
  agentApiBase: AGENT_API_BASE,
  pendingImages,
  currentProjectPath
});

const {
  startListening: startQuestionListening,
  stopListening: stopQuestionListening,
  submitAnswer,
  cancelQuestion
} = useQuestion({
  agentApiBase: AGENT_API_BASE,
  windows,
  activeWindowId,
  scrollToBottom
});

setStreamWelcomeMessage(streamWelcomeMessage);

const branchIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="6" y1="3" x2="6" y2="15"></line><circle cx="18" cy="6" r="3"></circle><circle cx="6" cy="18" r="3"></circle><path d="M18 9a9 9 0 0 1-9 9"></path></svg>';
const createIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><line x1="12" y1="5" x2="12" y2="19"></line><line x1="5" y1="12" x2="19" y2="12"></line></svg>';

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

const bubbleToSubAgent = (bubble: ChatBubble): SubAgent => {
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

const activeSubAgents = computed(() => {
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

const taskWidgetExpanded = ref(false);

watch(activeSubAgents, (newAgents, oldAgents) => {
  const newRunning = newAgents.some(a => a.status === 'running');
  const oldRunning = oldAgents?.some(a => a.status === 'running') ?? false;

  if (newAgents.length > 0 && (!oldAgents || oldAgents.length === 0)) {
    taskWidgetExpanded.value = true;
  }
  if (newRunning && !oldRunning) {
    taskWidgetExpanded.value = true;
  }
}, { deep: true });

const proposals = ref(proposalMocks);

const clearSelection = () => {
  store.clearSelection();
};

onMounted(async () => {
  initDefaultWindow();
  await checkAgentHealth();
  await gitStore.fetchBranches();
  await fetchProjectPath();
  startListening();
  startQuestionListening();
});

watch(() => props.panelReady, (newVal) => {
  if (newVal) {
    streamWelcomeMessage();
  }
});

watch(() => chatMessages.value, () => {
  if (shouldAutoScroll.value) {
    nextTick(() => {
      scrollToBottom();
    });
  }
}, { deep: true });

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    sendMessage();
  }
};

const handleGlobalClick = (event: MouseEvent) => {
  const target = event.target as HTMLElement;

  if (!target.closest('.add-context-wrapper')) {
    isContextMenuOpen.value = false;
    isAttachmentMenuOpen.value = false;
    activeSubmenu.value = null;
  }

  if (!target.closest('.window-tab.primary-clickable') && !target.closest('.branch-dropdown-overlay')) {
    isBranchDropdownOpen.value = false;
  }

  if (!target.closest('.new-window-wrapper') && !target.closest('.new-window-dropdown')) {
    showNewWindowDropdown.value = false;
  }

  if (!target.closest('.control-pill-wrapper.model')) {
    isModelMenuOpen.value = false;
  }

  if (!target.closest('.control-pill-wrapper.thinking')) {
    isThinkingMenuOpen.value = false;
  }

  if (!target.closest('.control-pill-wrapper.effort')) {
    isEffortMenuOpen.value = false;
  }
};

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
});

onUnmounted(() => {
  window.removeEventListener('click', handleGlobalClick);
  // SSE 连接由 singleton 服务管理，不随组件卸载关闭
  // stopListening() / stopQuestionListening() 已移除
  cleanupHealthCheck();
});

// 用 watch 代替 onMounted 注册 wheel 监听器
// 原因：onMounted 时 initDefaultWindow() 刚修改 windows.value，DOM 尚未更新，
// chatScrollRef.value 为 null，addEventListener 是 no-op，监听器从未注册。
// watch 会在 chatScrollRef 变为有效 DOM 元素后自动触发，同时处理多窗口切换。
watch(chatScrollRef, (newEl, oldEl) => {
  oldEl?.removeEventListener('wheel', handleTableWheel);
  newEl?.addEventListener('wheel', handleTableWheel, { passive: false });
});
</script>

<template>
  <Teleport to="body">
  <transition name="panel-slide" appear>
  <aside 
    class="ai-command-center" 
    :style="{ 
      width: panelWidth + 'px',
      position: 'fixed',
      top: '72px',
      right: '0',
      height: 'calc(100% - 72px)',
      zIndex: 190
    }"
    v-show="props.panelReady && !showScreenshotOverlay"
  >
    <!-- Resize Handle -->
    <div class="resize-handle" @mousedown="startResize">
        <div class="handle-bar"></div>
    </div>
    
    <div class="main-content">
      
      <!-- Layer 1: Context Header (Design J: Inline Branch & Strict New Window) -->
      <div class="layer-context">
        
        <!-- Row 1: Global Mode Switch (Left-Aligned Toolbar) -->
        <div class="header-toolbar">
            <div class="mode-switch">
                <button :class="{ active: mode === 'chat' }" @click="mode = 'chat'">Chat</button>
                <button :class="{ active: mode === 'tasks' }" @click="mode = 'tasks'">Task</button>
            </div>
            
            <!-- Right Side Actions (Placeholder for Balance) -->
            <div class="toolbar-actions">
                <button class="icon-btn" title="History">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <circle cx="12" cy="12" r="10"></circle>
                        <polyline points="12 6 12 12 16 14"></polyline>
                    </svg>
                </button>
            </div>
        </div>

        <!-- Row 2: Window Context (Tabs with Inline Branch) -->
        <div class="header-tabs" v-if="mode === 'chat'">
          
          <!-- Wrapper for Tabs + Fixed New Window Button -->
          <div class="tabs-wrapper">
              <!-- Window Tabs -->
              <div 
                class="window-tabs" 
                ref="windowTabsRef"
                @wheel="handleTabsWheel"
              >
                <div 
                  v-for="win in windows" 
                  :key="win.id"
                  class="window-tab"
                  :class="{ 
                    active: activeWindowId === win.id,
                    primary: win.isPrimary,
                    loading: win.isLoading,
                    error: win.error
                  }"
                  @click.stop="handleWindowTabClick(win)"
                >
                  <!-- Branch Info (Main Content) -->
                  <div class="tab-branch">
                      <!-- Main Content Wrapper for Centering -->
                      <div class="branch-main">
                          <span class="branch-icon">🌿</span>
                          <span class="branch-name">{{ win.branchId }}</span>
                      </div>

                      <!-- Primary Window Switch Indicator - Only clickable area -->
                      <button 
                          v-if="win.isPrimary" 
                          class="branch-switch-btn" 
                          :class="{ 'is-open': isBranchDropdownOpen }"
                          title="Switch Branch"
                          @click.stop="toggleBranchDropdown()"
                      >
                          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5">
                              <polyline points="6 9 12 15 18 9"></polyline>
                          </svg>
                      </button>
                  </div>

                  <!-- Status Indicators & Controls (Absolute Positioned) -->
                  <!-- 加载状态指示器 -->
                  <span v-if="win.isLoading" class="tab-status loading" title="加载中...">⏳</span>
                  <!-- 错误状态指示器 -->
                  <span v-else-if="win.error" class="tab-status error" :title="win.error">⚠️</span>
                  <!-- 关闭按钮：非主窗口且非加载中时显示 -->
                  <button v-if="!win.isPrimary && !win.isLoading" class="tab-close" @click.stop="closeWindow(win.id)">
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                          <line x1="18" y1="6" x2="6" y2="18"></line>
                          <line x1="6" y1="6" x2="18" y2="18"></line>
                      </svg>
                  </button>
                </div>

                <!-- New Window Button -->
                <div class="new-window-wrapper">
                  <button
                      class="new-window-btn"
                      title="New Window"
                      @click.stop="handleNewWindowClick"
                  >
                      <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="12" y1="5" x2="12" y2="19"></line>
                      <line x1="5" y1="12" x2="19" y2="12"></line>
                      </svg>
                  </button>
                </div>
              </div>
          </div>

          <!-- New Window Dropdown (Compact Style - Select Only) -->
          <div
              class="unified-dropdown new-window-dropdown"
              v-if="showNewWindowDropdown"
              :style="{
                top: newWindowDropdownPosition.top + 'px',
                left: newWindowDropdownPosition.left != null ? newWindowDropdownPosition.left + 'px' : 'auto',
                right: newWindowDropdownPosition.right != null ? newWindowDropdownPosition.right + 'px' : 'auto'
            }"
              @click.stop
          >
              <div
                  v-for="branch in branches"
                  :key="branch.name"
                  class="dropdown-option"
                  :class="{ disabled: isBranchOccupied(branch.name) }"
                  @click="!isBranchOccupied(branch.name) && addWindow(branch.name)"
              >
                  <div class="option-main">
                      <span class="option-icon" v-html="branchIcon"></span>
                      <span class="option-label">{{ branch.name }}</span>
                  </div>
                  <div v-if="branch.commit" class="option-tags">
                      <span class="tag-badge">{{ branch.commit.message.substring(0, 20) }}{{ branch.commit.message.length > 20 ? '...' : '' }}</span>
                  </div>
              </div>
          </div>

          <!-- Primary Window Branch Dropdown (Compact Style - Switch Mode with Create New) -->
          <div class="unified-dropdown branch-dropdown-overlay" v-if="isBranchDropdownOpen" @click.stop>
              <div
                  v-for="branch in branches"
                  :key="branch.id"
                  class="dropdown-option"
                  :class="{
                      selected: branch.name === currentBranch,
                      disabled: isBranchOccupiedByOther(branch.name)
                  }"
                  @click="!isBranchOccupiedByOther(branch.name) && selectBranch(branch.id)"
              >
                  <div class="option-main">
                      <span class="option-icon" v-html="branchIcon"></span>
                      <span class="option-label">{{ branch.name }}</span>
                  </div>
                  <div v-if="branch.commit" class="option-tags">
                      <span class="tag-badge">{{ branch.commit.message.substring(0, 20) }}{{ branch.commit.message.length > 20 ? '...' : '' }}</span>
                  </div>
              </div>
              <!-- 新建分支选项 -->
              <div class="dropdown-option create-new" @click="handleCreateNewBranchForPrimary">
                  <div class="option-main">
                      <span class="option-icon" v-html="createIcon"></span>
                      <span class="option-label">新建分支...</span>
                  </div>
              </div>
          </div>
        </div>

      </div>

      <!-- Layer 2: Intelligence Stream -->
      <div class="layer-stream">

         <!-- View: Chat - Phase 2: 多窗口 v-show 架构 -->
        <div v-show="mode === 'chat'" class="view-chat-container">
          <!-- 每个窗口独立的聊天容器 -->
          <div
            v-for="win in windows"
            :key="win.id"
            v-show="activeWindowId === win.id"
            class="view-chat window-chat-container"
            :ref="el => setChatScrollRef(win.id, el as HTMLElement)"
            @scroll="handleChatScroll(win.id)"
          >
            <!-- Actual Chat History -->
            <template v-for="(msg, msgIndex) in win.messages" :key="`${win.id}-${msgIndex}`">
                <div class="chat-message" :class="[msg.role === 'user' ? 'user' : 'ai', { streaming: msg.isStreaming }]">
                    <!-- Avatar Removed -->
                    <div class="message-wrapper">
                        <!-- 时间线气泡列表渲染 -->
                        <template v-for="bubble in msg.bubbles" :key="bubble.id">
                            <!-- Thinking 气泡 -->
                            <ThinkingBubble
                                v-if="bubble.type === 'thinking'"
                                :bubble="bubble"
                            />

                            <!-- 文本气泡 - 用户消息用纯文本，AI 消息用 Markdown -->
                            <div class="bubble" v-else-if="bubble.type === 'text' && (bubble.content || bubble.images?.length)">
                                <!-- 图片显示区域（用户消息专有） -->
                                <div class="bubble-images" v-if="bubble.images && bubble.images.length > 0">
                                    <img v-for="(img, idx) in bubble.images" :key="idx"
                                         :src="img" class="bubble-image" alt="attached image"
                                         @click="openLightbox(img)" />
                                </div>
                                <!-- 文本内容 -->
                                <template v-if="msg.role === 'user'">{{ bubble.content }}</template>
                                <MarkdownText v-else :content="bubble.content || ''" density="chat-compact" />
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

                            <!-- 问题气泡 (AskUserQuestion) -->
                            <QuestionBubble
                                v-else-if="bubble.type === 'question'"
                                :bubble="bubble"
                                @submit="submitAnswer"
                                @cancel="cancelQuestion"
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
            <div :ref="el => setChatBottomRef(win.id, el as HTMLElement)" class="chat-bottom-anchor"></div>
          </div>
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
            <!-- 1. Scope Chip (Always visible, defaults to 全局) -->
            <div class="context-chip scope">
                <span class="chip-icon">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                        <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
                        <polyline points="9 22 9 12 15 12 15 22"></polyline>
                    </svg>
                </span>
                <span class="chip-text">{{ scopeDisplayText }}</span>
            </div>

            <!-- 2. Selection Chip (Visible when any object selected) -->
            <transition name="chip-fade">
                <div class="context-chip selection" v-if="selectedCount > 0" @click.stop="isSelectionExpanded = !isSelectionExpanded">
                    <span class="chip-icon">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                        </svg>
                    </span>
                    <span class="chip-text">{{ selectionDisplayText }}</span>
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
                                    v-for="zone in availableZones"
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
                                    @click="handleAttachmentSelect(att)"
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

        <input
          ref="imageUploadInputRef"
          type="file"
          accept="image/*,.png,.jpg,.jpeg,.gif,.webp,.bmp,.tiff"
          multiple
          style="display: none;"
          @change="handleImageInputChange"
        />

        <!-- Antigravity Input Box -->
        <div class="antigravity-input-box">
            <!-- Pending Attachments Preview -->
            <div class="pending-attachments" v-if="pendingImages.length > 0">
              <div class="attachment-item" v-for="(img, idx) in pendingImages" :key="idx">
                <img :src="img" class="attachment-thumbnail" alt="attachment" @click="openLightbox(img)" />
                <button class="remove-attachment" @click.stop="removePendingImage(idx)">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <line x1="18" y1="6" x2="6" y2="18"></line>
                    <line x1="6" y1="6" x2="18" y2="18"></line>
                  </svg>
                </button>
              </div>
            </div>
            <textarea
              ref="textareaRef"
              v-model="inputMessage"
              :placeholder="agentStatus === 'connected' ? '你好' : agentStatus === 'connecting' ? '正在连接 Agent...' : 'Agent 未连接'"
              @keydown="handleKeydown"
              @paste="handleImagePaste"
              @input="adjustTextareaHeight"
              rows="1"
            ></textarea>
            
            <div class="input-footer">
                <div class="left-controls">
                    <!-- Attachment Button (Paperclip) -->
                    <!-- Screenshot Button -->
                    <button 
                        class="icon-btn" 
                        title="Screenshot"
                        @click="showScreenshotOverlay = true"
                    >
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                            <path d="M23 19a2 2 0 0 1-2 2H3a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h4l2-3h6l2 3h4a2 2 0 0 1 2 2z"></path>
                            <circle cx="12" cy="13" r="4"></circle>
                        </svg>
                    </button>

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
                                <!-- Screenshot Options REMOVED -->

                                <div class="menu-divider"></div>
                                <!-- Upload Options -->
                                <div class="menu-item" @click.stop="openImagePicker">
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

                    <!-- Effort Pill -->
                    <div class="control-pill-wrapper effort" :class="{ open: isEffortMenuOpen, disabled: isConfigLocked }">
                        <button class="control-pill" @click="!isConfigLocked && (isEffortMenuOpen = !isEffortMenuOpen)" :disabled="isConfigLocked">
                            <span class="text">{{ currentEffort.label }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isEffortMenuOpen">
                                <div class="menu-header">Effort</div>
                                <div
                                    v-for="e in effortLevels"
                                    :key="e.id"
                                    class="menu-item"
                                    :class="{ active: currentEffort.id === e.id }"
                                    @click="selectEffort(e)"
                                >
                                    <span class="item-text">{{ e.label }}</span>
                                    <svg v-if="currentEffort.id === e.id" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Thinking Pill -->
                    <div class="control-pill-wrapper thinking" :class="{ open: isThinkingMenuOpen, disabled: isConfigLocked }">
                        <button class="control-pill" @click="!isConfigLocked && (isThinkingMenuOpen = !isThinkingMenuOpen)" :disabled="isConfigLocked">
                            <span class="text">{{ currentThinking.label }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isThinkingMenuOpen">
                                <div class="menu-header">Thinking</div>
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
                    <!-- 停止按钮：AI 处理过程中显示 -->
                    <button
                      v-if="isLoading"
                      class="stop-btn-round"
                      @click="interruptMessage"
                      title="停止生成"
                    >
                        <svg viewBox="0 0 24 24" fill="currentColor">
                            <rect x="6" y="6" width="12" height="12" rx="1" />
                        </svg>
                    </button>
                    <!-- 发送按钮：空闲时显示 -->
                    <button
                      v-else
                      class="send-btn-round"
                      @click="sendMessage"
                      :disabled="!inputMessage.trim() || agentStatus !== 'connected'"
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

    <!-- Branch Creation Dialog (新建分支对话框) -->
    <BranchCreationDialog
      :visible="showBranchCreationDialog"
      :base-branch="currentBranch"
      :base-tags="[]"
      :all-branches="branchOptionsForDialog"
      @create="handleBranchCreated"
      @cancel="showBranchCreationDialog = false"
    />

    <!-- Screenshot Overlay for select area screenshot -->
    <Teleport to="body">
      <AdvancedScreenshotOverlay
        v-if="showScreenshotOverlay"
        @capture="handleScreenshotCapture"
        @cancel="handleScreenshotCancel"
      />
    </Teleport>

    <!-- Image Lightbox for enlarging images -->
    <ImageLightbox
      :visible="lightbox.visible"
      :src="lightbox.src"
      @close="closeLightbox"
    />
  </aside>
  </transition>
  </Teleport>
</template>

<style scoped lang="scss">
/* Panel Slide Animation */
.panel-slide-enter-active,
.panel-slide-leave-active {
  transition: transform 0.6s cubic-bezier(0.25, 1, 0.5, 1);
}

.panel-slide-enter-from,
.panel-slide-leave-to {
  transform: translateX(120%); /* Move completely off-screen to the right */
}

.ai-command-center {
  /* Local Variables */
  --chrome-bg: #0A0A0A; /* Pure Dark Grey for seamless integration */

  /* Layout & Positioning - Fixed to float over Canvas */
  position: fixed;
  z-index: 190; /* Ensure it's above canvas but below header (200) */
  top: 72px; /* Below header */
  right: 0;
  height: calc(100% - 72px);
  margin-left: auto;
  margin-right: 0;
  
  /* Aurora Glass Effect */
  /* Aurora Glass Effect */
  background: #050505; /* Darken header to Black for Chrome contrast */
  backdrop-filter: var(--glass-blur);
  -webkit-backdrop-filter: var(--glass-blur);
  border: 1px solid rgba(255, 255, 255, 0.2); /* Stronger border */
  border-right: none;
  border-radius: 24px 0 0 24px;
  
  /* Glare & Shadow */
  background-image: var(--glass-glare); /* Remove gradient to keep it dark */
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

/* --- Layer 1: Context Header (Design J Refined: Premium & Balanced) --- */
.layer-context {
    padding: 0;
    height: auto;
    border-bottom: none; /* Remove border to allow tabs to overlap content border */
    display: flex;
    flex-direction: column;
    flex-shrink: 0;

    /* Row 1: Global Mode Switch (Compact Toolbar) */
    .header-toolbar {
        height: 40px; /* Increased height for larger toggle */
        padding: 0 12px; /* Align with tabs padding */
        display: flex;
        align-items: center;
        justify-content: center; /* 居中显示 */
        position: relative; /* 用于绝对定位子元素 */
        background: var(--surface-dim);
        border-bottom: 1px solid var(--border-subtle);
    }

    /* Mode Switch (Refined Segmented Control) */
    .mode-switch {
        display: flex;
        align-items: center;
        align-items: center;
        background: rgba(0, 0, 0, 0.2);
        padding: 3px; /* Increased padding */
        border-radius: 6px;
        gap: 4px; /* 增加按钮间距 */

        button {
            border: none;
            background: transparent;
            padding: 4px 20px; /* Increased padding */
            color: var(--text-tertiary);
            font-size: 13px; /* Slightly larger font */
            font-weight: 500;
            cursor: pointer;
            transition: all 0.2s ease;
            border-radius: 4px;
            min-width: 72px; /* Increased min-width */
            text-align: center;

            &:hover {
                color: var(--text-secondary);
            }

            &.active {
                background: var(--surface-elevated);
                color: var(--text-primary);
                font-weight: 600;
                box-shadow: 0 1px 2px rgba(0,0,0,0.15);
            }
        }
    }

    /* Toolbar Actions (Right Side - 绝对定位不影响居中) */
    .toolbar-actions {
        position: absolute;
        right: 12px;
        display: flex;
        align-items: center;
        gap: 8px;

        .icon-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 24px;
            height: 24px;
            border-radius: 4px;
            border: none;
            background: transparent;
            color: var(--text-tertiary);
            cursor: pointer;
            transition: all 0.2s ease;

            &:hover {
                background: rgba(255, 255, 255, 0.1);
                color: var(--text-primary);
            }

            svg { width: 14px; height: 14px; }
        }
    }

    /* Row 2: Window Context (Tabs + Branch) */
    .header-tabs {
        height: 40px; /* Compact height */
        padding: 0; /* Remove padding for flush left alignment */
        display: flex;
        align-items: flex-end; /* Align tabs to bottom for connected look */
        background: transparent;
        position: relative;
        /* border-bottom is on layer-context, we will overlap it */
    }

    /* Wrapper for Tabs + Fixed Button */
    .tabs-wrapper {
        display: flex;
        align-items: center;
        width: 100%;
        height: 100%;
        gap: 4px;
    }

    /* Window Tabs (Scrollable Area) */
    .window-tabs {
        display: flex;
        align-items: flex-end; /* Align items to bottom */
        gap: 2px; /* Small gap between tabs */
        overflow-x: auto;
        overflow-y: visible; /* 确保下拉框不被截断 */
        scrollbar-width: none;
        flex: 1;
        min-width: 0;
        height: 100%;
        /* Remove segmented control style */
        background: transparent;
        padding: 0;
        border-radius: 0;
        align-self: stretch; /* Stretch to fill height */

        &::-webkit-scrollbar { display: none; }
    }

    .window-tab {
        display: flex;
        flex-direction: column;
        justify-content: center;
        gap: 1px; /* Tighter gap */
        padding: 0; /* Remove padding from container */
        background: transparent;
        color: rgba(255, 255, 255, 0.5); /* Dim Grey for inactive (50%) */
        font-weight: 500; /* Medium weight for inactive */
        /* Chrome Style: Physical Surface */
        border-radius: 8px 8px 0 0; /* Chrome-like rounded top */
        margin-right: -1px; /* Connect tabs visually */
        border: 1px solid transparent; /* Prepare for border */
        border-right: 1px solid rgba(255,255,255,0.05); /* Separator */
        border-bottom: none;
        position: relative; /* For absolute positioning of controls */
        margin-bottom: 0;
        
        /* Layout Fixes */
        height: 34px; /* Standard height for inactive tabs */
        margin-top: 1px; /* Push down to align text with active tab */
        min-width: 120px;
        max-width: 200px;
        padding: 0 20px; /* Space for content */

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            color: rgba(255, 255, 255, 0.8); /* 80% White on hover */
        }

        &.active {
            /* Chrome Style: Seamless Active State */
            background: var(--chrome-bg); /* Match content area background */
            color: #ffffff; /* Pure White */
            font-weight: 600; /* Semi-Bold for active */
            border: 1px solid var(--border-dim);
            border-bottom: none; /* Open to bottom */
            
            /* Overlap the content border */
            margin-bottom: -1px;
            margin-top: 0; /* Rise up */
            z-index: 100; /* Ensure it sits on top of content border */
            height: 35px; /* Increase height to ensure overlap */

            /* Branch name becomes white */
            .tab-branch { color: inherit; }
        }

        /* Controls visibility on hover */
        &:hover {
            .tab-close { opacity: 1; }
            /* .branch-switch-btn is always visible for primary, no hover needed */
        }

        /* 加载状态 */
        &.loading {
            opacity: 0.7;
            cursor: wait;
            .tab-name { color: var(--text-muted); }
        }

        /* 错误状态 */
        &.error {
            border-color: var(--error, #ef4444);
            background: rgba(239, 68, 68, 0.1);
            .tab-name { color: var(--error, #ef4444); }
        }

        /* Status Indicators (Absolute) */
        .tab-status {
            position: absolute;
            right: 8px;
            top: 50%;
            transform: translateY(-50%);
            font-size: 12px;
            flex-shrink: 0;
            z-index: 10;

            &.loading {
                animation: pulse 1.5s ease-in-out infinite;
            }

            &.error {
                cursor: help;
            }
        }

        @keyframes pulse {
            0%, 100% { opacity: 1; }
            50% { opacity: 0.4; }
        }

        /* Branch Info (Main Content) */
        .tab-branch {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 100%;
            height: 100%; /* Fill the tab */
            font-size: 12px; /* Compact font */
            font-weight: 500;
            color: inherit; /* Inherit from parent to match active/inactive state */
            padding: 0; /* Remove padding to allow true centering */
            transition: all 0.2s ease;

            /* Wrapper for Name + Icon */
            .branch-main {
                position: relative; /* For absolute positioning of icon */
                display: flex;
                align-items: center;
                justify-content: center;
                width: auto; /* Let text define width */
                max-width: 100%;
                /* Ensure this is centered */
                margin: 0 auto;
            }

            .branch-icon { 
                position: absolute;
                right: 100%; /* Hang on the left side of the text */
                margin-right: 6px;
                top: 50%;
                transform: translateY(-50%);
                font-size: 12px; /* Compact icon */
                opacity: 0.8; 
                display: flex;
                align-items: center;
                white-space: nowrap; /* Prevent wrapping */
            }

            .branch-name {
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
                flex: 1;
                text-align: center;
                font-family: 'JetBrains Mono', monospace;
            }

            .branch-switch-btn {
                position: absolute;
                right: 4px; /* Align with close button position */
                top: 50%;
                transform: translateY(-50%);
                padding: 0;
                width: 20px;
                height: 20px;
                display: flex;
                align-items: center;
                justify-content: center;
                background: transparent;
                border: none;
                border-radius: 4px;
                color: var(--text-tertiary);
                cursor: pointer;
                transition: background 0.2s, color 0.2s;
                z-index: 20; /* Higher than others */
                
                svg { 
                    width: 14px; 
                    height: 14px; 
                    transition: transform 0.3s cubic-bezier(0.4, 0, 0.2, 1);
                }

                &.is-open svg {
                    transform: rotate(180deg);
                }

                &:hover {
                    background: rgba(255, 255, 255, 0.1);
                    color: var(--text-primary);
                }
            }
        }

        /* Legacy: primary-clickable class (keep for compatibility) */
        &.primary-clickable {
            cursor: pointer;
        }
    }

    .tab-close {
        position: absolute;
        right: 8px;
        top: 50%;
        transform: translateY(-50%);
        display: flex;
        z-index: 10;
        align-items: center;
        justify-content: center;
        width: 14px;
        height: 14px;
        border-radius: 50%;
        border: none;
        background: transparent;
        color: var(--text-tertiary);
        cursor: pointer;
        opacity: 0;
        transition: all 0.2s ease;
        padding: 0;

        svg { width: 10px; height: 10px; }
    }

    .window-tab:hover .tab-close {
        opacity: 1;
    }

    .tab-close:hover {
        background: rgba(255, 255, 255, 0.2);
        color: var(--text-primary);
    }

    /* New Window Button Wrapper (紧跟窗口标签) */
    .new-window-wrapper {
        position: relative;
        flex-shrink: 0;
        height: 32px; /* Match tab height */
        display: flex;
        align-items: center;
        margin-left: 4px; /* Add spacing from the tabs */
        margin-bottom: 0; /* Sit on the line */
        align-self: flex-end; /* Align to bottom like tabs */
    }

    .new-window-btn {
        display: flex;
        align-items: center;
        justify-content: center;
        width: 24px;
        height: 24px;
        border-radius: 6px;
        background: transparent;
        border: none;
        color: var(--text-tertiary);
        cursor: pointer;
        transition: all 0.2s ease;

        &:hover:not(:disabled) {
            background: var(--surface-dim);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.3;
            cursor: not-allowed;
        }

        svg { width: 16px; height: 16px; }
    }

    /* Dropdowns (Premium Glassmorphism) */
    .branch-dropdown-menu,
    .new-window-dropdown,
    .branch-dropdown-overlay {
        position: absolute;
        background: rgba(30, 30, 35, 0.95); /* Dark semi-transparent */
        backdrop-filter: blur(12px);
        -webkit-backdrop-filter: blur(12px);
        border: 1px solid rgba(255, 255, 255, 0.1);
        border-radius: 8px;
        box-shadow: 
            0 4px 20px rgba(0, 0, 0, 0.4),
            0 0 0 1px rgba(255, 255, 255, 0.05) inset;
        z-index: 200;
        overflow: hidden;
        margin-top: 4px;
    }
    
    .new-window-dropdown {
        /* 位置通过 JavaScript 动态设置 */
        /* left 和 right 由 JS 动态控制 */
        width: 200px;
        
        .dropdown-header {
            padding: 6px 10px;
            font-size: 10px;
            font-weight: 600;
            color: var(--text-tertiary);
            text-transform: uppercase;
            letter-spacing: 0.5px;
            border-bottom: 1px solid rgba(255, 255, 255, 0.05);
            background: rgba(255, 255, 255, 0.02);
        }
    }

    .branch-dropdown-overlay {
        top: 100%;
        left: 12px; /* Align with padding */
        width: 240px;
        
        .branch-tree {
            max-height: 240px;
            overflow-y: auto;
            padding: 4px;
        }
    }

    .branch-list, .branch-tree {
        max-height: 200px;
        overflow-y: auto;
        padding: 4px;
    }

    .branch-item {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 6px 8px;
        border-radius: 6px;
        cursor: pointer;
        font-size: 12px;
        color: var(--text-secondary);
        transition: all 0.15s;
        border: 1px solid transparent;

        &:hover {
            background: rgba(255, 255, 255, 0.05);
            color: var(--text-primary);
        }

        &.current {
            background: rgba(var(--accent-primary-rgb), 0.1);
            color: var(--accent-primary);
            border-color: rgba(var(--accent-primary-rgb), 0.2);
            font-weight: 500;
        }

        &.occupied {
            opacity: 0.5;
            cursor: not-allowed;

            &:hover {
                background: transparent;
                color: var(--text-secondary);
            }
        }

        .branch-main {
            display: flex;
            align-items: center;
            gap: 8px;
            width: 100%;
        }

        .branch-icon { font-size: 12px; opacity: 0.7; }
        .branch-name { font-family: 'JetBrains Mono', monospace; flex: 1; }
        .current-indicator { margin-left: auto; color: var(--accent-primary); display: flex; }
        .occupied-hint {
            margin-left: auto;
            font-size: 10px;
            color: var(--text-tertiary);
            font-style: italic;
        }

        /* 新建分支选项样式 */
        &.create-new {
            color: var(--accent-primary);
            &:hover {
                background: rgba(var(--accent-primary-rgb), 0.1);
            }
        }
    }

    /* 下拉框分隔线 */
    .dropdown-divider {
        height: 1px;
        background: rgba(255, 255, 255, 0.1);
        margin: 4px 0;
    }
}

/* --- Layer 2: Intelligence Stream --- */
.layer-stream {
    flex: 1;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    position: relative;
    min-height: 0;
    background: var(--chrome-bg); /* Unified opaque background */
    border-top: 1px solid var(--border-dim); /* Add border here for tabs to overlap */
    border-left: 1px solid var(--border-dim); /* Match tab border for alignment */
    border-right: 1px solid var(--border-dim); /* Match tab border for alignment */
    margin-top: -1px; /* Pull up to meet tabs */
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

/* Phase 2: 多窗口聊天容器 */
.view-chat-container {
    position: relative;
    width: 100%;
    height: 100%;
    overflow: hidden;
}

.window-chat-container {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    overflow-y: auto;
    overflow-x: hidden;
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
            .message-wrapper {
                width: 100%; // AI 消息强制全宽，避免短内容时 wrapper 被压缩
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

            // === 用户消息附带图片样式 ===
            .bubble-images {
                display: flex;
                gap: 4px;
                margin-bottom: 6px;
                flex-wrap: wrap;
            }

            .bubble-image {
                width: 72px;
                height: 72px;
                border-radius: 4px;
                object-fit: cover;
                cursor: pointer;
                border: 1px solid var(--border-dim);
                transition: opacity 0.15s;

                &:hover {
                    opacity: 0.85;
                }
            }

            &.empty {
                min-height: 20px;
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

    .pending-attachments {
        display: flex;
        gap: 8px;
        padding: 12px 12px 8px;
        flex-wrap: wrap;

        .attachment-item {
            position: relative;
            width: 64px;
            height: 64px;
            flex-shrink: 0;
        }

        .attachment-thumbnail {
            width: 100%;
            height: 100%;
            object-fit: cover;
            border-radius: 8px;
            border: 1px solid rgba(255, 255, 255, 0.15);
            cursor: pointer;
        }

        .remove-attachment {
            position: absolute;
            top: -6px;
            right: -6px;
            width: 20px;
            height: 20px;
            border-radius: 50%;
            background: #ff4444;
            border: none;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            transition: transform 0.15s ease, background 0.15s ease;

            &:hover {
                background: #ff2222;
                transform: scale(1.1);
            }

            svg {
                width: 12px;
                height: 12px;
                stroke: white;
            }
        }
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

    .control-pill-wrapper.disabled {
        .control-pill {
            opacity: 0.35;
            cursor: not-allowed;
            &:hover {
                background: transparent;
                border-color: transparent;
                color: rgba(255, 255, 255, 0.5);
            }
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

    /* 停止按钮样式（Codex 风格） */
    .stop-btn-round {
        width: 32px;
        height: 32px;
        border-radius: 50%;
        background: #ff3b30; /* 红色警示 */
        border: none;
        display: flex;
        align-items: center;
        justify-content: center;
        color: white;
        cursor: pointer;
        transition: transform 0.1s, background 0.2s;

        svg { width: 14px; height: 14px; }

        &:hover {
            background: #e0352b;
        }
        &:active {
            transform: scale(0.95);
        }
    }

    /* Menus */
}

/* Branch Select Override for Compact Tab View */
.branch-select-compact {
  display: inline-flex;
  
  :deep(.select-trigger) {
    padding: 0;
    border: none;
    background: transparent !important;
    height: auto;
    min-height: unset;
    
    &:hover {
      background: transparent !important;
      
      .branch-trigger {
        background: rgba(255, 255, 255, 0.1);
      }
    }
    
    &.active .branch-trigger {
      background: rgba(255, 255, 255, 0.15);
      color: var(--text-primary);
    }
  }
}

.branch-trigger {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 2px 6px;
  border-radius: 4px;
  transition: all 0.2s;
  cursor: pointer;
  color: var(--text-secondary);

  &:hover {
    background: rgba(255, 255, 255, 0.1);
    color: var(--text-primary);
  }

  .branch-name {
    max-width: 100px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  
  .chevron {
    opacity: 0.5;
  }
}

/* New Window Button Override */
.new-window-btn {
  width: 28px;
  height: 28px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 6px;
  border: 1px solid transparent;
  background: transparent;
  color: var(--text-secondary);
  cursor: pointer;
  transition: all 0.2s;
  flex-shrink: 0;
  margin-left: 4px;

  &:hover {
    background: rgba(255, 255, 255, 0.1);
    color: var(--text-primary);
  }
  
  /* When GlassSelect is open, it adds 'active' class to trigger */
  /* But here the trigger IS the button inside the slot */
}

/* ============================================== */
/* Unified Dropdown Styles (GlassSelect-like)    */
/* ============================================== */
.unified-dropdown {
  position: absolute;
  background: #14141e !important; /* Solid opaque background - force override */
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 8px;
  padding: 4px;
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.6);
  z-index: 200;
  width: max-content; /* Grow to fit content */
  min-width: 260px; /* Reasonable minimum to fit branch name + tags */
  max-width: 320px; /* Prevent excessive width */
  max-height: 320px;
  overflow-y: auto;
  overflow-x: hidden;

  .dropdown-option {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
    padding: 6px 10px; /* More compact like GlassSelect */
    color: var(--text-secondary);
    font-size: 0.8rem; /* Slightly smaller */
    cursor: pointer;
    border-radius: 4px;
    transition: all 0.2s;
    position: relative;

    &:hover {
      background: rgba(255, 255, 255, 0.05);
      color: var(--text-primary);
    }

    &.selected {
      background: rgba(59, 130, 246, 0.15);
      color: var(--accent-blue);
    }

    &.disabled {
      opacity: 0.5;
      cursor: not-allowed;

      &:hover {
        background: transparent;
        color: var(--text-secondary);
      }
    }

    &.create-new {
      /* Inherit default color */

      &:hover {
        background: rgba(59, 130, 246, 0.1);
      }
    }
  }

  .option-main {
    display: flex;
    align-items: center;
    gap: 8px;
    flex: 1;
    min-width: 0; /* Enable truncation in flex child */
  }

  .option-icon {
    font-size: 1rem;
    line-height: 1;
    display: flex;
    align-items: center;
    flex-shrink: 0;

    svg {
      width: 16px;
      height: 16px;
    }
  }

  .option-label {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .option-tags {
    display: flex;
    gap: 4px;
    flex-shrink: 0;
  }

  .tag-badge {
    font-size: 0.65rem;
    padding: 2px 6px;
    background: rgba(255, 255, 255, 0.08);
    border: 1px solid rgba(255, 255, 255, 0.05);
    border-radius: 4px;
    color: var(--text-secondary);
    white-space: nowrap;
    max-width: 120px;
    overflow: hidden;
    text-overflow: ellipsis;
    font-family: var(--font-mono, monospace);
  }

  .option-hint {
    font-size: 0.7rem;
    color: var(--text-muted);
    font-style: italic;
    white-space: nowrap;
  }

  .check-icon {
    flex-shrink: 0;
    color: var(--accent-blue);
    margin-left: 4px;
  }

  .dropdown-divider {
    height: 1px;
    background: rgba(255, 255, 255, 0.1);
    margin: 4px 0;
  }
}

/* Position override for specific dropdowns */
.branch-dropdown-overlay {
  top: 100%;
  left: 0;
  margin-top: 4px;
}

.new-window-dropdown {
  /* Position already set via inline styles */
}
</style>
