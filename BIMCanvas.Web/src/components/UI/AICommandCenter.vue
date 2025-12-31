<script setup lang="ts">
import { ref, onMounted, nextTick, computed } from 'vue';
import { useCanvasStore } from '../../stores/canvasStore';
import { storeToRefs } from 'pinia';

// Agent API Configuration
const AGENT_API_BASE = 'http://127.0.0.1:8765';

const panelWidth = ref(360);
const isResizing = ref(false);
const currentBranch = ref('loading...');
const isBranchDropdownOpen = ref(false);
const mode = ref('chat'); // 'chat' | 'tasks'
const isTaskSummaryExpanded = ref(false);

// Git Data Interface
interface GitBranch {
  id: string;
  name: string;
  isCurrent: boolean;
  commit: {
    message: string;
    time: string;
    hash: string;
    author: string;
  };
}

// Real Git Data (will be fetched)
const branches = ref<GitBranch[]>([]);

// Store Integration
const store = useCanvasStore();
const { selectedObjects } = storeToRefs(store);

// Computed Selection State
const selectedModuleCount = computed(() => {
  return selectedObjects.value.filter(obj => obj.type === 'module').length;
});

const selectedRoom = computed(() => {
  // Try to find the room of the first selected module
  // For now, fallback to currentZone or a placeholder
  if (selectedModuleCount.value > 0) {
    // TODO: Implement actual room lookup based on module coordinates
    return 'Living Room'; 
  }
  return '';
});

// Agent connection state
const agentStatus = ref<'connecting' | 'connected' | 'disconnected'>('disconnected');
const currentProjectPath = ref('');

// Chat state
const chatMessages = ref<Array<{ role: 'user' | 'ai'; content: string }>>([]);
const inputMessage = ref('');
const isLoading = ref(false);
const chatScrollRef = ref<HTMLElement | null>(null);

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

// Select branch
const selectBranch = async (branchId: string) => {
  // In a real app, this would trigger a git checkout via API
  currentBranch.value = branchId;
  branches.value.forEach(b => b.isCurrent = b.id === branchId);
  isBranchDropdownOpen.value = false;
};

// Fetch Git Info
const fetchGitInfo = async () => {
  try {
    // Try to fetch from Agent API
    const response = await fetch(`${AGENT_API_BASE}/api/git/branches`);
    if (response.ok) {
        const data = await response.json();
        branches.value = data;
        const current = branches.value.find(b => b.isCurrent);
        if (current) currentBranch.value = current.name;
    } else {
        throw new Error('API not available');
    }
  } catch (e) {
    // Fallback to realistic mock data if API fails (for demo purposes)
    console.warn('Failed to fetch git info, using mock data');
    branches.value = [
      { 
        id: 'main', 
        name: 'main', 
        isCurrent: false,
        commit: { message: 'Merge pull request #42 from feat/login', time: '2h ago', hash: 'a1b2c3d', author: 'Dev' }
      },
      { 
        id: 'feat/ai-proposal-A', 
        name: 'feat/ai-proposal-A', 
        isCurrent: true,
        commit: { message: 'Optimize living room layout for flow', time: '5m ago', hash: 'e5f6g7h', author: 'AI Agent' }
      },
      { 
        id: 'feat/storage-opt', 
        name: 'feat/storage-opt', 
        isCurrent: false,
        commit: { message: 'Add custom cabinet modules', time: '1d ago', hash: 'i8j9k0l', author: 'User' }
      }
    ];
    currentBranch.value = 'feat/ai-proposal-A';
  }
};

// Check Agent health on mount
onMounted(async () => {
  await checkAgentHealth();
  await fetchGitInfo();
});

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

  // Scroll to bottom
  await nextTick();
  scrollToBottom();

  try {
    const response = await fetch(`${AGENT_API_BASE}/api/chat`, {
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

    const data = await response.json();

    // Add AI response to chat
    chatMessages.value.push({ role: 'ai', content: data.reply });
    agentStatus.value = 'connected';

  } catch (error) {
    console.error('Chat error:', error);
    chatMessages.value.push({
      role: 'ai',
      content: 'Sorry, I encountered an error. Please check if the Agent server is running.'
    });
    agentStatus.value = 'disconnected';
  } finally {
    isLoading.value = false;
    await nextTick();
    scrollToBottom();
  }
};

const clearHistory = async () => {
  try {
    await fetch(`${AGENT_API_BASE}/api/clear-history`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ projectPath: currentProjectPath.value })
    });
    chatMessages.value = [];
  } catch (error) {
    console.error('Clear history error:', error);
  }
};

