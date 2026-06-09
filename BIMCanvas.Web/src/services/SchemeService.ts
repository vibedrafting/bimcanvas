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
 * Server 派生的方案元数据（GET /api/scheme/variants 载荷元素）。Web 仅消费。
 * state: adopted / hidden / variant
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
 * GET /api/scheme/variants/{dz}/{slug}/zones 的返回结构。
 * subZones：该方案 slug 的有效分区（与 GetVariantModules 对称）；单叶子候选为空数组。
 */
export interface VariantZonesResponse {
    designZoneId: string;
    variantSlug: string;
    subZones: any[];
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
     * 获取指定 design zone + 方案 slug 的有效分区（SubZones），供实时切换候选方案时刷新分区线。
     * 与 getModules 的变体路径对称；Server 复用 BuildEffectiveZoneView(by variantId) 同一塑形源。
     */
    static async getVariantZones(
        designZoneId: string,
        variantSlug: string
    ): Promise<VariantZonesResponse> {
        const response = await axios.get<VariantZonesResponse>(
            `${API_BASE}/variants/${encodeURIComponent(designZoneId)}/${encodeURIComponent(variantSlug)}/zones`
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
     * 列出指定 design zone 下所有方案（按 schemes/{dz}/ 子目录枚举；state: adopted/hidden/variant）。
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
     * 采纳方案：翻转父 {dz}/DESIGN.md 的 adopted 指针使其生效（零复制 / 零删除 / 零降级、可逆）。
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
     * 通用 artifact 读取(scene-agnostic)。
     *
     * 调用 `GET /api/scheme/artifacts/{artifactKind}`(SceneArtifactsController)。
     * artifactKind 枚举:
     * - `modules`:聚合返回 schemes/ 下所有叶子 modules.json
     * - `zones`:schemes/zones.json(原始 JSON)
     * - `semantic_plan` / `reference_analysis`:聚合 schemes/ 下各 zoneId 的对应文件
     * - `readme`:项目根 README.md(平台级 baseline)
     *
     * 返回结构因 artifactKind 而异;调用方负责按需解析。404 时抛错。
     * （sceneId 形参保留兼容调用方;回退后数据按物理 zone 组织,URL 不再带 sceneId 段。）
     */
    static async getSceneArtifact(
        _sceneId: string,
        artifactKind: 'modules' | 'zones' | 'semantic_plan' | 'reference_analysis' | 'readme'
    ): Promise<any> {
        const response = await axios.get(
            `${API_BASE}/artifacts/${artifactKind}`
        );
        return response.data;
    }
}
