import { defineStore } from 'pinia';
import { ref } from 'vue';
import { ProjectService } from '../services/ProjectService';
import { useCanvasStore } from './canvasStore';
import { useDebugStore } from './debugStore';
import type { ProjectSummary, RecentProjectEntry } from '../types/homepage';
import { getWebRuntime } from '../runtime/runtimeRegistry';
import { supports } from '../runtime/WebRuntimeProtocol';

export type AppView = 'homepage' | 'workspace';

export const useAppStore = defineStore('app', () => {
    const debugStore = useDebugStore();
    const runtime = getWebRuntime();

    // === 状态 ===
    const currentView = ref<AppView>('homepage');
    const projectList = ref<ProjectSummary[]>([]);
    const recentProjects = ref<RecentProjectEntry[]>([]);
    const isLoadingList = ref(false);
    const listError = ref<string | null>(null);
    const pendingProjectWarnings = ref<string[]>([]);

    // === Actions ===

    /** 获取项目列表 + 最近打开记录 */
    const fetchProjectList = async () => {
        if (!supports(runtime.capabilities.projectCatalog)) {
            projectList.value = [];
            recentProjects.value = [];
            listError.value = null;
            return;
        }

        isLoadingList.value = true;
        listError.value = null;
        try {
            // Server GET /api/project/list 返回 ProjectSummary[]
            // Server GET /api/project/recent 返回 RecentProjectEntry[]
            const [projects, recent] = await Promise.all([
                ProjectService.listProjects(),
                ProjectService.getRecentProjects()
            ]);
            projectList.value = projects;
            recentProjects.value = recent;
            debugStore.log(`[AppStore] Loaded ${projects.length} projects, ${recent.length} recent`);
        } catch (err: any) {
            listError.value = err.message || '获取项目列表失败';
            debugStore.error(`[AppStore] Failed to fetch projects: ${err}`);
        } finally {
            isLoadingList.value = false;
        }
    };

    /**
     * 打开项目（从首页）
     * 成功后自动切换到 workspace 视图
     * App.vue 的 watch 会触发 enterWorkspace() → loadInitialProject()
     */
    const openProject = async (folderPath: string): Promise<boolean> => {
        if (!supports(runtime.capabilities.projectCatalog)) {
            debugStore.warn('[AppStore] Standalone Runtime 不支持打开 Server 项目目录');
            return false;
        }

        debugStore.log(`[AppStore] Opening project: ${folderPath}`);
        try {
            const result = await ProjectService.openFolder(folderPath);
            if (result.status === 'Success') {
                stageProjectWarnings(result.warnings);
                currentView.value = 'workspace';
                return true;
            } else {
                clearPendingProjectWarnings();
                debugStore.error(`[AppStore] Open failed: ${result.message}`);
                return false;
            }
        } catch (err: any) {
            clearPendingProjectWarnings();
            debugStore.error(`[AppStore] Open error: ${err}`);
            return false;
        }
    };

    /**
     * 关闭项目（返回首页）
     * 清理 canvasStore 状态，切换到 homepage 视图
     * HomePage 的 onMounted 会自动 fetchProjectList
     */
    const closeProject = async (force: boolean = false): Promise<{ success: boolean; hasUnsavedChanges?: boolean }> => {
        debugStore.log(`[AppStore] Closing project (force=${force})`);
        try {
            if (supports(runtime.capabilities.serverPersistence)) {
                const result = await ProjectService.closeProject(force);

                if (!result.success && result.hasUnsavedChanges && !force) {
                    return { success: false, hasUnsavedChanges: true };
                }
            } else {
                await runtime.closeProject();
            }

            // 成功关闭（或强制关闭）
            const canvasStore = useCanvasStore();
            canvasStore.resetProject();

            clearPendingProjectWarnings();
            currentView.value = 'homepage';
            return { success: true };
        } catch (err: any) {
            debugStore.error(`[AppStore] Close error: ${err}`);
            // 即使 API 报错也尝试本地清理
            const canvasStore = useCanvasStore();
            canvasStore.resetProject();

            clearPendingProjectWarnings();
            currentView.value = 'homepage';
            return { success: true };
        }
    };

    /** 删除项目 */
    const deleteProject = async (name: string): Promise<boolean> => {
        if (!supports(runtime.capabilities.projectCatalog)) {
            debugStore.warn('[AppStore] Standalone Runtime 不支持删除 Server 项目');
            return false;
        }

        debugStore.log(`[AppStore] Deleting project: ${name}`);
        try {
            const result = await ProjectService.deleteProject(name);
            if (result.success) {
                await fetchProjectList();
                return true;
            }
            debugStore.error(`[AppStore] Delete failed: ${result.message}`);
            return false;
        } catch (err: any) {
            debugStore.error(`[AppStore] Delete error: ${err}`);
            return false;
        }
    };

    /** 导航到首页 */
    const goToHomepage = () => {
        clearPendingProjectWarnings();
        currentView.value = 'homepage';
    };

    /** 导航到工作区 */
    const goToWorkspace = () => {
        currentView.value = 'workspace';
    };

    const stageProjectWarnings = (warnings?: string[]) => {
        pendingProjectWarnings.value = warnings ? [...warnings] : [];
    };

    const applyPendingProjectWarning = () => {
        if (pendingProjectWarnings.value.length === 0) {
            return;
        }

        const canvasStore = useCanvasStore();
        canvasStore.setPrompt(pendingProjectWarnings.value[0] ?? null);
        pendingProjectWarnings.value = [];
    };

    const clearPendingProjectWarnings = () => {
        pendingProjectWarnings.value = [];
    };

    return {
        currentView,
        projectList,
        recentProjects,
        isLoadingList,
        listError,
        pendingProjectWarnings,
        fetchProjectList,
        openProject,
        closeProject,
        deleteProject,
        goToHomepage,
        goToWorkspace,
        stageProjectWarnings,
        applyPendingProjectWarning,
        clearPendingProjectWarnings
    };
});
