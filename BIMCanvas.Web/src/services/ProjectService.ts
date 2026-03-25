import axios from 'axios';
import type { ProjectSummary, RecentProjectEntry, CloseProjectResult } from '../types/homepage';

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
    warnings?: string[];
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
     * 上传 BCP 文件（带冲突检测）
     * @param file BCP 文件
     * @returns 加载结果（可能是 Success、Conflict 或 Error）
     */
    static async uploadProject(file: File): Promise<ProjectLoadResult> {
        try {
            const formData = new FormData();
            formData.append('file', file);

            const response = await axios.post<ProjectLoadResult>(
                `${API_BASE}/upload`,
                formData,
                {
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    }
                }
            );
            return response.data;
        } catch (error: any) {
            // 409 Conflict 也会抛出异常，需要从 response 中提取数据
            if (error.response?.status === 409) {
                return error.response.data as ProjectLoadResult;
            }
            // 其他错误
            return {
                status: 'Error',
                message: error.response?.data?.message || error.message || '上传项目失败'
            };
        }
    }

    /**
     * 解决上传文件的冲突
     * @param file BCP 文件
     * @param resolution 解决策略：Overwrite（覆盖）或 UseExisting（使用已存在）
     */
    static async uploadResolveConflict(
        file: File,
        resolution: 'Overwrite' | 'UseExisting'
    ): Promise<ProjectLoadResult> {
        try {
            const formData = new FormData();
            formData.append('file', file);

            const response = await axios.post<ProjectLoadResult>(
                `${API_BASE}/upload-resolve?resolution=${resolution}`,
                formData,
                {
                    headers: {
                        'Content-Type': 'multipart/form-data'
                    }
                }
            );
            return response.data;
        } catch (error: any) {
            return {
                status: 'Error',
                message: error.response?.data?.message || error.message || '解决冲突失败'
            };
        }
    }

    /**
     * 打开 BCP 文件（带冲突检测）- 基于路径（仅供服务端场景使用）
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
     * 解决冲突 - 基于路径（仅供服务端场景使用）
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

    // ========== 首页 API ==========

    /**
     * 获取项目列表（扫描默认目录）
     * Server 返回 ProjectSummary[] 数组
     */
    static async listProjects(): Promise<ProjectSummary[]> {
        const response = await axios.get<ProjectSummary[]>(`${API_BASE}/list`);
        return response.data;
    }

    /**
     * 获取最近打开记录
     * Server 返回 RecentProjectEntry[] 数组
     */
    static async getRecentProjects(): Promise<RecentProjectEntry[]> {
        const response = await axios.get<RecentProjectEntry[]>(`${API_BASE}/recent`);
        return response.data;
    }

    /**
     * 打开项目文件夹（从首页选择）
     * Server 返回 ProjectLoadResult { Status, ProjectPath, Message }
     */
    static async openFolder(folderPath: string): Promise<ProjectLoadResult> {
        try {
            const response = await axios.post<ProjectLoadResult>(`${API_BASE}/open-folder`, {
                folderPath
            });
            return response.data;
        } catch (error: any) {
            return {
                status: 'Error',
                message: error.response?.data?.message || error.message || '打开项目失败'
            };
        }
    }

    /**
     * 关闭当前项目
     * Server 返回格式：
     * - 成功 200: { message: "项目已关闭" }
     * - 未保存变更 409: { message: "...", hasUncommittedChanges: true }
     * 前端适配为统一的 CloseProjectResult
     */
    static async closeProject(force: boolean = false): Promise<CloseProjectResult> {
        try {
            const response = await axios.post(
                `${API_BASE}/close`,
                { force }
            );
            return {
                success: true,
                hasUnsavedChanges: false,
                message: response.data.message
            };
        } catch (error: any) {
            // 409 Conflict: 有未保存变更
            if (error.response?.status === 409) {
                return {
                    success: false,
                    hasUnsavedChanges: true,
                    message: error.response.data.message
                };
            }
            return {
                success: false,
                hasUnsavedChanges: false,
                message: error.response?.data?.message || error.message || '关闭项目失败'
            };
        }
    }

    /**
     * 删除项目
     * Server 返回: { message: "..." }
     * 成功 200 / 禁止 400 / 不存在 404 / 错误 500
     */
    static async deleteProject(name: string): Promise<{ success: boolean; message?: string }> {
        try {
            const response = await axios.delete(`${API_BASE}/${encodeURIComponent(name)}`);
            return { success: true, message: response.data.message };
        } catch (error: any) {
            return {
                success: false,
                message: error.response?.data?.message || error.message || '删除项目失败'
            };
        }
    }

    /**
     * 导出当前项目为 BCP 文件
     * @returns Blob 对象和建议的文件名
     */
    static async exportProject(): Promise<{ blob: Blob; filename: string }> {
        const response = await axios.get(`${API_BASE}/export`, {
            responseType: 'blob'
        });

        // 从 Content-Disposition 头获取文件名
        const contentDisposition = response.headers['content-disposition'];
        let filename = 'BIMCanvas_export.bcp';
        if (contentDisposition) {
            const match = contentDisposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
            if (match && match[1]) {
                filename = match[1].replace(/['"]/g, '');
            }
        }

        return { blob: response.data, filename };
    }
}
