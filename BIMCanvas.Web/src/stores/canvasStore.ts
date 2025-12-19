
import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { CanvasDocument } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';
import { SignalRService } from '../services/SignalRService';

import { useDebugStore } from './debugStore';

export const useCanvasStore = defineStore('canvas', () => {
    const document = ref<CanvasDocument | null>(null);
    const isLoading = ref(false);

    // P1 类型安全增强：使用 selectedId 作为真正的状态源
    const selectedId = ref<string | null>(null);

    // 兼容层：保留 selectedObject 作为只读计算属性，供现有代码使用
    const selectedObject = computed(() => {
        if (!selectedId.value || !document.value) return null;
        return document.value.modules?.find(m => m.id === selectedId.value) ?? null;
    });

    const debugMsg = ref<string>('');
    const instanceId = Math.random().toString(36).substring(7);
    const error = ref<string | null>(null); // Added error state
    const promptMessage = ref<string | null>(null); // Instructional prompt
    console.log('CanvasStore Created:', instanceId);
    const timeline = new TimelineManager();
    const signalR = SignalRService.getInstance();
    const debugStore = useDebugStore();

    // Initialize SignalR
    signalR.start();

    const agentConnectionState = ref<'Connected' | 'Disconnected' | 'Reconnecting'>('Disconnected');
    const currentOperation = ref<string | null>(null);

    window.addEventListener('bimcanvas:connection-state', (e: any) => {
        agentConnectionState.value = e.detail;
        console.log('Store: Connection State Updated ->', agentConnectionState.value);
    });

    const canUndo = ref(false);
    const canRedo = ref(false);

    const updateHistoryState = () => {
        canUndo.value = timeline.canUndo;
        canRedo.value = timeline.canRedo;
    };

    // Helper to push state to timeline without triggering watch loops if we were watching
    // For now, we call this manually when a significant change happens (e.g. drag end)
    const saveState = () => {
        if (document.value) {
            timeline.push(document.value);
            updateHistoryState();
        }
    };

    const loadDocument = (doc: CanvasDocument) => {
        document.value = doc;
        timeline.clear();
        saveState(); // Initial state
        // saveState calls updateHistoryState
    };

    const setSelectedObject = (obj: any | null) => {
        // 从对象中提取 id，兼容多种输入格式
        if (obj === null) {
            selectedId.value = null;
        } else if (typeof obj === 'string') {
            selectedId.value = obj;
        } else if (obj.id) {
            selectedId.value = obj.id;
        } else if (obj.userData?.id) {
            // 兼容 THREE.Object3D 的 userData
            selectedId.value = obj.userData.id;
        } else {
            selectedId.value = null;
        }
        debugMsg.value += `\nSet: ${selectedId.value || 'NULL'} at ${Date.now()}`;
        console.log('Store setSelectedObject:', selectedId.value, '->', selectedObject.value);
    };

    const clearSelection = () => {
        selectedId.value = null;
    };

    const loadDemoData = async (url: string) => {
        try {
            isLoading.value = true;
            debugStore.log(`Loading demo data from: ${url}`);
            const response = await axios.get<CanvasDocument>(url);
            document.value = response.data;
            timeline.clear();
            saveState(); // Initial state
            console.log('Demo data loaded:', document.value);
            debugStore.success(`Successfully loaded demo data. Modules: ${document.value.modules.length}`);
            error.value = null; // Clear any previous error
        } catch (err: any) {
            console.error('Failed to load demo data:', err);
            debugStore.error(`Failed to load demo data: ${err.message || err}`);
            error.value = `Failed to load demo data: ${err.message || err}`;
        } finally {
            isLoading.value = false;
        }
    };

    const undo = () => {
        const prevState = timeline.undo();
        if (prevState) {
            document.value = prevState;
            updateHistoryState();
        }
    };

    const redo = () => {
        const nextState = timeline.redo();
        if (nextState) {
            document.value = nextState;
            updateHistoryState();
        }
    };

    const updateModule = (moduleId: string, updates: Partial<any>) => {
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            const updatedModule = { ...document.value.modules[moduleIndex], ...updates } as any;
            document.value.modules[moduleIndex] = updatedModule;

            // Use nextTick to ensure state is settled before saving
            nextTick(() => saveState());

            // Sync with server
            signalR.sendUpdate({
                type: 'module_update',
                moduleId,
                updates
            });
        }
    };

    const removeModule = (moduleId: string) => {
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            document.value.modules.splice(moduleIndex, 1);
            selectedId.value = null; // Deselect

            // Use nextTick to ensure state is settled before saving
            nextTick(() => saveState());

            // Sync with server
            signalR.sendUpdate({
                type: 'module_remove',
                moduleId
            });
        }
    };

    const setPrompt = (msg: string | null) => {
        promptMessage.value = msg;
    };


    return {
        // State
        document,
        selectedId,       // 新增：真正的选择状态
        selectedObject,   // 兼容层：计算属性
        isLoading,
        error,
        agentConnectionState,
        currentOperation,

        // Getters
        canUndo,
        canRedo,

        // Actions
        loadDemoData,
        setSelectedObject,
        clearSelection,   // 新增：显式清除选择
        updateModule,
        removeModule,
        undo,
        redo,

        // UI State
        promptMessage,
        setPrompt,
        debugMsg
    };
});
