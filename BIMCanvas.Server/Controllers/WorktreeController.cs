using System.Collections.Generic;
using System.Linq;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// Worktree 元数据 API 控制器
    /// 提供 Worktree 名称到分支名称的映射服务
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class WorktreeController : ControllerBase
    {
        private readonly ILogger<WorktreeController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly IWorktreeMetadataServiceFactory _metadataServiceFactory;

        public WorktreeController(
            ILogger<WorktreeController> logger,
            ProjectContext projectContext,
            IWorktreeMetadataServiceFactory metadataServiceFactory)
        {
            _logger = logger;
            _projectContext = projectContext;
            _metadataServiceFactory = metadataServiceFactory;
        }

        /// <summary>
        /// 获取完整元数据
        /// </summary>
        [HttpGet("metadata")]
        public ActionResult<MetadataResponse> GetMetadata()
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { success = false, error = "项目未打开" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;
            var metadataService = _metadataServiceFactory.Create(projectPath);
            var metadata = metadataService.Load();

            return Ok(new MetadataResponse
            {
                Success = true,
                Worktrees = metadata.Worktrees.Select(e => new WorktreeMetadataDto
                {
                    Name = e.Name,
                    BranchName = e.BranchName,
                    Intent = e.Intent,
                    BaseBranch = e.BaseBranch,
                    CreatedAt = e.CreatedAt,
                    CreatedBy = e.CreatedBy
                }).ToList()
            });
        }

        /// <summary>
        /// 批量解析 worktree 名称到分支名称
        /// </summary>
        [HttpPost("batch-resolve")]
        public ActionResult<BatchResolveResponse> BatchResolve([FromBody] BatchResolveRequest request)
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { success = false, error = "项目未打开" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;
            var metadataService = _metadataServiceFactory.Create(projectPath);

            // ✅ 优化：一次性加载所有元数据，避免循环内重复 I/O
            var metadata = metadataService.Load();
            var mapping = new Dictionary<string, string>();
            var errors = new List<string>();

            foreach (var name in request.Names)
            {
                var entry = metadata.Worktrees.FirstOrDefault(w => w.Name == name);
                if (entry != null)
                {
                    mapping[name] = entry.BranchName;
                }
                else
                {
                    errors.Add($"未找到 worktree: {name}");
                }
            }

            return Ok(new BatchResolveResponse
            {
                Success = errors.Count == 0,
                Mapping = mapping,
                Errors = errors.Count > 0 ? errors : null
            });
        }

        #region DTOs

        public class BatchResolveRequest
        {
            public List<string> Names { get; set; } = new List<string>();
        }

        public class BatchResolveResponse
        {
            public bool Success { get; set; }
            public Dictionary<string, string> Mapping { get; set; } = new Dictionary<string, string>();
            public List<string>? Errors { get; set; }
        }

        public class MetadataResponse
        {
            public bool Success { get; set; }
            public List<WorktreeMetadataDto> Worktrees { get; set; } = new List<WorktreeMetadataDto>();
        }

        public class WorktreeMetadataDto
        {
            public string Name { get; set; } = string.Empty;
            public string BranchName { get; set; } = string.Empty;
            public string Intent { get; set; } = string.Empty;
            public string BaseBranch { get; set; } = string.Empty;
            public System.DateTime CreatedAt { get; set; }
            public string CreatedBy { get; set; } = string.Empty;
        }

        #endregion
    }
}
