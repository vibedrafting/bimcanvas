import { defineStore } from 'pinia';
import { ref } from 'vue';
import { ProjectService } from '../services/ProjectService';
import { useCanvasStore } from './canvasStore';
import { useDebugStore } from './debugStore';
import type { ProjectSummary, RecentProjectEntry } from '../types/homepage';

export type AppView = 'homepage' | 'workspace';

export const useAppStore = defineStore('app', () => {
    const debugStore = useDebugStore();

    // === 状态 ===
    const currentView = ref<AppView>('homepage');
    const projectList = ref<ProjectSummary[]>([]);
    const recentProjects = ref<RecentProjectEntry[]>([]);
    const isLoadingList = ref(false);
    const listError = ref<string | null>(null);

    // === Actions ===

    /** 获取项目列表 + 最近打开记录 */
    const fetchProjectList = async () => {
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
     * App.vue 的 watch 会触发 enterWorkspace() → loadProject()
     */
    const openProject = async (folderPath: string): Promise<boolean> => {
        debugStore.log(`[AppStore] Opening project: ${folderPath}`);
        try {
            const result = await ProjectService.openFolder(folderPath);
            if (result.status === 'Success') {
                currentView.value = 'workspace';
                return true;
            } else {
                debugStore.error(`[AppStore] Open failed: ${result.message}`);
                return false;
            }
        } catch (err: any) {
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
            const result = await ProjectService.closeProject(force);

            if (!result.success && result.hasUnsavedChanges && !force) {
                return { success: false, hasUnsavedChanges: true };
            }

            // 成功关闭（或强制关闭）
            const canvasStore = useCanvasStore();
            canvasStore.resetProject();
            currentView.value = 'homepage';
            return { success: true };
        } catch (err: any) {
            debugStore.error(`[AppStore] Close error: ${err}`);
            // 即使 API 报错也尝试本地清理
            const canvasStore = useCanvasStore();
            canvasStore.resetProject();
            currentView.value = 'homepage';
            return { success: true };
        }
    };

    /** 删除项目 */
    const deleteProject = async (name: string): Promise<boolean> => {
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
        currentView.value = 'homepage';
    };

    /** 导航到工作区 */
    const goToWorkspace = () => {
        currentView.value = 'workspace';
    };

    return {
        currentView,
        projectList,
        recentProjects,
        isLoadingList,
        listError,
        fetchProjectList,
        openProject,
        closeProject,
        deleteProject,
        goToHomepage,
        goToWorkspace
    };
});
