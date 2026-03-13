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
    const projectsRoot = ref('');
    const isLoadingList = ref(false);
    const listError = ref<string | null>(null);

    // === Actions ===

    /** 获取项目列表 */
    const fetchProjectList = async () => {
        isLoadingList.value = true;
        listError.value = null;
        try {
            const [listRes, recentRes] = await Promise.all([
                ProjectService.listProjects(),
                ProjectService.getRecentProjects()
            ]);
            projectList.value = listRes.projects;
            projectsRoot.value = listRes.projectsRoot;
            recentProjects.value = recentRes;
            debugStore.log(`[AppStore] Loaded ${listRes.projects.length} projects`);
        } catch (err: any) {
            listError.value = err.message || '获取项目列表失败';
            debugStore.error(`[AppStore] Failed to fetch projects: ${err}`);
        } finally {
            isLoadingList.value = false;
        }
    };

    /** 打开项目（从首页） */
    const openProject = async (folderPath: string): Promise<boolean> => {
        debugStore.log(`[AppStore] Opening project: ${folderPath}`);
        const result = await ProjectService.openFolder(folderPath);
        if (result.status === 'Success') {
            currentView.value = 'workspace';
            return true;
        } else {
            debugStore.error(`[AppStore] Open failed: ${result.message}`);
            return false;
        }
    };

    /** 关闭项目（返回首页） */
    const closeProject = async (force: boolean = false): Promise<{ success: boolean; hasUnsavedChanges?: boolean }> => {
        debugStore.log(`[AppStore] Closing project (force=${force})`);
        const result = await ProjectService.closeProject(force);

        if (result.status === 'Warning' && result.hasUnsavedChanges && !force) {
            return { success: false, hasUnsavedChanges: true };
        }

        if (result.status === 'Success' || force) {
            const canvasStore = useCanvasStore();
            canvasStore.resetProject();
            currentView.value = 'homepage';
            await fetchProjectList();
            return { success: true };
        }

        return { success: false };
    };

    /** 删除项目 */
    const deleteProject = async (name: string): Promise<boolean> => {
        debugStore.log(`[AppStore] Deleting project: ${name}`);
        const result = await ProjectService.deleteProject(name);
        if (result.status === 'Success') {
            await fetchProjectList();
            return true;
        }
        debugStore.error(`[AppStore] Delete failed: ${result.message}`);
        return false;
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
        projectsRoot,
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
