using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 合并 API - 分区级差异对比和选择性合并
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class MergeController : ControllerBase
    {
        private readonly ILogger<MergeController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly MergeService _mergeService;

        public MergeController(
            ILogger<MergeController> logger,
            ProjectContext projectContext,
            MergeService mergeService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _mergeService = mergeService;
        }

        /// <summary>
        /// 获取分区级差异
        /// </summary>
        /// <param name="sourceBranch">源分支</param>
        /// <param name="targetBranch">目标分支（可选，默认当前分支）</param>
        /// <returns>分区差异列表</returns>
        [HttpGet("diff")]
        public async Task<ActionResult<List<ZoneDiff>>> GetZoneDiffs(
            [FromQuery] string sourceBranch,
            [FromQuery] string? targetBranch = null)
        {
            _logger.LogInformation(">>> [MergeController] GetZoneDiffs: Source={Source}, Target={Target}",
                sourceBranch, targetBranch ?? "(current)");

            if (string.IsNullOrEmpty(sourceBranch))
            {
                return BadRequest(new { message = "源分支不能为空" });
            }

            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            try
            {
                var diffs = await _mergeService.ComputeZoneDiffsAsync(
                    projectPath, sourceBranch, targetBranch);

                return Ok(diffs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取分区差异失败");
                return StatusCode(500, new { message = $"获取分区差异失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 执行选择性合并
        /// </summary>
        /// <param name="request">合并请求</param>
        /// <returns>合并结果</returns>
        [HttpPost("selective")]
        public async Task<ActionResult<SelectiveMergeResult>> ExecuteSelectiveMerge(
            [FromBody] SelectiveMergeRequest request)
        {
            _logger.LogInformation(">>> [MergeController] ExecuteSelectiveMerge: Source={Source}, Zones={Zones}",
                request?.SourceBranch ?? "(null)",
                request?.SelectedZones != null ? string.Join(",", request.SelectedZones) : "(null)");

            if (string.IsNullOrEmpty(request?.SourceBranch))
            {
                return BadRequest(new { message = "源分支不能为空" });
            }

            if (request.SelectedZones == null || request.SelectedZones.Count == 0)
            {
                return BadRequest(new { message = "请选择要合并的分区" });
            }

            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            try
            {
                var result = await _mergeService.ExecuteSelectiveMergeAsync(
                    projectPath, request.SourceBranch, request.SelectedZones);

                if (result.Success)
                {
                    _logger.LogInformation("选择性合并成功: {Count} 个分区", result.MergedZones.Count);
                }
                else
                {
                    _logger.LogWarning("选择性合并部分失败: 成功 {Success}, 失败 {Failed}",
                        result.MergedZones.Count, result.FailedZones.Count);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行选择性合并失败");
                return StatusCode(500, new { message = $"执行选择性合并失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 获取可合并的分支列表（有差异的分支）
        /// </summary>
        /// <returns>分支列表</returns>
        [HttpGet("branches")]
        public ActionResult<List<MergeBranchInfo>> GetMergeableBranches()
        {
            _logger.LogDebug(">>> [MergeController] GetMergeableBranches");

            if (!_projectContext.IsLoaded)
            {
                return Ok(new List<MergeBranchInfo>());
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            try
            {
                // 获取所有分支（除当前分支外）
                var gitService = HttpContext.RequestServices.GetService<GitWorktreeService>();
                if (gitService == null)
                {
                    return StatusCode(500, new { message = "GitWorktreeService 不可用" });
                }

                var currentBranch = gitService.GetCurrentBranch(projectPath);
                var allBranches = gitService.GetBranchesWithDetails(projectPath);

                var result = allBranches
                    .Where(b => !b.IsCurrent)
                    .Select(b => new MergeBranchInfo
                    {
                        Name = b.Name,
                        LastCommit = b.Commit?.Message ?? "",
                        LastCommitTime = b.Commit?.Time ?? "",
                        IsAiBranch = b.Name.StartsWith("feat/ai-")
                    })
                    .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取可合并分支列表失败");
                return StatusCode(500, new { message = $"获取可合并分支列表失败: {ex.Message}" });
            }
        }
    }

    #region DTOs

    /// <summary>
    /// 选择性合并请求
    /// </summary>
    public class SelectiveMergeRequest
    {
        /// <summary>
        /// 源分支
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;

        /// <summary>
        /// 选择的分区 ID 列表
        /// </summary>
        public List<string> SelectedZones { get; set; } = new();
    }

    /// <summary>
    /// 可合并分支信息
    /// </summary>
    public class MergeBranchInfo
    {
        /// <summary>
        /// 分支名
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 最后提交信息
        /// </summary>
        public string LastCommit { get; set; } = string.Empty;

        /// <summary>
        /// 最后提交时间
        /// </summary>
        public string LastCommitTime { get; set; } = string.Empty;

        /// <summary>
        /// 是否是 AI 生成的分支
        /// </summary>
        public bool IsAiBranch { get; set; }
    }

    #endregion
}
