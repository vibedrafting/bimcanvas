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
            // 分发服务端更新事件
            window.dispatchEvent(new CustomEvent('bimcanvas:server-update', { detail: data }));
        });

        this.connection.on("ReceiveGhostPatch", (patch: any) => {
            window.dispatchEvent(new CustomEvent('bimcanvas:ghost-patch', { detail: patch }));
        });

        // Git 状态变化事件
        this.connection.on("GitStatusChanged", (status: any) => {
            window.dispatchEvent(new CustomEvent('bimcanvas:git-status-changed', { detail: status }));
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

    /**
     * 注册窗口并获取分支锁，用于 SignalR 断开时清理资源
     * @param windowId 窗口 ID
     * @param branchName 分支名（可选，用于获取分支锁）
     * @returns 是否成功（分支锁获取结果）
     */
    public async registerWindow(windowId: string, branchName?: string): Promise<boolean> {
        if (this.connection.state === signalR.HubConnectionState.Connected) {
            try {
                return await this.connection.invoke<boolean>("RegisterWindow", windowId, branchName);
            } catch (err) {
                console.error('SignalR: Failed to register window', err);
                return false;
            }
        }
        return false;
    }

    public getConnectionState(): signalR.HubConnectionState {
        return this.connection.state;
    }
}
