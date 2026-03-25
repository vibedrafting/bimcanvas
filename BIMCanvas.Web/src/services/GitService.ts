import axios from 'axios';
import { SERVER_API } from '../config/api';

const API_BASE = `${SERVER_API}/git`;

/**
 * Git 工作区状态
 */
export interface GitStatus {
  isLoaded: boolean;
  isGitRepo?: boolean;
  hasUncommittedChanges: boolean;
  currentBranch?: string;
}

/**
 * 提交信息
 */
export interface CommitInfo {
  hash: string;
  message: string;
  author?: string;
  date?: string;
}

/**
 * 提交请求
 */
export interface CommitRequest {
  message?: string;
  worktreeName?: string;
}

/**
 * 提交结果
 */
export interface CommitResult {
  success: boolean;
  message: string;
  committed: boolean;
  commit?: CommitInfo;
  worktree?: string;
}

/**
 * Git 服务 - 封装与 Server 的 Git API
 */
export class GitService {
  /**
   * 获取工作区状态（是否有未提交的更改）
   */
  static async getStatus(): Promise<GitStatus> {
    try {
      const response = await axios.get<GitStatus>(`${API_BASE}/status`);
      return response.data;
    } catch (error: any) {
      console.error('Failed to get git status:', error);
      return {
        isLoaded: false,
        hasUncommittedChanges: false
      };
    }
  }

  /**
   * 提交当前更改（存档）
   * @param request 提交请求（可选消息和 worktree 名称）
   */
  static async commit(request?: CommitRequest): Promise<CommitResult> {
    try {
      const response = await axios.post<CommitResult>(`${API_BASE}/commit`, request || {});
      return response.data;
    } catch (error: any) {
      console.error('Failed to commit:', error);
      return {
        success: false,
        message: error.response?.data?.message || error.message || '提交失败',
        committed: false
      };
    }
  }

  /**
   * 获取当前分支
   */
  static async getCurrentBranch(): Promise<string | null> {
    try {
      const response = await axios.get<{ branch: string }>(`${API_BASE}/current-branch`);
      return response.data.branch;
    } catch (error: any) {
      console.error('Failed to get current branch:', error);
      return null;
    }
  }
}
