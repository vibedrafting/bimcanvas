using System;
using System.IO;
using BIMCanvas.Server.Services.ProjectHealth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 项目健康检查 + 修复入口。
    /// 不依赖当前已加载项目——首页项目列表可对任何 .bcp 项目目录触发。
    /// </summary>
    [ApiController]
    [Route("api/project/health")]
    public class ProjectHealthController : ControllerBase
    {
        private readonly ProjectHealthService _service;
        private readonly ILogger<ProjectHealthController> _logger;

        public ProjectHealthController(
            ProjectHealthService service,
            ILogger<ProjectHealthController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// 只查不改。返回各 check 发现的问题清单。
        /// </summary>
        [HttpPost("inspect")]
        public ActionResult<ProjectInspectionReport> Inspect([FromBody] ProjectHealthRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FolderPath))
                return BadRequest(new { message = "folderPath 必填" });

            try
            {
                var report = _service.InspectAll(request.FolderPath);
                _logger.LogInformation(
                    "[ProjectHealth] Inspect {Path}：{Total} 个问题",
                    request.FolderPath, report.TotalIssues);
                return Ok(report);
            }
            catch (DirectoryNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectHealth] Inspect 失败");
                return StatusCode(500, new { message = $"Inspect 失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 实际修复。修复前 Server 自动 git commit 兜底。
        /// </summary>
        [HttpPost("repair")]
        public ActionResult<ProjectRepairReport> Repair([FromBody] ProjectHealthRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.FolderPath))
                return BadRequest(new { message = "folderPath 必填" });

            try
            {
                var report = _service.RepairAll(request.FolderPath, autoGitCommit: true);
                _logger.LogInformation(
                    "[ProjectHealth] Repair {Path}：snapshot={Snapshot}",
                    request.FolderPath, report.SnapshotCommitHash ?? "-");
                return Ok(report);
            }
            catch (DirectoryNotFoundException ex)
            {
                return NotFound(new { message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                // 项目不是 git 仓库 / commit 失败 等可恢复错误
                return Conflict(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ProjectHealth] Repair 失败");
                return StatusCode(500, new { message = $"Repair 失败: {ex.Message}" });
            }
        }
    }

    public class ProjectHealthRequest
    {
        public string FolderPath { get; set; } = string.Empty;
    }
}
