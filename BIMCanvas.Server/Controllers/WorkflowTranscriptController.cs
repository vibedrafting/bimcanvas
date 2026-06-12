using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// Workflow transcript 端点（Task 页 tier C 完成详情）。
    /// Web 在 workflow 完成 / 用户点开详情时按需 GET，绝不轮询。
    /// 经 AddNewtonsoftJson 序列化（camelCase），与前端 WorkflowTranscript 接口对齐。
    /// </summary>
    [ApiController]
    [Route("api/workflows")]
    public class WorkflowTranscriptController : ControllerBase
    {
        private readonly WorkflowTranscriptService _transcriptService;

        public WorkflowTranscriptController(WorkflowTranscriptService transcriptService)
        {
            _transcriptService = transcriptService;
        }

        /// <summary>
        /// 按 sdkSessionId 读取 workflow 各子 agent 的执行详情（model / tokens / tools / prompt / outcome）。
        /// 未找到时返回空 agents 列表（200），由前端按"无详情"渲染。
        /// </summary>
        [HttpGet("{sdkSessionId}/transcript")]
        public IActionResult GetTranscript(string sdkSessionId, [FromQuery] string? taskId = null)
        {
            if (string.IsNullOrWhiteSpace(sdkSessionId))
            {
                return BadRequest(new { success = false, message = "sdkSessionId 不能为空" });
            }
            var result = _transcriptService.GetTranscript(sdkSessionId, taskId);
            return Ok(result);
        }

        /// <summary>
        /// 后台 Task 详情（bg-task-panel 行展开按需拉取）：Bash 输出尾部 / Agent 型 outcome / Workflow 内派生归属。
        /// 未找到任何详情时返回 kind=null（200），前端按"无详情"渲染。
        /// </summary>
        [HttpGet("{sdkSessionId}/tasks/{taskId}")]
        public IActionResult GetTaskDetail(string sdkSessionId, string taskId, [FromQuery] string? toolUseId = null)
        {
            if (string.IsNullOrWhiteSpace(sdkSessionId) || string.IsNullOrWhiteSpace(taskId))
            {
                return BadRequest(new { success = false, message = "sdkSessionId / taskId 不能为空" });
            }
            var result = _transcriptService.GetTaskDetail(sdkSessionId, taskId, toolUseId);
            return Ok(result);
        }
    }
}
