import { defineStore } from 'pinia';
import { ref, computed } from 'vue';
import type { CanvasDocument } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';

export const useCanvasStore = defineStore('canvas', () => {
    const document = ref<CanvasDocument | null>(null);
    const isLoading = ref(false);
    const selectedObject = ref<any | null>(null);
    const timeline = new TimelineManager();

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
    };

    const loadDemoData = async (url: string) => {
        try {
            isLoading.value = true;
            const response = await axios.get<CanvasDocument>(url);
            document.value = response.data;
            timeline.clear();
            saveState(); // Initial state
            console.log('Demo data loaded:', document.value);
        } catch (error) {
            console.error('Failed to load demo data:', error);
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
        }
    };

    const removeModule = (moduleId: string) => {
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            document.value.modules.splice(moduleIndex, 1);
            selectedObject.value = null; // Deselect
            saveState();
        }
    };


    const canUndo = computed(() => timeline.canUndo);
    const canRedo = computed(() => timeline.canRedo);

    return {
        document,
        isLoading,
        selectedObject,
        loadDocument,
        setSelectedObject,
        loadDemoData,
        undo,
        redo,
        canUndo,
        canRedo,
        canRedo,
        saveState,
        updateModule,
        removeModule
    };

});
