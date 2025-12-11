import { defineStore } from 'pinia';
import type { CanvasDocument, ElementChange } from '@/types/canvas';

export const useCanvasStore = defineStore('canvas', {
    state: () => ({
        document: null as CanvasDocument | null,
        pendingChanges: [] as ElementChange[],
        selectedElementId: null as string | null,
        connectionStatus: 'disconnected' as 'connected' | 'disconnected' | 'error',
    }),

    getters: {
        hasChanges: (state) => state.pendingChanges.length > 0,
        currentVersion: (state) => state.document?.version ?? 0,
    },

    actions: {
        setDocument(doc: CanvasDocument) {
            this.document = doc;
        },

        select(elementId: string | null) {
            this.selectedElementId = elementId;
        },

        setConnectionStatus(status: 'connected' | 'disconnected' | 'error') {
            this.connectionStatus = status;
        },

        discardChanges() {
            this.pendingChanges = [];
        },
    },
});
