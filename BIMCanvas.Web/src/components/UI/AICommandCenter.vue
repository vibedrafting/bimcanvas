<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { storeToRefs } from 'pinia';
import { useCanvasStore } from '../../stores/canvasStore';
import { useGitStore } from '../../stores/gitStore';
import type { SubAgent, ToolCall, ChatBubble } from '../../types/agent';
import type { ChatAttachmentRef, ChatAttachmentSourceKind } from '../../types/chatAttachment';
import { proposalMocks } from '../../constants/aiCommandCenter';
import { useAgentConfig } from '../../composables/aiCommandCenter/useAgentConfig';
import { useChatScroll } from '../../composables/aiCommandCenter/useChatScroll';
import { useChatStream } from '../../composables/aiCommandCenter/useChatStream';
import { useContextMenu } from '../../composables/aiCommandCenter/useContextMenu';
import { usePanelUI } from '../../composables/aiCommandCenter/usePanelUI';
import { useScreenshot } from '../../composables/aiCommandCenter/useScreenshot';
import { useQuestion } from '../../composables/aiCommandCenter/useQuestion';
import { useBackgroundTask } from '../../composables/aiCommandCenter/useBackgroundTask';
import { useSelectionContext } from '../../composables/aiCommandCenter/useSelectionContext';
import { useSpatialMarking } from '../../composables/aiCommandCenter/useSpatialMarking';
import { useWindowManager } from '../../composables/aiCommandCenter/useWindowManager';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';
import BranchCreationDialog from './Ribbon/BranchCreationDialog.vue';
import ThinkingBubble from './ThinkingBubble.vue';
import ToolCallBubble from './ToolCallBubble.vue';
import TodoProgressPanel from './TodoProgressPanel.vue';
import SubAgentBubble from './SubAgentBubble.vue';
import QuestionBubble from './QuestionBubble.vue';
import RateLimitBanner from './RateLimitBanner.vue';
import WaitingIndicator from './WaitingIndicator.vue';
import TaskSummaryWidget from './TaskSummaryWidget.vue';
import WorkflowProgressPanel from './WorkflowProgressPanel.vue';
import { useWorkflowProgress } from '../../composables/aiCommandCenter/useWorkflowProgress';
import MarkdownText from './base/MarkdownText.vue';
import AdvancedScreenshotOverlay from './AdvancedScreenshotOverlay.vue';
import ImageLightbox from './ImageLightbox.vue';
import { AGENT_API, SERVER_BASE } from '../../config/api';
import { ChatAttachmentService, getImageDimensions } from '../../services/ChatAttachmentService';

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

const AGENT_API_BASE = AGENT_API;
const SERVER_API_BASE = SERVER_BASE;

const { panelWidth, windowTabsRef, carouselTrackRef, startResize, handleTabsWheel, handleWheel } = usePanelUI();
void windowTabsRef;
void carouselTrackRef;

const mode = ref<'chat' | 'tasks'>('chat');

const gitStore = useGitStore();
const { branches, currentBranch } = storeToRefs(gitStore);

const store = useCanvasStore();

const {
  selectedCount,
  selectionDisplayText,
  scopeDisplayText,
  availableZones,
  buildContextPayload,
  buildContextSnapshot
} = useSelectionContext();
const DEFAULT_SPATIAL_INTENT_OPTIONS = ['家具（位置）', '设计区', '通道', '禁区'] as const;
const SPATIAL_INTENT_STORAGE_KEY = 'bimcanvas:space-mark:intent-options';
const isSelectionExpanded = ref(false);
const spatialIntentOptions = ref<string[]>([...DEFAULT_SPATIAL_INTENT_OPTIONS]);
const isSpatialIntentMenuOpen = ref(false);
const spatialLabelInputRef = ref<HTMLInputElement | null>(null);

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
  restoreWindowSession,
  syncWindowActivation,
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

const pendingAttachments = computed({
  get: () => activeWindow.value?.pendingAttachments || [],
  set: (val) => { if (activeWindow.value) activeWindow.value.pendingAttachments = val; }
});

const isLoading = computed({
  get: () => activeWindow.value?.isStreaming || false,
  set: (val) => { if (activeWindow.value) activeWindow.value.isStreaming = val; }
});

const activeTodoProgress = computed(() => activeWindow.value?.todoProgress ?? null);
const todoProgressOverlayRef = ref<HTMLElement | null>(null);
const todoProgressOverlayHeight = ref(0);
const todoProgressSpace = computed(() =>
  todoProgressOverlayHeight.value > 0 ? `${todoProgressOverlayHeight.value}px` : '0px'
);

let todoProgressResizeObserver: ResizeObserver | null = null;

const setTodoProgressOverlayRef = (el: HTMLElement | null) => {
  todoProgressResizeObserver?.disconnect();
  todoProgressResizeObserver = null;
  todoProgressOverlayRef.value = el;

  if (!el) {
    todoProgressOverlayHeight.value = 0;
    return;
  }

  const updateHeight = () => {
    todoProgressOverlayHeight.value = Math.ceil(el.getBoundingClientRect().height);
  };

  updateHeight();

  if (typeof ResizeObserver !== 'undefined') {
    todoProgressResizeObserver = new ResizeObserver(updateHeight);
    todoProgressResizeObserver.observe(el);
  }
};

const toggleTodoProgressCollapsed = () => {
  const progress = activeWindow.value?.todoProgress;
  if (progress) {
    progress.isCollapsed = !progress.isCollapsed;
  }
};

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
  hasFallback,
  isModelMenuOpen,
  isThinkingMenuOpen,
  isEffortMenuOpen,
  fetchAgentConfig,
  selectModel,
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

const {
  activeDraft,
  draftScopeDisplayText,
  pendingSpatialMarks,
  topLevelZones,
  startSpatialMarking,
  cancelSpatialMarking,
  setDraftCellSize,
  setDraftLabel,
  setDraftDescription,
  clearDraftSelection,
  completeDraft,
  editPendingMark,
  removePendingMark
} = useSpatialMarking({
  windows,
  activeWindowId,
  activeWindow
});

const getSpatialMarkZoneName = (zoneId: string) =>
  topLevelZones.value.find(zone => zone.id === zoneId)?.label || zoneId;

const getEventValue = (event: Event) => (event.target as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement).value;

