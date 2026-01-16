using System.Collections.Concurrent;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目上下文单例 - 持有当前加载的项目状态
    /// 实现单项目模式：Server 一次只服务一个项目
    /// 支持多窗口 Worktree 隔离：每个窗口可以有独立的 Worktree 路径
    /// </summary>
    public class ProjectContext
    {
        /// <summary>
        /// 当前项目文件夹路径（解压后的目录 / 主仓库路径）
        /// </summary>
        public string? CurrentProjectPath { get; private set; }

        /// <summary>
        /// 当前项目的 BCP 源文件路径
        /// </summary>
        public string? SourceBcpPath { get; private set; }

        /// <summary>
        /// 项目是否已加载
        /// </summary>
        public bool IsLoaded => !string.IsNullOrEmpty(CurrentProjectPath);

        /// <summary>
        /// Git 操作是否正在进行中
        /// 当此标记为 true 时，FileWatcher 会暂停触发更新
        /// </summary>
        public bool IsGitOperationInProgress { get; set; }

        /// <summary>
        /// 当前激活的窗口 ID
        /// 用于确定 GetProjectData 应读取哪个 Worktree 的数据
        /// </summary>
        public string? ActiveWindowId { get; set; }

        /// <summary>
        /// 窗口 → Worktree 路径映射（线程安全）
        /// Key: 窗口 ID, Value: Worktree 绝对路径
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _windowWorktreePaths = new();

        /// <summary>
        /// 设置当前项目
        /// </summary>
        /// <param name="projectPath">项目文件夹路径</param>
        /// <param name="bcpPath">BCP 源文件路径（可选）</param>
        public void SetProject(string projectPath, string? bcpPath = null)
        {
            CurrentProjectPath = projectPath;
            SourceBcpPath = bcpPath;
        }

        /// <summary>
        /// 清空当前项目
        /// </summary>
        public void Clear()
        {
            CurrentProjectPath = null;
            SourceBcpPath = null;
            ActiveWindowId = null;
            _windowWorktreePaths.Clear();
        }

        /// <summary>
        /// 注册窗口的 Worktree 路径
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <param name="worktreePath">Worktree 绝对路径</param>
        public void RegisterWindowWorktree(string windowId, string worktreePath)
        {
            _windowWorktreePaths[windowId] = worktreePath;
        }

        /// <summary>
        /// 注销窗口的 Worktree 路径
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        public void UnregisterWindowWorktree(string windowId)
        {
            _windowWorktreePaths.TryRemove(windowId, out _);
        }

        /// <summary>
        /// 获取指定窗口的 Worktree 路径
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <returns>Worktree 路径，如果不存在则返回 null</returns>
        public string? GetWorktreePath(string windowId)
        {
            return _windowWorktreePaths.TryGetValue(windowId, out var path) ? path : null;
        }

        /// <summary>
        /// 获取当前激活窗口的 Worktree 路径
        /// </summary>
        /// <returns>Worktree 路径，如果未设置激活窗口或映射不存在则返回 null</returns>
        public string? GetActiveWorktreePath()
        {
            if (string.IsNullOrEmpty(ActiveWindowId)) return null;
            return GetWorktreePath(ActiveWindowId);
        }

        /// <summary>
        /// 获取所有已注册的窗口 ID 列表
        /// </summary>
        public IEnumerable<string> GetRegisteredWindowIds()
        {
            return _windowWorktreePaths.Keys;
        }
    }
}
