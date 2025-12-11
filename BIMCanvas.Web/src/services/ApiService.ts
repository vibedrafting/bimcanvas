import axios from 'axios';
import type { CanvasDocument, ElementChange } from '@/types/canvas';

const api = axios.create({
    baseURL: '/api',
});

export interface ChangeSet {
    id: string;
    changes: ElementChange[];
}

export const ApiService = {
    async getCanvas(id: string): Promise<CanvasDocument> {
        const { data } = await api.get(`/canvas/${id}`);
        return data;
    },

    async createCanvas(document: CanvasDocument): Promise<CanvasDocument> {
        const { data } = await api.post('/canvas', document);
        return data;
    },

    async commitChanges(id: string, changeSet: ChangeSet): Promise<void> {
        await api.post(`/canvas/${id}/commit`, changeSet);
    },
};