const activeQueuedMessage = computed(() => activeWindow.value?.queuedMessage ?? null);
const hasInputSendableContent = computed(() =>
  inputMessage.value.trim().length > 0
  || pendingAttachments.value.length > 0
  || pendingSpatialMarks.value.length > 0
);
const canRestoreQueuedMessage = computed(() =>
  !!activeQueuedMessage.value && !hasInputSendableContent.value
);
const queuedAttachmentCount = computed(() => activeQueuedMessage.value?.attachments.length ?? 0);
const queuedSpatialMarkCount = computed(() => activeQueuedMessage.value?.spatialMarks.length ?? 0);
const queuedMessagePreview = computed(() => {
  const queued = activeQueuedMessage.value;
  if (!queued) return '';

  const text = queued.text || '等待发送的上下文';
  const badges: string[] = [];
  if (queued.attachments.length > 0) badges.push(`${queued.attachments.length} 图`);
  if (queued.spatialMarks.length > 0) badges.push(`${queued.spatialMarks.length} Space Mark`);
  return badges.length > 0 ? `${text} · ${badges.join(' · ')}` : text;
});

const updatePendingSpatialLabel = (markId: string, value: string) => {
  const mark = pendingSpatialMarks.value.find(item => item.id === markId);
  if (mark) mark.label = value;
};

const updatePendingSpatialDescription = (markId: string, value: string) => {
  const mark = pendingSpatialMarks.value.find(item => item.id === markId);
  if (mark) mark.description = value;
};

const normalizeSpatialIntent = (value: string) => value.trim();
const activeSpatialIntent = computed(() => normalizeSpatialIntent(activeDraft.value?.label ?? ''));
const canAddSpatialIntent = computed(() =>
  activeSpatialIntent.value.length > 0 && !spatialIntentOptions.value.includes(activeSpatialIntent.value)
);
const canDeleteSpatialIntent = computed(() => spatialIntentOptions.value.includes(activeSpatialIntent.value));

const saveSpatialIntentOptions = () => {
  try {
    window.localStorage.setItem(SPATIAL_INTENT_STORAGE_KEY, JSON.stringify(spatialIntentOptions.value));
  } catch (error) {
    console.warn('[AICommandCenter] Failed to save Space Mark tag presets:', error);
  }
};

const loadSpatialIntentOptions = () => {
  try {
    const raw = window.localStorage.getItem(SPATIAL_INTENT_STORAGE_KEY);
    if (!raw) return;

    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return;

    const options = parsed
      .map(item => normalizeSpatialIntent(String(item)))
      .filter((item, index, list) => item.length > 0 && list.indexOf(item) === index);
    spatialIntentOptions.value = options;
  } catch (error) {
    console.warn('[AICommandCenter] Failed to load Space Mark tag presets:', error);
  }
};

const selectSpatialIntent = (intent: string) => {
  setDraftLabel(intent);
  isSpatialIntentMenuOpen.value = false;
};

const addSpatialIntentOption = () => {
  const intent = activeSpatialIntent.value;
  if (!intent || spatialIntentOptions.value.includes(intent)) return;
  spatialIntentOptions.value = [...spatialIntentOptions.value, intent];
  saveSpatialIntentOptions();
};

const deleteSpatialIntentOption = () => {
  const intent = activeSpatialIntent.value;
  if (!intent) return;
  const nextOptions = spatialIntentOptions.value.filter(item => item !== intent);
  if (nextOptions.length === spatialIntentOptions.value.length) return;
  spatialIntentOptions.value = nextOptions;
  saveSpatialIntentOptions();
};

const dismissSpatialLabelSuggestions = () => {
  if (document.activeElement === spatialLabelInputRef.value) {
    spatialLabelInputRef.value?.blur();
  }
  isSpatialIntentMenuOpen.value = false;
};

watch(
  () => activeDraft.value?.selectedCells.map(cell => `${cell.col}:${cell.row}`).join(',') ?? '',
  (nextSelection, previousSelection) => {
    if (nextSelection !== previousSelection) {
      dismissSpatialLabelSuggestions();
    }
  }
);

onMounted(loadSpatialIntentOptions);

const toggleSpaceMarkFromButton = () => {
  if (activeDraft.value) {
    cancelSpatialMarking();
  } else {
    startSpatialMarking();
  }
  isContextMenuOpen.value = false;
  activeSubmenu.value = null;
};

// === Image Upload ===
const imageUploadInputRef = ref<HTMLInputElement | null>(null);
const imageExtPattern = /\.(png|jpe?g|gif|webp|bmp|tiff)$/i;

const ensureAttachmentUploadContext = async () => {
  if (!currentProjectPath.value) {
    await fetchProjectPath();
  }

  const projectPath = currentProjectPath.value;
  const windowState = activeWindow.value;
  if (!projectPath || !windowState) {
    throw new Error('项目路径或当前窗口不存在，无法上传图片附件');
  }

  return {
    projectPath,
    windowId: windowState.id,
    clientMessageId: windowState.draftMessageId
  };
};

const appendImageFiles = async (files: File[], sourceKind: ChatAttachmentSourceKind = 'upload') => {
  const imageFiles = files.filter(file =>
    file.type.startsWith('image/') || imageExtPattern.test(file.name)
  );
  if (imageFiles.length === 0) return;

  const { projectPath, windowId, clientMessageId } = await ensureAttachmentUploadContext();
  const uploadedAttachments: ChatAttachmentRef[] = [];

  for (const file of imageFiles) {
    const dimensions = await getImageDimensions(file).catch(() => undefined);
    const attachment = await ChatAttachmentService.uploadAttachment({
      projectPath,
      windowId,
      clientMessageId,
      sourceKind,
      file,
      width: dimensions?.width,
      height: dimensions?.height
    });
    uploadedAttachments.push(attachment);
  }

  pendingAttachments.value.push(...uploadedAttachments);
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
      await appendImageFiles(files, 'upload');
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
  await appendImageFiles(files, 'upload');
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
  await appendImageFiles(imageFiles, 'paste');

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
  isAwaitingTaskResult,
  streamWelcomeMessage,
  sendMessage,
  sendQueuedMessageNow,
  restoreQueuedMessage,
  deleteQueuedMessage,
  restoreHistory,
  waitForInteractionContinuation,
  interruptMessage,
  injectBackgroundSummary,
  injectBackgroundTurn,
  checkAgentHealth,
  fetchProjectPath,
  cleanupHealthCheck,
  cleanupHistoryPolling
} = useChatStream({
  agentApiBase: AGENT_API_BASE,
  windows,
  activeWindowId,
  activeWindow,
  addMessage,
  addMessageToWindow,
  getWindowMessage,
  pendingAttachments,
  currentModel,
  currentEffort,
  currentThinking,
  scrollToBottom,
  fetchAgentConfig,
  hasFallback,
  buildContextPayload: (spatialMarks = pendingSpatialMarks.value) => buildContextPayload(spatialMarks),
  buildContextSnapshot: (spatialMarks = pendingSpatialMarks.value) => buildContextSnapshot(spatialMarks)
});

const hasProgressOverlay = computed(() => !!activeTodoProgress.value || hasBackgroundActivity.value);

