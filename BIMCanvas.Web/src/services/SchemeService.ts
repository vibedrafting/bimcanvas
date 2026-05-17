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
 * Server 派生的 variant 元数据（GET /api/scheme/variants 载荷元素）。Web 仅消费。
 * state: variant / prev-adopted / unknown
 */
export interface VariantDescriptor {
    slug: string;
    createdAt: string | null;
    state: string;
    summary: string;
}

/**
 * GET /api/scheme/variants 的返回结构。
 */
export interface VariantListResponse {
    designZoneId: string;
    variants: VariantDescriptor[];
}

/**
 * 采纳变体的请求体。
 */
export interface AdoptVariantRequest {
    designZoneId: string;
    variantSlug: string;
}

/**
 * /api/scheme/variants/summary 的字典值（designZone-level 索引）。
 * variantSlugs 按目录创建时间升序排序，与 listVariants 顺序一致；用于反查 active variant 在序列中的位置。
 */
export interface VariantSummaryEntry {
    count: number;
    variantSlugs: string[];
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
     * 获取模块数据。
     * variant.variantSlug 非空 → 走 New 路径变体 endpoint；否则走 canonical。
     */
    static async getModules(
        source: string,
        variant?: { designZoneId: string; leafZoneId: string; variantSlug: string }
    ): Promise<SchemeModulesResponse> {
        if (variant && variant.variantSlug && variant.designZoneId && variant.leafZoneId) {
            const response = await axios.get<SchemeModulesResponse>(
                `${API_BASE}/variants/${encodeURIComponent(variant.designZoneId)}/${encodeURIComponent(variant.variantSlug)}/modules`,
                { params: { leafZoneId: variant.leafZoneId } }
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
     * 列出指定 design zone 下所有变体（含 prev-* 降级目录）。
     */
    static async listVariants(designZoneId: string): Promise<VariantDescriptor[]> {
        const response = await axios.get<VariantListResponse>(
            `${API_BASE}/variants`,
            { params: { designZoneId } }
        );
        return response.data?.variants ?? [];
    }

    /**
     * 按 designZoneId 索引的变体计数摘要；零变体的 design zone 不入字典。
     * Web 端用 variantSlugs 反查 active variant 序列位置，渲染 zone label 上的 (current/total) 分页号。
     */
    static async listVariantsSummary(): Promise<Record<string, VariantSummaryEntry>> {
        const response = await axios.get<Record<string, VariantSummaryEntry>>(
            `${API_BASE}/variants/summary`
        );
        return response.data ?? {};
    }

    /**
     * 采纳变体：检测 canonical → 降级（如非空，生成 prev-{ts}）→ 晋升被采纳变体 → 删除原 variant 目录。
     */
    static async adoptVariant(request: AdoptVariantRequest): Promise<{
        success: boolean;
        adopted: string;
        designZoneId: string;
        demotedSlug: string | null;
    }> {
        const response = await axios.post(`${API_BASE}/variant/adopt`, request);
        return response.data;
    }

    /**
     * 删除变体目录 schemes/{designZoneId}/variants/{variantSlug}/（含 semantic_plan + modules）。
     */
    static async deleteVariant(request: { designZoneId: string; variantSlug: string }): Promise<{
        success: boolean;
        deleted: string;
        designZoneId: string;
    }> {
        const response = await axios.delete(
            `${API_BASE}/variant`,
            { params: { designZoneId: request.designZoneId, variantSlug: request.variantSlug } }
        );
        return response.data;
    }

    /**
     * (组 5 §5.C.2) 跨 scene 只读 artifact 读取。
     *
     * 调用 `GET /api/scheme/scenes/{sceneId}/{artifactKind}`(SceneArtifactsController)。
     * artifactKind 枚举:
     * - `modules`:聚合返回该 scene 下所有叶子 modules.json
     * - `zones`:全 scene 共享 schemes/zones.json(原始 JSON)
     * - `semantic_plan` / `reference_analysis`:聚合该 scene 下各 designZoneId 的对应文件
     * - `readme`:项目根 README.md(平台级 baseline)
     *
     * 返回结构因 artifactKind 而异;调用方负责按需解析。404 时抛错。
     */
    static async getSceneArtifact(
        sceneId: string,
        artifactKind: 'modules' | 'zones' | 'semantic_plan' | 'reference_analysis' | 'readme'
    ): Promise<any> {
        const response = await axios.get(
            `${API_BASE}/scenes/${encodeURIComponent(sceneId)}/${artifactKind}`
        );
        return response.data;
    }
}
