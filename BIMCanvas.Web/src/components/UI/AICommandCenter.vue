<script setup lang="ts">
import { ref } from 'vue';

const panelWidth = ref(360);
const isResizing = ref(false);
const currentZone = ref('Living Room');
const currentBranch = ref('feat/ai-proposal-A');
const mode = ref('chat'); // 'chat' | 'review'

// Mock Data
const tasks = ref([
  { id: 1, name: 'Generating 3 parallel schemes...', progress: 45 }
]);

const proposals = ref([
  { id: 'A', name: 'Max Storage', score: { storage: 9.0, flow: 6.0 }, color: '#4facfe' },
  { id: 'B', name: 'Flow Priority', score: { storage: 6.0, flow: 9.0 }, color: '#00f2fe' },
  { id: 'C', name: 'Minimalist', score: { storage: 5.0, flow: 8.0 }, color: '#a18cd1' },
]);

const contextScope = ref('Living Room');
const contextSelection = ref(['Sofa', 'Coffee Table']);

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
          <div class="badge zone-badge">
            <span class="icon">📂</span>
            <span class="text">{{ currentZone }}</span>
          </div>
          <div class="badge branch-badge">
            <span class="icon">🌿</span>
            <span class="text">{{ currentBranch }}</span>
          </div>
        </div>
        <div class="mode-switch">
          <button :class="{ active: mode === 'chat' }" @click="mode = 'chat'">Chat</button>
          <button :class="{ active: mode === 'review' }" @click="mode = 'review'">Review</button>
        </div>
      </div>

      <!-- Layer 2: Intelligence Stream -->
      <div class="layer-stream">
        
        <!-- Task Card -->
        <div class="card task-card" v-for="task in tasks" :key="task.id">
          <div class="card-header">
            <div class="spinner"></div>
            <span class="task-name">{{ task.name }}</span>
          </div>
          <div class="progress-track">
            <div class="progress-fill" :style="{ width: task.progress + '%' }"></div>
          </div>
          <div class="card-actions">
            <button class="text-btn">Cancel</button>
            <button class="text-btn">Background</button>
          </div>
        </div>

        <!-- Proposal Carousel -->
        <div class="carousel-section">
            <div class="section-title">Proposals</div>
            <div class="carousel-track" ref="carouselTrackRef" @wheel="handleWheel">
                <div class="proposal-card" v-for="p in proposals" :key="p.id">
                    <div class="preview-box" :style="{ background: `linear-gradient(135deg, ${p.color}20, ${p.color}40)` }">
                        <span class="preview-text">Preview {{ p.id }}</span>
                    </div>
                    <div class="meta">
                        <div class="title">{{ p.name }}</div>
                        <div class="radar-mini">
                            <span class="stat">Storage: {{ p.score.storage }}</span>
                        </div>
                    </div>
                    <div class="hover-actions">
                        <button class="action-btn">Refine</button>
                        <button class="action-btn primary">Compare</button>
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

      <!-- Layer 3: Command Footer -->
      <div class="layer-footer">
        
        <!-- Context Status Bar -->
        <div class="context-status-bar">
            <div class="status-item scope" v-if="contextScope">
                <span class="icon">📂</span>
                <span class="text">{{ contextScope }}</span>
                <button class="close-btn" @click="removeContext('scope')">×</button>
            </div>
            <div class="status-item selection" v-for="item in contextSelection" :key="item">
                <span class="icon">🎯</span>
                <span class="text">{{ item }}</span>
                <button class="close-btn" @click="removeContext('selection', item)">×</button>
            </div>
        </div>

        <!-- Input Area -->
        <div class="input-area">
            <input type="text" placeholder="Type /gen or /fix..." />
            <button class="send-btn">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="19" x2="12" y2="5"></line><polyline points="5 12 12 5 19 12"></polyline></svg>
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
.layer-context {
    padding: 20px;
    border-bottom: 1px solid var(--border-dim);

    .context-row {
        display: flex;
        gap: 8px;
        margin-bottom: 12px;
        flex-wrap: wrap;
    }

    .badge {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 4px 8px;
        border-radius: 6px;
        background: var(--surface-highlight);
        font-size: 0.75rem;
        color: var(--text-secondary);
        border: 1px solid var(--border-dim);
        white-space: nowrap;

        .icon { font-size: 0.8rem; }
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
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;

    /* Scrollbar styling */
    &::-webkit-scrollbar { width: 4px; }
    &::-webkit-scrollbar-thumb { background: var(--border-dim); border-radius: 2px; }
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
    min-width: 140px;
    background: var(--surface-card);
    border-radius: 10px;
    overflow: hidden;
    border: 1px solid var(--border-dim);
    cursor: pointer;
    transition: all 0.2s;
    position: relative;

    &:hover {
        transform: translateY(-2px);
        border-color: var(--accent-primary);
        box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        .hover-actions { opacity: 1; }
    }

    .preview-box {
        height: 80px;
        display: flex;
        align-items: center;
        justify-content: center;
        .preview-text { font-size: 0.7rem; color: var(--text-tertiary); }
    }

    .meta {
        padding: 8px;
        .title { font-size: 0.8rem; font-weight: 500; margin-bottom: 4px; }
        .radar-mini { font-size: 0.7rem; color: var(--text-secondary); }
    }

    .hover-actions {
        position: absolute;
        inset: 0;
        background: rgba(0,0,0,0.6);
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        gap: 6px;
        opacity: 0;
        transition: opacity 0.2s;
        
        .action-btn {
            font-size: 0.7rem;
            padding: 4px 12px;
            border-radius: 12px;
            border: 1px solid rgba(255,255,255,0.2);
            background: rgba(0,0,0,0.5);
            color: white;
            cursor: pointer;
            
            &.primary {
                background: var(--accent-primary);
                border-color: var(--accent-primary);
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

.context-status-bar {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
    margin-bottom: 10px;
    min-height: 24px;

    .status-item {
        display: flex;
        align-items: center;
        gap: 4px;
        padding: 2px 6px;
        border-radius: 4px;
        font-size: 0.7rem;
        
        &.scope {
            background: rgba(79, 172, 254, 0.15);
            color: #4facfe;
            border: 1px solid rgba(79, 172, 254, 0.3);
        }
        &.selection {
            background: rgba(255, 165, 0, 0.15);
            color: #ffb74d;
            border: 1px solid rgba(255, 165, 0, 0.3);
        }

        .close-btn {
            background: none;
            border: none;
            color: inherit;
            opacity: 0.6;
            cursor: pointer;
            font-size: 0.8rem;
            padding: 0 2px;
            &:hover { opacity: 1; }
        }
    }
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
</style>