const {
  showScreenshotOverlay,
  startListening,
  stopListening: stopScreenshotListening,
  handleScreenshotCapture,
  handleScreenshotCancel,
  removePendingAttachment
} = useScreenshot({
  agentApiBase: AGENT_API_BASE,
  windows,
  pendingAttachments,
  currentProjectPath,
  activeWindow,
  ensureProjectPath: fetchProjectPath
});

const {
  startListening: startQuestionListening,
  stopListening: stopQuestionListening,
  submitAnswer,
  cancelQuestion
} = useQuestion({
  agentApiBase: AGENT_API_BASE,
  windows,
  scrollToBottom,
  waitForInteractionContinuation
});

const {
  startListening: startBackgroundTaskListening,
  stopListening: stopBackgroundTaskListening
} = useBackgroundTask({
  agentApiBase: AGENT_API_BASE,
  windows,
  scrollToBottom,
  injectBackgroundSummary,
  injectBackgroundTurn
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

const handleRestoreQueuedMessage = () => {
  if (!restoreQueuedMessage()) {
    return;
  }
  nextTick(() => adjustTextareaHeight());
};

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
    if (!msg) {
      continue;
    }
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

// Workflow 进度（Task 页可视化）。hasActiveWorkflow 锚到 workflow 工具调用触发信号（见 useChatStream tool.started）。
const { hasActiveWorkflow, hasCompletedWorkflow, backgroundTaskCount } = useWorkflowProgress();
// 占位 mock 与 workflow 进度面板互补：有 workflow（进行中或已完成留存）→ 隐占位、显进度。
const hasWorkflowView = computed(() => hasActiveWorkflow.value || hasCompletedWorkflow.value);

// 统一后台活动灯：合并原 polling-indicator / workflow-indicator 两盏灯——用户视角只关心
// "AI 还有事没干完吗"，类型区分（workflow 阶段树 / 普通任务）下沉到 Task 页。
// 文案按信息量分级：阻塞等待 > workflow（附普通任务计数） > 仅普通任务。
const hasBackgroundActivity = computed(() =>
  isAwaitingTaskResult.value || hasActiveWorkflow.value || backgroundTaskCount.value > 0);
const bgActivityClickable = computed(() => !isAwaitingTaskResult.value);
const bgActivityText = computed(() => {
  if (isAwaitingTaskResult.value) return '正在等待后台任务结果...';
  const n = backgroundTaskCount.value;
  if (hasActiveWorkflow.value) return n > 0 ? `Workflow 后台运行中 (+${n})` : 'Workflow 后台运行中';
  return `后台任务运行中 (${n})`;
});

// workflow 运行中 → Chat 底部气泡 → 点击跳 Task 页。
const goToTasks = () => { mode.value = 'tasks'; };

const clearSelection = () => {
  store.clearSelection();
};

onMounted(async () => {
  await checkAgentHealth();
  await gitStore.fetchBranches();
  await fetchProjectPath();
  const restoredWindowSession = await restoreWindowSession();
  if (!restoredWindowSession) {
    initDefaultWindow();
  }

  await restoreHistory(windows.value.map(window => window.id));

  if (restoredWindowSession && activeWindowId.value) {
    await syncWindowActivation(activeWindowId.value);
  }

  await Promise.all([
    startListening(),
    startQuestionListening(),
    startBackgroundTaskListening()
  ]);
});

watch(() => props.panelReady, (newVal) => {
  if (newVal) {
    streamWelcomeMessage();
  }
});

watch([activeWindowId, () => chatMessages.value.length], () => {
  if (shouldAutoScroll.value) {
    nextTick(() => {
      scrollToBottom();
    });
  }
});

watch(
  () => [
    activeTodoProgress.value?.updatedAt,
    activeTodoProgress.value?.isCollapsed,
    activeTodoProgress.value?.todos.length
  ],
  () => {
    nextTick(() => {
      const overlay = todoProgressOverlayRef.value;
      todoProgressOverlayHeight.value = overlay
        ? Math.ceil(overlay.getBoundingClientRect().height)
        : 0;
    });
  }
);

const handleKeydown = (event: KeyboardEvent) => {
  if (event.key === 'Enter' && !event.shiftKey) {
    event.preventDefault();
    sendMessage();
  }
};

const handleSpatialMarkKeydown = (event: KeyboardEvent) => {
  if (!activeDraft.value) return;

  if (event.key === 'Escape') {
    event.preventDefault();
    cancelSpatialMarking();
    return;
  }

  if (event.key === 'Enter') {
    const target = event.target as HTMLElement | null;
    const tagName = target?.tagName?.toLowerCase();
    if (tagName === 'input' || tagName === 'textarea' || target?.isContentEditable) {
      return;
    }

    event.preventDefault();
    void completeDraft();
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

  if (!target.closest('.intent-combo')) {
    isSpatialIntentMenuOpen.value = false;
  }
};

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
  window.addEventListener('keydown', handleSpatialMarkKeydown);
});

onUnmounted(() => {
  window.removeEventListener('click', handleGlobalClick);
  window.removeEventListener('keydown', handleSpatialMarkKeydown);
  todoProgressResizeObserver?.disconnect();
  todoProgressResizeObserver = null;
  stopScreenshotListening();
  stopQuestionListening();
  stopBackgroundTaskListening();
  cleanupHealthCheck();
  cleanupHistoryPolling();
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
      <div class="layer-stream" :class="{ 'stream-tasks': mode === 'tasks' }">

         <!-- View: Chat - Phase 2: 多窗口 v-show 架构 -->
        <div
          v-show="mode === 'chat'"
          class="view-chat-container"
          :style="{ '--todo-progress-space': todoProgressSpace }"
        >
          <!-- 每个窗口独立的聊天容器 -->
          <div
            v-for="win in windows"
            :key="win.id"
            v-show="activeWindowId === win.id"
            class="view-chat window-chat-container"
            :class="{ 'has-todo-progress': hasProgressOverlay }"
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
                                v-if="bubble.type === 'thinking' && !hasFallback('hide-thinking-panel')"
                                :bubble="bubble"
                            />

                            <!-- 文本气泡 - 用户消息用纯文本，AI 消息用 Markdown -->
                            <div class="bubble" v-else-if="bubble.type === 'text' && (bubble.content || bubble.attachments?.length || bubble.sentContext)">
                                <!-- 发送时的上下文快照 chip（用户消息专有，只读） -->
                                <div class="bubble-sent-context"
                                     v-if="msg.role === 'user' && bubble.sentContext">
                                    <span class="sent-chip scope">
                                        <svg class="chip-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"></path>
                                            <polyline points="9 22 9 12 15 12 15 22"></polyline>
                                        </svg>
                                        {{ bubble.sentContext.scope.text }}
                                    </span>
                                    <span class="sent-chip selection" v-if="bubble.sentContext.selection">
                                        <svg class="chip-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path>
                                            <polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline>
                                            <line x1="12" y1="22.08" x2="12" y2="12"></line>
                                        </svg>
                                        {{ bubble.sentContext.selection.text }}
                                    </span>
                                    <span class="sent-chip mark" v-if="bubble.sentContext.spatialMarks">
                                        <svg class="chip-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                                            <path d="M4 4l7.5 16 2.4-6.1L20 11.5 4 4z"></path>
                                            <path d="M13.5 13.5L19 19"></path>
                                        </svg>
                                        <template v-if="bubble.sentContext.spatialMarks.count <= 3 && bubble.sentContext.spatialMarks.labels.length > 0">{{ bubble.sentContext.spatialMarks.labels.join(', ') }}</template>
                                        <template v-else>{{ bubble.sentContext.spatialMarks.count }} 个标记</template>
                                    </span>
                                </div>
                                <!-- 图片显示区域（用户消息专有） -->
                                <div class="bubble-images" v-if="bubble.attachments && bubble.attachments.length > 0">
                                    <img v-for="attachment in bubble.attachments" :key="attachment.attachmentId"
                                         :src="attachment.contentUrl" class="bubble-image" alt="attached image"
                                         @click="openLightbox(attachment.contentUrl)" />
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

          <transition name="todo-panel-fade">
            <!-- 容器=布局壳：两个住户（todo 面板 / 后台活动灯）共享输入框上方锚位，状态机互不相干。
                 todo 在上（前台回合计划，turn 结束收口）、活动灯贴底（跨回合后台状态）。 -->
            <div
              v-if="hasProgressOverlay"
              class="todo-progress-overlay"
              :ref="el => setTodoProgressOverlayRef(el as HTMLElement | null)"
            >
              <TodoProgressPanel
                v-if="activeTodoProgress"
                :progress="activeTodoProgress"
                @toggle="toggleTodoProgressCollapsed"
              />
              <transition name="slide-down">
                <div
                  v-if="hasBackgroundActivity"
                  class="bg-activity-indicator"
                  :class="{ 'is-waiting': isAwaitingTaskResult, 'is-clickable': bgActivityClickable }"
                  :title="bgActivityClickable ? '点击查看 Task 页进度' : undefined"
                  @click="bgActivityClickable && goToTasks()"
                >
                  <span class="bg-activity-dot"></span>
                  <span class="bg-activity-text">{{ bgActivityText }}</span>
                  <span v-if="bgActivityClickable" class="bg-activity-link">查看进度 →</span>
                </div>
              </transition>
            </div>
          </transition>
        </div>

        <!-- View: Tasks (formerly Review) -->
        <div v-show="mode === 'tasks'" class="view-tasks">
            <!-- Agent Activity Monitor (SubAgent tracking) -->
            <TaskSummaryWidget
                v-if="!hasFallback('hide-subtask-activity-panel')"
                :sub-agents="activeSubAgents"
                v-model:expanded="taskWidgetExpanded"
            />

            <!-- Workflow 进度（有活跃/已完成 workflow 时显示，与下方占位 mock 互补） -->
            <WorkflowProgressPanel v-if="hasWorkflowView" />

            <!-- Proposal Carousel（占位 mock：仅在无 workflow 视图时显示） -->
            <div class="carousel-section" v-if="!hasWorkflowView">
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

            <!-- Alert Card (Mock)：仅在无 workflow 视图时显示 -->
            <div class="card alert-card" v-if="!hasWorkflowView">
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

            <!-- 3. Space Mark Button -->
            <button
                class="space-mark-context-btn"
                title="Space Mark"
                :class="{ active: !!activeDraft }"
                @click.stop="toggleSpaceMarkFromButton"
            >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M4 4l7.5 16 2.4-6.1L20 11.5 4 4z"></path>
                    <path d="M13.5 13.5L19 19"></path>
                </svg>
            </button>

            <!-- 4. Add Context Button -->
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
            <!-- WP-Web: RateLimit 全局徽章(消费后端 runtime.rate_limit event;allowed 时不渲染) -->
            <RateLimitBanner />
            <div class="queued-message-card" v-if="activeQueuedMessage">
              <div class="queued-message-main" :title="`等待发送：${queuedMessagePreview}`">
                <span class="queued-message-status">等待发送</span>
                <span class="queued-message-text">{{ queuedMessagePreview }}</span>
                <span class="queued-message-meta" v-if="queuedAttachmentCount > 0" title="附件">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"></path>
                  </svg>
                  <span>{{ queuedAttachmentCount }}</span>
                </span>
                <span class="queued-message-meta" v-if="queuedSpatialMarkCount > 0" title="Space Mark">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <path d="M4 4l7.5 16 2.4-6.1L20 11.5 4 4z"></path>
                    <path d="M13.5 13.5L19 19"></path>
                  </svg>
                  <span>{{ queuedSpatialMarkCount }}</span>
                </span>
              </div>
              <div class="queued-message-actions">
                <button class="queued-action" title="立即发送" @click.stop="sendQueuedMessageNow">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">
                    <line x1="12" y1="19" x2="12" y2="5"></line>
                    <polyline points="5 12 12 5 19 12"></polyline>
                  </svg>
                </button>
                <button
                  class="queued-action"
                  title="撤回编辑"
                  :disabled="!canRestoreQueuedMessage"
                  @click.stop="handleRestoreQueuedMessage"
                >
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M9 14L4 9l5-5"></path>
                    <path d="M4 9h9a7 7 0 0 1 7 7v3"></path>
                  </svg>
                </button>
                <button class="queued-action danger" title="删除" @click.stop="deleteQueuedMessage">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <polyline points="3 6 5 6 21 6"></polyline>
                    <path d="M19 6l-1 14H6L5 6"></path>
                    <path d="M10 11v6"></path>
                    <path d="M14 11v6"></path>
                    <path d="M9 6V4h6v2"></path>
                  </svg>
                </button>
              </div>
            </div>
            <div class="pending-spatial-marks" v-if="pendingSpatialMarks.length > 0">
              <div class="pending-spatial-mark" v-for="mark in pendingSpatialMarks" :key="mark.id">
                <div class="pending-spatial-main">
                  <input
                    class="pending-spatial-label"
                    :value="mark.label"
                    :disabled="activeWindow?.isStreaming"
                    @input="updatePendingSpatialLabel(mark.id, getEventValue($event))"
                  />
                  <span>{{ getSpatialMarkZoneName(mark.zoneId) }} · {{ mark.geometry.length }} geometry</span>
                </div>
                <input
                  class="pending-spatial-description"
                  v-if="mark.description"
                  :value="mark.description"
                  :disabled="activeWindow?.isStreaming"
                  @input="updatePendingSpatialDescription(mark.id, getEventValue($event))"
                />
                <div class="pending-spatial-actions">
                  <button
                    class="edit-spatial-mark"
                    title="查看/修改"
                    @click.stop="editPendingMark(mark.id)"
                    :disabled="activeWindow?.isStreaming"
                  >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M12 20h9"></path>
                      <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z"></path>
                    </svg>
                  </button>
                  <button class="remove-spatial-mark" title="移除" @click.stop="removePendingMark(mark.id)" :disabled="activeWindow?.isStreaming">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <line x1="18" y1="6" x2="6" y2="18"></line>
                      <line x1="6" y1="6" x2="18" y2="18"></line>
                    </svg>
                  </button>
                </div>
              </div>
            </div>

            <!-- Pending Attachments Preview -->
            <div class="pending-attachments" v-if="pendingAttachments.length > 0">
              <div class="attachment-item" v-for="(attachment, idx) in pendingAttachments" :key="attachment.attachmentId">
                <img :src="attachment.contentUrl" class="attachment-thumbnail" alt="attachment" @click="openLightbox(attachment.contentUrl)" />
                <button class="remove-attachment" @click.stop="removePendingAttachment(idx)">
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
              :placeholder="isLoading ? '要求后续变更' : agentStatus === 'connected' ? '你好' : agentStatus === 'connecting' ? '正在连接 Agent...' : 'Agent 未连接'"
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
                    <div v-if="!hasFallback('hide-thinking-panel')" class="control-pill-wrapper thinking" :class="{ open: isThinkingMenuOpen, disabled: isConfigLocked }">
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
                      v-if="isLoading && !hasInputSendableContent"
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
                      :disabled="!hasInputSendableContent || agentStatus !== 'connected' || (isLoading && !!activeQueuedMessage)"
                      :title="isLoading && activeQueuedMessage ? '已有等待发送消息' : '发送'"
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

  <transition name="spatial-panel-fade">
    <aside
      v-if="activeDraft && props.panelReady && !showScreenshotOverlay"
      class="spatial-property-panel"
    >
      <div class="panel-header">
        <button class="icon-btn back-btn" @click.stop="cancelSpatialMarking" title="Close Space Mark" :disabled="activeWindow?.isStreaming">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <line x1="19" y1="12" x2="5" y2="12"></line>
            <polyline points="12 19 5 12 12 5"></polyline>
          </svg>
        </button>

        <div class="title">SPACE MARK</div>

        <span class="header-spacer" aria-hidden="true"></span>
      </div>

      <div class="panel-content">
        <div class="prop-list">
          <div class="prop-row">
            <span class="label">Scope</span>
            <span class="value readonly-value">{{ draftScopeDisplayText }}</span>
          </div>

          <div class="prop-row cell-row">
            <label class="label" for="spatial-cell-size">Cell</label>
            <div class="cell-stepper">
              <button
                class="stepper-btn"
                title="Decrease cell size"
                :disabled="activeDraft.isCompleting || activeWindow?.isStreaming || activeDraft.cellSize <= 50"
                @click.stop="setDraftCellSize(activeDraft.cellSize - 50)"
              >-</button>
              <input
                id="spatial-cell-size"
                class="cell-stepper-input"
                type="number"
                min="50"
                step="50"
                :value="activeDraft.cellSize"
                :disabled="activeDraft.isCompleting || activeWindow?.isStreaming"
                @change="setDraftCellSize(Number(getEventValue($event)))"
              />
              <button
                class="stepper-btn"
                title="Increase cell size"
                :disabled="activeDraft.isCompleting || activeWindow?.isStreaming"
                @click.stop="setDraftCellSize(activeDraft.cellSize + 50)"
              >+</button>
            </div>
          </div>

          <div class="prop-row intent-row">
            <label class="label" for="spatial-label">Tag</label>
            <div class="intent-combo">
              <input
                ref="spatialLabelInputRef"
                id="spatial-label"
                class="intent-input"
                :value="activeDraft.label"
                placeholder="Tag"
                autocomplete="off"
                autocapitalize="off"
                autocorrect="off"
                spellcheck="false"
                aria-autocomplete="none"
                :disabled="activeDraft.isCompleting || activeWindow?.isStreaming"
                @input="setDraftLabel(getEventValue($event))"
              />
              <button
                class="intent-menu-btn"
                title="Select preset intent"
                :class="{ open: isSpatialIntentMenuOpen }"
                :disabled="activeDraft.isCompleting || activeWindow?.isStreaming"
                @click.stop="isSpatialIntentMenuOpen = !isSpatialIntentMenuOpen"
              >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                  <polyline points="6 9 12 15 18 9"></polyline>
                </svg>
              </button>
              <div class="intent-menu" v-if="isSpatialIntentMenuOpen">
                <button
                  v-for="intent in spatialIntentOptions"
                  :key="intent"
                  class="intent-option"
                  :class="{ active: activeDraft.label === intent }"
                  @click.stop="selectSpatialIntent(intent)"
                >
                  {{ intent }}
                </button>
                <div class="intent-actions">
                  <button
                    class="intent-action-btn"
                    title="添加当前 Tag 到预设"
                    :disabled="!canAddSpatialIntent"
                    @click.stop="addSpatialIntentOption"
                  >
                    +
                  </button>
                  <button
                    class="intent-action-btn"
                    title="删除当前 Tag 预设"
                    :disabled="!canDeleteSpatialIntent"
                    @click.stop="deleteSpatialIntentOption"
                  >
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                      <path d="M3 6h18"></path>
                      <path d="M8 6V4h8v2"></path>
                      <path d="M19 6l-1 14H6L5 6"></path>
                    </svg>
                  </button>
                </div>
              </div>
            </div>
          </div>

          <div class="prop-row textarea-row">
            <label class="label" for="spatial-description">Description</label>
            <textarea
              id="spatial-description"
              class="value spatial-input spatial-description"
              :value="activeDraft.description"
              placeholder="Description"
              rows="3"
              :disabled="activeDraft.isCompleting || activeWindow?.isStreaming"
              @input="setDraftDescription(getEventValue($event))"
            ></textarea>
          </div>

          <div class="prop-row">
            <span class="label">Selected</span>
            <span class="value readonly-value">{{ activeDraft.selectedCells.length }} cells</span>
          </div>
        </div>

        <div class="spatial-error" v-if="activeDraft.error">{{ activeDraft.error }}</div>

        <div class="panel-actions">
          <button @click.stop="clearDraftSelection" :disabled="activeDraft.isCompleting || activeWindow?.isStreaming">Clear</button>
          <button class="primary" @click.stop="completeDraft" :disabled="activeDraft.isCompleting || activeWindow?.isStreaming">
            {{ activeDraft.isCompleting ? (activeDraft.editingMarkId ? 'Updating...' : 'Merging...') : (activeDraft.editingMarkId ? 'Update' : 'Done') }}
          </button>
        </div>
      </div>
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

/* Task 模式：layer-stream 收口——底部留外边距 + 下边框，不顶到面板底；四角保持直角（不倒角）。
   Chat 模式下方有 layer-footer(composer)承接底边，故不加；仅 Task 模式生效。 */
.layer-stream.stream-tasks {
    margin-bottom: 16px;
    border-bottom: 1px solid var(--border-dim);
    border-radius: 0; /* 四角直角，去倒角 */
}

.view-tasks {
    display: flex;
    flex-direction: column;
    gap: 16px;
}

/* Phase 2: 多窗口聊天容器 */
.view-chat-container {
    --todo-progress-space: 0px;
    position: relative;
    flex: 1;
    width: 100%;
    height: 100%;
    min-height: 0;
    overflow: hidden;
}

.window-chat-container {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: var(--todo-progress-space);
    overflow-y: auto;
    overflow-x: hidden;
    transition: bottom 0.18s ease;

    &.has-todo-progress {
        padding-bottom: 8px;
    }
}

.todo-progress-overlay {
    position: absolute;
    left: 0;
    right: 18px;
    bottom: 0;
    z-index: 8;
    pointer-events: none;
    display: flex;
    flex-direction: column;
    gap: 8px;

    :deep(.todo-progress-panel) {
        margin: 0;
        pointer-events: auto;
    }
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

            // === 用户消息：发送时上下文快照 chip（只读） ===
            // 视觉差异化于输入区 .context-chip：更小、更暗、无 close、无 hover
            .bubble-sent-context {
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
                margin-bottom: 6px;
                pointer-events: none; // 全行只读，不响应任何鼠标事件

                .sent-chip {
                    display: inline-flex;
                    align-items: center;
                    gap: 4px;
                    padding: 2px 6px;
                    border-radius: 4px;
                    font-size: 0.65rem;
                    line-height: 1.2;
                    background: rgba(255, 255, 255, 0.14);
                    color: rgba(255, 255, 255, 0.85);
                    border: 1px solid rgba(255, 255, 255, 0.16);
                    white-space: nowrap;
                    max-width: 100%;
                    overflow: hidden;
                    text-overflow: ellipsis;

                    .chip-icon {
                        width: 10px;
                        height: 10px;
                        flex-shrink: 0;
                    }

                    // 三类微差异化：scope 描边略亮、selection 背景更深、mark 偏中性
                    &.scope {
                        border-color: rgba(255, 255, 255, 0.28);
                    }
                    &.selection {
                        background: rgba(0, 0, 0, 0.18);
                    }
                    &.mark {
                        border-style: dashed;
                    }
                }
            }

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

.todo-panel-fade-enter-active,
.todo-panel-fade-leave-active {
    transition: opacity 0.18s ease, transform 0.18s ease;
}

.todo-panel-fade-enter-from,
.todo-panel-fade-leave-to {
    opacity: 0;
    transform: translateY(6px);
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

    .space-mark-context-btn,
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

        &:hover:not(:disabled) {
            border-color: var(--text-secondary);
            color: var(--text-secondary);
            background: var(--surface-dim);
        }

        &:disabled {
            opacity: 0.45;
            cursor: not-allowed;
        }
    }

    .space-mark-context-btn.active {
        border-style: solid;
        border-color: rgba(10, 132, 255, 0.45);
        background: rgba(10, 132, 255, 0.12);
        color: var(--accent-blue);
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

/* --- 统一后台活动灯（合并原 polling-indicator / workflow-indicator）---
 * 默认态 = workflow 蓝（可点击跳 Task 页）；.is-waiting = 主控阻塞等结果，amber 警示色、不可点。 */
.bg-activity-indicator {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 8px 16px;
    margin: 0;
    background: rgba(79, 172, 254, 0.1);
    border-radius: 8px;
    border: 1px solid rgba(79, 172, 254, 0.3);
    color: rgba(79, 172, 254, 0.95);
    font-size: 0.8rem;
    cursor: default;
    pointer-events: auto;
    transition: background 0.2s;

    &.is-clickable { cursor: pointer; }
    &.is-clickable:hover { background: rgba(79, 172, 254, 0.18); }

    .bg-activity-dot {
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background: rgba(79, 172, 254, 0.95);
        animation: pulse-polling 1.5s ease-in-out infinite;
    }

    .bg-activity-text { font-weight: 500; }

    .bg-activity-link {
        margin-left: auto;
        font-size: 0.72rem;
        font-weight: 600;
        opacity: 0.85;
    }

    &.is-waiting {
        background: rgba(251, 191, 36, 0.1);
        border-color: rgba(251, 191, 36, 0.3);
        color: rgba(251, 191, 36, 0.9);

        .bg-activity-dot { background: rgba(251, 191, 36, 0.9); }
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

.spatial-panel-fade-enter-active,
.spatial-panel-fade-leave-active {
    transition: opacity 0.3s ease, transform 0.3s ease;
}

.spatial-panel-fade-enter-from,
.spatial-panel-fade-leave-to {
    opacity: 0;
    transform: translateY(20px) scale(0.95);
}

.spatial-property-panel {
    position: fixed;
    left: 24px;
    top: 120px;
    width: min(320px, calc(100vw - 48px));
    max-height: calc(100vh - 144px);

    background: var(--glass-bg);
    backdrop-filter: var(--glass-blur);
    -webkit-backdrop-filter: var(--glass-blur);

    border: 1px solid rgba(255, 255, 255, 0.2);
    border-radius: 20px;

    background-image: var(--glass-glare), linear-gradient(to bottom, var(--glass-bg), var(--glass-bg));
    box-shadow:
        0 12px 40px rgba(0, 0, 0, 0.4),
        0 0 0 1px rgba(255, 255, 255, 0.1) inset,
        0 0 20px rgba(255, 255, 255, 0.15);

    display: flex;
    flex-direction: column;
    overflow: hidden;
    z-index: 150;

    .panel-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        padding: 10px 14px;
        border-bottom: 1px solid var(--border-subtle);
        flex-shrink: 0;

        .title {
            font-weight: 600;
            font-size: 0.84rem;
            color: var(--text-primary);
            letter-spacing: 0.5px;
        }

        .icon-btn,
        .header-spacer {
            width: 26px;
            height: 26px;
            flex-shrink: 0;
        }

        .icon-btn {
            background: transparent;
            border: none;
            color: var(--text-secondary);
            cursor: pointer;
            padding: 4px;
            border-radius: 50%;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s ease;

            svg {
                width: 18px;
                height: 18px;
            }

            &:hover:not(:disabled) {
                background: var(--surface-hover);
                color: var(--text-primary);
            }

            &:disabled {
                opacity: 0.45;
                cursor: not-allowed;
            }
        }
    }

    .panel-content {
        flex: 1;
        overflow-y: auto;
        padding: 10px 14px 12px;

        &::-webkit-scrollbar {
            width: 4px;
        }

        &::-webkit-scrollbar-track {
            background: transparent;
        }

        &::-webkit-scrollbar-thumb {
            background: var(--border-strong);
            border-radius: 2px;
        }
    }

    .prop-list {
        display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .prop-row {
        display: grid;
        grid-template-columns: 74px minmax(0, 1fr);
        align-items: center;
        gap: 8px;
        font-size: 0.8rem;
        line-height: 1.4;

        &.textarea-row {
            grid-template-columns: 1fr;
            align-items: flex-start;
            gap: 6px;
        }

        .label {
            color: var(--text-secondary);
            max-width: 100%;
            overflow: hidden;
            text-overflow: ellipsis;
            white-space: nowrap;
        }

        .value {
            color: var(--text-primary);
            text-align: left;
            min-width: 0;
            width: 100%;
            word-break: break-word;
            white-space: pre-wrap;
            font-family: var(--font-mono);
        }
    }

    .readonly-value {
        opacity: 0.95;
        text-align: left;
    }

    .spatial-input {
        width: 100%;
        box-sizing: border-box;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.16);
        color: var(--text-primary);
        font: inherit;
        font-size: 0.78rem;
        line-height: 1.35;
        padding: 5px 8px;
        outline: none;

        &:focus {
            border-color: rgba(10, 132, 255, 0.55);
            background: rgba(0, 0, 0, 0.22);
        }

        &:disabled {
            opacity: 0.55;
            cursor: not-allowed;
        }
    }

    .spatial-description {
        min-height: 54px;
        resize: vertical;
        text-align: left;
    }

    .intent-combo {
        position: relative;
        width: 116px;
        min-width: 116px;
        max-width: 116px;
        height: 30px;
        justify-self: start;
        display: inline-flex;
        align-items: stretch;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.16);
        font-family: var(--font-mono);

        &:focus-within {
            border-color: rgba(10, 132, 255, 0.55);
            background: rgba(0, 0, 0, 0.22);
        }
    }

    .intent-input {
        width: 86px;
        min-width: 0;
        height: 100%;
        border: none;
        background: transparent;
        color: var(--text-primary);
        font: inherit;
        font-size: 0.78rem;
        line-height: 1.35;
        text-align: center;
        outline: none;
        padding: 0 8px;

        &::placeholder {
            color: var(--text-tertiary);
        }

        &:disabled {
            opacity: 0.55;
            cursor: not-allowed;
        }
    }

    .intent-menu-btn {
        width: 30px;
        height: 100%;
        border: none;
        border-left: 1px solid rgba(255, 255, 255, 0.08);
        background: transparent;
        color: var(--text-secondary);
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        padding: 0;

        svg {
            width: 14px;
            height: 14px;
            transition: transform 0.2s ease;
        }

        &.open svg {
            transform: rotate(180deg);
        }

        &:hover:not(:disabled) {
            background: rgba(255, 255, 255, 0.08);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.45;
            cursor: not-allowed;
        }
    }

    .intent-menu {
        position: absolute;
        left: 0;
        top: calc(100% + 4px);
        width: 100%;
        z-index: 20;
        padding: 4px;
        display: flex;
        flex-direction: column;
        gap: 2px;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 8px;
        background: rgba(20, 20, 20, 0.96);
        box-shadow: 0 10px 28px rgba(0, 0, 0, 0.45);

        button {
            border: none;
            border-radius: 5px;
            background: transparent;
            color: var(--text-secondary);
            cursor: pointer;
            font: inherit;
            font-size: 0.76rem;
            text-align: left;
            padding: 5px 7px;

            &:hover,
            &.active {
                background: rgba(255, 255, 255, 0.08);
                color: var(--text-primary);
            }
        }

        .intent-actions {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 4px;
            margin-top: 3px;
            padding-top: 4px;
            border-top: 1px solid rgba(255, 255, 255, 0.08);
        }

        .intent-action-btn {
            height: 24px;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            text-align: center;

            svg {
                width: 13px;
                height: 13px;
            }

            &:disabled {
                opacity: 0.35;
                cursor: not-allowed;
            }

            &:hover:disabled {
                background: transparent;
                color: var(--text-secondary);
            }
        }
    }

    .cell-stepper {
        width: 116px;
        min-width: 116px;
        max-width: 116px;
        height: 28px;
        justify-self: start;
        display: inline-flex;
        align-items: center;
        overflow: hidden;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.16);
        font-family: var(--font-mono);

        &:focus-within {
            border-color: rgba(10, 132, 255, 0.55);
            background: rgba(0, 0, 0, 0.22);
        }
    }

    .stepper-btn {
        width: 28px;
        height: 100%;
        border: none;
        background: transparent;
        color: var(--text-secondary);
        cursor: pointer;
        font: inherit;
        font-size: 0.85rem;
        line-height: 1;
        padding: 0;

        &:hover:not(:disabled) {
            background: rgba(255, 255, 255, 0.08);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.35;
            cursor: not-allowed;
        }
    }

    .cell-stepper-input {
        width: 60px;
        height: 100%;
        min-width: 0;
        border: none;
        border-left: 1px solid rgba(255, 255, 255, 0.08);
        border-right: 1px solid rgba(255, 255, 255, 0.08);
        background: transparent;
        color: var(--text-primary);
        font: inherit;
        font-size: 0.78rem;
        font-weight: 600;
        text-align: center;
        outline: none;
        padding: 0 4px;
        appearance: textfield;

        &::-webkit-outer-spin-button,
        &::-webkit-inner-spin-button {
            margin: 0;
            appearance: none;
        }

        &:disabled {
            opacity: 0.55;
            cursor: not-allowed;
        }
    }

    input[type='number'].spatial-input {
        text-align: right;
    }

    .spatial-error {
        color: #ffb4a8;
        font-size: 0.74rem;
        margin-top: 10px;
    }

    .panel-actions {
        display: flex;
        justify-content: flex-end;
        gap: 8px;
        margin-top: 10px;

        button {
            border: 1px solid rgba(255, 255, 255, 0.14);
            border-radius: 6px;
            background: rgba(255, 255, 255, 0.06);
            color: var(--text-secondary);
            cursor: pointer;
            font-size: 0.74rem;
            padding: 5px 9px;

            &.primary {
                background: rgba(10, 132, 255, 0.18);
                border-color: rgba(10, 132, 255, 0.42);
                color: var(--accent-blue);
            }

            &:hover:not(:disabled) {
                background: rgba(255, 255, 255, 0.1);
                color: var(--text-primary);
            }

            &:disabled {
                opacity: 0.55;
                cursor: not-allowed;
            }
        }
    }
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

    .spatial-mark-panel {
        margin: 10px 10px 6px;
        padding: 10px;
        border-radius: 8px;
        border: 1px solid rgba(10, 132, 255, 0.28);
        background: rgba(10, 132, 255, 0.08);
        display: flex;
        flex-direction: column;
        gap: 8px;
    }

    .spatial-mark-header,
    .spatial-mark-footer,
    .pending-spatial-main {
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
    }

    .spatial-mark-header {
        color: var(--text-primary);
        font-size: 0.78rem;
        font-weight: 600;
    }

    .spatial-mark-row {
        display: grid;
        grid-template-columns: minmax(0, 1fr) 88px;
        gap: 8px;
    }

    .spatial-input {
        min-width: 0;
        width: 100%;
        box-sizing: border-box;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.16);
        color: var(--text-primary);
        font: inherit;
        font-size: 0.78rem;
        line-height: 1.35;
        padding: 7px 8px;
        outline: none;

        &:focus {
            border-color: rgba(10, 132, 255, 0.55);
            background: rgba(0, 0, 0, 0.22);
        }

        &:disabled {
            opacity: 0.55;
            cursor: not-allowed;
        }
    }

    .spatial-scope-label {
        min-width: 0;
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        background: rgba(0, 0, 0, 0.16);
        color: var(--text-primary);
        font-size: 0.78rem;
        line-height: 1.35;
        padding: 7px 8px;
    }

    .spatial-description {
        max-height: 72px;
        resize: vertical;
        padding: 7px 8px;
        background: rgba(0, 0, 0, 0.16);
        border: 1px solid rgba(255, 255, 255, 0.12);
        border-radius: 6px;
        color: var(--text-primary);
    }

    .spatial-mark-footer {
        color: var(--text-secondary);
        font-size: 0.74rem;
    }

    .spatial-actions {
        display: flex;
        gap: 6px;

        button {
            border: 1px solid rgba(255, 255, 255, 0.14);
            border-radius: 6px;
            background: rgba(255, 255, 255, 0.06);
            color: var(--text-secondary);
            cursor: pointer;
            font-size: 0.74rem;
            padding: 5px 8px;

            &.primary {
                background: rgba(10, 132, 255, 0.18);
                border-color: rgba(10, 132, 255, 0.42);
                color: var(--accent-blue);
            }

            &:disabled {
                opacity: 0.55;
                cursor: not-allowed;
            }
        }
    }

    .spatial-icon-btn,
    .remove-spatial-mark {
        width: 22px;
        height: 22px;
        border: none;
        border-radius: 6px;
        background: rgba(255, 255, 255, 0.06);
        color: var(--text-tertiary);
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        padding: 0;

        svg {
            width: 13px;
            height: 13px;
        }

        &:hover:not(:disabled) {
            background: rgba(255, 255, 255, 0.1);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.45;
            cursor: not-allowed;
        }
    }

    .spatial-error {
        color: #ffb4a8;
        font-size: 0.74rem;
    }

    .queued-message-card {
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        align-items: center;
        gap: 8px;
        margin: 8px 10px 2px;
        padding: 7px 8px;
        border-radius: 8px;
        background: rgba(255, 255, 255, 0.055);
        border: 1px solid rgba(255, 255, 255, 0.1);
        box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.04);
    }

    .queued-message-main {
        min-width: 0;
        display: flex;
        align-items: center;
        gap: 6px;
        color: rgba(255, 255, 255, 0.62);
        font-size: 0.76rem;
    }

    .queued-message-status {
        flex: 0 0 auto;
        color: rgba(255, 255, 255, 0.42);
        font-size: 0.68rem;
        font-weight: 500;
        white-space: nowrap;
    }

    .queued-message-text {
        min-width: 0;
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
    }

    .queued-message-meta {
        flex: 0 0 auto;
        display: inline-flex;
        align-items: center;
        gap: 3px;
        color: rgba(255, 255, 255, 0.42);
        font-size: 0.68rem;

        svg {
            width: 12px;
            height: 12px;
        }
    }

    .queued-message-actions {
        display: flex;
        align-items: center;
        gap: 3px;
    }

    .queued-action {
        width: 24px;
        height: 24px;
        border: none;
        border-radius: 6px;
        background: transparent;
        color: rgba(255, 255, 255, 0.46);
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        padding: 0;
        transition: background 0.15s ease, color 0.15s ease;

        svg {
            width: 14px;
            height: 14px;
        }

        &:hover:not(:disabled) {
            background: rgba(255, 255, 255, 0.09);
            color: rgba(255, 255, 255, 0.82);
        }

        &.danger:hover:not(:disabled) {
            color: #ff8a80;
            background: rgba(255, 59, 48, 0.12);
        }

        &:disabled {
            opacity: 0.32;
            cursor: not-allowed;
        }
    }

    .pending-spatial-marks {
        display: flex;
        flex-direction: column;
        gap: 4px;
        padding: 8px 10px 4px;
    }

    .pending-spatial-mark {
        position: relative;
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        column-gap: 6px;
        row-gap: 2px;
        padding: 6px 7px;
        border-radius: 7px;
        background: rgba(52, 199, 89, 0.08);
        border: 1px solid rgba(52, 199, 89, 0.22);
    }

    .pending-spatial-main {
        grid-column: 1;
        min-width: 0;

        span {
            color: var(--text-tertiary);
            font-size: 0.66rem;
            white-space: nowrap;
        }
    }

    .pending-spatial-label,
    .pending-spatial-description {
        min-width: 0;
        border: none;
        outline: none;
        background: transparent;
        color: var(--text-primary);
        font: inherit;
    }

    .pending-spatial-label {
        font-size: 0.76rem;
        font-weight: 600;
    }

    .pending-spatial-description {
        grid-column: 1;
        color: var(--text-secondary);
        font-size: 0.68rem;
        line-height: 1.2;
    }

    .pending-spatial-actions {
        grid-column: 2;
        grid-row: 1 / span 2;
        align-self: center;
        justify-self: end;
        display: flex;
        align-items: center;
        gap: 4px;
    }

    .edit-spatial-mark {
        width: 22px;
        height: 22px;
        border: 1px solid rgba(52, 199, 89, 0.28);
        border-radius: 6px;
        background: rgba(52, 199, 89, 0.08);
        color: rgba(210, 255, 222, 0.78);
        display: flex;
        align-items: center;
        justify-content: center;
        cursor: pointer;
        padding: 0;

        svg {
            width: 12px;
            height: 12px;
        }

        &:hover:not(:disabled) {
            background: rgba(52, 199, 89, 0.14);
            border-color: rgba(52, 199, 89, 0.4);
            color: var(--text-primary);
        }

        &:disabled {
            opacity: 0.45;
            cursor: not-allowed;
        }
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
