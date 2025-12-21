
import { defineStore } from 'pinia';
import { ref, computed, nextTick } from 'vue';
import type { CanvasDocument } from '../types/canvas';
import axios from 'axios';
import { TimelineManager } from '../services/state/TimelineManager';
import { SignalRService } from '../services/SignalRService';

import { useDebugStore } from './debugStore';

export const useCanvasStore = defineStore('canvas', () => {
    const document = ref<CanvasDocument | null>(null);
    const isLoading = ref(false);

    // === 多选支持 ===
    // selectedIds 取代原来的 selectedId，支持多选
    const selectedIds = ref<string[]>([]);

    // 兼容层：selectedObject 返回第一个选中对象（给旧代码用）
    const selectedObject = computed(() => {
        if (selectedIds.value.length === 0 || !document.value) return null;
        const firstId = selectedIds.value[0];
        if (!firstId) return null;
        return findObjectById(firstId);
    });

    // 新增：返回所有选中对象
    const selectedObjects = computed(() => {
        if (selectedIds.value.length === 0 || !document.value) return [];
        return selectedIds.value
            .map(id => findObjectById(id))
            .filter((obj): obj is NonNullable<typeof obj> => obj !== null);
    });

    // 辅助函数：在所有对象类型中查找
    const findObjectById = (id: string): any | null => {
        // 延迟获取 debugStore（避免初始化顺序问题）
        const debug = useDebugStore();

        if (!document.value) {
            debug.warn('[Store] findObjectById: document is null');
            return null;
        }

        debug.log(`[Store] findObjectById: ${id}`);

        // 显示 walls 列表便于对比
        const wallIds = document.value.walls?.map(w => w.id).slice(0, 3) || [];
        debug.log(`[Store] Available wall IDs: [${wallIds.join(', ')}...]`);

        // 在 modules 中查找
        const module = document.value.modules?.find(m => m.id === id);
        if (module) {
            debug.success(`[Store] findObjectById: found in modules`);
            return { ...module, type: 'module' };
        }

        // 在 walls 中查找
        const wall = document.value.walls?.find(w => w.id === id);
        if (wall) {
            debug.success(`[Store] findObjectById: found in walls`);
            return { ...wall, type: 'wall' };
        }

        // 在 columns 中查找
        const column = document.value.columns?.find(c => c.id === id);
        if (column) {
            debug.success(`[Store] findObjectById: found in columns`);
            return { ...column, type: 'column' };
        }

        // 在 openings 中查找（门窗）- 使用类型断言
        const doc = document.value as any;
        if (doc.outline?.openings) {
            const opening = doc.outline.openings.find((o: any) => o.id === id);
            if (opening) {
                debug.success(`[Store] findObjectById: found in openings`);
                return { ...opening, type: opening.type || 'opening' };
            }
        }

        // 列出所有可用 ID 帮助调试
        const allIds: string[] = [];
        document.value.modules?.forEach(m => allIds.push(`mod:${m.id}`));
        document.value.walls?.forEach(w => allIds.push(`wall:${w.id}`));
        debug.warn(`[Store] findObjectById: NOT FOUND (${id}). Available: ${allIds.slice(0, 5).join(', ')}...`);

        return null;
    };

    const debugMsg = ref<string>('');
    const instanceId = Math.random().toString(36).substring(7);
    const error = ref<string | null>(null); // Added error state
    const promptMessage = ref<string | null>(null); // Instructional prompt
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

    // === 批量更新支持 ===
    // 当进行批量更新时（如移动多个模块），跳过每次更新后的 saveState
    // 只在批量操作结束后保存一次状态
    const batchUpdateMode = ref(false);

    const updateHistoryState = () => {
        canUndo.value = timeline.canUndo;
        canRedo.value = timeline.canRedo;
    };

    // Helper to push state to timeline without triggering watch loops if we were watching
    // For now, we call this manually when a significant change happens (e.g. drag end)
    const saveState = () => {
        if (document.value) {
            timeline.push(document.value);
            updateHistoryState();
        }
    };

    const loadDocument = (doc: CanvasDocument) => {
        document.value = doc;
        timeline.clear();
        saveState(); // Initial state
        // saveState calls updateHistoryState
    };

    // === 多选操作方法 ===

    // 兼容旧代码：设置单选（清空现有选择，设置为新单选）
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

    // 新增：批量设置选择（替换整个选择集）
    const setSelection = (ids: string[]) => {
        selectedIds.value = [...ids];
        debugMsg.value += `\nSetSelection: [${ids.join(',')}] at ${Date.now()}`;
    };

    // 新增：添加到选择集
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

    // 新增：从选择集移除
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

    // 新增：切换选择状态（已选则取消，未选则添加）
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

    // 新增：检查对象是否被选中
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

    const loadDemoData = async (url: string) => {
        try {
            isLoading.value = true;
            debugStore.log(`Loading demo data from: ${url}`);
            const response = await axios.get<CanvasDocument>(url);
            document.value = response.data;
            timeline.clear();
            saveState(); // Initial state
            console.log('Demo data loaded:', document.value);
            debugStore.success(`Successfully loaded demo data. Modules: ${document.value.modules.length}`);
            error.value = null; // Clear any previous error
        } catch (err: any) {
            console.error('Failed to load demo data:', err);
            debugStore.error(`Failed to load demo data: ${err.message || err}`);
            error.value = `Failed to load demo data: ${err.message || err}`;
        } finally {
            isLoading.value = false;
        }
    };

    // 从 JSON 对象加载文档（供本地文件加载使用）
    const loadFromJson = (jsonContent: CanvasDocument) => {
        document.value = jsonContent;
        timeline.clear();
        saveState();
        error.value = null;
        debugStore.success('Document loaded from local file');
    };

    const undo = () => {
        const prevState = timeline.undo();
        if (prevState) {
            document.value = prevState;
            updateHistoryState();
        }
    };

    const redo = () => {
        const nextState = timeline.redo();
        if (nextState) {
            document.value = nextState;
            updateHistoryState();
        }
    };

    const updateModule = (moduleId: string, updates: Partial<any>) => {
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            const updatedModule = { ...document.value.modules[moduleIndex], ...updates } as any;
            document.value.modules[moduleIndex] = updatedModule;
            // 批量模式下跳过自动保存，由 endBatchUpdate 统一保存
            if (!batchUpdateMode.value) {
                nextTick(() => saveState());
            }
            signalR.sendUpdate({ type: 'module_update', moduleId, updates });
        }
    };

    const updateWall = (wallId: string, updates: Partial<any>) => {
        if (!document.value) return;
        const index = document.value.walls.findIndex(w => w.id === wallId);
        if (index !== -1) {
            const updated = { ...document.value.walls[index], ...updates } as any;
            document.value.walls[index] = updated;
            nextTick(() => saveState());
            // signalR.sendUpdate({ type: 'wall_update', wallId, updates }); // TODO: Server support
        }
    };

    const updateColumn = (colId: string, updates: Partial<any>) => {
        if (!document.value || !document.value.columns) return;
        const index = document.value.columns.findIndex(c => c.id === colId);
        if (index !== -1) {
            const updated = { ...document.value.columns[index], ...updates } as any;
            document.value.columns[index] = updated;
            nextTick(() => saveState());
        }
    };

    const updateOpening = (opId: string, updates: Partial<any>) => {
        if (!document.value || !document.value.openings) return;
        const index = document.value.openings.findIndex(o => o.id === opId);
        if (index !== -1) {
            const updated = { ...document.value.openings[index], ...updates } as any;
            document.value.openings[index] = updated;
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
        if (!document.value) return;
        const moduleIndex = document.value.modules.findIndex(m => m.id === moduleId);
        if (moduleIndex !== -1) {
            document.value.modules.splice(moduleIndex, 1);
            selectedIds.value = []; // Deselect

            // Use nextTick to ensure state is settled before saving
            nextTick(() => saveState());

            // Sync with server
            signalR.sendUpdate({
                type: 'module_remove',
                moduleId
            });
        }
    };

    const setPrompt = (msg: string | null) => {
        promptMessage.value = msg;
    };

    // === 批量更新 API ===
    // 开始批量更新（暂停自动保存）
    const beginBatchUpdate = () => {
        batchUpdateMode.value = true;
    };

    // 结束批量更新（保存一次状态）
    const endBatchUpdate = () => {
        batchUpdateMode.value = false;
        nextTick(() => saveState());
    };


    return {
        // State
        document,
        selectedIds,      // 多选：选中对象 ID 数组
        selectedObject,   // 兼容层：第一个选中对象
        selectedObjects,  // 新增：所有选中对象数组
        isLoading,
        error,
        agentConnectionState,
        currentOperation,

        // Getters
        canUndo,
        canRedo,

        // Actions
        loadDemoData,
        loadFromJson,
        setSelectedObject,  // 兼容层：单选
        setSelection,       // 新增：批量设置
        addToSelection,     // 新增：添加到选择集
        removeFromSelection,// 新增：从选择集移除
        toggleSelection,    // 新增：切换选择
        isSelected,         // 新增：检查是否选中
        clearSelection,
        updateModule,
        updateElement, // Export generic update
        removeModule,
        undo,
        redo,

        // Batch Update API
        beginBatchUpdate,
        endBatchUpdate,

        // UI State
        promptMessage,
        setPrompt,
        debugMsg
    };
});
