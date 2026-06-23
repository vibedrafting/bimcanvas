import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { ProjectData, Module, Zone, Wall, Column, Opening } from '../types/canvas';
import { StrategyApproach, StrategyStatus } from '../types/canvas';
import { TimelineManager } from '../services/state/TimelineManager';
import { VariantHistory, type EditTarget } from '../services/state/VariantHistory';
import { createLogger } from '../utils/logger';
import { ChangeSource, ChangeType, type LoadOptions } from '../types/history';
import { moduleLibraryService } from '../services/ModuleLibraryService';
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { supports } from '../runtime/WebRuntimeProtocol';
import { SchemeService } from '../services/SchemeService';
import { useSystemStore } from './systemStore';
export const useCanvasStore = defineStore('canvas', () => {
    const runtime = getWebRuntime();
    // === 核心状态 ===
    const projectData = ref<ProjectData | null>(null);
    const isLoading = ref(false);
    const error = ref<string | null>(null);
    const promptMessage = ref<string | null>(null);

    // === 脏数据标记：追踪内存中是否有未保存的修改 ===
    const isDirty = ref(false);

    // === 视图保持标记：加载项目时是否保持当前视图（用于分支切换） ===
    const preserveViewOnLoad = ref(false);

    // === 截图渲染模式：用于后台截图页 ===
    const isScreenshotRender = ref(false);

    // === 禁止自动重建：截图页手动控制渲染流程 ===
    const suppressAutoBuild = ref(false);

    // 用于跳过一次由本地写入触发的 ServerSync（避免撤回/重做清空 redo 栈）
    let pendingServerSyncSkips = 0;

    // === 多选支持 ===
    const selectedIds = ref<string[]>([]);

    // 兼容层：selectedObject 返回第一个选中对象
    const selectedObject = computed(() => {
        if (selectedIds.value.length === 0 || !projectData.value) return null;
        const firstId = selectedIds.value[0];
        if (!firstId) return null;
        return findObjectById(firstId);
    });

    // 返回所有选中对象
    const selectedObjects = computed(() => {
        if (selectedIds.value.length === 0 || !projectData.value) return [];
        return selectedIds.value
            .map(id => findObjectById(id))
            .filter((obj): obj is NonNullable<typeof obj> => obj !== null);
    });

    // Scene 数据缓存：用于竞态条件下的降级回退
    // 当 Scene mesh 的 userData.data 与 Store 数据暂时不同步时，
    // 使用缓存的 Scene 数据确保属性面板仍可展示
    const sceneDataCache = new Map<string, any>();

    // 辅助函数：在所有对象类型中查找
    // 注意：此函数在 computed 中调用，禁止在此写 reactive 日志 buffer（logger 会 unshift logBuffer，
    // 在 computed 内触发响应式副作用导致无限循环）——故此函数内不打日志。
    const findObjectById = (id: string): any | null => {
        if (!projectData.value) {
            return null;
        }

        const baseline = projectData.value.baseline;
        const activeScheme = projectData.value.activeScheme;

        // 在 modules 中查找
        const module = activeScheme?.modules?.find(m => m.id === id);
        if (module) {
            return { ...module, type: 'module' };
        }

        // 在 walls 中查找
        const wall = baseline?.walls?.find(w => w.id === id);
        if (wall) {
            return { ...wall, type: 'wall' };
        }

        // 在 columns 中查找
        const column = baseline?.columns?.find(c => c.id === id);
        if (column) {
            return { ...column, type: 'column' };
        }

        // 在 openings 中查找
        const opening = baseline?.openings?.find(o => o.id === id);
        if (opening) {
            const typeName = opening.type === 0 ? 'door' : 'window';
            return { ...opening, type: typeName };
        }

        // 在 activeScheme.zones 中查找（设计区域，含嵌套子分区）
        const schemeZone = activeScheme?.zones?.find(z => z.id === id);
        if (schemeZone) {
            return { ...schemeZone, type: 'zone' };
        }
        // 搜索嵌套子分区
        for (const z of activeScheme?.zones ?? []) {
            const subZone = z.subZones?.find(sz => sz.id === id);
            if (subZone) {
                return { ...subZone, type: 'zone', parentZoneId: z.id };
            }
        }

        // 在 computed.roomZones 中查找（房间区域）
        const computed = projectData.value.computed;
        const computedZone = computed?.roomZones?.find(z => z.id === id);
        if (computedZone) {
            return { ...computedZone, type: 'zone' };
        }

        // 在 computed.exclusions 中查找（禁区）
        const exclusion = computed?.exclusions?.find(e => e.id === id);
        if (exclusion) {
            return { ...exclusion, type: 'exclusion' };
        }

        // 降级：使用 Scene 数据缓存（解决 Scene 与 Store 竞态不同步）
        const cached = sceneDataCache.get(id);
        if (cached) {
            return cached;
        }

        return null;
    };

    const debugMsg = ref<string>('');

    // timeline 仅保留「加载时视图策略」职责（shouldPreserveView）；撤销/重做的栈职责已迁到 VariantHistory。
    const timeline = new TimelineManager();
    // 家具撤销/重做：按编辑目标 (designZone, variant) 分栈，与「纯指针式平级 + Zone 递归」模型对齐。
    const history = new VariantHistory();
    // 当前撤销目标：跟随选择（选中模块所属设计区×变体），无选择则取末次编辑目标。Ctrl+Z/Y 只作用于它。
    const activeUndoTarget = ref<EditTarget | null>(null);
    const sysLog = createLogger('SYS');
    const recvLog = createLogger('RECV');
    const userLog = createLogger('USER');

    const dispatchLocalUpdate = (detail: Record<string, unknown>) => {
        if (supports(runtime.capabilities.realtimeProjectSync)) {
            window.dispatchEvent(new CustomEvent('bimcanvas:local-update', { detail }));
        }
    };

    // === 变体方案（按设计区索引）===
    // activeVariantByDesignZone：每个设计区显示哪一份 modules（缺失 = canonical）。
    // 同一设计区下所有叶子共享一份 variantSlug——variant 是设计区级别的方案。
    // 仅存内存，刷新页面 / 重启 Web 都重置为 canonical（不写 project.json）。
    const activeVariantByDesignZone = ref<Map<string, string>>(new Map());

    // canonical 快照：在每次 applyProjectData 时记录服务端发回的 canonical modules，
    // 切换/取消变体时基于该快照重组 projectData.activeScheme.modules，避免反复打服务端。
    const canonicalModulesSnapshot = ref<Module[] | null>(null);

    // canonical zones 快照（含 subZones）：同 canonicalModulesSnapshot，供来源② 切换时 patch / 还原 adopted 分区线。
    const canonicalZonesSnapshot = ref<Zone[] | null>(null);

    // variantInfoByDesignZone：项目级缓存"哪些设计区有几份变体 + slug 列表 + 是否已有采纳"。
    // 键为 designZoneId。Zone label 上 (current/total) 分页号——
    // current 通过 active variantSlug 在 variantSlugs 列表里的 index 反算而来。
    // hasAdopted=false（多方案待用户终选）时角标不含 canonical 槽，且自动激活首个变体显示。
    interface VariantInfo {
        count: number;
        variantSlugs: string[];
        hasAdopted: boolean;
        // 被采纳方案的具体 slug（无采纳时 null）；供画布选中上下文告知 AI「已采纳哪个」。
        adoptedSlug: string | null;
    }
    const variantInfoByDesignZone = ref<Map<string, VariantInfo>>(new Map());

    // Server 派生的 variant 元数据缓存（state / summary / createdAt），供 VariantNavigatorBar 显示样式区分与 chip tooltip。
    interface VariantMetadataLite {
        slug: string;
        createdAt: string | null;
        state: string;
        summary: string;
    }
    const variantMetadataByDesignZone = ref<Map<string, Map<string, VariantMetadataLite>>>(new Map());

    /**
     * 从任意 zoneId 反查所属 designZoneId。
     * 顶层 zone（projectData.activeScheme.zones 中直接命中）→ 自身；子叶子（subZones 中命中）→ 顶层 zone id。
     */
    function resolveDesignZoneId(zoneId: string | null | undefined): string | null {
        if (!zoneId || !projectData.value?.activeScheme?.zones) return null;
        for (const z of projectData.value.activeScheme.zones) {
            if (z.id === zoneId) return z.id;
            if (z.subZones?.some(sz => sz.id === zoneId)) return z.id;
        }
        return null;
    }

    /**
     * 列出指定设计区下所有叶子 zoneId。
     * 顶层叶子（design zone 自身就是叶子，无 subZones）→ [designZoneId]；容器 → subZones 的 id 列表。
     */
    function getLeafZoneIdsForDesignZone(designZoneId: string): string[] {
        if (!projectData.value?.activeScheme?.zones) return [];
        const dz = projectData.value.activeScheme.zones.find(z => z.id === designZoneId);
        if (!dz) return [];
        if (dz.subZones && dz.subZones.length > 0) {
            return dz.subZones.map(sz => sz.id).filter((id): id is string => !!id);
        }
        return [designZoneId];
    }

    function getActiveVariant(designZoneId: string): string | null {
        return activeVariantByDesignZone.value.get(designZoneId) ?? null;
    }

    /**
     * 计算某 zone 在 [canonical, ...sortedVariants] 序列中的"当前 / 总数"页码。
     * 接受任意 zoneId（顶层 design zone 或子叶子），内部反查 designZoneId。
     * 没有变体时返回 null（label 不显示后缀）。
     */
    function getVariantSlot(zoneId: string): { current: number; total: number } | null {
        const dz = resolveDesignZoneId(zoneId);
        if (!dz) return null;
        // 角标只在设计区根（rz_*/单叶子 dz）出；叶子 subZone 不挂（判据=自身是否设计区，不为 rz_/dz_ 写特例，
        // 天然兼容场景②来源①——届时叶子本身是设计区会自动显示叶子级角标）。
        if (dz !== zoneId) return null;
        const info = variantInfoByDesignZone.value.get(dz);
        if (!info || info.count <= 0) return null;
        // canonical 槽只在确有 adopted 方案时存在——口径与 VariantNavigatorBar 的 sequence 一致；
        // 无 adopted（多方案待用户终选）时 total 不再 +1，避免 (1/2) 与导航条 (1/1) 漂移。
        const total = info.count + (info.hasAdopted ? 1 : 0);
        const activeSlug = activeVariantByDesignZone.value.get(dz) ?? null;
        if (!activeSlug) return { current: 1, total };
        const idx = info.variantSlugs.indexOf(activeSlug);
        return { current: idx >= 0 ? idx + (info.hasAdopted ? 2 : 1) : 1, total };
    }

    /**
     * 拉取项目级变体摘要（designZoneId → {count, variantSlugs}），写入 variantInfoByDesignZone，
     * 并派发 bimcanvas:variant-counts-changed 让 ThreeSceneService 触发 label 重建。
     * 失败时静默清空 Map（视觉上回到"没有变体"，不抛错）。
     */
    async function refetchVariantCounts(): Promise<void> {
        try {
            const dict = await SchemeService.listVariantsSummary();
            const next = new Map<string, VariantInfo>();
            for (const [designZoneId, rawEntry] of Object.entries(dict)) {
                if (!designZoneId || rawEntry == null) continue;
                const count = (rawEntry as { count?: number }).count ?? 0;
                const variantSlugs = Array.isArray((rawEntry as any).variantSlugs)
                    ? [...(rawEntry as { variantSlugs: string[] }).variantSlugs]
                    : [];
                if (count <= 0) continue;
                const hasAdopted = (rawEntry as { hasAdopted?: boolean }).hasAdopted === true;
                const adoptedSlug = (rawEntry as { adoptedSlug?: string | null }).adoptedSlug ?? null;
                next.set(designZoneId, { count, variantSlugs, hasAdopted, adoptedSlug });
            }
            variantInfoByDesignZone.value = next;

            // 无 adopted 的设计区：自动激活首个可见变体，避免画布默认渲染空 canonical。
            // （多方案待用户终选时无 adopted 是常态终态——不自动激活则 Agent 设计完成后画布一片空白，
            //  需要用户手动点导航条切换才看得见方案。）已有 active / 已有 adopted 的设计区不动。
            for (const [dz, info] of next) {
                const firstSlug = info.variantSlugs[0];
                if (!info.hasAdopted && firstSlug && !activeVariantByDesignZone.value.has(dz)) {
                    void setActiveVariant(dz, firstSlug);
                }
            }
        } catch (err: any) {
            sysLog.warn('variant summary fetch failed', { err: err?.message ?? err });
            variantInfoByDesignZone.value = new Map();
        } finally {
            window.dispatchEvent(new CustomEvent('bimcanvas:variant-counts-changed', {
                detail: { size: variantInfoByDesignZone.value.size }
            }));
        }
    }

    /**
     * VariantNavigatorBar 调 listVariants 后回填某设计区的 variant 元数据列表，供样式与 tooltip 消费。
     */
    function cacheVariantMetadata(designZoneId: string, list: VariantMetadataLite[]): void {
        const inner = new Map<string, VariantMetadataLite>();
        for (const m of list) {
            if (m?.slug) inner.set(m.slug, m);
        }
        const next = new Map(variantMetadataByDesignZone.value);
        next.set(designZoneId, inner);
        variantMetadataByDesignZone.value = next;
    }

    /**
     * 切换某设计区的活跃变体。
     * - variantSlug 为空 → 还原该设计区为 canonical
     * - variantSlug 非空 → 拉变体下各叶子 modules，替换该设计区的 canonical 内容
     */
    async function setActiveVariant(
        designZoneId: string,
        variantSlug: string | null
    ): Promise<void> {
        if (!designZoneId) {
            sysLog.warn('setActiveVariant: empty designZoneId');
            return;
        }
        if (!variantSlug) {
            if (activeVariantByDesignZone.value.has(designZoneId)) {
                activeVariantByDesignZone.value.delete(designZoneId);
                activeVariantByDesignZone.value = new Map(activeVariantByDesignZone.value);
                await recomputeDisplayModules();
            }
            return;
        }
        activeVariantByDesignZone.value.set(designZoneId, variantSlug);
        activeVariantByDesignZone.value = new Map(activeVariantByDesignZone.value);
        await recomputeDisplayModules();
    }

    async function clearActiveVariant(designZoneId: string): Promise<void> {
        if (activeVariantByDesignZone.value.has(designZoneId)) {
            activeVariantByDesignZone.value.delete(designZoneId);
            activeVariantByDesignZone.value = new Map(activeVariantByDesignZone.value);
            await recomputeDisplayModules();
        }
    }

    /**
     * 基于 canonical 快照 + 当前 activeVariantByDesignZone 重组 projectData.activeScheme.modules。
     * 流程：(1) 构建 leaf→designZone 反查表；(2) 从 canonical 过滤掉所有"有 active 变体"的设计区下所有叶子的模块；
     *      (3) 对每个 (designZoneId, variantSlug) 拉每个叶子的 variant modules 合并；
     *      (4) 写回 projectData.activeScheme.modules，触发响应式刷新。
     * 任一叶子拉取失败 → 该叶子保留为空（不打断其他叶子；不撤 designZone 整体激活）。
     * 不再因 canonical 为空 early-return：multi-plan 模式下 canonical modules 本就可能为空。
     */
    async function recomputeDisplayModules(): Promise<void> {
        if (!projectData.value || !projectData.value.activeScheme) return;

        // 来源②：先按候选刷新分区线/叶子，模块再按"正确的方案叶子"拉取（避免按 adopted 叶子取错）。
        await recomputeDisplayZones();

        const activeMap = activeVariantByDesignZone.value;
        const baseSnapshot = canonicalModulesSnapshot.value ?? [];

        // 构建 leafZoneId → designZoneId 反查表（含顶层叶子自映射）。
        // 关键：必须用 canonical zones 快照，而非已被 recomputeDisplayZones 改写成「变体拓扑」的 live zones。
        // baseSnapshot 是 canonical 模块，其 zoneId 是 canonical 叶子 id；若用变体拓扑的 live zones 建表，
        // canonical 与所切变体叶子 id 不一致的设计区会映射失败（get→undefined→dz=''），该设计区的 canonical
        // 模块漏过下方过滤、与变体模块叠加渲染（切换变体重叠 bug）。拉变体模块那一步仍用 live 拓扑（见下方
        // getLeafZoneIdsForDesignZone），两处拓扑需求相反，不要混用。
        const filterZones = canonicalZonesSnapshot.value ?? projectData.value.activeScheme.zones ?? [];
        const leafToDesignZone = new Map<string, string>();
        for (const z of filterZones) {
            if (!z.id) continue;
            if (z.subZones && z.subZones.length > 0) {
                for (const sz of z.subZones) {
                    if (sz.id) leafToDesignZone.set(sz.id, z.id);
                }
            } else {
                leafToDesignZone.set(z.id, z.id);
            }
        }

        // (1) canonical 过滤：保留模块所属 designZone 未被激活的项
        const activeDesignZoneIds = new Set(activeMap.keys());
        const baseModules = baseSnapshot.filter(m => {
            const dz = leafToDesignZone.get(m.zoneId ?? '') ?? '';
            return !activeDesignZoneIds.has(dz);
        });

        // (2) 对每个 active designZone 拉所有叶子的变体 modules
        const variantBlocks: Module[][] = [];
        for (const [designZoneId, variantSlug] of Array.from(activeMap.entries())) {
            const leafIds = getLeafZoneIdsForDesignZone(designZoneId);
            for (const leafZoneId of leafIds) {
                try {
                    const resp = await SchemeService.getModules('main', {
                        designZoneId, leafZoneId, variantSlug
                    });
                    const variantModules = (resp.modules ?? []).map(m => ({
                        ...m,
                        zoneId: (m as any).zoneId ?? leafZoneId
                    })) as Module[];
                    variantBlocks.push(variantModules);
                } catch (err: any) {
                    // 单叶子 404/失败不致命：该叶子展示为空，不撤 designZone 整体激活
                    sysLog.warn('variant leaf load failed', { dz: designZoneId, slug: variantSlug, leaf: leafZoneId, err: err?.message ?? err });
                }
            }
        }

        // (3) 写回（使用展开避免 reactive 丢失）
        projectData.value.activeScheme.modules = [
            ...baseModules,
            ...variantBlocks.flat()
        ];

        // 切换变体后：为新目标播种 baseline（栈空才播），并把 activeUndoTarget 的 slug 同步到当前变体，
        // 避免 Ctrl+Z 仍指向旧变体目标（结构性杜绝跨变体撤销）。
        seedVisibleBaselines();
        if (activeUndoTarget.value) {
            activeUndoTarget.value = targetForDesignZone(activeUndoTarget.value.designZoneId);
        }
        refreshUndoState();
    }

    /**
     * 来源②：按当前 active 变体刷新各设计区 subZones（分区线跟随候选方案），从 adopted 快照重建后逐个 patch。
     * 复用 Server GetVariantZones（内部 BuildEffectiveZoneView 同一塑形源，不另造解析）；
     * 单叶子候选返回空 subZones→该设计区按"无内部分区"渲染；拉取失败保留 adopted 分区线、不撤激活；
     * 切回 canonical（active 删除）→ 不 patch，自然还原 adopted。
     */
    async function recomputeDisplayZones(): Promise<void> {
        if (!projectData.value?.activeScheme) return;
        const snapshot = canonicalZonesSnapshot.value;
        if (!snapshot) return;
        const nextZones: Zone[] = JSON.parse(JSON.stringify(snapshot));
        for (const [designZoneId, variantSlug] of Array.from(activeVariantByDesignZone.value.entries())) {
            try {
                const resp = await SchemeService.getVariantZones(designZoneId, variantSlug);
                const root = nextZones.find(z => z.id === designZoneId);
                if (root) root.subZones = (resp.subZones ?? []) as Zone[];
            } catch (err: any) {
                sysLog.warn('variant zones load failed', { dz: designZoneId, slug: variantSlug, err: err?.message ?? err });
            }
        }
        projectData.value.activeScheme.zones = nextZones;
    }

    // ===== 撤销/重做：几何分组 + 按目标定向持久化（与指针式平级 + Zone 递归对齐）=====

    // 射线法点-多边形包含（与 Server CollisionDetector.Contains 等价语义）。
    function pointInPolygon(pt: [number, number], poly: Zone['rawBoundary']): boolean {
        if (!poly || poly.length < 3) return false;
        const [x, y] = pt;
        let inside = false;
        for (let i = 0, j = poly.length - 1; i < poly.length; j = i, i += 1) {
            const [xi, yi] = poly[i]!;
            const [xj, yj] = poly[j]!;
            const intersects = ((yi > y) !== (yj > y)) &&
                (x < ((xj - xi) * (y - yi)) / ((yj - yi) || Number.EPSILON) + xi);
            if (intersects) inside = !inside;
        }
        return inside;
    }

    function moduleCenter(m: Module): [number, number] {
        const pts = m.bounds ?? [];
        if (pts.length === 0) return [NaN, NaN];
        let sx = 0, sy = 0;
        for (const p of pts) { sx += p[0]; sy += p[1]; }
        return [sx / pts.length, sy / pts.length];
    }

    // 顶层设计区列表（baseline 房间 rz_* / 单叶子 dz），用 canonical zones 快照（稳定房间边界）。
    function topLevelDesignZones(): Zone[] {
        return canonicalZonesSnapshot.value ?? projectData.value?.activeScheme?.zones ?? [];
    }

    // 按 bounds 几何把模块归到顶层设计区（与 Server 同语义：按房间边界，而非 stale 的 zoneId）。
    // 关键：移动跨区时模块 zoneId 是旧值，必须用当前 bounds 现算，才能让旧区/新区都被正确重写。
    function designZoneOfModule(m: Module): string | null {
        const center = moduleCenter(m);
        if (Number.isNaN(center[0])) return null;
        for (const z of topLevelDesignZones()) {
            if (!z.id) continue;
            const boundary = z.computedBoundary ?? z.rawBoundary;
            if (boundary && pointInPolygon(center, boundary)) return z.id;
        }
        return null;
    }

    function groupModulesByDesignZone(modules: Module[]): Map<string, Module[]> {
        const groups = new Map<string, Module[]>();
        for (const m of modules) {
            const dz = designZoneOfModule(m);
            if (!dz) continue;   // 孤儿（不落任何房间）——与 Server orphan 处理一致，跳过
            const arr = groups.get(dz) ?? [];
            arr.push(m);
            groups.set(dz, arr);
        }
        return groups;
    }

    function targetForDesignZone(dz: string): EditTarget {
        return { designZoneId: dz, variantSlug: activeVariantByDesignZone.value.get(dz) ?? null };
    }

    // 定向落盘：只写该设计区目标的叶子（scope=[dz]），范围外文件不碰。
    async function scopedSaveTarget(
        target: EditTarget,
        modules: Module[],
        opts?: { suppressServerSync?: boolean }
    ): Promise<boolean> {
        const variantSelection: Record<string, string> = {};
        if (target.variantSlug) variantSelection[target.designZoneId] = target.variantSlug;
        const saved = await runtime.saveModules(modules, variantSelection, [target.designZoneId]);
        if (saved && opts?.suppressServerSync && supports(runtime.capabilities.realtimeProjectSync)) {
            pendingServerSyncSkips += 1;
        }
        return saved;
    }

    // 投影建立后（加载 / 切换变体）为每个可见顶层设计区播种 baseline（栈空才播），
    // 使首次编辑可撤回到投影初态。空模块设计区也播（空数组），保证首次 add 可撤销。
    function seedVisibleBaselines(): void {
        if (!projectData.value?.activeScheme) return;
        const groups = groupModulesByDesignZone(projectData.value.activeScheme.modules ?? []);
        for (const z of topLevelDesignZones()) {
            if (!z.id) continue;
            history.seedBaseline(targetForDesignZone(z.id), groups.get(z.id) ?? []);
        }
    }

    function modulesEqual(a: Module[] | null, b: Module[]): boolean {
        if (a === null) return false;
        return JSON.stringify(a) === JSON.stringify(b);
    }

    // 编辑提交：对每个发生变化的顶层设计区，push 历史 + 定向落盘（仅变化区，未变区不碰）。
    // 跨区移动时旧区（少了模块）与新区（多了模块）都「变化」，故都会被正确重写。
    async function persistAndRecordEdits(opts?: { suppressServerSync?: boolean }): Promise<void> {
        if (!projectData.value?.activeScheme) return;
        const groups = groupModulesByDesignZone(projectData.value.activeScheme.modules ?? []);
        for (const z of topLevelDesignZones()) {
            if (!z.id) continue;
            const target = targetForDesignZone(z.id);
            const slice = groups.get(z.id) ?? [];
            if (modulesEqual(history.peek(target), slice)) continue;   // 该区未变，不写不记
            history.push(target, slice, { changeType: ChangeType.Update });
            activeUndoTarget.value = target;
            await scopedSaveTarget(target, slice, opts);
        }
        refreshUndoState();
    }

    // 把某目标的模块应用回合并视图（撤销/重做用）：替换该设计区的几何切片，并同步 canonical 快照。
    function applyTargetModules(target: EditTarget, mods: Module[]): void {
        if (!projectData.value?.activeScheme) return;
        const dz = target.designZoneId;
        const others = (projectData.value.activeScheme.modules ?? []).filter(m => designZoneOfModule(m) !== dz);
        projectData.value.activeScheme.modules = [...others, ...mods];
        // canonical 目标：同步 canonical 快照该区切片，保证后续 recompute 一致（修隐患3）。
        if (target.variantSlug === null && canonicalModulesSnapshot.value) {
            const snapOthers = canonicalModulesSnapshot.value.filter(m => designZoneOfModule(m) !== dz);
            canonicalModulesSnapshot.value = [...snapOthers, ...mods];
        }
    }

    function refreshUndoState(): void {
        canUndo.value = history.canUndo(activeUndoTarget.value);
        canRedo.value = history.canRedo(activeUndoTarget.value);
    }

    /**
     * Legacy 变体侧链文件名判定（modules-alt-*.json）。
     * 与 server 端 trigger=variant-files-changed 信号互补——后者覆盖 New 路径 variants/ 子树。
     */
    function isLegacyVariantSidecarFile(fileName: string | undefined): boolean {
        if (!fileName) return false;
        return fileName.toLowerCase().startsWith('modules-alt-')
            && fileName.toLowerCase().endsWith('.json');
    }

    // 监听 Server 推送的文件变化事件（文件驱动架构的核心链路）
    if (supports(runtime.capabilities.realtimeProjectSync)) {
      window.addEventListener('bimcanvas:server-update', async (e: any) => {
        const data = e.detail;
        recvLog.debug('server update received', { data });

        const fileName = data.file as string | undefined;

        const trigger = data.trigger as string | undefined;

        // 变体侧链：trigger=variant-files-changed（server 派发，覆盖 variants/ 子树）
        // 或 trigger=variant-cloned（clone endpoint 显式广播，走轻量 refetch 路径避免整 canvas reload）
        // 或 Legacy modules-alt-*.json 文件（兼容老项目残留）
        if (trigger === 'variant-files-changed' || trigger === 'variant-cloned' || isLegacyVariantSidecarFile(fileName)) {
            recvLog.debug('variant file changed', { file: fileName });
            window.dispatchEvent(new CustomEvent('bimcanvas:variant-files-changed', {
                detail: { file: fileName, trigger: data.trigger }
            }));
            void refetchVariantCounts();
            return;
        }

        if (data.action === 'reload') {
            // 采纳变体后服务端发 trigger=variant-adopt + file=modules.json + designZoneId
            // 此时仅清空被采纳设计区的 active 状态（其他设计区的 active 不动）
            if (trigger === 'variant-adopt') {
                const adoptedDz = data.designZoneId as string | undefined;
                if (adoptedDz && activeVariantByDesignZone.value.has(adoptedDz)) {
                    activeVariantByDesignZone.value.delete(adoptedDz);
                    activeVariantByDesignZone.value = new Map(activeVariantByDesignZone.value);
                    recvLog.debug('variant adopted, reload canonical', { dz: adoptedDz });
                }
                // 采纳=翻指针：该设计区 canonical 内容已变，旧家具历史失效
                if (adoptedDz) history.invalidate(adoptedDz);
                // 采纳=翻指针（不删目录、不生成 prev-*）；刷新计数字典让 Canvas 更新角标
                void refetchVariantCounts();
            }

            // Agent/重连/手动触发的更新：重置 skip 计数器，确保更新不被跳过
            if (trigger === 'agent' || trigger === 'reconnect' || trigger === 'manual' || trigger === 'variant-adopt') {
                pendingServerSyncSkips = 0;
                recvLog.debug('explicit trigger, reset skip counter', { trigger });
                // 真实远程改动（Agent/重连/手动）整工程重投影：全清家具历史，防陈旧本地 undo 覆盖远程结果。
                // variant-adopt 已按 dz 定向失效，不再全清。
                if (trigger !== 'variant-adopt') history.invalidate();
            } else if (fileName === 'modules.json' && pendingServerSyncSkips > 0) {
                // 仅 FileSystemWatcher 触发的普通更新才走 skip 逻辑
                pendingServerSyncSkips -= 1;
                recvLog.debug('skip local-write-triggered ServerSync');
                return;
            }

            // 保持当前视图，重新加载数据
            recvLog.debug('trigger data reload', { trigger: trigger || 'watcher' });
            await syncFromServer({ description: 'Server file changed', metadata: { trigger: trigger || 'watcher' } });
        }
      });
    }

    const agentConnectionState = ref<'Connected' | 'Disconnected' | 'Reconnecting'>('Disconnected');
    const currentOperation = ref<string | null>(null);

    window.addEventListener('bimcanvas:connection-state', (e: any) => {
        agentConnectionState.value = e.detail;
        sysLog.debug('connection state updated', { state: agentConnectionState.value });
    });

    const canUndo = ref(false);
    const canRedo = ref(false);

    // 批量更新模式
    const batchUpdateMode = ref(false);

    // 当前 PlaceTool 期望的尺寸 + 上下文（仅会话内，不持久化）。
    // - moduleId：让 PlacementSizeBar 反查 morphology 决定输入控件形态；
    // - width/depth：用户在 PlacementSizeBar 调整后的当前值，PlaceTool 实时读取重画预览 + 落库。
    // PlaceTool.activate 写入；deactivate 清为 null。
    const placementSize = ref<{ moduleId: string; width: number; depth: number } | null>(null);
    const setPlacementSize = (size: { moduleId: string; width: number; depth: number } | null) => {
        placementSize.value = size;
    };

    // 编辑提交：每次家具变更（拖动/旋转/增删/批量结束）调用——按目标 push 历史 + 定向落盘。
    // 取代旧 saveState（整工程快照）+ 整工程 saveModules：见 persistAndRecordEdits。
    const commitEdit = async (): Promise<void> => {
        if (!projectData.value) return;
        isDirty.value = true;
        await persistAndRecordEdits({ suppressServerSync: true });
    };

    // 保留名兼容 baseline 编辑器（updateWall/Column/Opening，当前 UI 未接入）；只刷新撤销态，不做整工程快照。
    const saveState = () => {
        refreshUndoState();
    };

    const normalizeLoadOptions = (options: LoadOptions | ChangeSource): LoadOptions =>
        typeof options === 'string' ? { source: options } : options;

    const createId = (prefix: string): string => {
        const random = Math.random().toString(36).slice(2, 10);
        return `${prefix}_${Date.now().toString(36)}_${random}`;
    };

    const createBlankProjectData = (name: string): ProjectData => {
        const now = new Date().toISOString();
        const projectId = createId('project');
        const schemeId = 'default';
        const baselineHash = 'standalone-empty';

        return {
            project: {
                id: projectId,
                name,
                version: '3.0',
                createdAt: now,
                updatedAt: now,
                coordinateSystem: 'cartesian_mm_yUp',
                activeSchemeId: schemeId,
                schemes: [
                    { id: schemeId, path: './schemes', name: '默认方案' }
                ]
            },
            baseline: {
                metadata: {
                    placementElevation: 3000,
                    origin: [0, 0, 0],
                    rotation: 0,
                    baselineHash
                },
                walls: [],
                columns: [],
                openings: [],
                rooms: [],
                locationLines: []
            },
            activeScheme: {
                strategy: {
                    id: schemeId,
                    name: '默认方案',
                    approach: StrategyApproach.Custom,
                    description: 'Standalone 空白项目',
                    createdAt: now,
                    updatedAt: now,
                    lastValidatedBaselineHash: baselineHash,
                    status: StrategyStatus.Dirty
                },
                zones: [],
                finishes: [],
                modules: []
            },
            computed: {
                roomZones: [],
                exclusions: []
            }
        };
    };

    const refreshModuleLibrary = async () => {
        try {
            await moduleLibraryService.reload();
        } catch (moduleError) {
            sysLog.warn('module library reload failed', { error: moduleError });
        }
    };

    const applyProjectData = async (data: ProjectData, opts: LoadOptions): Promise<void> => {
        const preserveHistory = opts.preserveHistory ?? timeline.shouldPreserveHistory(opts.source);

        // 内容去重：仅当新 data 与当前 projectData 逐字相同才跳过赋值。
        // 赋值是 ThreeSceneService deep-watch 的唯一触发点 → buildFromDocument 先 clearScene 全销毁再重建 → 家具"闪一下"。
        // 服务端校验/规范化（normalize/validate）回写虽已在源头去重，但 Agent 写盘 / 多源 reload 仍可能送来内容未变的 reload；
        // 此处兜底：若赋值会产生与现状完全一致的 projectData，则是可证明的视觉 no-op，跳过即不重建、不闪烁。
        // 有活跃变体时 projectData 已被变体 patch、不会等于 canonical 入参 → 不命中、走原逻辑（无回归）。
        const isVisualNoop = !!projectData.value
            && JSON.stringify(data) === JSON.stringify(projectData.value);
        if (!isVisualNoop) {
            projectData.value = data;
        }
        isDirty.value = false;
        sceneDataCache.clear();

        // 保存 canonical modules 快照供变体切换器使用（深拷贝，避免后续 swap 污染原始数据）
        const canonicalModules = data.activeScheme?.modules ?? [];
        canonicalModulesSnapshot.value = canonicalModules.length > 0
            ? JSON.parse(JSON.stringify(canonicalModules)) as Module[]
            : [];

        // 来源②：同样快照 adopted zones（含 subZones），切换候选时基于它 patch、切回 canonical 时还原分区线。
        const canonicalZones = data.activeScheme?.zones ?? [];
        canonicalZonesSnapshot.value = JSON.parse(JSON.stringify(canonicalZones)) as Zone[];

        // 如果还有活跃变体（in-session SignalR 重载场景），重新应用（recomputeDisplayModules 内部会先刷 zones）
        if (activeVariantByDesignZone.value.size > 0) {
            await recomputeDisplayModules();
        }

        // 项目级变体计数（首次加载 + 后续 reload 都拉一次；不 await 避免阻塞画布构建）
        void refetchVariantCounts();

        await refreshModuleLibrary();

        // 历史策略（per-target 模型）：
        //  · 清空历史源（新项目 / 系统初始化等）→ 全清家具历史。
        //  · 其余（含 ServerSync 重投影）→ 不清；为各可见目标播种 baseline（栈空才播），既有历史不动。
        if (!preserveHistory && timeline.shouldClearHistory(opts.source)) {
            sysLog.debug('clearing furniture history due to source type');
            history.clear();
            activeUndoTarget.value = null;
        }
        seedVisibleBaselines();
        refreshUndoState();

        sysLog.info('project loaded', {
            name: data.project?.name || 'Unknown',
            walls: data.baseline?.walls?.length || 0,
            rooms: data.baseline?.rooms?.length || 0,
            zones: data.activeScheme?.zones?.length || 0,
            modules: data.activeScheme?.modules?.length || 0,
        });

        const zoneErrors = data.activeScheme?.zoneErrors;
        if (zoneErrors && zoneErrors.length > 0) {
          sysLog.warn('zone errors', { zoneErrors });
          const sys = useSystemStore();
          zoneErrors.forEach(e => {
            sys.pushToast({
              type: 'warning',
              title: `分区 ${e.zoneId} 数据损坏`,
              message: e.message,
            });
          });
        }
    };

    const loadInitialProject = async (options: LoadOptions | ChangeSource): Promise<boolean> => {
        const opts = normalizeLoadOptions(options);
        const preserveView = opts.preserveView ?? timeline.shouldPreserveView(opts.source);

        isLoading.value = true;
        error.value = null;
        preserveViewOnLoad.value = preserveView;

        try {
            sysLog.debug('loading project from runtime', { mode: runtime.mode, source: opts.source, preserveView });

            const data = await runtime.loadInitialProject();
            if (!data) {
                sysLog.debug('runtime has no initial project');
                return false;
            }

            await applyProjectData(data, opts);
            return true;
        } catch (err: any) {
            sysLog.error('load project failed', { err: err.message || err });
            error.value = `Failed to load project: ${err.message || err}`;
            return false;
        } finally {
            isLoading.value = false;
            if (preserveView) {
                setTimeout(() => {
                    preserveViewOnLoad.value = false;
                }, 200);
            }
        }
    };

    const importSnapshot = async (file: File, options: LoadOptions | ChangeSource = ChangeSource.UserUpload): Promise<boolean> => {
        const opts = normalizeLoadOptions(options);
        const preserveView = opts.preserveView ?? timeline.shouldPreserveView(opts.source);

        isLoading.value = true;
        error.value = null;
        preserveViewOnLoad.value = preserveView;

        try {
            const data = await runtime.importSnapshot(file);
            await applyProjectData(data, opts);
            return true;
        } catch (err: any) {
            sysLog.error('import snapshot failed', { err: err.message || err });
            error.value = `Failed to import snapshot: ${err.message || err}`;
            return false;
        } finally {
            isLoading.value = false;
            if (preserveView) {
                setTimeout(() => {
                    preserveViewOnLoad.value = false;
                }, 200);
            }
        }
    };

    const createBlankProject = async (name: string = '未命名项目'): Promise<void> => {
        const data = createBlankProjectData(name.trim() || '未命名项目');
        await applyProjectData(data, {
            source: ChangeSource.UserCreate,
            description: 'Create blank project'
        });
        isDirty.value = true;
    };

    // === 多选操作方法 ===

    // 选择 → 撤销目标：取首个选中「模块」所属设计区×变体，作为 Ctrl+Z/Y 的作用目标。
    // 选中非模块（zone/wall）不改目标，保留末次模块目标。
    const applySelectionUndoTarget = (ids: string[]): void => {
        const mods = projectData.value?.activeScheme?.modules ?? [];
        for (const id of ids) {
            const m = mods.find(x => x.id === id);
            if (!m) continue;
            const dz = designZoneOfModule(m);
            if (dz) {
                activeUndoTarget.value = targetForDesignZone(dz);
                refreshUndoState();
                return;
            }
        }
    };

    const setSelectedObject = (obj: any | null) => {
        if (obj === null) {
            selectedIds.value = [];
        } else {
            let id: string | null = null;
            if (typeof obj === 'string') {
                id = obj;
            } else if (obj.id) {
                id = obj.id;
            } else if (obj.userData?.id) {
                id = obj.userData.id;
            }
            selectedIds.value = id ? [id] : [];

            // 缓存 Scene mesh 数据快照（竞态降级回退，兼容 THREE.Mesh 的 userData）
            const cacheData = obj.data || obj.userData?.data;
            if (id && cacheData) {
                sceneDataCache.set(id, { ...cacheData, type: obj.type || obj.userData?.type || 'module' });
            }
        }
        debugMsg.value += `\nSet: ${selectedIds.value.join(',')} at ${Date.now()}`;
        sysLog.debug('setSelectedObject', { ids: selectedIds.value });
        applySelectionUndoTarget(selectedIds.value);
    };

    const setSelection = (ids: string[]) => {
        selectedIds.value = [...ids];
        debugMsg.value += `\nSetSelection: [${ids.join(',')}] at ${Date.now()}`;
        applySelectionUndoTarget(selectedIds.value);
    };

    const addToSelection = (obj: any) => {
        let id: string | null = null;
        if (typeof obj === 'string') {
            id = obj;
        } else if (obj?.id) {
            id = obj.id;
        } else if (obj?.userData?.id) {
            id = obj.userData.id;
        }
        if (id && !selectedIds.value.includes(id)) {
            selectedIds.value = [...selectedIds.value, id];
            debugMsg.value += `\nAdd: ${id} at ${Date.now()}`;
        }
        // 缓存 Scene mesh 数据快照（竞态降级回退，兼容 THREE.Mesh 的 userData）
        const addCacheData = obj?.data || obj?.userData?.data;
        if (id && addCacheData) {
            sceneDataCache.set(id, { ...addCacheData, type: obj.type || obj.userData?.type || 'module' });
        }
    };

    const removeFromSelection = (obj: any) => {
        let id: string | null = null;
        if (typeof obj === 'string') {
            id = obj;
        } else if (obj?.id) {
            id = obj.id;
        } else if (obj?.userData?.id) {
            id = obj.userData.id;
        }
        if (id) {
            selectedIds.value = selectedIds.value.filter(i => i !== id);
            debugMsg.value += `\nRemove: ${id} at ${Date.now()}`;
        }
    };

    const toggleSelection = (obj: any) => {
        let id: string | null = null;
        if (typeof obj === 'string') {
            id = obj;
        } else if (obj?.id) {
            id = obj.id;
        } else if (obj?.userData?.id) {
            id = obj.userData.id;
        }
        if (id) {
            // 缓存 Scene mesh 数据快照（竞态降级回退，兼容 THREE.Mesh 的 userData）
            const toggleCacheData = obj?.data || obj?.userData?.data;
            if (toggleCacheData) {
                sceneDataCache.set(id, { ...toggleCacheData, type: obj.type || obj.userData?.type || 'module' });
            }
            if (selectedIds.value.includes(id)) {
                removeFromSelection(id);
            } else {
                addToSelection(id);
            }
        }
    };

    const isSelected = (obj: any): boolean => {
        let id: string | null = null;
        if (typeof obj === 'string') {
            id = obj;
        } else if (obj?.id) {
            id = obj.id;
        } else if (obj?.userData?.id) {
            id = obj.userData.id;
        }
        return id ? selectedIds.value.includes(id) : false;
    };

    const clearSelection = () => {
        selectedIds.value = [];
    };

    // === Undo/Redo ===

    // 撤销/重做只作用于 activeUndoTarget（当前聚焦的设计区×变体）：
    // 取该目标历史的上/下一态 → 定向重投影（替换该区切片）→ 定向落盘（仅该区）。绝不取实时全局选择、不碰其它设计区。
    const undo = () => {
        const target = activeUndoTarget.value;
        if (!target) return;
        const mods = history.undo(target);
        if (mods === null) return;
        userLog.info('undo', { dz: target.designZoneId, slug: target.variantSlug ?? '@canonical', modules: mods.length });
        preserveViewOnLoad.value = true;
        applyTargetModules(target, mods);
        isDirty.value = true;
        refreshUndoState();
        setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
        void scopedSaveTarget(target, mods, { suppressServerSync: true });
    };

    const redo = () => {
        const target = activeUndoTarget.value;
        if (!target) return;
        const mods = history.redo(target);
        if (mods === null) return;
        userLog.info('redo', { dz: target.designZoneId, slug: target.variantSlug ?? '@canonical', modules: mods.length });
        preserveViewOnLoad.value = true;
        applyTargetModules(target, mods);
        isDirty.value = true;
        refreshUndoState();
        setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
        void scopedSaveTarget(target, mods, { suppressServerSync: true });
    };

    // === 元素更新方法 ===

    const updateModule = (moduleId: string, updates: Partial<Module>) => {
        if (!projectData.value?.activeScheme?.modules) return;
        const moduleIndex = projectData.value.activeScheme.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            const existingModule = projectData.value.activeScheme.modules[moduleIndex];
            if (!existingModule) return;
            const updatedModule = { ...existingModule, ...updates };
            projectData.value.activeScheme.modules[moduleIndex] = updatedModule;
            isDirty.value = true;  // 标记数据已修改
            if (!batchUpdateMode.value) {
                nextTick(() => { void commitEdit(); });
            }
            dispatchLocalUpdate({ type: 'module_update', moduleId, updates });
        }
    };

    const updateWall = (wallId: string, updates: Partial<Wall>) => {
        if (!projectData.value?.baseline?.walls) return;
        const index = projectData.value.baseline.walls.findIndex(w => w.id === wallId);
        if (index !== -1) {
            const existingWall = projectData.value.baseline.walls[index];
            if (!existingWall) return;
            const updated = { ...existingWall, ...updates };
            projectData.value.baseline.walls[index] = updated;
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
        }
    };

    const updateColumn = (colId: string, updates: Partial<Column>) => {
        if (!projectData.value?.baseline?.columns) return;
        const index = projectData.value.baseline.columns.findIndex(c => c.id === colId);
        if (index !== -1) {
            const existingColumn = projectData.value.baseline.columns[index];
            if (!existingColumn) return;
            const updated = { ...existingColumn, ...updates };
            projectData.value.baseline.columns[index] = updated;
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
        }
    };

    const updateOpening = (opId: string, updates: Partial<Opening>) => {
        if (!projectData.value?.baseline?.openings) return;
        const index = projectData.value.baseline.openings.findIndex(o => o.id === opId);
        if (index !== -1) {
            const existingOpening = projectData.value.baseline.openings[index];
            if (!existingOpening) return;
            const updated = { ...existingOpening, ...updates };
            projectData.value.baseline.openings[index] = updated;
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
        }
    };

    const updateElement = (id: string, type: string, updates: Partial<any>) => {
        switch (type) {
            case 'module': updateModule(id, updates); break;
            case 'wall': updateWall(id, updates); break;
            case 'column': updateColumn(id, updates); break;
            case 'door':
            case 'window':
            case 'opening': updateOpening(id, updates); break;
            default: sysLog.warn('unknown element type for update', { type });
        }
    };

    const removeModule = async (moduleId: string) => {
        if (!projectData.value?.activeScheme?.modules) return;
        const moduleIndex = projectData.value.activeScheme.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            projectData.value.activeScheme.modules.splice(moduleIndex, 1);
            selectedIds.value = [];
            isDirty.value = true;  // 标记数据已修改

            dispatchLocalUpdate({ type: 'module_remove', moduleId });

            // 历史 + 定向落盘（删除使该模块所属设计区切片变化，被 commitEdit 检出并重写）
            if (!batchUpdateMode.value) {
                await commitEdit();
            }
        }
    };

    const addModule = (module: Module) => {
        if (!projectData.value?.activeScheme?.modules) return;
        projectData.value.activeScheme.modules.push(module);
        isDirty.value = true;  // 标记数据已修改
        if (!batchUpdateMode.value) {
            nextTick(() => { void commitEdit(); });
        }
        dispatchLocalUpdate({ type: 'module_add', module });
    };

    const setPrompt = (msg: string | null) => {
        promptMessage.value = msg;
    };

    // === 批量更新 API ===
    const beginBatchUpdate = () => {
        batchUpdateMode.value = true;
    };

    const endBatchUpdate = async () => {
        batchUpdateMode.value = false;
        await nextTick();
        // 历史 + 定向落盘：按目标只记录/写入发生变化的设计区（含跨区移动的旧区/新区）。
        await commitEdit();
    };

    // === 脏数据管理 API ===

    /**
     * 重置项目状态（关闭项目返回首页时调用）
     */
    const resetProject = () => {
        projectData.value = null;
        isDirty.value = false;
        selectedIds.value = [];
        error.value = null;
        promptMessage.value = null;
        sceneDataCache.clear();
        moduleLibraryService.dispose();
        timeline.clear();
        history.clear();
        activeUndoTarget.value = null;
        activeVariantByDesignZone.value = new Map();
        canonicalModulesSnapshot.value = null;
        canonicalZonesSnapshot.value = null;
        sysLog.debug('project state reset');
    };

    /**
     * 清除脏数据标记
     * 用于放弃更改后重置状态
     */
    const clearDirty = () => {
        isDirty.value = false;
    };

    /**
     * 从当前 Runtime 重新同步数据（保留历史）
     *
     * 专用于 Agent 修改、Server 推送等场景。
     * 与初始加载的区别：总是保留历史栈，追加新快照。
     *
     * @param options 可选配置
     * @param options.description 自定义描述
     * @param options.metadata 元数据
     * @returns 加载是否成功
     */
    const syncFromServer = async (options?: {
        description?: string;
        metadata?: Record<string, any>;
    }): Promise<boolean> => {
        return loadInitialProject({
            source: ChangeSource.ServerSync,
            preserveView: true,
            preserveHistory: true,
            description: options?.description || 'Sync from server',
            metadata: options?.metadata
        });
    };

    /**
     * 强制从 Server 同步数据（手动刷新兜底）
     * 重置 skip 计数器，确保数据一定被加载
     */
    const forceSync = async (): Promise<boolean> => {
        pendingServerSyncSkips = 0;
        sysLog.debug('force sync: reset skip counter');
        return syncFromServer({ description: 'Manual force sync', metadata: { trigger: 'manual' } });
    };

    /**
     * 保存当前模块集合。
     * Connected Runtime 会推送到 Server；Standalone Runtime 只确认内存编辑。
     * @returns 保存是否成功
     */
    const saveModules = async (options?: { suppressServerSync?: boolean }): Promise<boolean> => {
        if (!projectData.value?.activeScheme?.modules) {
            sysLog.warn('saveModules: no module data');
            return false;
        }

        try {
            // variantSelection：设计区级索引 designZoneId→slug（variant 是设计区级方案）。
            // 后端据此用该变体自身 zones.json 把模块按子分区落盘到 schemes/{dz}/{slug}/[{leaf}/]modules.json，canonical 不动。
            // 不再在前端展开成 leafZoneId→slug——带子分区变体的 leaf key（dz_*）与后端按房间分组的 key（rz_*）对不上，
            // 会让后端静默回落 canonical（=adopted，常为 bootstrap 出的 main），编辑写错文件（见 SaveModules 子分区分组）。
            const variantSelection: Record<string, string> = {};
            for (const [designZoneId, variantSlug] of activeVariantByDesignZone.value) {
                variantSelection[designZoneId] = variantSlug;
            }
            const saved = await runtime.saveModules(
                projectData.value.activeScheme.modules,
                variantSelection
            );
            if (saved) {
                if (supports(runtime.capabilities.serverPersistence)) {
                    // Connected 模式下，Server 已经落盘；Standalone 的保存语义是导出 Snapshot。
                    isDirty.value = false;
                }
                sysLog.info('modules saved');
                if (options?.suppressServerSync && supports(runtime.capabilities.realtimeProjectSync)) {
                    pendingServerSyncSkips += 1;
                }
                return true;
            }

            sysLog.error('save modules failed');
            return false;
        } catch (err: any) {
            const errorMessage = err.message || err;
            sysLog.error('save modules failed', { error: errorMessage });
            return false;
        }
    };

    const formatTimestamp = (date: Date): string => {
        const pad = (value: number) => value.toString().padStart(2, '0');
        return `${date.getFullYear()}${pad(date.getMonth() + 1)}${pad(date.getDate())}-${pad(date.getHours())}${pad(date.getMinutes())}${pad(date.getSeconds())}`;
    };

    const getSnapshotFilename = (): string => {
        const rawName = projectData.value?.project?.name?.trim() || 'BIMCanvas';
        const safeName = rawName.replace(/[\\/:*?"<>|]+/g, '_');
        return `${safeName}_snapshot_${formatTimestamp(new Date())}.json`;
    };

    const exportSnapshot = async (): Promise<{ blob: Blob; filename: string } | null> => {
        if (!projectData.value) {
            return null;
        }

        const blob = await runtime.exportSnapshot(projectData.value);
        return {
            blob,
            filename: getSnapshotFilename()
        };
    };

    const exportBcpProject = async (): Promise<{ blob: Blob; filename: string } | null> => {
        if (!supports(runtime.capabilities.bcpExport)) {
            return null;
        }
        return runtime.exportBcpProject();
    };

    // saveZoneToServer 已移除：v3.4 不再需要按分区保存，Server 自动计算

    return {
        // State
        projectData,
        selectedIds,
        selectedObject,
        selectedObjects,
        isLoading,
        error,
        agentConnectionState,
        currentOperation,
        isDirty,  // 脏数据标记
        preserveViewOnLoad,  // 视图保持标记（分支切换时使用）
        isScreenshotRender,  // 截图渲染模式
        suppressAutoBuild,   // 禁止自动重建

        // Getters
        canUndo,
        canRedo,

        // Actions
        loadInitialProject,
        importSnapshot,
        createBlankProject,
        setSelectedObject,
        setSelection,
        addToSelection,
        removeFromSelection,
        toggleSelection,
        isSelected,
        clearSelection,
        updateModule,
        updateElement,
        addModule,
        removeModule,
        undo,
        redo,

        // Batch Update API
        beginBatchUpdate,
        endBatchUpdate,

        // Dirty Data Management
        clearDirty,
        saveModules,
        exportSnapshot,
        exportBcpProject,
        forceSync,
        resetProject,
        // saveZoneToServer 已移除：v3.4 Server 自动计算分区

        // UI State
        promptMessage,
        setPrompt,
        debugMsg,

        // PlaceTool 运行时尺寸（不持久化）
        placementSize,
        setPlacementSize,

        // 变体方案（按设计区索引）
        activeVariantByDesignZone,
        getActiveVariant,
        setActiveVariant,
        clearActiveVariant,
        resolveDesignZoneId,
        getLeafZoneIdsForDesignZone,

        // 项目级变体计数 + 元数据（Canvas 角标 + Navigator 样式/tooltip）
        variantInfoByDesignZone,
        variantMetadataByDesignZone,
        cacheVariantMetadata,
        getVariantSlot,
        refetchVariantCounts,
    };
});