const scrollToBottom = () => {
  if (chatScrollRef.value) {
    chatScrollRef.value.scrollTop = chatScrollRef.value.scrollHeight;
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

import TaskSummaryWidget from './TaskSummaryWidget.vue';
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
      <div class="layer-stream">
        
        <!-- View: Chat -->
        <div v-if="mode === 'chat'" class="view-chat" ref="chatScrollRef">

            <!-- Task Summary Widget -->
            <TaskSummaryWidget :tasks="tasks" />

            <!-- Welcome message when no chat history -->
            <div v-if="chatMessages.length === 0" class="chat-message ai">
                <div class="avatar">AI</div>
                <div class="bubble">
                    你好！我是 BIMCanvas 的布置助手。我可以帮助你分析房间功能、提供布置建议。有什么我能帮你的吗？
                </div>
            </div>

            <!-- Actual Chat History -->
            <template v-for="(msg, index) in chatMessages" :key="index">
                <div class="chat-message" :class="msg.role === 'user' ? 'user' : 'ai'">
                    <div v-if="msg.role === 'ai'" class="avatar">AI</div>
                    <div class="bubble">{{ msg.content }}</div>
                </div>
            </template>

            <!-- Loading indicator -->
            <div v-if="isLoading" class="chat-message ai">
                <div class="avatar">AI</div>
                <div class="bubble loading">
                    <span class="typing-dot"></span>
                    <span class="typing-dot"></span>
                    <span class="typing-dot"></span>
                </div>
            </div>
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
        
        <!-- Selection Status Bar -->
        <div class="selection-status-bar">
          <template v-if="selectedModuleCount > 0">
            <span class="icon">🎯</span>
            <span class="text">已选中 {{ selectedModuleCount }} 个模块</span>
            <span class="separator">·</span>
            <span class="icon">📂</span>
            <span class="text">{{ selectedRoom }}</span>
          </template>
          <template v-else>
            <span class="icon">🎯</span>
            <span class="text muted">未选中</span>
          </template>
        </div>

        <!-- Input Area -->
        <div class="input-area">
            <input
              type="text"
              v-model="inputMessage"
              placeholder="输入消息..."
              @keydown="handleKeydown"
              :disabled="isLoading || agentStatus !== 'connected'"
            />
            <button
              class="send-btn"
              @click="sendMessage"
              :disabled="isLoading || !inputMessage.trim() || agentStatus !== 'connected'"
            >
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="19" x2="12" y2="5"></line><polyline points="5 12 12 5 19 12"></polyline></svg>
            </button>
            <button class="clear-btn" @click="clearHistory" title="清空对话">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
            </button>
        </div>
        
        <div class="strategy-toggle">
            <span class="label">Creative</span>
            <div class="toggle-switch"></div>
            <span class="label">Strict</span>
        </div>
      </div>

    </div>
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
  overflow: hidden;
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
    padding: 20px;
    border-bottom: 1px solid var(--border-dim);

    .context-row {
        display: flex;
        align-items: center;
        width: 100%;
        margin-bottom: 12px;
    }

    /* Branch Dropdown Redesign */
    .branch-dropdown {
        position: relative;
        width: 100%;
        z-index: 10;

        .dropdown-trigger {
            width: 100%;
            display: flex;
            align-items: center;
            gap: 8px;
            padding: 8px 12px;
            background: var(--surface-dim);
            border: 1px solid var(--border-dim);
            border-radius: 8px;
            color: var(--text-primary);
            font-size: 0.85rem;
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
            background: var(--surface-highlight);
            border-color: var(--accent-primary);
            .chevron { transform: rotate(180deg); }
        }

        .dropdown-menu {
            position: absolute;
            top: calc(100% + 8px);
            left: 0;
            width: 320px; /* Wider dropdown for details */
            background: var(--surface-elevated);
            border: 1px solid var(--border-subtle);
            border-radius: 12px;
            box-shadow: 0 8px 32px rgba(0,0,0,0.2);
            padding: 8px;
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
                gap: 4px;
                padding: 10px 12px;
                border-radius: 8px;
                cursor: pointer;
                border: 1px solid transparent;
                transition: all 0.2s;

                &:hover {
                    background: var(--surface-highlight);
                    border-color: var(--border-subtle);
                }

                &.current {
                    background: rgba(var(--accent-primary-rgb), 0.08);
                    border-color: rgba(var(--accent-primary-rgb), 0.2);
                    
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
                        font-size: 0.9rem;
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
                    font-size: 0.75rem;
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
        border-radius: 10px;
        padding: 4px;
        border: 1px solid var(--border-subtle); /* Standardized border */
        
        button {
            flex: 1;
            border: 1px solid transparent;
            background: none;
            color: var(--text-secondary);
            font-size: 0.85rem;
            font-weight: 500;
            padding: 6px 12px;
            border-radius: 8px;
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

        .bubble {
            padding: 8px 12px;
            border-radius: 12px;
            font-size: 0.85rem;
            line-height: 1.4;
            max-width: 85%;
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
                transition: width 0.3s;
            }
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

.selection-status-bar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 12px;
    padding: 8px 12px;
    background: var(--surface-dim);
    border: 1px solid var(--border-dim);
    border-radius: 8px;
    font-size: 0.8rem;
    color: var(--text-secondary);

    .icon { font-size: 0.9rem; }
    .text { font-weight: 500; }
    .text.muted { color: var(--text-tertiary); font-weight: 400; }
    .separator { color: var(--text-tertiary); font-weight: bold; }
}

.input-area {
    display: flex;
    gap: 8px;
    margin-bottom: 12px;
    
    input {
        flex: 1;
        background: var(--surface-highlight); /* White in light mode */
        border: 1px solid var(--border-dim); /* Visible border */
        border-radius: 8px;
        padding: 8px 12px;
        color: var(--text-primary);
        font-size: 0.9rem;
        outline: none;
        transition: all 0.2s;
        box-shadow: 0 2px 6px rgba(0,0,0,0.05); /* Subtle shadow */
        
        &:focus {
            border-color: var(--accent-primary);
            box-shadow: 0 2px 8px rgba(0,0,0,0.1); /* Stronger shadow on focus */
        }
        &::placeholder { color: var(--text-tertiary); }
    }

    .send-btn {
        width: 36px;
        height: 36px;
        display: flex;
        align-items: center;
        justify-content: center;
        background: var(--accent-primary);
        border: none;
        border-radius: 8px;
        color: white;
        cursor: pointer;
        transition: transform 0.1s;
        
        svg { width: 18px; height: 18px; }
        &:active { transform: scale(0.95); }
    }
}

.strategy-toggle {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    font-size: 0.7rem;
    color: var(--text-secondary);
    
    .toggle-switch {
        width: 32px;
        height: 16px;
        background: var(--surface-dim);
        border-radius: 8px;
        position: relative;
        cursor: pointer;
        
        &::after {
            content: '';
            position: absolute;
            left: 2px;
            top: 2px;
            width: 12px;
            height: 12px;
            background: var(--text-secondary);
            border-radius: 50%;
            transition: transform 0.2s;
        }
    }
}

@keyframes spin {
    to { transform: rotate(360deg); }
}

@keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
}

/* Typing animation for loading indicator */
.bubble.loading {
    display: flex;
    gap: 4px;
    padding: 12px 16px;

    .typing-dot {
        width: 6px;
        height: 6px;
        background: var(--text-tertiary);
        border-radius: 50%;
        animation: typing 1.4s ease-in-out infinite;

        &:nth-child(1) { animation-delay: 0s; }
        &:nth-child(2) { animation-delay: 0.2s; }
        &:nth-child(3) { animation-delay: 0.4s; }
    }
}

@keyframes typing {
    0%, 60%, 100% {
        transform: translateY(0);
        opacity: 0.4;
    }
    30% {
        transform: translateY(-4px);
        opacity: 1;
    }
}

/* Clear button */
.clear-btn {
    width: 36px;
    height: 36px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--surface-highlight);
    border: 1px solid var(--border-dim);
    border-radius: 8px;
    color: var(--text-secondary);
    cursor: pointer;
    transition: all 0.2s;

    svg { width: 16px; height: 16px; }

    &:hover {
        background: var(--surface-elevated);
        color: var(--text-primary);
        border-color: var(--border-subtle);
    }
}

/* Disabled state for input/button */
.input-area {
    input:disabled {
        opacity: 0.6;
        cursor: not-allowed;
    }

    .send-btn:disabled {
        opacity: 0.5;
        cursor: not-allowed;
    }
}
</style>
