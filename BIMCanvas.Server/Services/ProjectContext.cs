using System.Collections.Concurrent;
using System.Collections.Generic;
using BIMCanvas.Server.Models.Plugins;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目上下文单例 - 持有当前加载的项目状态
    /// 实现单项目模式：Server 一次只服务一个项目
    /// 支持多窗口 Worktree 隔离：每个窗口可以有独立的 Worktree 路径
    /// <para>
    /// v1.1 扩展 (主真理源 §3.2 / §4.7 / 模板 §4.7):新增四态状态机 + LaunchContext 字段。
    /// 旧 <see cref="IsLoaded"/> 保留向后兼容,实现为 <c>State != None</c>。
    /// </para>
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
        /// 项目状态机当前值 (主真理源 §3.2 四态 + §4.7 pending)。
        /// </summary>
        public ProjectState State { get; private set; } = ProjectState.None;

        /// <summary>
        /// OpenProject 返回时附带的状态投影 (Web 据此决定 UX)。
        /// </summary>
        public OpenStatus? OpenStatus { get; private set; }

        /// <summary>
        /// 当前 LaunchContext (仅 <see cref="ProjectState.Bound"/> / <see cref="ProjectState.Launched"/> 非空)。
        /// </summary>
        public PluginLaunchContext? LaunchContext { get; private set; }

        /// <summary>
        /// 当 OpenStatus = SceneSelectRequired 时,Web 用此字段渲染候选 scenes。
        /// </summary>
        public IReadOnlyList<ProjectScene>? PendingSceneCandidates { get; private set; }

        /// <summary>
        /// 项目是否已加载 (向后兼容字段:与 <c>State != None</c> 等价)。
        /// </summary>
        public bool IsLoaded => State != ProjectState.None;

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
        /// 设置当前项目 (兼容入口,行为等价 SetBound 但不要求 LaunchContext)。
        /// 新代码请用 <see cref="SetPending"/> / <see cref="SetBound"/>。
        /// </summary>
        public void SetProject(string projectPath, string? bcpPath = null)
        {
            CurrentProjectPath = projectPath;
            SourceBcpPath = bcpPath;
            // 向后兼容:保持 State 进入 Bound 但不强制 LaunchContext
            if (State == ProjectState.None) State = ProjectState.Bound;
        }

        /// <summary>
        /// 进入 Pending 状态 (主真理源 §4.7):scene 未绑定 / 多候选未选。
        /// 所有写入 API 在该状态下 403 (V12a)。
        /// </summary>
        public void SetPending(string projectPath, string? bcpPath, OpenStatus openStatus,
            IReadOnlyList<ProjectScene>? candidates = null)
        {
            CurrentProjectPath = projectPath;
            SourceBcpPath = bcpPath;
            State = ProjectState.Pending;
            OpenStatus = openStatus;
            PendingSceneCandidates = candidates;
            LaunchContext = null;
        }

        /// <summary>
        /// 进入 Bound 状态 (scene 绑定完成 + LaunchContext 就绪):允许写入。
        /// </summary>
        public void SetBound(string projectPath, string? bcpPath, PluginLaunchContext launchContext)
        {
            CurrentProjectPath = projectPath;
            SourceBcpPath = bcpPath;
            State = ProjectState.Bound;
            OpenStatus = Models.Plugins.OpenStatus.Bound;
            LaunchContext = launchContext;
            PendingSceneCandidates = null;
        }

        /// <summary>
        /// 标记 Agent 子进程已 launch (M1 阶段才完整启用,M0 留 stub)。
        /// </summary>
        public void MarkLaunched()
        {
            if (State == ProjectState.Bound)
                State = ProjectState.Launched;
        }

        /// <summary>
        /// 写入 gate 检查 (V12a / R3 / 模板 §4.7 pending 阶段拒绝写入)。
        /// 所有写入 API 在改业务数据前必须先调本方法,失败返回 403 + Code。
        /// <para>
        /// 兼容性 (M0 / M1 早期):无 active plugin (LaunchContext=null) 时 State=Bound 即放行 ——
        /// legacy .bcp 项目 + core-base 默认环境的写入路径保留。一旦 plugin 体系激活
        /// (LaunchContext 非空),严格 V12a + V12b 检查启用。
        /// </para>
        /// <para>
        /// (组5 §5.B.5 重载) 接受 <paramref name="targetRelativePath"/> 时启用 V12b 路径隔离检查。
        /// 详见带参数版本。本无参版本仅做 State + Mode 检查(向后兼容)。
        /// </para>
        /// </summary>
        public WriteGateResult CheckWriteAllowed()
        {
            return CheckWriteAllowed(null);
        }

        /// <summary>
        /// (组5 §5.B.5 / V12b) 带路径的写入 gate 检查。
        /// <para>
        /// 在 V12a (State / Mode 检查) 之上,额外检查 <paramref name="targetRelativePath"/>:
        /// </para>
        /// <list type="bullet">
        /// <item>仅在 <c>LaunchContext.ActiveSceneId</c> 非空时启用(legacy 项目不受影响)</item>
        /// <item>路径以 <c>baseline/</c> 或 <c>computed/</c> 开头 → 返回 <c>readonly_zone</c></item>
        /// <item>路径以 <c>schemes/</c> 开头但**不**以 <c>schemes/{activeSceneId}/</c> 开头(且非 <c>schemes/zones.json</c>)→ 返回 <c>scene_write_isolation</c></item>
        /// <item>路径以 <c>references/</c> 或 <c>modules/</c> 开头但**不**以 <c>references/{activeSceneId}/</c> 或 <c>modules/{activeSceneId}/</c> 开头 → 返回 <c>scene_write_isolation</c></item>
        /// </list>
        /// <para>
        /// 调用方负责传入相对项目根的规范化路径(Unix 风格前向斜杠,无前导 /)。
        /// 无路径时(<paramref name="targetRelativePath"/> == null)仅检查 State + Mode。
        /// </para>
        /// </summary>
        public WriteGateResult CheckWriteAllowed(string? targetRelativePath)
        {
            if (State == ProjectState.None)
            {
                return new WriteGateResult(false, "project_pending_binding", "未加载项目,写入禁止");
            }

            if (State == ProjectState.Pending)
            {
                return new WriteGateResult(false, "project_pending_binding",
                    "项目未完成 scene 绑定,写入禁止 (OpenStatus="
                    + (OpenStatus?.ToString() ?? "<unknown>") + ")");
            }

            // State == Bound | Launched
            if (LaunchContext is null)
            {
                // legacy 兼容:无 active plugin 时放行 (M0 主要走这条路径)
                return WriteGateResult.Ok;
            }

            if (LaunchContext.Mode != LaunchMode.ProjectBound)
            {
                return new WriteGateResult(false, "project_pending_binding",
                    "LaunchContext.Mode = Projectless,写入禁止 (V12a)");
            }

            // V12b: 路径隔离检查(组5 §5.B.5)
            // 仅在 path 参数非空 + ActiveSceneId 非空时启用
            var activeSceneId = LaunchContext.ActiveSceneId;
            if (!string.IsNullOrEmpty(targetRelativePath) && !string.IsNullOrEmpty(activeSceneId))
            {
                // 规范化:转 unix 风格 + 去前导斜杠
                var normalized = targetRelativePath.Replace('\\', '/').TrimStart('/');

                // baseline/ + computed/ 全 scene 共享只读
                if (normalized.StartsWith("baseline/", System.StringComparison.OrdinalIgnoreCase) ||
                    normalized.StartsWith("computed/", System.StringComparison.OrdinalIgnoreCase))
                {
                    return new WriteGateResult(false, "readonly_zone",
                        "只读区(baseline / computed)禁止写入: " + normalized);
                }

                // schemes/zones.json 全 scene 共享(主真理源 §3.9):放行
                if (normalized.Equals("schemes/zones.json", System.StringComparison.OrdinalIgnoreCase))
                {
                    return WriteGateResult.Ok;
                }

                // schemes/ 子路径必须以 schemes/{activeSceneId}/ 开头
                if (normalized.StartsWith("schemes/", System.StringComparison.OrdinalIgnoreCase))
                {
                    var requiredPrefix = "schemes/" + activeSceneId + "/";
                    if (!normalized.StartsWith(requiredPrefix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return new WriteGateResult(false, "scene_write_isolation",
                            "写入路径超出当前 scene 范围 (activeSceneId=" + activeSceneId + "): " + normalized);
                    }
                }

                // references/ + modules/ 必须以 references/{activeSceneId}/ 或 modules/{activeSceneId}/ 开头
                foreach (var topDir in new[] { "references/", "modules/" })
                {
                    if (normalized.StartsWith(topDir, System.StringComparison.OrdinalIgnoreCase))
                    {
                        var requiredPrefix = topDir + activeSceneId + "/";
                        if (!normalized.StartsWith(requiredPrefix, System.StringComparison.OrdinalIgnoreCase))
                        {
                            return new WriteGateResult(false, "scene_write_isolation",
                                "写入路径超出当前 scene 范围 (activeSceneId=" + activeSceneId + "): " + normalized);
                        }
                    }
                }
            }

            return WriteGateResult.Ok;
        }

        /// <summary>
        /// 清空当前项目
        /// </summary>
        public void Clear()
        {
            CurrentProjectPath = null;
            SourceBcpPath = null;
            ActiveWindowId = null;
            State = ProjectState.None;
            OpenStatus = null;
            LaunchContext = null;
            PendingSceneCandidates = null;
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
