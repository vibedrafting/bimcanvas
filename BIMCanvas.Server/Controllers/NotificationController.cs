using BIMCanvas.Server.Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 通知控制器 - 处理 Agent 到 Web 的通知推送
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(
            IHubContext<CanvasHub> hubContext,
            ILogger<NotificationController> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        /// <summary>
        /// Agent 发送通知到 Web 端
        /// </summary>
        [HttpPost("agent")]
        public async Task<IActionResult> SendAgentNotification([FromBody] AgentNotificationRequest request)
        {
            if (string.IsNullOrEmpty(request.Title) && string.IsNullOrEmpty(request.Message))
            {
                return BadRequest(new { success = false, message = "title 或 message 至少需要一个" });
            }

            _logger.LogInformation("Agent 通知: {Title} - {Type}", request.Title, request.Type ?? "info");

            await _hubContext.Clients.All.SendAsync("AgentNotification", new
            {
                title = request.Title ?? "",
                message = request.Message ?? "",
                type = request.Type ?? "info",
                timestamp = DateTime.Now.ToString("O"),
                metadata = request.Metadata  // 转发元数据
            });

            return Ok(new { success = true });
        }

        /// <summary>
        /// Agent 数据变更通知 - 显式通知 Web 端数据已更新（绕开 FileSystemWatcher）
        /// </summary>
        [HttpPost("data-changed")]
        public async Task<IActionResult> NotifyDataChanged([FromBody] DataChangedRequest request)
        {
            var source = request.Source ?? "agent";
            var files = request.ChangedFiles ?? new List<string> { "modules.json" };

            _logger.LogInformation("数据变更通知: source={Source}, files=[{Files}]",
                source, string.Join(", ", files));

            // 为每个变更文件广播 ReceiveUpdate 事件（复用现有事件名）
            foreach (var file in files)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                {
                    type = "file_changed",
                    file,
                    timestamp = DateTime.UtcNow,
                    action = "reload",
                    trigger = source  // 标识来源，Web 端据此决定是否跳过
                });
            }

            return Ok(new { success = true, filesNotified = files.Count });
        }
    }

    /// <summary>
    /// Agent 通知请求模型
    /// </summary>
    public class AgentNotificationRequest
    {
        /// <summary>
        /// 通知标题
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// 通知消息内容
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// 通知类型: info, success, warning, error
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// 元数据（如 worktreeNames 列表供 Web 端删除）
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }
    }

    /// <summary>
    /// 数据变更通知请求模型
    /// </summary>
    public class DataChangedRequest
    {
        /// <summary>
        /// 变更的文件列表（如 ["modules.json"]）
        /// </summary>
        public List<string>? ChangedFiles { get; set; }

        /// <summary>
        /// 变更来源标识（如 "agent"）
        /// </summary>
        public string? Source { get; set; }
    }
}
