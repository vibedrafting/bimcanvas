/**
 * Worktree 类型定义
 * 与后端 WorktreeInfoDto 对应
 */

/**
 * Worktree 信息
 */
export interface WorktreeInfo {
  name: string;
  path: string;
  branch: string | null;
  commitHash: string | null;
  isMain: boolean;
}

/**
 * 创建 Worktree 请求
 */
export interface CreateWorktreeRequest {
  name: string;
  branch: string;
  baseBranch?: string;
}

/**
 * 创建 Worktree 响应
 */
export interface CreateWorktreeResponse {
  name: string;
  path: string;
  branch: string;
  isMain: boolean;
}

/**
 * 删除 Worktree 响应
 */
export interface DeleteWorktreeResponse {
  success: boolean;
  message: string;
}
