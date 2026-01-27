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
  intent?: 'isolation' | 'parallel';  // 创建意图：isolation=隔离测试（删除worktree时删除分支），parallel=并行开发（保留分支）
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

/**
 * Worktree 元数据条目
 * 对应后端 WorktreeMetadataEntry
 */
export interface WorktreeMetadataEntry {
  name: string;
  branchName: string;
  intent: 'isolation' | 'parallel';
  baseBranch: string;
  createdAt: string;
  createdBy: string;
}

/**
 * 获取元数据响应
 */
export interface WorktreeMetadataResponse {
  success: boolean;
  worktrees: WorktreeMetadataEntry[];
}
