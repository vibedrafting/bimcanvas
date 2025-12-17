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

    public async start() {
        try {
            await this.connection.start();
            console.log("SignalR Connected.");
        } catch (err) {
            console.error("SignalR Connection Error: ", err);
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
