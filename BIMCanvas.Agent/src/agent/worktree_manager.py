"""WorktreeManager - 管理 SubAgent 的 Worktree 生命周期

为 SubAgent 并行任务创建隔离的 Git Worktree 环境。
每个 SubAgent 在独立的分支和工作目录中执行，互不干扰。
"""

import asyncio
import logging
from dataclasses import dataclass
from typing import Optional
import aiohttp

logger = logging.getLogger(__name__)

# Server REST API URL
SERVER_URL = "http://localhost:5000"


@dataclass
class WorktreeContext:
    """Worktree 上下文信息"""
    job_id: str
    """任务 ID（如 "storage", "zone_rz_001"）"""

    worktree_name: str
    """Worktree 名称（如 "ai-storage"）"""

    branch_name: str
    """分支名称（如 "feat/ai-storage"）"""

    path: str
    """Worktree 完整路径"""


class WorktreeManager:
    """
    Worktree 生命周期管理器

    职责：
    1. 为 SubAgent 创建隔离的 Worktree
    2. 跟踪活跃的 Worktree
    3. 清理完成的 Worktree
    4. 协调并行任务的 Git 操作
    """

    # 分支命名前缀
    AI_BRANCH_PREFIX = "feat/ai-"
    AI_WORKTREE_PREFIX = "ai-"

    def __init__(self, main_repo_path: str):
        """
        初始化 WorktreeManager

        Args:
            main_repo_path: 主仓库路径
        """
        self.main_repo_path = main_repo_path
        self._active_worktrees: dict[str, WorktreeContext] = {}  # job_id -> context
        self._lock = asyncio.Lock()

    async def create_for_subagent(
        self,
        job_id: str,
        base_branch: str = "main"
    ) -> Optional[WorktreeContext]:
        """
        为 SubAgent 创建 Worktree

        Args:
            job_id: 任务 ID（如 "storage", "zone_rz_001"）
            base_branch: 基准分支（默认 "main"）

        Returns:
            WorktreeContext 或 None（如果创建失败）
        """
        worktree_name = f"{self.AI_WORKTREE_PREFIX}{job_id}"
        branch_name = f"{self.AI_BRANCH_PREFIX}{job_id}"

        async with self._lock:
            # 检查是否已存在
            if job_id in self._active_worktrees:
                logger.warning(f"Worktree for job '{job_id}' already exists")
                return self._active_worktrees[job_id]

            try:
                async with aiohttp.ClientSession() as session:
                    async with session.post(
                        f"{SERVER_URL}/api/git/worktrees",
                        json={
                            "name": worktree_name,
                            "branchName": branch_name
                        }
                    ) as resp:
                        if resp.status != 200:
                            error_data = await resp.json()
                            logger.error(f"Failed to create worktree: {error_data.get('message', 'Unknown error')}")
                            return None

                        result = await resp.json()
                        worktree_path = result.get("path", "")

                        context = WorktreeContext(
                            job_id=job_id,
                            worktree_name=worktree_name,
                            branch_name=branch_name,
                            path=worktree_path
                        )

                        self._active_worktrees[job_id] = context
                        logger.info(f"Created worktree for job '{job_id}': {worktree_path}")
                        return context

            except aiohttp.ClientError as e:
                logger.error(f"Failed to connect to Server: {e}")
                return None

    async def remove(self, job_id: str) -> bool:
        """
        删除 Worktree

        Args:
            job_id: 任务 ID

        Returns:
            是否成功删除
        """
        async with self._lock:
            if job_id not in self._active_worktrees:
                logger.warning(f"Worktree for job '{job_id}' not found")
                return False

            context = self._active_worktrees[job_id]

            try:
                async with aiohttp.ClientSession() as session:
                    async with session.delete(
                        f"{SERVER_URL}/api/git/worktrees/{context.worktree_name}"
                    ) as resp:
                        if resp.status != 200:
                            error_data = await resp.json()
                            logger.error(f"Failed to remove worktree: {error_data.get('message', 'Unknown error')}")
                            return False

                        del self._active_worktrees[job_id]
                        logger.info(f"Removed worktree for job '{job_id}'")
                        return True

            except aiohttp.ClientError as e:
                logger.error(f"Failed to connect to Server: {e}")
                return False

    async def commit_and_merge(
        self,
        job_id: str,
        commit_message: str,
        target_branch: str = "main"
    ) -> bool:
        """
        提交 Worktree 的更改并合并到目标分支

        Args:
            job_id: 任务 ID
            commit_message: 提交信息
            target_branch: 目标分支（默认 "main"）

        Returns:
            是否成功合并
        """
        if job_id not in self._active_worktrees:
            logger.warning(f"Worktree for job '{job_id}' not found")
            return False

        context = self._active_worktrees[job_id]

        try:
            async with aiohttp.ClientSession() as session:
                # 1. 在 Worktree 中提交更改
                # 注意：这里需要在 Worktree 路径下执行提交
                # 但当前 API 使用的是主仓库路径，需要扩展 API
                # 暂时假设 Server 会处理

                # 2. 切换到目标分支（主仓库）
                async with session.post(
                    f"{SERVER_URL}/api/git/checkout",
                    json={"branchName": target_branch}
                ) as resp:
                    if resp.status != 200:
                        error_data = await resp.json()
                        logger.warning(f"Failed to checkout target branch: {error_data.get('message', '')}")

                # 3. 合并 Worktree 分支
                async with session.post(
                    f"{SERVER_URL}/api/git/merge",
                    json={
                        "sourceBranch": context.branch_name,
                        "commitMessage": f"Merge {context.branch_name}: {commit_message}"
                    }
                ) as resp:
                    if resp.status != 200:
                        error_data = await resp.json()
                        logger.error(f"Failed to merge: {error_data.get('message', 'Unknown error')}")
                        return False

                    result = await resp.json()
                    if result.get("hasConflicts"):
                        logger.warning(f"Merge has conflicts for job '{job_id}'")
                        return False

                    logger.info(f"Successfully merged job '{job_id}' to {target_branch}")
                    return True

        except aiohttp.ClientError as e:
            logger.error(f"Failed to connect to Server: {e}")
            return False

    async def cleanup_all(self) -> int:
        """
        清理所有活跃的 Worktree

        Returns:
            清理的 Worktree 数量
        """
        job_ids = list(self._active_worktrees.keys())
        count = 0

        for job_id in job_ids:
            if await self.remove(job_id):
                count += 1

        logger.info(f"Cleaned up {count} worktrees")
        return count

    def get_context(self, job_id: str) -> Optional[WorktreeContext]:
        """
        获取 Worktree 上下文

        Args:
            job_id: 任务 ID

        Returns:
            WorktreeContext 或 None
        """
        return self._active_worktrees.get(job_id)

    def get_active_job_ids(self) -> list[str]:
        """
        获取所有活跃的任务 ID

        Returns:
            任务 ID 列表
        """
        return list(self._active_worktrees.keys())

    @property
    def active_count(self) -> int:
        """活跃 Worktree 数量"""
        return len(self._active_worktrees)
