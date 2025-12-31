<script setup lang="ts">
import { ref } from 'vue';

interface Task {
  id: number;
  name: string;
  progress: number;
  status: string;
}

defineProps<{
  tasks: Task[];
}>();

const isExpanded = ref(false);
</script>

<template>
  <div class="task-summary-widget" v-if="tasks.length > 0" :class="{ expanded: isExpanded }">
    <div class="widget-header" @click="isExpanded = !isExpanded">
      <div class="widget-content">
        <div class="spinner-mini"></div>
        <span class="info">{{ tasks.length }} active task{{ tasks.length > 1 ? 's' : '' }} running...</span>
      </div>
      <div class="widget-action">
        <svg class="chevron" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <polyline points="6 9 12 15 18 9"></polyline>
        </svg>
      </div>
    </div>
    
    <!-- Expanded Details -->
    <div class="widget-details" v-if="isExpanded">
      <div class="mini-task-item" v-for="task in tasks" :key="task.id">
        <div class="task-row">
          <span class="task-name">{{ task.name }}</span>
          <span class="task-status">{{ task.progress }}%</span>
        </div>
        <div class="mini-progress-track">
          <div class="mini-progress-fill" :style="{ width: task.progress + '%' }"></div>
        </div>
        <div class="task-meta-row">
          <span class="status-text">{{ task.status }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped lang="scss">
.task-summary-widget {
    background: var(--surface-elevated);
    border: 1px solid var(--border-subtle);
    border-radius: 10px;
    overflow: hidden;
    transition: all 0.2s;
    margin-bottom: 4px;

    &.expanded {
        border-color: var(--accent-primary);
        box-shadow: 0 4px 12px rgba(0,0,0,0.1);
        
        .widget-action .chevron {
            transform: rotate(180deg);
        }
    }

    .widget-header {
        padding: 10px 12px;
        display: flex;
        align-items: center;
        justify-content: space-between;
        cursor: pointer;
        
        &:hover {
            background: var(--surface-highlight);
        }
    }

    .widget-content {
        display: flex;
        align-items: center;
        gap: 8px;
        
        .spinner-mini {
            width: 14px;
            height: 14px;
            border: 2px solid var(--accent-primary);
            border-top-color: transparent;
            border-radius: 50%;
            animation: spin 1s linear infinite;
        }
        
        .info {
            font-size: 0.8rem;
            color: var(--text-primary);
            font-weight: 500;
        }
    }

    .widget-action {
        display: flex;
        align-items: center;
        color: var(--text-secondary);
        
        .chevron { 
            width: 16px; 
            height: 16px; 
            transition: transform 0.2s;
        }
    }
    
    .widget-details {
        padding: 0 12px 12px 12px;
        border-top: 1px solid var(--border-subtle);
        background: var(--surface-dim);
        
        .mini-task-item {
            margin-top: 12px;
            padding-bottom: 8px;
            border-bottom: 1px solid var(--border-subtle);
            
            &:last-child {
                border-bottom: none;
                padding-bottom: 0;
            }
            
            .task-row {
                display: flex;
                justify-content: space-between;
                align-items: center;
                font-size: 0.75rem;
                margin-bottom: 6px;
                color: var(--text-primary);
                
                .task-name {
                    font-weight: 500;
                    white-space: nowrap;
                    overflow: hidden;
                    text-overflow: ellipsis;
                    max-width: 75%;
                }
                
                .task-status {
                    color: var(--accent-primary);
                    font-weight: 600;
                    font-size: 0.7rem;
                }
            }
            
            .mini-progress-track {
                height: 4px;
                background: var(--border-dim);
                border-radius: 2px;
                overflow: hidden;
                margin-bottom: 4px;
                
                .mini-progress-fill {
                    height: 100%;
                    background: var(--accent-primary);
                    transition: width 0.3s;
                }
            }

            .task-meta-row {
                display: flex;
                justify-content: flex-end;
                
                .status-text {
                    font-size: 0.65rem;
                    color: var(--text-tertiary);
                }
            }
        }
    }
}

@keyframes spin {
    to { transform: rotate(360deg); }
}
</style>
