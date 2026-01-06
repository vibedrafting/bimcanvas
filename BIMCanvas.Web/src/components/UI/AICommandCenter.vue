<script setup lang="ts">
import { ref, onMounted, nextTick, computed, watch, defineProps } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { useGitStore } from '../../stores/gitStore';
import { storeToRefs } from 'pinia';
import BranchCheckoutConfirmDialog from './Ribbon/BranchCheckoutConfirmDialog.vue';

// Props from parent (MainLayout)
const props = defineProps<{
  panelReady?: boolean;
}>();

// API Configuration
const AGENT_API_BASE = 'http://127.0.0.1:8765';

const panelWidth = ref(360);
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

// Chat state
interface ChatMessage {
  role: 'user' | 'ai';
  content: string;
  thinking?: string;
  isStreaming?: boolean;
  startTime?: number;
  endTime?: number;
  thinkingDuration?: string;
}
const chatMessages = ref<ChatMessage[]>([]);
const inputMessage = ref('');
const isLoading = ref(false);
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

// Mock Data for Tasks (unchanged)
const tasks = ref([
  { id: 1, name: "Living Room 'Ultimate Storage' Design", progress: 45, status: 'Generating geometry...' },
  { id: 2, name: "Living Room 'Flow Priority' Design", progress: 30, status: 'Calculating paths...' },
  { id: 3, name: "Living Room 'Minimalist White' Design", progress: 10, status: 'Initializing...' }
]);

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
    
    chatMessages.value.push({
        role: 'ai',
        content: '',
        isStreaming: true
    });
    
    const msgIndex = chatMessages.value.length - 1;

    // Simulate typing effect
    let i = 0;
     const interval = setInterval(() => {
         if (i < welcomeText.length) {
             chatMessages.value[msgIndex].content += welcomeText[i];
             i++;
             scrollToBottom();
         } else {
             clearInterval(interval);
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
    } else {
      agentStatus.value = 'disconnected';
    }
  } catch {
    agentStatus.value = 'disconnected';
  }
};

