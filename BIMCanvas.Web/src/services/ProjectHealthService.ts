import axios from 'axios';
import { SERVER_API } from '../config/api';

const API_BASE = `${SERVER_API}/project/health`;
const WEB_CONFIG_URL = `${SERVER_API}/web_config`;

export interface HealthIssue {
    relativePath: string;
    issueType: string;
    description: string;
}

export interface CheckInspectionResult {
    checkId: string;
    checkDescription: string;
    issues: HealthIssue[];
    errors: string[];
}

export interface ProjectInspectionReport {
    checks: CheckInspectionResult[];
    totalIssues: number;
}

export interface CheckRepairResult {
    checkId: string;
    checkDescription: string;
    migrated: string[];
    skipped: string[];
    errors: string[];
}

export interface ProjectRepairReport {
    snapshotCommitHash: string | null;
    checks: CheckRepairResult[];
}

/** 已注册 check 的元信息（配置面板渲染勾选项用）。 */
export interface HealthCheckInfo {
    id: string;
    description: string;
}

/** 健康检查偏好（存 web_config.json）。 */
export interface HealthCheckPrefs {
    /** 是否在导入/新建/恢复项目时自动跑健康检查。默认 false。 */
    autoCheckOnLoad: boolean;
    /** 勾选启用的 check id 子集；null = 全部。 */
    enabledCheckIds: string[] | null;
}

const DEFAULT_PREFS: HealthCheckPrefs = { autoCheckOnLoad: false, enabledCheckIds: null };

/**
 * 项目健康检查 + 修复服务
 * 对应 Server endpoint /api/project/health/{checks,inspect,repair}
 * 不依赖当前已加载项目——首页项目列表里任何项目都能触发。
 */
export class ProjectHealthService {
    /** 列出已注册 check（id + 描述），供配置面板渲染勾选项。 */
    static async listChecks(): Promise<HealthCheckInfo[]> {
        const response = await axios.get<HealthCheckInfo[]>(`${API_BASE}/checks`);
        return response.data;
    }

    /**
     * 只查不改：返回各 check 发现的问题清单。
     * @param checkIds 要跑的 check 子集；省略 / null = 全部。
     */
    static async inspect(folderPath: string, checkIds?: string[] | null): Promise<ProjectInspectionReport> {
        const response = await axios.post<ProjectInspectionReport>(
            `${API_BASE}/inspect`,
            { folderPath, checkIds: checkIds ?? null }
        );
        return response.data;
    }

    /**
     * 实际修复。修复前 Server 自动 git commit 兜底；返回 snapshotCommitHash。
     * @param checkIds 要跑的 check 子集；省略 / null = 全部。
     */
    static async repair(folderPath: string, checkIds?: string[] | null): Promise<ProjectRepairReport> {
        const response = await axios.post<ProjectRepairReport>(
            `${API_BASE}/repair`,
            { folderPath, checkIds: checkIds ?? null }
        );
        return response.data;
    }

    /** 读健康检查偏好（缺失 → 默认值：不自动检查、全部 check）。 */
    static async getPrefs(): Promise<HealthCheckPrefs> {
        try {
            const response = await axios.get<any>(WEB_CONFIG_URL);
            const hc = response.data?.healthCheck;
            if (!hc) return { ...DEFAULT_PREFS };
            return {
                autoCheckOnLoad: !!hc.autoCheckOnLoad,
                enabledCheckIds: Array.isArray(hc.enabledCheckIds) ? hc.enabledCheckIds : null
            };
        } catch {
            return { ...DEFAULT_PREFS };
        }
    }

    /** 写健康检查偏好：读-合并-写 web_config，避免覆盖 layerPresets 等其它字段。 */
    static async savePrefs(prefs: HealthCheckPrefs): Promise<void> {
        let current: any = {};
        try {
            const response = await axios.get<any>(WEB_CONFIG_URL);
            current = response.data ?? {};
        } catch {
            current = {};
        }
        const merged = { ...current, healthCheck: prefs };
        await axios.post(WEB_CONFIG_URL, merged);
        // 通知监听 web_config 的组件刷新（与 useAgentConfig 的事件约定一致）。
        window.dispatchEvent(new CustomEvent('bimcanvas:web-config-updated', { detail: merged }));
    }
}
