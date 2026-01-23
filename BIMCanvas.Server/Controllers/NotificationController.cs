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
}