const sendMessage = async () => {
  const message = inputMessage.value.trim();
  if (!message || isLoading.value) return;

  // Add user message to chat
  chatMessages.value.push({ role: 'user', content: message });
  inputMessage.value = '';
  isLoading.value = true;

  // Force scroll to bottom when user sends message
  shouldAutoScroll.value = true;
  await nextTick();
  scrollToBottom({ force: true });
  // Additional scroll attempts to handle DOM rendering delays
  requestAnimationFrame(() => scrollToBottom({ force: true }));
  setTimeout(() => scrollToBottom({ force: true }), 50);
  setTimeout(() => scrollToBottom({ force: true }), 150);

  // Add placeholder AI message for streaming
  const aiMessageIndex = chatMessages.value.length;
  chatMessages.value.push({ 
    role: 'ai', 
    content: '', 
    thinking: '', 
    isStreaming: true,
    startTime: Date.now(),
    thinkingDuration: undefined // Start as undefined, will be set when text starts
  });

  // Start thinking timer - updates every second while streaming
  const timerInterval = setInterval(() => {
    const msg = chatMessages.value[aiMessageIndex];
    // Only update if still streaming and no content yet (still in thinking phase)
    if (msg && msg.isStreaming && !msg.content && msg.thinking) {
        const duration = Math.round((Date.now() - (msg.startTime || Date.now())) / 1000);
        // Update the timer display for live thinking phase
        msg.thinkingDuration = duration + 's';
    } else {
        clearInterval(timerInterval);
    }
  }, 1000);

  try {
    const response = await fetch(`${AGENT_API_BASE}/api/chat/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        projectPath: currentProjectPath.value,
        message: message
      })
    });

    if (!response.ok) {
      throw new Error(`HTTP error: ${response.status}`);
    }

    // Read SSE stream
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
            } else if (parsed.type === 'text' || parsed.type === 'text_complete') {
                 const msg = chatMessages.value[aiMessageIndex];
                 
                 // Auto-collapse thinking when text starts (only do this once)
                 if (msg.thinking && expandedThinking.value[aiMessageIndex] === true) {
                     // Calculate final thinking duration
                     msg.endTime = Date.now();
                     const duration = Math.round((msg.endTime - (msg.startTime || msg.endTime)) / 1000);
                     msg.thinkingDuration = duration + 's';
                     
                     // Collapse the thinking section
                     expandedThinking.value = { ...expandedThinking.value, [aiMessageIndex]: false };
                     
                      // Force scroll after collapse
                      nextTick(() => scrollToBottom());
                  }
                 
                 if (parsed.type === 'text_complete') {
                    msg.content = parsed.content;
                 } else {
                    msg.content += parsed.content;
                 }
            } else if (parsed.error) {
              currentMsg.content = `Error: ${parsed.error}`;
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
    chatMessages.value[aiMessageIndex].isStreaming = false;
    agentStatus.value = 'connected';

  } catch (error) {
    console.error('Chat error:', error);
    const currentMsg = chatMessages.value[aiMessageIndex];
    if (currentMsg && !currentMsg.content) {
      currentMsg.content = 'Sorry, I encountered an error. Please check if the Agent server is running.';
    }
    currentMsg.isStreaming = false;
    agentStatus.value = 'disconnected';
  } finally {
     isLoading.value = false;
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
const currentModel = ref('Claude 3.5 Sonnet');
const currentThinking = ref('Off');
const isModelMenuOpen = ref(false);
const isThinkingMenuOpen = ref(false);

const models = [
  { id: 'claude-3-5-sonnet', label: 'Claude 3.5 Sonnet' },
  { id: 'gemini-1-5-pro', label: 'Gemini 1.5 Pro' },
  { id: 'gpt-4o', label: 'GPT-4o' }
];

const thinkingLevels = [
  { id: 'off', label: 'Off' },
  { id: 'low', label: 'Low' },
  { id: 'high', label: 'High' }
];

const selectModel = (model: any) => {
  currentModel.value = model.label;
  isModelMenuOpen.value = false;
};

const selectThinking = (level: any) => {
  currentThinking.value = level.label;
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

const handleContextSelect = (type: string, item: any) => {
  console.log('Selected context:', type, item);
  
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

onMounted(() => {
  window.addEventListener('click', handleGlobalClick);
});

import { onUnmounted } from 'vue';
onUnmounted(() => {
  window.removeEventListener('click', handleGlobalClick);
});

import TaskSummaryWidget from './TaskSummaryWidget.vue';
import MarkdownText from './base/MarkdownText.vue';
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
          <button :class="{ active: mode === 'tasks' }" @click="mode = 'tasks'">Tasks</button>
        </div>
      </div>

      <!-- Layer 2: Intelligence Stream -->
      <div class="layer-stream" ref="chatScrollRef" @scroll="handleChatScroll">
         
         <!-- View: Chat -->
        <div v-if="mode === 'chat'" class="view-chat">

            <!-- Task Summary Widget -->
            <TaskSummaryWidget :tasks="tasks" />

            <!-- Actual Chat History -->
            <template v-for="(msg, index) in chatMessages" :key="index">
                <div class="chat-message" :class="[msg.role === 'user' ? 'user' : 'ai', { streaming: msg.isStreaming }]">
                    <div v-if="msg.role === 'ai'" class="avatar">AI</div>
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
                                    <template v-if="msg.isStreaming && !msg.content">
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
                        <!-- Main Content Bubble -->
                        <div 
                            class="bubble" 
                            :class="{ 
                                empty: !msg.content && msg.isStreaming,
                                generating: msg.isStreaming && !msg.thinking && !msg.content
                            }"
                            v-if="msg.content || (!msg.thinking && msg.isStreaming)"
                        >
                            <MarkdownText v-if="msg.content" :content="msg.content" />
                            <span v-else-if="msg.isStreaming && !msg.thinking" class="generating-indicator">
                                Generating<span class="dot">.</span><span class="dot">.</span><span class="dot">.</span>
                            </span>
                        </div>
                    </div>
                </div>
            </template>

             <!-- Note: Loading state now handled within streaming messages -->
            <div ref="chatBottomRef" class="chat-bottom-anchor"></div>
         </div>

        <!-- View: Tasks (formerly Review) -->
        <div v-else-if="mode === 'tasks'" class="view-tasks">
            <!-- Task Summary Widget (Replaces old Task Cards) -->
            <TaskSummaryWidget :tasks="tasks" />

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
                            <div class="menu-label">Add Context</div>
                            
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
                                <div class="menu-label">Select Zone</div>
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
                                <div class="menu-label">Apply Regulation</div>
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
                                <div class="menu-label">Attach File</div>
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
                                <div class="menu-item">
                                    <span class="icon">
                                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                            <rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect>
                                            <circle cx="8.5" cy="8.5" r="1.5"></circle>
                                            <polyline points="21 15 16 10 5 21"></polyline>
                                        </svg>
                                    </span>
                                    <span class="item-text">Upload Image</span>
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
                                    <span class="item-text">Upload File</span>
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
                            <span class="text">{{ currentModel }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isModelMenuOpen">
                                <div class="menu-header">Model</div>
                                <div 
                                    v-for="m in models" 
                                    :key="m.id" 
                                    class="menu-item"
                                    :class="{ active: currentModel === m.label }"
                                    @click="selectModel(m)"
                                >
                                    <span class="item-text">{{ m.label }}</span>
                                    <svg v-if="currentModel === m.label" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                        <polyline points="20 6 9 17 4 12"></polyline>
                                    </svg>
                                </div>
                            </div>
                        </transition>
                    </div>

                    <!-- Thinking Pill -->
                     <div class="control-pill-wrapper thinking" :class="{ open: isThinkingMenuOpen }">
                        <button class="control-pill" @click="isThinkingMenuOpen = !isThinkingMenuOpen">
                            <span class="text">{{ currentThinking }}</span>
                        </button>
                        <transition name="scale-up">
                            <div class="pill-menu" v-if="isThinkingMenuOpen">
                                <div class="menu-header">Thinking Intensity</div>
                                <div 
                                    v-for="t in thinkingLevels" 
                                    :key="t.id" 
                                    class="menu-item"
                                    :class="{ active: currentThinking === t.label }"
                                    @click="selectThinking(t)"
                                >
                                    <span class="item-text">{{ t.label }}</span>
                                    <svg v-if="currentThinking === t.label" class="check-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
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
    overflow-y: auto;
    overflow-x: hidden;
    position: relative;
    
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

.context-menu {
    position: absolute;
    bottom: 100%; /* Open upwards */
    right: 0;
    margin-bottom: 12px;
    width: 160px; /* Reduced from 200px */
    background: var(--surface-elevated);
    border: 1px solid var(--border-subtle);
    border-radius: 10px; /* Reduced radius */
    box-shadow: 
        0 4px 24px rgba(0, 0, 0, 0.2),
        0 0 0 1px rgba(255, 255, 255, 0.05) inset;
    backdrop-filter: blur(20px);
    padding: 4px; /* Reduced padding */
    z-index: 1000;
    display: flex; /* To hold submenu container */
    
    /* Ensure it doesn't get clipped */
    /* Note: If the parent has overflow:hidden, this might be an issue. 
       The .ai-command-center has overflow:hidden, but .layer-footer is inside it.
       We might need to adjust .ai-command-center overflow or use a portal if this gets clipped.
       For now, let's try to keep it inside or assume the footer has enough space or z-index context.
       Actually, .ai-command-center has overflow:hidden. This menu WILL be clipped if it goes out of bounds.
       However, since it opens upwards from the bottom footer, it should be fine as long as it's not taller than the panel.
    */
}

.menu-section {
    width: 100%;
    display: flex;
    flex-direction: column;
    gap: 2px;
}

.menu-label {
    font-size: 0.65rem; /* Reduced from 0.75rem */
    color: var(--text-tertiary);
    padding: 6px 10px 2px; /* Reduced padding */
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 0.5px;
}

.menu-item {
    display: flex;
    align-items: center;
    gap: 8px; /* Reduced gap */
    padding: 6px 10px; /* Reduced padding */
    border-radius: 6px; /* Reduced radius */
    cursor: pointer;
    transition: all 0.2s;
    color: var(--text-primary);
    font-size: 0.8rem; /* Reduced from 0.9rem */
    position: relative;

    &:hover, &.active {
        background: var(--surface-highlight);
    }

    .item-text {
        flex: 1;
    }

    .chevron {
        width: 14px;
        height: 14px;
        color: var(--text-tertiary);
        opacity: 0.7;
    }
}

/* Submenu Container (Flyout) */
.submenu-container {
    position: absolute;
    bottom: 0; /* Align bottom */
    width: 180px; /* Reduced from 220px */
    background: var(--surface-elevated);
    border: 1px solid var(--border-subtle);
    border-radius: 10px; /* Reduced radius */
    box-shadow: 0 4px 24px rgba(0, 0, 0, 0.2);
    backdrop-filter: blur(20px);
    padding: 4px; /* Reduced padding */
    /* Animation is handled by specific direction classes */
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
            }
        }

        &.ai {
            .bubble {
                background: var(--surface-card);
                border: 1px solid var(--border-dim);
                color: var(--text-primary);
            }
        }

        .avatar {
            width: 24px;
            height: 24px;
            border-radius: 50%;
            background: var(--surface-dim);
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 0.6rem;
            color: var(--text-secondary);
            border: 1px solid var(--border-dim);
            flex-shrink: 0;
        }

        .message-wrapper {
            display: flex;
            flex-direction: column;
            gap: 8px;
            max-width: 85%;
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

            &.generating {
                background: transparent;
                border-color: transparent;
                padding: 0;
                display: flex;
                align-items: center;
                height: 24px; /* Match avatar height */
            }

            .generating-indicator {
                font-size: 0.8rem;
                color: var(--text-tertiary);
                font-style: italic;
                
                .dot {
                    animation: dot-fade 1.5s infinite;
                    opacity: 0;
                }
                .dot:nth-child(1) { animation-delay: 0.0s; }
                .dot:nth-child(2) { animation-delay: 0.5s; }
                .dot:nth-child(3) { animation-delay: 1.0s; }
            }
        }

        @keyframes dot-fade {
            0% { opacity: 0; }
            50% { opacity: 1; }
            100% { opacity: 0; }
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
        border-color: var(--accent-primary);
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
        background: rgba(0, 0, 0, 0.6);
        box-shadow: 
            inset 0 0 0 0.5px rgba(255, 255, 255, 0.15),
            inset 0 1px 0 rgba(255, 255, 255, 0.08),
            0 8px 32px rgba(0, 0, 0, 0.4);
    }

    textarea {
        width: 100%;
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
        overflow-y: auto;
        
        /* Custom Scrollbar */
        &::-webkit-scrollbar { width: 6px; }
        &::-webkit-scrollbar-track { background: transparent; }
        &::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.2); 
            border-radius: 3px; 
        }
        &:hover::-webkit-scrollbar-thumb { 
            background: rgba(255, 255, 255, 0.3); 
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
    .context-menu, .pill-menu {
        position: absolute;
        bottom: 100%;
        left: 0;
        margin-bottom: 6px;
        background: rgba(20, 20, 20, 0.95); /* Deep dark background */
        backdrop-filter: blur(0); /* No blur needed for opaque dark */
        border: 1px solid rgba(255, 255, 255, 0.08);
        border-radius: 8px; /* Sharper radius */
        padding: 4px;
        box-shadow: 0 12px 40px rgba(0,0,0,0.6);
        z-index: 100;
        min-width: 160px;
        display: flex;
        flex-direction: column;
        gap: 1px;
        transform-origin: bottom left;
        animation: scale-up 0.1s cubic-bezier(0.2, 0, 0.13, 1.5); /* Even snappier */

        .menu-header {
            padding: 6px 8px 4px;
            font-size: 10px; /* Micro label */
            color: rgba(255, 255, 255, 0.3);
            font-weight: 600;
            letter-spacing: 0.5px;
            text-transform: uppercase;
        }
        
        .menu-label {
             padding: 6px 8px 4px;
            font-size: 11px;
            color: rgba(255, 255, 255, 0.4);
            font-weight: 500;
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

            &:hover, &.active {
                background: rgba(255, 255, 255, 0.08);
                color: white;
            }
            
            .icon {
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
        }
    }
}
</style>
