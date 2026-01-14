using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace BIMCanvas.Server.Services.Git
{
    /// <summary>
    /// 分支锁管理器
    /// 用于多窗口场景中的分支互斥控制
    ///
    /// 核心规则：一个分支只能被一个窗口打开
    /// </summary>
    public class BranchLockManager
    {
        /// <summary>
        /// 分支 -> 窗口 ID 的映射
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _locks = new();

        /// <summary>
        /// 尝试获取分支锁
        /// </summary>
        /// <param name="branch">分支名称</param>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>是否成功获取锁</returns>
        public bool TryAcquire(string branch, string windowId)
        {
            // 如果已经被同一个窗口持有，直接返回成功（幂等）
            if (_locks.TryGetValue(branch, out var existingWindowId))
            {
                return existingWindowId == windowId;
            }

            // 尝试添加锁
            return _locks.TryAdd(branch, windowId);
        }

        /// <summary>
        /// 释放分支锁
        /// </summary>
        /// <param name="branch">分支名称</param>
        /// <param name="windowId">窗口 ID（只有持有者才能释放）</param>
        /// <returns>是否成功释放</returns>
        public bool Release(string branch, string windowId)
        {
            // 只有持有者才能释放锁
            if (_locks.TryGetValue(branch, out var existingWindowId))
            {
                if (existingWindowId == windowId)
                {
                    return _locks.TryRemove(branch, out _);
                }
            }
            return false;
        }

        /// <summary>
        /// 释放指定窗口持有的所有锁
        /// 用于窗口关闭时清理
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>释放的分支数量</returns>
        public int ReleaseAllForWindow(string windowId)
        {
            var branchesToRelease = _locks
                .Where(kvp => kvp.Value == windowId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var branch in branchesToRelease)
            {
                _locks.TryRemove(branch, out _);
            }

            return branchesToRelease.Count;
        }

        /// <summary>
        /// 获取分支锁的持有者
        /// </summary>
        /// <param name="branch">分支名称</param>
        /// <returns>窗口 ID，如果未被锁定则返回 null</returns>
        public string? GetOwner(string branch)
        {
            return _locks.TryGetValue(branch, out var windowId) ? windowId : null;
        }

        /// <summary>
        /// 检查分支是否被锁定
        /// </summary>
        /// <param name="branch">分支名称</param>
        /// <returns>是否被锁定</returns>
        public bool IsLocked(string branch)
        {
            return _locks.ContainsKey(branch);
        }

        /// <summary>
        /// 检查分支是否被指定窗口锁定
        /// </summary>
        /// <param name="branch">分支名称</param>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>是否被该窗口锁定</returns>
        public bool IsLockedBy(string branch, string windowId)
        {
            return _locks.TryGetValue(branch, out var owner) && owner == windowId;
        }

        /// <summary>
        /// 获取所有已锁定的分支
        /// </summary>
        /// <returns>分支名称列表</returns>
        public IEnumerable<string> GetLockedBranches()
        {
            return _locks.Keys.ToList();
        }

        /// <summary>
        /// 获取所有锁定信息
        /// </summary>
        /// <returns>分支 -> 窗口 ID 的字典</returns>
        public IDictionary<string, string> GetAllLocks()
        {
            return new Dictionary<string, string>(_locks);
        }

        /// <summary>
        /// 获取指定窗口锁定的所有分支
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>分支名称列表</returns>
        public IEnumerable<string> GetBranchesLockedByWindow(string windowId)
        {
            return _locks
                .Where(kvp => kvp.Value == windowId)
                .Select(kvp => kvp.Key)
                .ToList();
        }

        /// <summary>
        /// 当前锁定的分支数量
        /// </summary>
        public int Count => _locks.Count;
    }
}
