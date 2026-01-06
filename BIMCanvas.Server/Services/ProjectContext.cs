namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目上下文单例 - 持有当前加载的项目状态
    /// 实现单项目模式：Server 一次只服务一个项目
    /// </summary>
    public class ProjectContext
    {
        /// <summary>
        /// 当前项目文件夹路径（解压后的目录）
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
        }
    }
}
