import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { ProjectData, Module, Wall, Column, Opening } from '../types/canvas';
import { StrategyApproach, StrategyStatus } from '../types/canvas';
import { TimelineManager } from '../services/state/TimelineManager';
import { useDebugStore } from './debugStore';
import { ChangeSource, ChangeType, type LoadOptions } from '../types/history';
import { moduleLibraryService } from '../services/ModuleLibraryService';
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { supports } from '../runtime/WebRuntimeProtocol';
import { SchemeService } from '../services/SchemeService';
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
    // 注意：此函数在 computed 中调用，禁止使用 debugStore（会产生响应式副作用导致无限循环）
    const findObjectById = (id: string): any | null => {
        if (!projectData.value) {
            console.warn('[Store] findObjectById: projectData is null');
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
            console.warn(`[Store] findObjectById: using scene cache for (${id})`);
            return cached;
        }

        console.warn(`[Store] findObjectById: NOT FOUND (${id})`);
        return null;
    };

    const debugMsg = ref<string>('');

    const timeline = new TimelineManager();
    const debugStore = useDebugStore();

    const dispatchLocalUpdate = (detail: Record<string, unknown>) => {
        if (supports(runtime.capabilities.realtimeProjectSync)) {
            window.dispatchEvent(new CustomEvent('bimcanvas:local-update', { detail }));
        }
    };

    // === module-relocation-agent 变体方案 ===
    // activeVariantByZone：当前每个叶子分区显示哪一份 modules（null/缺失 = canonical）
    // 仅存内存，刷新页面 / 重启 Web 都重置为 canonical（不写 project.json）。
    interface ActiveVariantState {
        variantId: string;
        leafZonePath: string;
    }
    const activeVariantByZone = ref<Map<string, ActiveVariantState>>(new Map());

    // canonical 快照：在每次 applyProjectData 时记录服务端发回的 canonical modules，
    // 切换/取消变体时基于该快照重组 projectData.activeScheme.modules，避免反复打服务端。
    const canonicalModulesSnapshot = ref<Module[] | null>(null);

    // variantInfoByZone：项目级缓存"哪些叶子分区有几份 modules-alt-*.json 变体 + 它们的 ID 列表"。
    // 键为叶子 zone.id（leafZonePath 最后一段）。
    // 用于在 zone label 上渲染 (current/total) 分页号——current 通过 active variantId 在 variantIds
    // 列表里的 index 反算而来。
    interface VariantInfo {
        count: number;
        variantIds: string[];
        leafZonePath: string;
    }
    const variantInfoByZone = ref<Map<string, VariantInfo>>(new Map());

    function getActiveVariant(leafZoneId: string): string | null {
        return activeVariantByZone.value.get(leafZoneId)?.variantId ?? null;
    }

    /**
     * 计算某个叶子分区在 [canonical, ...sortedVariants] 序列中的"当前 / 总数"页码。
     * 与 VariantNavigatorBar 内部的 sequence 计算口径一致：
     *   - canonical（无 active）→ current = 1
     *   - active variantId 在 variantIds[i] → current = i + 2
     *   - active 已失效（list 里找不到）→ current = 1 兜底
     * 没有变体时返回 null（label 不显示后缀）。
     */
    function getVariantSlot(leafZoneId: string): { current: number; total: number } | null {
        const info = variantInfoByZone.value.get(leafZoneId);
        if (!info || info.count <= 0) return null;
        const total = info.count + 1;
        const activeId = activeVariantByZone.value.get(leafZoneId)?.variantId ?? null;
        if (!activeId) return { current: 1, total };
        const idx = info.variantIds.indexOf(activeId);
        return { current: idx >= 0 ? idx + 2 : 1, total };
    }

    /**
     * 拉取项目级变体摘要（leafZonePath → {count, variantIds}），写入 variantInfoByZone，并派发
     * bimcanvas:variant-counts-changed 让 ThreeSceneService 触发 label 重建。
     * 任何调用方都安全：失败时静默清空 Map（视觉上回到"没有变体"，不抛错）。
     */
    async function refetchVariantCounts(): Promise<void> {
        try {
            const dict = await SchemeService.listVariantsSummary();
            const next = new Map<string, VariantInfo>();
            for (const [leafZonePath, rawEntry] of Object.entries(dict)) {
                if (!leafZonePath || rawEntry == null) continue;
                // Fallback：服务端旧版只返回数字，新版返回 { count, variantIds }。
                // 兼容两者，旧版退化为"只有 count，无 variantIds"——分页号会卡在 current=1。
                const count = typeof rawEntry === 'number'
                    ? rawEntry
                    : (rawEntry as { count?: number }).count ?? 0;
                const variantIds = typeof rawEntry === 'object' && Array.isArray((rawEntry as any).variantIds)
                    ? [...(rawEntry as { variantIds: string[] }).variantIds]
                    : [];
                if (count <= 0) continue;
                const lastSlash = leafZonePath.lastIndexOf('/');
                const leafZoneId = lastSlash >= 0 ? leafZonePath.slice(lastSlash + 1) : leafZonePath;
                if (leafZoneId) {
                    next.set(leafZoneId, { count, variantIds, leafZonePath });
                }
            }
            variantInfoByZone.value = next;
        } catch (err: any) {
            debugStore.warn(`[Store] 变体摘要拉取失败: ${err?.message ?? err}`);
            variantInfoByZone.value = new Map();
        } finally {
            window.dispatchEvent(new CustomEvent('bimcanvas:variant-counts-changed', {
                detail: { size: variantInfoByZone.value.size }
            }));
        }
    }

    /**
     * 切换某叶子分区的活跃变体。
     * - variantId 为空 → 还原 canonical
     * - variantId 非空 → 拉变体 modules，替换该 zone 的 canonical 内容
     * 任一情况均会重算 projectData.activeScheme.modules（基于 canonical 快照 + 当前 active map）
     */
    // 注：setActiveVariant / clearActiveVariant 会通过 recomputeDisplayModules() 改
    // projectData.activeScheme.modules，ThreeSceneService 的 watch(projectData, {deep:true})
    // 会自动重建 LabelBuilder，把 (current/total) 后缀同步到新的 active variant；
    // 所以这里不需要额外派发事件。

    async function setActiveVariant(
        leafZoneId: string,
        leafZonePath: string,
        variantId: string | null
    ): Promise<void> {
        if (!variantId) {
            if (activeVariantByZone.value.has(leafZoneId)) {
                activeVariantByZone.value.delete(leafZoneId);
                activeVariantByZone.value = new Map(activeVariantByZone.value);
                await recomputeDisplayModules();
            }
            return;
        }
        if (!leafZonePath) {
            debugStore.warn(`[Store] setActiveVariant: leafZonePath 不能为空 (zone=${leafZoneId})`);
            return;
        }
        activeVariantByZone.value.set(leafZoneId, { variantId, leafZonePath });
        activeVariantByZone.value = new Map(activeVariantByZone.value);
        await recomputeDisplayModules();
    }

    async function clearActiveVariant(leafZoneId: string): Promise<void> {
        if (activeVariantByZone.value.has(leafZoneId)) {
            activeVariantByZone.value.delete(leafZoneId);
            activeVariantByZone.value = new Map(activeVariantByZone.value);
            await recomputeDisplayModules();
        }
    }

    /**
     * 基于 canonical 快照 + 当前 activeVariantByZone 重组 projectData.activeScheme.modules。
     * 流程：(1) 从 canonical 中过滤掉所有"有 active 变体"的叶子分区的模块；
     *      (2) 拉取每个 active 变体的 modules，逐 zone append；
     *      (3) 写回 projectData.activeScheme.modules，触发响应式刷新。
     * 任一变体拉取失败 → 从 active map 中静默丢弃，回退到该 zone 的 canonical。
     */
    async function recomputeDisplayModules(): Promise<void> {
        if (!projectData.value || !projectData.value.activeScheme) return;
        if (canonicalModulesSnapshot.value === null) return;

        const activeMap = activeVariantByZone.value;
        const activeZoneIds = new Set(activeMap.keys());

        // (1) canonical 过滤：保留非 active zone 的模块
        const baseModules = canonicalModulesSnapshot.value.filter(
            m => !activeZoneIds.has(m.zoneId ?? '')
        );

        // (2) 拉每个变体并合并
        const variantBlocks: Module[][] = [];
        const failedZones: string[] = [];
        for (const [leafZoneId, state] of Array.from(activeMap.entries())) {
            try {
                const resp = await SchemeService.getModules('main', {
                    leafZonePath: state.leafZonePath,
                    variantId: state.variantId
                });
                const variantModules = (resp.modules ?? []).map(m => ({
                    ...m,
                    zoneId: (m as any).zoneId ?? leafZoneId
                })) as Module[];
                variantBlocks.push(variantModules);
            } catch (err: any) {
                debugStore.warn(`[Store] 变体加载失败 zone=${leafZoneId} variant=${state.variantId}: ${err?.message ?? err}`);
                failedZones.push(leafZoneId);
            }
        }
        for (const z of failedZones) activeMap.delete(z);
        if (failedZones.length > 0) {
            activeVariantByZone.value = new Map(activeMap);
        }

        // (3) 写回（使用展开避免 reactive 丢失）
        projectData.value.activeScheme.modules = [
            ...baseModules,
            ...variantBlocks.flat()
        ];
    }

    /**
     * 判定一个 SignalR 文件名是否属于 module-relocation-agent 的变体侧链
     * （modules-alt-*.json / modules-alt-*.meta.json）。
     * 这类文件不应触发整个 canvas 数据刷新——只通知变体切换器 refetch /api/scheme/variants。
     */
    function isVariantSidecarFile(fileName: string | undefined): boolean {
        if (!fileName) return false;
        return fileName.toLowerCase().startsWith('modules-alt-')
            && fileName.toLowerCase().endsWith('.json');
    }

    // 监听 Server 推送的文件变化事件（文件驱动架构的核心链路）
    if (supports(runtime.capabilities.realtimeProjectSync)) {
      window.addEventListener('bimcanvas:server-update', async (e: any) => {
        const data = e.detail;
        debugStore.log(`[Store] 收到服务端更新: ${JSON.stringify(data)}`);

        const fileName = data.file as string | undefined;

        // 变体侧链：modules-alt-{n}.json / modules-alt-{n}.meta.json
        // 不重载整个项目，只广播给变体切换器 + 刷新项目级变体计数（Canvas 角标 / 副轮廓）
        if (isVariantSidecarFile(fileName)) {
            debugStore.log(`[Store] 变体文件变化，分发给切换器: ${fileName}`);
            window.dispatchEvent(new CustomEvent('bimcanvas:variant-files-changed', {
                detail: { file: fileName, trigger: data.trigger }
            }));
            void refetchVariantCounts();
            return;
        }

        if (data.action === 'reload') {
            const trigger = data.trigger as string | undefined;

            // 采纳变体后服务端会发 trigger=variant-adopt 并附带 file=modules.json
            // 此时被采纳的叶子分区的 active 状态应清空回 canonical
            if (trigger === 'variant-adopt') {
                activeVariantByZone.value.clear();
                activeVariantByZone.value = new Map(activeVariantByZone.value);
                debugStore.log('[Store] 变体已采纳，清空所有 activeVariantByZone 并重载 canonical');
                // 采纳会删除该叶子分区下所有 modules-alt-*；刷新计数字典让 Canvas 摘掉角标
                void refetchVariantCounts();
            }

            // Agent/重连/手动触发的更新：重置 skip 计数器，确保更新不被跳过
            if (trigger === 'agent' || trigger === 'reconnect' || trigger === 'manual' || trigger === 'variant-adopt') {
                pendingServerSyncSkips = 0;
                debugStore.log(`[Store] 显式触发 (${trigger})，重置 skip 计数器`);
            } else if (fileName === 'modules.json' && pendingServerSyncSkips > 0) {
                // 仅 FileSystemWatcher 触发的普通更新才走 skip 逻辑
                pendingServerSyncSkips -= 1;
                debugStore.log('[Store] 跳过本地写入触发的 ServerSync');
                return;
            }

            // 保持当前视图，重新加载数据
            debugStore.log(`[Store] 触发数据重载 (preserveView=true, trigger=${trigger || 'watcher'})`);
            await syncFromServer({ description: 'Server file changed', metadata: { trigger: trigger || 'watcher' } });
        }
      });
    }

    const agentConnectionState = ref<'Connected' | 'Disconnected' | 'Reconnecting'>('Disconnected');
    const currentOperation = ref<string | null>(null);

    window.addEventListener('bimcanvas:connection-state', (e: any) => {
        agentConnectionState.value = e.detail;
        console.log('Store: Connection State Updated ->', agentConnectionState.value);
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

    const updateHistoryState = () => {
        canUndo.value = timeline.canUndo;
        canRedo.value = timeline.canRedo;
    };

    const saveState = () => {
        if (projectData.value) {
            timeline.push(projectData.value, ChangeSource.UserEdit, {
                description: 'User interaction',
                changeType: ChangeType.Update
            });
            updateHistoryState();
        }
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
            debugStore.warn(`[Store] Module library reload failed: ${moduleError}`);
        }
    };

    const applyProjectData = async (data: ProjectData, opts: LoadOptions): Promise<void> => {
        const preserveHistory = opts.preserveHistory ?? timeline.shouldPreserveHistory(opts.source);

        projectData.value = data;
        isDirty.value = false;
        sceneDataCache.clear();

        // 保存 canonical modules 快照供变体切换器使用（深拷贝，避免后续 swap 污染原始数据）
        const canonicalModules = data.activeScheme?.modules ?? [];
        canonicalModulesSnapshot.value = canonicalModules.length > 0
            ? JSON.parse(JSON.stringify(canonicalModules)) as Module[]
            : [];

        // 如果还有活跃变体（in-session SignalR 重载场景），重新应用
        if (activeVariantByZone.value.size > 0) {
            await recomputeDisplayModules();
        }

        // 项目级变体计数（首次加载 + 后续 reload 都拉一次；不 await 避免阻塞画布构建）
        void refetchVariantCounts();

        await refreshModuleLibrary();

        if (!preserveHistory && timeline.shouldClearHistory(opts.source)) {
            debugStore.log('[Store] Clearing history due to source type');
            timeline.clear();
        }

        timeline.push(data, opts.source, {
            description: opts.description || `Load from ${opts.source}`,
            metadata: opts.metadata
        });

        updateHistoryState();

        debugStore.success(`[Store] Project loaded: ${data.project?.name || 'Unknown'}`);
        debugStore.log(`  - Walls: ${data.baseline?.walls?.length || 0}`);
        debugStore.log(`  - Rooms: ${data.baseline?.rooms?.length || 0}`);
        debugStore.log(`  - Zones: ${data.activeScheme?.zones?.length || 0}`);
        debugStore.log(`  - Modules: ${data.activeScheme?.modules?.length || 0}`);

        const zoneErrors = data.activeScheme?.zoneErrors;
        if (zoneErrors && zoneErrors.length > 0) {
          debugStore.warn(`[Store] ZoneErrors: ${JSON.stringify(zoneErrors)}`);
          zoneErrors.forEach(e => {
            window.dispatchEvent(new CustomEvent('bimcanvas:agent-notification', {
              detail: { type: 'warning', title: `分区 ${e.zoneId} 数据损坏`, message: e.message }
            }));
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
            debugStore.log(`[Store] Loading project from ${runtime.mode} runtime... ${JSON.stringify({
                source: opts.source,
                preserveView
            })}`);

            const data = await runtime.loadInitialProject();
            if (!data) {
                debugStore.log('[Store] Runtime has no initial project');
                return false;
            }

            await applyProjectData(data, opts);
            return true;
        } catch (err: any) {
            console.error('Failed to load project:', err);
            debugStore.error(`[Store] Load failed: ${err.message || err}`);
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
            console.error('Failed to import snapshot:', err);
            debugStore.error(`[Store] Snapshot import failed: ${err.message || err}`);
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
        console.log('Store setSelectedObject:', selectedIds.value, '->', selectedObject.value);
    };

    const setSelection = (ids: string[]) => {
        selectedIds.value = [...ids];
        debugMsg.value += `\nSetSelection: [${ids.join(',')}] at ${Date.now()}`;
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

    const undo = () => {
        const prevState = timeline.undo();
        if (prevState) {
            // 撤销时保持当前视图
            preserveViewOnLoad.value = true;
            projectData.value = JSON.parse(prevState.state) as ProjectData;
            isDirty.value = true;
            updateHistoryState();
            setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
            void saveModules({ suppressServerSync: true });
        }
    };

    const redo = () => {
        const nextState = timeline.redo();
        if (nextState) {
            // 重做时保持当前视图
            preserveViewOnLoad.value = true;
            projectData.value = JSON.parse(nextState.state) as ProjectData;
            isDirty.value = true;
            updateHistoryState();
            setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
            void saveModules({ suppressServerSync: true });
        }
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
                nextTick(() => saveState());
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
            default: console.warn(`Unknown element type for update: ${type}`);
        }
    };

    const removeModule = async (moduleId: string) => {
        if (!projectData.value?.activeScheme?.modules) return;
        const moduleIndex = projectData.value.activeScheme.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            projectData.value.activeScheme.modules.splice(moduleIndex, 1);
            selectedIds.value = [];
            isDirty.value = true;  // 标记数据已修改

            if (!batchUpdateMode.value) {
                nextTick(() => saveState());
            }
            dispatchLocalUpdate({ type: 'module_remove', moduleId });

            // 持久化到文件系统
            if (!batchUpdateMode.value) {
                await saveModules();
            }
        }
    };

    const addModule = (module: Module) => {
        if (!projectData.value?.activeScheme?.modules) return;
        projectData.value.activeScheme.modules.push(module);
        isDirty.value = true;  // 标记数据已修改
        if (!batchUpdateMode.value) {
            nextTick(() => saveState());
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

        // 1. 保存到本地Timeline历史（Undo/Redo）
        await nextTick();
        saveState();

        // 2. 持久化到文件系统（File-Driven Architecture）
        // 符合架构文档"即时写入"设计：用户交互结束时立即写入硬盘
        if (isDirty.value) {
            await saveModules();
        }
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
        debugStore.log('[Store] Project state reset');
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
        debugStore.log('[Store] 强制同步: 重置 skip 计数器');
        return syncFromServer({ description: 'Manual force sync', metadata: { trigger: 'manual' } });
    };

    /**
     * 保存当前模块集合。
     * Connected Runtime 会推送到 Server；Standalone Runtime 只确认内存编辑。
     * @returns 保存是否成功
     */
    const saveModules = async (options?: { suppressServerSync?: boolean }): Promise<boolean> => {
        if (!projectData.value?.activeScheme?.modules) {
            console.warn('[CanvasStore] saveModules: 无模块数据可保存');
            return false;
        }

        try {
            // 派生 variantSelection：activeVariantByZone 里每个条目代表"这个 zone 当前显示的是变体"，
            // 后端按此映射决定写 modules-{vid}.json 还是 canonical modules.json，避免编辑变体时污染 canonical。
            const variantSelection: Record<string, string> = {};
            for (const [leafZoneId, state] of activeVariantByZone.value) {
                variantSelection[leafZoneId] = state.variantId;
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
                debugStore.success('[CanvasStore] 模块保存成功');
                if (options?.suppressServerSync && supports(runtime.capabilities.realtimeProjectSync)) {
                    pendingServerSyncSkips += 1;
                }
                return true;
            }

            debugStore.error('[CanvasStore] 保存失败');
            return false;
        } catch (err: any) {
            const errorMessage = err.message || err;
            debugStore.error(`[CanvasStore] 保存失败: ${errorMessage}`);
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

        // 变体方案（module-relocation-agent 产出）
        activeVariantByZone,
        getActiveVariant,
        setActiveVariant,
        clearActiveVariant,

        // 项目级变体计数（Canvas 上为有变体的 zone label 显示 (current/total) 后缀）
        variantInfoByZone,
        getVariantSlot,
        refetchVariantCounts
    };
});
