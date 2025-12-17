
import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { CanvasDocument } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';
import { SignalRService } from '../services/SignalRService';

import { useDebugStore } from './debugStore';

export const useCanvasStore = defineStore('canvas', () => {
    const document = ref<CanvasDocument | null>(null);
    const isLoading = ref(false);
    const selectedObject = ref<any | null>(null);
    const debugMsg = ref<string>('');
    const instanceId = Math.random().toString(36).substring(7);
    const error = ref<string | null>(null); // Added error state
    console.log('CanvasStore Created:', instanceId);
    const timeline = new TimelineManager();
    const signalR = SignalRService.getInstance();
    const debugStore = useDebugStore();

    // Initialize SignalR
    signalR.start();

    // Helper to push state to timeline without triggering watch loops if we were watching
    // For now, we call this manually when a significant change happens (e.g. drag end)
    const saveState = () => {
        if (document.value) {
            timeline.push(document.value);
        }
    };

    const loadDocument = (doc: CanvasDocument) => {
        document.value = doc;
        timeline.clear();
        saveState(); // Initial state
    };

    const setSelectedObject = (obj: any | null) => {
        selectedObject.value = obj;
        debugMsg.value += `\nSet: ${obj ? (obj.userData?.type || 'Obj') : 'NULL'} at ${Date.now()}`;
        console.log('Store setSelectedObject:', obj);
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
        }
    };

    const redo = () => {
        const nextState = timeline.redo();
        if (nextState) {
            document.value = nextState;
        }
    };

    const updateModule = (moduleId: string, updates: Partial<any>) => {
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            const updatedModule = { ...document.value.modules[moduleIndex], ...updates };
            document.value.modules[moduleIndex] = updatedModule;
            saveState();

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
            selectedObject.value = null; // Deselect
            saveState();

            // Sync with server
            signalR.sendUpdate({
                type: 'module_remove',
                moduleId
            });
        }
    };


    const canUndo = computed(() => timeline.canUndo);
    const canRedo = computed(() => timeline.canRedo);

    return {
        // State
        document,
        selectedObject,
        isLoading,
        error,

        // Getters
        canUndo,
        canRedo,

        // Actions
        loadDemoData,
        setSelectedObject,
        updateModule,
        removeModule,
        undo,
        redo
    };
});
