import axios from 'axios';

const API_BASE = 'http://localhost:5000/api/project';

/**
 * 项目加载结果
 */
export interface ProjectLoadResult {
    status: 'Success' | 'Conflict' | 'Error';
    projectPath?: string;
    existingPath?: string;
    projectName?: string;
    message?: string;
}

/**
 * 项目状态
 */
export interface ProjectStatus {
    isLoaded: boolean;
    projectPath: string | null;
    sourceBcpPath: string | null;
}

/**
 * 项目服务 - 封装与 Server 的项目管理 API
 */
export class ProjectService {
    /**
     * 获取当前项目状态
     */
    static async getStatus(): Promise<ProjectStatus> {
        const response = await axios.get<ProjectStatus>(`${API_BASE}/status`);
        return response.data;
    }

    /**
     * 打开 BCP 文件（带冲突检测）
     * @param bcpFilePath BCP 文件路径
     * @returns 加载结果（可能是 Success、Conflict 或 Error）
     */
    static async openProject(bcpFilePath: string): Promise<ProjectLoadResult> {
        try {
            const response = await axios.post<ProjectLoadResult>(`${API_BASE}/open`, {
                bcpFilePath
            });
            return response.data;
        } catch (error: any) {
            // 409 Conflict 也会抛出异常，需要从 response 中提取数据
            if (error.response?.status === 409) {
                return error.response.data as ProjectLoadResult;
            }
            // 其他错误
            return {
                status: 'Error',
                message: error.response?.data?.message || error.message || '打开项目失败'
            };
        }
    }

    /**
     * 解决冲突
     * @param bcpFilePath BCP 文件路径
     * @param resolution 解决策略：Overwrite（覆盖）或 UseExisting（使用已存在）
     */
    static async resolveConflict(
        bcpFilePath: string,
        resolution: 'Overwrite' | 'UseExisting'
    ): Promise<ProjectLoadResult> {
        try {
            const response = await axios.post<ProjectLoadResult>(`${API_BASE}/resolve-conflict`, {
                bcpFilePath,
                resolution
            });
            return response.data;
        } catch (error: any) {
            return {
                status: 'Error',
                message: error.response?.data?.message || error.message || '解决冲突失败'
            };
        }
    }
}
