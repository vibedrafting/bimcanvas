import * as signalR from '@microsoft/signalr';
import { useCanvasStore } from '@/stores/canvasStore';
import type { CanvasDocument } from '@/types/canvas';

export class SignalRService {
    private connection: signalR.HubConnection;

    constructor(hubUrl: string) {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(hubUrl)
            .withAutomaticReconnect()
            .build();

        this.setupHandlers();
    }

    private setupHandlers() {
        const store = useCanvasStore();

        this.connection.on('DocumentUpdated', (document: CanvasDocument) => {
            store.setDocument(document);
        });

        this.connection.onreconnecting(() => {
            store.setConnectionStatus('disconnected');
        });

        this.connection.onreconnected(() => {
            store.setConnectionStatus('connected');
        });
    }

    async connect() {
        try {
            await this.connection.start();
            useCanvasStore().setConnectionStatus('connected');
        } catch (err) {
            console.error('SignalR Connection Error: ', err);
            useCanvasStore().setConnectionStatus('error');
        }
    }

    async joinCanvas(canvasId: string) {
        await this.connection.invoke('JoinCanvas', canvasId);
    }
}
