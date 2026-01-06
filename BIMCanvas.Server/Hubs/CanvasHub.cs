using Microsoft.AspNetCore.SignalR;

namespace BIMCanvas.Server.Hubs
{
    /// <summary>
    /// 画布实时通信 Hub
    /// 用于向 Web 客户端推送项目数据更新
    /// </summary>
    public class CanvasHub : Hub
    {
        private readonly ILogger<CanvasHub> _logger;

        public CanvasHub(ILogger<CanvasHub> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 客户端连接时调用
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            _logger.LogInformation("SignalR 客户端已连接: {ConnectionId}", Context.ConnectionId);
            await base.OnConnectedAsync();
        }

        /// <summary>
        /// 客户端断开时调用
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _logger.LogInformation("SignalR 客户端已断开: {ConnectionId}", Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// 客户端发送更新（双向通信）
        /// </summary>
        public async Task SendUpdate(object data)
        {
            _logger.LogDebug("收到客户端更新: {ConnectionId}", Context.ConnectionId);
            // 广播给所有其他客户端
            await Clients.Others.SendAsync("ReceiveUpdate", data);
        }
    }
}
