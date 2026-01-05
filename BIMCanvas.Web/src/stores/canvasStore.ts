import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { ProjectData, Module, Wall, Column, Opening } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';
import { SignalRService } from '../services/SignalRService';
import { useDebugStore } from './debugStore';

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

    // 辅助函数：在所有对象类型中查找
    const findObjectById = (id: string): any | null => {
        const debug = useDebugStore();

        if (!projectData.value) {
            debug.warn('[Store] findObjectById: projectData is null');
            return null;
        }

        debug.log(`[Store] findObjectById: ${id}`);

        const baseline = projectData.value.baseline;
        const activeScheme = projectData.value.activeScheme;

        // 在 modules 中查找
        const module = activeScheme?.modules?.find(m => m.id === id);
        if (module) {
            debug.success(`[Store] findObjectById: found in modules`);
            return { ...module, type: 'module' };
        }

        // 在 walls 中查找
        const wall = baseline?.walls?.find(w => w.id === id);
        if (wall) {
            debug.success(`[Store] findObjectById: found in walls`);
            return { ...wall, type: 'wall' };
        }

        // 在 columns 中查找
        const column = baseline?.columns?.find(c => c.id === id);
        if (column) {
            debug.success(`[Store] findObjectById: found in columns`);
            return { ...column, type: 'column' };
        }

        // 在 openings 中查找
        const opening = baseline?.openings?.find(o => o.id === id);
        if (opening) {
            debug.success(`[Store] findObjectById: found in openings`);
            const typeName = opening.type === 0 ? 'door' : 'window';
            return { ...opening, type: typeName };
        }

        // 在 activeScheme.zones 中查找（设计区域）
        const schemeZone = activeScheme?.zones?.find(z => z.id === id);
        if (schemeZone) {
            debug.success(`[Store] findObjectById: found in activeScheme.zones`);
            return { ...schemeZone, type: 'zone' };
        }

        // 在 computed.roomZones 中查找（房间区域）
        const computed = projectData.value.computed;
        const computedZone = computed?.roomZones?.find(z => z.id === id);
        if (computedZone) {
            debug.success(`[Store] findObjectById: found in computed.roomZones`);
            return { ...computedZone, type: 'zone' };
        }

        // 在 computed.exclusions 中查找（禁区）
        const exclusion = computed?.exclusions?.find(e => e.id === id);
        if (exclusion) {
            debug.success(`[Store] findObjectById: found in computed.exclusions`);
            return { ...exclusion, type: 'exclusion' };
        }

        debug.warn(`[Store] findObjectById: NOT FOUND (${id})`);
        return null;
    };

    const debugMsg = ref<string>('');
    const instanceId = Math.random().toString(36).substring(7);
    console.log('CanvasStore Created:', instanceId);

    const timeline = new TimelineManager();
    const signalR = SignalRService.getInstance();
    const debugStore = useDebugStore();

    // Initialize SignalR
    signalR.start();

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
            timeline.push(projectData.value);
            updateHistoryState();
        }
    };

    // === 核心加载方法：从 Server 获取当前项目（单项目模式：无需路径参数）===
    /**
     * 加载项目数据
     * @param preserveView 是否保持当前视图（用于分支切换时不重置缩放/位置）
     */
    const loadProject = async (preserveView: boolean = false) => {
        isLoading.value = true;
        error.value = null;

        // 设置视图保持标记，供 ThreeSceneService 的 watch 检查
        preserveViewOnLoad.value = preserveView;

        try {
            debugStore.log(`Loading current project from server... (preserveView=${preserveView})`);

            const response = await axios.get<ProjectData>('http://localhost:5000/api/project');

            projectData.value = response.data;
            isDirty.value = false;  // 重置脏标记
            timeline.clear();
            saveState();

            debugStore.success(`Project loaded: ${response.data.project?.name || 'Unknown'}`);
            debugStore.log(`  - Walls: ${response.data.baseline?.walls?.length || 0}`);
            debugStore.log(`  - Rooms: ${response.data.baseline?.rooms?.length || 0}`);
            debugStore.log(`  - Zones: ${response.data.activeScheme?.zones?.length || 0}`);
            debugStore.log(`  - Modules: ${response.data.activeScheme?.modules?.length || 0}`);

        } catch (err: any) {
            console.error('Failed to load project:', err);
            debugStore.error(`Failed to load project: ${err.message || err}`);
            error.value = `Failed to load project: ${err.message || err}`;
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
            projectData.value = prevState as ProjectData;
            updateHistoryState();
            setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
        }
    };

    const redo = () => {
        const nextState = timeline.redo();
        if (nextState) {
            // 重做时保持当前视图
            preserveViewOnLoad.value = true;
            projectData.value = nextState as ProjectData;
            updateHistoryState();
            setTimeout(() => { preserveViewOnLoad.value = false; }, 200);
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

    const removeModule = (moduleId: string) => {
        if (!projectData.value?.activeScheme?.modules) return;
        const moduleIndex = projectData.value.activeScheme.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            projectData.value.activeScheme.modules.splice(moduleIndex, 1);
            selectedIds.value = [];
            isDirty.value = true;  // 标记数据已修改
            nextTick(() => saveState());
            signalR.sendUpdate({ type: 'module_remove', moduleId });
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

    const endBatchUpdate = () => {
        batchUpdateMode.value = false;
        nextTick(() => saveState());
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
     * 保存当前数据到 Server 文件系统
     * @returns 保存是否成功
     */
    const saveToServer = async (): Promise<boolean> => {
        if (!projectData.value?.activeScheme?.modules) {
            console.warn('[CanvasStore] saveToServer: 无模块数据可保存');
            return false;
        }

        try {
            debugStore.log('[CanvasStore] 正在保存模块数据到 Server...');

            const response = await axios.post('http://localhost:5000/api/project/save', {
                modules: projectData.value.activeScheme.modules
            });

            if (response.status === 200) {
                isDirty.value = false;
                debugStore.success(`[CanvasStore] 保存成功: ${projectData.value.activeScheme.modules.length} 个模块`);
                return true;
            }

            debugStore.error('[CanvasStore] 保存失败: 非200响应');
            return false;
        } catch (err: any) {
            console.error('[CanvasStore] 保存失败:', err);
            debugStore.error(`[CanvasStore] 保存失败: ${err.message || err}`);
            return false;
        }
    };

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

        // UI State
        promptMessage,
        setPrompt,
        debugMsg
    };
});
