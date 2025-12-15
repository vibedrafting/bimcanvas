import { defineStore } from 'pinia';
import { ref } from 'vue';
import type { CanvasDocument } from '../types/canvas';
import axios from 'axios';

export const useCanvasStore = defineStore('canvas', () => {
    const document = ref<CanvasDocument | null>(null);
    const isLoading = ref(false);

    const loadDocument = (doc: CanvasDocument) => {
        document.value = doc;
    };

    const loadDemoData = async (url: string) => {
        try {
            isLoading.value = true;
            const response = await axios.get<CanvasDocument>(url);
            document.value = response.data;
            console.log('Demo data loaded:', document.value);
        } catch (error) {
            console.error('Failed to load demo data:', error);
        } finally {
            isLoading.value = false;
        }
    };

    return {
        document,
        isLoading,
        loadDocument,
        loadDemoData
    };
});
