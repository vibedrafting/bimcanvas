import axios from 'axios';

const API_BASE = 'http://localhost:5000/api/scheme';

/**
 * 模块数据响应
 */
export interface SchemeModulesResponse {
    source: string;
    branch: string | null;
    modules: any[];
}

/**
 * 保存模块响应
 */
export interface SaveSchemeModulesResponse {
    success: boolean;
    savedCount: number;
    commit?: {
        hash: string;
        message: string;
    };
}

/**
 * 方案数据服务 - 支持跨分支/Worktree 的模块数据读写
 *
 * source 参数格式：
 * - main: 主仓库当前分支
 * - worktree:{name}: 指定 Worktree（如 worktree:ai-job-v）
 */
export class SchemeService {
    /**
     * 获取模块数据
     * @param source 数据源标识
     */
    static async getModules(source: string): Promise<SchemeModulesResponse> {
        const response = await axios.get<SchemeModulesResponse>(
            `${API_BASE}/${source}/modules`
        );
        return response.data;
    }

    /**
     * 保存模块数据（接受 Agent 修改）
     * @param source 数据源标识
     * @param modules 模块列表
     * @param commitMessage 可选的提交信息（如果提供则自动提交）
     */
    static async saveModules(
        source: string,
        modules: any[],
        commitMessage?: string
    ): Promise<SaveSchemeModulesResponse> {
        const response = await axios.put<SaveSchemeModulesResponse>(
            `${API_BASE}/${source}/modules`,
            {
                modules,
                commitMessage
            }
        );
        return response.data;
    }
}
