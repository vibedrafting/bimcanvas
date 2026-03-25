/**
 * Git Worktree 服务
 * 封装与后端 GitController 的 Worktree API 交互
 */

import type {
  WorktreeInfo,
  CreateWorktreeRequest,
  CreateWorktreeResponse,
  DeleteWorktreeResponse
} from '../types/worktree';
import { SERVER_API } from '../config/api';

const API_BASE = `${SERVER_API}/git`;

/**
 * Git Worktree 服务类
 * 用于管理多窗口对应的 Git Worktree
 */
export class GitWorktreeService {
  /**
   * 获取所有 Worktree 列表
   */
  static async getWorktrees(): Promise<WorktreeInfo[]> {
    const resp = await fetch(`${API_BASE}/worktrees`);
    if (!resp.ok) {
      const error = await resp.json().catch(() => ({ message: `HTTP ${resp.status}` }));
      throw new Error(error.message || '获取 Worktree 列表失败');
    }
    return resp.json();
  }

  /**
   * 创建 Worktree（对应虚拟窗口）
   * @param request 创建请求参数
   */
  static async createWorktree(request: CreateWorktreeRequest): Promise<CreateWorktreeResponse> {
    const resp = await fetch(`${API_BASE}/worktrees`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request)
    });
    if (!resp.ok) {
      const error = await resp.json().catch(() => ({ message: `HTTP ${resp.status}` }));
      throw new Error(error.message || '创建 Worktree 失败');
    }
    return resp.json();
  }

  /**
   * 删除 Worktree（关闭虚拟窗口）
   * @param name Worktree 名称
   * @param deleteBranch 是否同时删除关联分支
   */
  static async deleteWorktree(name: string, deleteBranch = false): Promise<DeleteWorktreeResponse> {
    const url = deleteBranch
      ? `${API_BASE}/worktrees/${encodeURIComponent(name)}?deleteBranch=true`
      : `${API_BASE}/worktrees/${encodeURIComponent(name)}`;

    const resp = await fetch(url, { method: 'DELETE' });
    if (!resp.ok) {
      const error = await resp.json().catch(() => ({ message: `HTTP ${resp.status}` }));
      throw new Error(error.message || '删除 Worktree 失败');
    }
    return resp.json();
  }
}
