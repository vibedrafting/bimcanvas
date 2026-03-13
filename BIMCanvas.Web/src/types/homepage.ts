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

/**
 * 最近打开记录（来自 GET /api/project/recent）
 * 注意：Server 端 LoadWithExistsCheck 会过滤不存在的条目，
 * 所以返回的条目都是存在的
 */
export interface RecentProjectEntry {
  name: string;
  folderPath: string;
  lastOpenedAt: string;
  openCount: number;
}

/** 关闭项目的标准化响应（前端适配层） */
export interface CloseProjectResult {
  success: boolean;
  hasUnsavedChanges: boolean;
  message?: string;
}
