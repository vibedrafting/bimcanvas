using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// WorktreeMetadataService 工厂接口
    /// 用于在需要动态传入 projectPath 的场景下创建服务实例
    /// </summary>
    public interface IWorktreeMetadataServiceFactory
    {
        /// <summary>
        /// 创建 WorktreeMetadataService 实例
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <returns>WorktreeMetadataService 实例</returns>
        WorktreeMetadataService Create(string projectPath);
    }

    /// <summary>
    /// WorktreeMetadataService 工厂实现
    /// </summary>
    public class WorktreeMetadataServiceFactory : IWorktreeMetadataServiceFactory
    {
        private readonly ILogger<WorktreeMetadataService> _logger;

        public WorktreeMetadataServiceFactory(ILogger<WorktreeMetadataService> logger)
        {
            _logger = logger;
        }

        public WorktreeMetadataService Create(string projectPath)
        {
            return new WorktreeMetadataService(projectPath, _logger);
        }
    }
}
