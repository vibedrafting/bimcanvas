/**
 * 首页相关类型定义 — 与 Server 端 DTO 对齐
 */

/** 项目摘要（来自 GET /api/project/list） */
export interface ProjectSummary {
  name: string;
  folderPath: string;
  createdAt: string | null;
  updatedAt: string | null;
  schemeCount: number;
  activeScheme: string | null;
  version: string | null;
  isValid: boolean;
  errorMessage: string | null;
}

/** 最近打开记录（来自 GET /api/project/recent） */
export interface RecentProjectEntry {
  name: string;
  folderPath: string;
  lastOpenedAt: string;
  openCount: number;
  exists: boolean;
}

/** 项目列表 API 响应 */
export interface ProjectListResponse {
  projects: ProjectSummary[];
  projectsRoot: string;
}

/** 关闭项目 API 响应 */
export interface CloseProjectResponse {
  status: 'Success' | 'Warning' | 'Error';
  hasUnsavedChanges: boolean;
  message?: string;
}
