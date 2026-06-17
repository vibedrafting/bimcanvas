import * as signalR from '@microsoft/signalr';
import { SIGNALR_HUB } from '../config/api';
import { useSystemStore } from '../stores/systemStore';
import type { AgentNotificationDto } from '../types/notification';
import { createLogger } from '../utils/logger';

const log = createLogger('RECV');

// GitStatusChanged 去重:Server 端 .git watcher 在 agent 频繁 commit 时每 1-2s 推送一次,
// 分支大多没变。只在分支真变化时 info,同分支心跳降 debug,避免刷屏。
let lastGitBranch: string | undefined;

export class SignalRService {
    private connection: signalR.HubConnection;
    private static instance: SignalRService;

    private constructor() {
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl(SIGNALR_HUB)
            .configureLogging(signalR.LogLevel.Warning)  // 压掉库自身 Information 级噪音(WebSocket connected 等)
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
            log.info('ReceiveUpdate', { file: data?.file, action: data?.action });
            window.dispatchEvent(new CustomEvent('bimcanvas:server-update', { detail: data }));
        });

        this.connection.on("ReceiveGhostPatch", (patch: any) => {
            log.debug('ReceiveGhostPatch');
            window.dispatchEvent(new CustomEvent('bimcanvas:ghost-patch', { detail: patch }));
        });

        // Git 状态变化事件
        this.connection.on("GitStatusChanged", (status: any) => {
            const branch = status?.branch ?? status?.currentBranch;
            if (branch !== lastGitBranch) {
                log.info('GitStatusChanged', { branch, from: lastGitBranch });
                lastGitBranch = branch;
            } else {
                log.debug('GitStatusChanged', { branch });
            }
            window.dispatchEvent(new CustomEvent('bimcanvas:git-status-changed', { detail: status }));
        });

        // Agent 通知事件 — 统一收口到 systemStore;JSON 数组 message 分流为 worktree 全屏 modal
        this.connection.on("AgentNotification", (data: AgentNotificationDto) => {
            const sys = useSystemStore();
            log.info('AgentNotification', { title: data.title, type: data.type });
            try {
                const parsed = JSON.parse(data.message);
                if (Array.isArray(parsed) && parsed.length > 0) {
                    sys.pushWorktreeNotification({
                        title: data.title,
                        worktreeNames: parsed,
                        type: data.type ?? 'info',
                    });
                    return;
                }
            } catch { /* 普通文本,落 toast */ }
            sys.pushToast({ title: data.title, message: data.message, type: data.type });
        });

        // 边界段调试可视化数据
        this.connection.on("BoundaryDebugData", (data: string) => {
            try {
                const parsed = JSON.parse(data);
                window.dispatchEvent(new CustomEvent('bimcanvas:boundary-debug', { detail: parsed }));
            } catch {
                log.error('BoundaryDebugData parse failed');
            }
        });

        // 通用 scene artifact 更新(plugin-agnostic,业务下沉派单纲领 §4.1)
        // payload: { sceneId, artifactKind, path?, plugin?, timestamp }
        this.connection.on("SceneArtifactUpdated", (data: any) => {
            log.info('SceneArtifactUpdated', { kind: data?.artifactKind, plugin: data?.plugin });
            window.dispatchEvent(new CustomEvent('bimcanvas:scene-artifact-updated', { detail: data }));
        });
    }

    private setupLifecycleHooks() {
        this.connection.onreconnecting((error) => {
            log.warn('reconnecting', { error });
            this.dispatchConnectionState('Reconnecting');
        });

        this.connection.onreconnected((connectionId) => {
            log.info('reconnected', { connectionId });
            this.dispatchConnectionState('Connected');
            // 重连后自动触发数据重载，弥补断连期间丢失的更新
            window.dispatchEvent(new CustomEvent('bimcanvas:server-update', {
                detail: {
                    type: 'file_changed',
                    file: 'modules.json',
                    action: 'reload',
                    trigger: 'reconnect'
                }
            }));
        });

        this.connection.onclose((error) => {
            log.error('connection closed', { error });
            this.dispatchConnectionState('Disconnected');
        });
    }

    private dispatchConnectionState(state: 'Connected' | 'Disconnected' | 'Reconnecting') {
        window.dispatchEvent(new CustomEvent('bimcanvas:connection-state', { detail: state }));
    }

    public async start() {
        try {
            await this.connection.start();
            log.info('connected');
            this.dispatchConnectionState('Connected');
        } catch (err) {
            log.error('connection error', { err });
            this.dispatchConnectionState('Disconnected');
            // 初始连接失败时 5 秒后重试（withAutomaticReconnect 只处理建立后的断连）
            setTimeout(() => {
                log.debug('retrying initial connection');
                this.start();
            }, 5000);
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
                log.error('register window failed', { win: windowId, err });
                return false;
            }
        }
        return false;
    }

    public getConnectionState(): signalR.HubConnectionState {
        return this.connection.state;
    }
}
