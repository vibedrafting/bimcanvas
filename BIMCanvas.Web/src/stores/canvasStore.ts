import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { ProjectData, Module, Wall, Column, Opening } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';
import { SignalRService } from '../services/SignalRService';
import { useDebugStore } from './debugStore';
import { ChangeSource, ChangeType, type LoadOptions } from '../types/history';
export const useCanvasStore = defineStore('canvas', () => {
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
    const signalR = SignalRService.getInstance();
    const debugStore = useDebugStore();

    // Initialize SignalR
    signalR.start();

    // 监听 Server 推送的文件变化事件（文件驱动架构的核心链路）
    window.addEventListener('bimcanvas:server-update', async (e: any) => {
        const data = e.detail;
        debugStore.log(`[Store] 收到服务端更新: ${JSON.stringify(data)}`);

        if (data.action === 'reload') {
            const trigger = data.trigger as string | undefined;

            // Agent/重连/手动触发的更新：重置 skip 计数器，确保更新不被跳过
            if (trigger === 'agent' || trigger === 'reconnect' || trigger === 'manual') {
                pendingServerSyncSkips = 0;
                debugStore.log(`[Store] 显式触发 (${trigger})，重置 skip 计数器`);
            } else if (data.file === 'modules.json' && pendingServerSyncSkips > 0) {
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

    // === 核心加载方法：从 Server 获取当前项目（单项目模式：无需路径参数）===
    /**
     * 加载项目数据
     * @param preserveView 是否保持当前视图（用于分支切换时不重置缩放/位置）
     */
    /**
     * 从 Server 加载项目数据 - 统一入口
     *
     * @param options 加载选项（支持 ChangeSource 简写）
     * @returns 加载是否成功
     */
    const loadProject = async (options: LoadOptions | ChangeSource): Promise<boolean> => {
        // 兼容简写参数
        const opts: LoadOptions = typeof options === 'string'
            ? { source: options }
            : options;

        // 智能决策：是否保留历史/视图
        const preserveHistory = opts.preserveHistory ?? timeline.shouldPreserveHistory(opts.source);
        const preserveView = opts.preserveView ?? timeline.shouldPreserveView(opts.source);

        isLoading.value = true;
        error.value = null;
        preserveViewOnLoad.value = preserveView;

        try {
            debugStore.log('[Store] Loading project...', {
                source: opts.source,
                preserveHistory,
                preserveView
            });

            // 从 Server 获取数据
            const response = await axios.get<ProjectData>('http://localhost:5000/api/project');
            projectData.value = response.data;
            isDirty.value = false;
            sceneDataCache.clear(); // 数据已刷新，清理 Scene 降级缓存

            // 历史管理策略
            if (timeline.shouldClearHistory(opts.source)) {
                debugStore.log('[Store] Clearing history due to source type');
                timeline.clear();
            }

            // 保存快照
            timeline.push(response.data, opts.source, {
                description: opts.description || `Load from ${opts.source}`,
                metadata: opts.metadata
            });

            updateHistoryState();

            debugStore.success(`[Store] Project loaded: ${response.data.project?.name || 'Unknown'}`);
            debugStore.log(`  - Walls: ${response.data.baseline?.walls?.length || 0}`);
            debugStore.log(`  - Rooms: ${response.data.baseline?.rooms?.length || 0}`);
            debugStore.log(`  - Zones: ${response.data.activeScheme?.zones?.length || 0}`);
            debugStore.log(`  - Modules: ${response.data.activeScheme?.modules?.length || 0}`);

            return true;

        } catch (err: any) {
            console.error('Failed to load project:', err);
            debugStore.error(`[Store] Load failed: ${err.message || err}`);
            error.value = `Failed to load project: ${err.message || err}`;
            return false;

        } finally {
            isLoading.value = false;
            // 重置标记，确保下次默认加载仍会适配屏幕
            if (preserveView) {
                setTimeout(() => {
                    preserveViewOnLoad.value = false;
                }, 200);
            }
        }
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
            void saveToServer({ suppressServerSync: true });
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
            void saveToServer({ suppressServerSync: true });
        }
    };

    // === 元素更新方法 ===

    const updateModule = (moduleId: string, updates: Partial<Module>) => {
        if (!projectData.value?.activeScheme?.modules) return;
        const moduleIndex = projectData.value.activeScheme.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            const updatedModule = { ...projectData.value.activeScheme.modules[moduleIndex], ...updates };
            projectData.value.activeScheme.modules[moduleIndex] = updatedModule;
            isDirty.value = true;  // 标记数据已修改
            if (!batchUpdateMode.value) {
                nextTick(() => saveState());
            }
            signalR.sendUpdate({ type: 'module_update', moduleId, updates });
        }
    };

    const updateWall = (wallId: string, updates: Partial<Wall>) => {
        if (!projectData.value?.baseline?.walls) return;
        const index = projectData.value.baseline.walls.findIndex(w => w.id === wallId);
        if (index !== -1) {
            const updated = { ...projectData.value.baseline.walls[index], ...updates };
            projectData.value.baseline.walls[index] = updated;
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
        }
    };

    const updateColumn = (colId: string, updates: Partial<Column>) => {
        if (!projectData.value?.baseline?.columns) return;
        const index = projectData.value.baseline.columns.findIndex(c => c.id === colId);
        if (index !== -1) {
            const updated = { ...projectData.value.baseline.columns[index], ...updates };
            projectData.value.baseline.columns[index] = updated;
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
        }
    };

    const updateOpening = (opId: string, updates: Partial<Opening>) => {
        if (!projectData.value?.baseline?.openings) return;
        const index = projectData.value.baseline.openings.findIndex(o => o.id === opId);
        if (index !== -1) {
            const updated = { ...projectData.value.baseline.openings[index], ...updates };
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
            signalR.sendUpdate({ type: 'module_remove', moduleId });

            // 持久化到文件系统
            if (!batchUpdateMode.value) {
                await saveToServer();
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
        signalR.sendUpdate({ type: 'module_add', module });
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
            await saveToServer();
        }
    };

    // === 脏数据管理 API ===

    /**
     * 清除脏数据标记
     * 用于放弃更改后重置状态
     */
    const clearDirty = () => {
        isDirty.value = false;
    };

    /**
     * 从 Server 同步数据（保留历史）
     *
     * 专用于 Agent 修改、Server 推送等场景。
     * 与 loadProject 的区别：总是保留历史栈，追加新快照。
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
        return loadProject({
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
     * 保存当前数据到 Server 文件系统
     * v3.4: Server 根据模块 bounds 位置自动计算分区
     * @returns 保存是否成功
     */
    const saveToServer = async (options?: { suppressServerSync?: boolean }): Promise<boolean> => {
        if (!projectData.value?.activeScheme?.modules) {
            console.warn('[CanvasStore] saveToServer: 无模块数据可保存');
            return false;
        }

        try {
            const response = await axios.post('http://localhost:5000/api/project/save', {
                modules: projectData.value.activeScheme.modules
                // Server 根据 bounds 位置自动计算分区
            });

            if (response.status === 200) {
                isDirty.value = false;
                const result = response.data;
                debugStore.success(`[CanvasStore] 保存成功: ${result.modulesCount} 个模块`);

                if (result.orphanCount > 0) {
                    debugStore.warn(`[CanvasStore] ${result.orphanCount} 个模块不在任何分区内，已保存到 _unzoned`);
                }
                if (options?.suppressServerSync) {
                    pendingServerSyncSkips += 1;
                }
                return true;
            }

            debugStore.error('[CanvasStore] 保存失败: 非200响应');
            return false;
        } catch (err: any) {
            debugStore.error(`[CanvasStore] 保存失败: ${err.message || err}`);
            return false;
        }
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
        loadProject,
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
        saveToServer,
        forceSync,
        // saveZoneToServer 已移除：v3.4 Server 自动计算分区

        // UI State
        promptMessage,
        setPrompt,
        debugMsg
    };
});
