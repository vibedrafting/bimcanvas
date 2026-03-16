using System.Net.Http;
using System.Text;
using System.Text.Json;
using BIMCanvas.Server.Models;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Server → Agent HTTP 通信客户端
    /// 用于在清理 Worktree 前通知 Agent 关闭窗口（释放 CWD 文件锁）
    /// </summary>
    public class AgentClientService
    {
        private readonly ILogger<AgentClientService> _logger;
        private readonly int _agentPort;
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public AgentClientService(ILogger<AgentClientService> logger, ServerConfig config)
        {
            _logger = logger;
            _agentPort = config.Server.Port;
        }

        /// <summary>
        /// 通知 Agent 关闭指定窗口的 claude.exe 进程（释放 CWD 锁）
        /// </summary>
        /// <param name="windowId">窗口 ID</param>
        /// <param name="waitMs">等待进程释放文件锁的毫秒数</param>
        /// <returns>是否成功</returns>
        public async Task<bool> CloseAgentAsync(string windowId, int waitMs = 1000)
        {
            try
            {
                var url = $"http://127.0.0.1:{_agentPort}/api/agent/close";
                var json = JsonSerializer.Serialize(new { windowId });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Agent 已关闭窗口: {WindowId}", windowId);
                    await Task.Delay(waitMs);
                    return true;
                }

                // 404 = Agent 实例不存在（已自行关闭），视为成功
                // 仍需等待：进程可能刚被关闭，CWD 文件锁尚未释放
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Agent 窗口不存在（已关闭）: {WindowId}", windowId);
                    await Task.Delay(waitMs);
                    return true;
                }

                _logger.LogWarning("关闭 Agent 失败: {WindowId}, 状态码: {StatusCode}", windowId, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                // Agent 服务不可达（已退出），视为成功
                // 仍需等待：进程可能正在退出，CWD 文件锁尚未释放
                _logger.LogDebug("Agent 服务不可达（已退出）: {Message}", ex.Message);
                await Task.Delay(waitMs);
                return true;
            }
        }

        /// <summary>
        /// 同步版本（用于 ProcessExit 等无法 async 的场景）
        /// </summary>
        public bool CloseAgentSync(string windowId, int waitMs = 1000)
        {
            try
            {
                return CloseAgentAsync(windowId, waitMs).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }
    }
}
