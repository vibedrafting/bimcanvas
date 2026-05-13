import axios from 'axios';
import { SERVER_API } from '../config/api';

const API_BASE = `${SERVER_API}/project/health`;

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

/**
 * 项目健康检查 + 修复服务
 * 对应 Server endpoint /api/project/health/{inspect,repair}
 * 不依赖当前已加载项目——首页项目列表里任何项目都能触发。
 */
export class ProjectHealthService {
    /**
     * 只查不改：返回各 check 发现的问题清单。
     */
    static async inspect(folderPath: string): Promise<ProjectInspectionReport> {
        const response = await axios.post<ProjectInspectionReport>(
            `${API_BASE}/inspect`,
            { folderPath }
        );
        return response.data;
    }

    /**
     * 实际修复。修复前 Server 自动 git commit 兜底；返回 snapshotCommitHash。
     */
    static async repair(folderPath: string): Promise<ProjectRepairReport> {
        const response = await axios.post<ProjectRepairReport>(
            `${API_BASE}/repair`,
            { folderPath }
        );
        return response.data;
    }
}
