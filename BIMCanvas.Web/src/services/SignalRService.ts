import * as signalR from '@microsoft/signalr';

export class SignalRService {
    private connection: signalR.HubConnection;
    private static instance: SignalRService;

    private constructor() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("http://localhost:5000/hubs/canvas") // Adjust URL as needed
            .withAutomaticReconnect()
            .build();

        this.setupListeners();
        this.setupLifecycleHooks();
    }

    public static getInstance(): SignalRService {
        if (!SignalRService.instance) {
            SignalRService.instance = new SignalRService();
        }
        return SignalRService.instance;
    }

    private setupListeners() {
        this.connection.on("ReceiveUpdate", (data: any) => {
            console.log("Received update from server:", data);
            // Dispatch event or update store directly
            window.dispatchEvent(new CustomEvent('bimcanvas:server-update', { detail: data }));
        });

        this.connection.on("ReceiveGhostPatch", (patch: any) => {
            console.log("Received ghost patch:", patch);
            window.dispatchEvent(new CustomEvent('bimcanvas:ghost-patch', { detail: patch }));
        });
    }

    private setupLifecycleHooks() {
        this.connection.onreconnecting((error) => {
            console.warn('SignalR Reconnecting...', error);
            this.dispatchConnectionState('Reconnecting');
        });

        this.connection.onreconnected((connectionId) => {
            console.log('SignalR Reconnected.', connectionId);
            this.dispatchConnectionState('Connected');
        });

        this.connection.onclose((error) => {
            console.error('SignalR Connection Closed.', error);
            this.dispatchConnectionState('Disconnected');
        });
    }

    private dispatchConnectionState(state: 'Connected' | 'Disconnected' | 'Reconnecting') {
        window.dispatchEvent(new CustomEvent('bimcanvas:connection-state', { detail: state }));
    }

    public async start() {
        try {
            await this.connection.start();
            console.log("SignalR Connected.");
            this.dispatchConnectionState('Connected');
        } catch (err) {
            console.error("SignalR Connection Error: ", err);
            this.dispatchConnectionState('Disconnected');
            // Retry logic could go here
        }
    }

    public async sendUpdate(data: any) {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            await this.connection.invoke("SendUpdate", data);
        }
    }

    public getConnectionState(): signalR.HubConnectionState {
        return this.connection.state;
    }
}
