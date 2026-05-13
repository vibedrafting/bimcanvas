import axios from 'axios';
import { SERVER_API } from '../config/api';

const API_BASE = `${SERVER_API}/scheme`;

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
 * 模块布置变体描述（来自 module-relocation-agent 产出）
 *
 * v1.1：sidecar 文件已废弃，所有元数据内嵌进变体文件本体的 wrapper.summary 字段。
 * 服务端 ListVariants 解析每个变体文件的 summary 后回填到这里。
 */
export interface VariantDescriptor {
    variantId: string;
    filename: string;
    leafZonePath: string;
    /** 一句话描述本变体核心改动，用于 chip tooltip。变体文件不含 summary 时为空字符串。 */
    summary: string;
}

/**
 * 采纳变体的请求体
 */
export interface AdoptVariantRequest {
    variantId: string;
    leafZonePath: string;
}

/**
 * /api/scheme/variants/summary 的字典值。variantIds 按字典序，
 * 与单分区的 listVariants 顺序一致。
 */
export interface VariantSummaryEntry {
    count: number;
    variantIds: string[];
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
     * @param variant 可选的变体描述：{ leafZonePath, variantId } 命中时打变体接口
     */
    static async getModules(
        source: string,
        variant?: { leafZonePath: string; variantId: string }
    ): Promise<SchemeModulesResponse> {
        if (variant && variant.variantId && variant.leafZonePath) {
            const response = await axios.get<SchemeModulesResponse>(
                `${API_BASE}/variant/${encodeURIComponent(variant.variantId)}/modules`,
                { params: { leafZonePath: variant.leafZonePath } }
            );
            return response.data;
        }
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

    /**
     * 列出指定叶子分区下的所有变体方案
     * @param leafZonePath 叶子分区相对 schemes/ 的路径，如 "rz_3/dz_1"
     */
    static async listVariants(leafZonePath: string): Promise<VariantDescriptor[]> {
        const response = await axios.get<VariantDescriptor[]>(
            `${API_BASE}/variants`,
            { params: { leafZonePath } }
        );
        return response.data;
    }

    /**
     * 一次性拉取 "哪些叶子分区有几份变体 + 它们的 variantId 列表" 的字典，零变体的叶子不入字典。
     * variantIds 已按字典序排序，与 listVariants 输出顺序一致；Web 端用它来反查 active variant
     * 在序列中的位置，从而在 zone label 上渲染 (current/total) 分页号。
     */
    static async listVariantsSummary(): Promise<Record<string, VariantSummaryEntry>> {
        const response = await axios.get<Record<string, VariantSummaryEntry>>(
            `${API_BASE}/variants/summary`
        );
        return response.data ?? {};
    }

    /**
     * 采纳某个变体（轮换语义）：旧 canonical 降级为 modules-alt-prev-{ts}.json，
     * 被采纳的 variant 晋升为 canonical 并删除其原变体文件，其他 alt 文件保留。
     */
    static async adoptVariant(request: AdoptVariantRequest): Promise<{
        success: boolean;
        adopted: string;
        archivedAs: string | null;
        deletedVariants: string[];
    }> {
        const response = await axios.post(`${API_BASE}/variant/adopt`, request);
        return response.data;
    }

    /**
     * 删除指定的可变方案（modules-{variantId}.json + sidecar）。不动 canonical / 其他 alt。
     */
    static async deleteVariant(request: { variantId: string; leafZonePath: string }): Promise<{
        success: boolean;
        deleted: string;
        deletedFiles: string[];
    }> {
        const response = await axios.delete(
            `${API_BASE}/variant/${encodeURIComponent(request.variantId)}`,
            { params: { leafZonePath: request.leafZonePath } }
        );
        return response.data;
    }
}
