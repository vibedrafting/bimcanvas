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
        private readonly string _agentHealthPath;
        private readonly RuntimeEndpointState _runtimeEndpointState;
        private readonly string _configuredAgentBaseUrl;
        private static readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };

        public AgentClientService(
            ILogger<AgentClientService> logger,
            ServerConfig config,
            RuntimeEndpointState runtimeEndpointState)
        {
            _logger = logger;
            _runtimeEndpointState = runtimeEndpointState;
            _configuredAgentBaseUrl = config.Agent.GetResolvedBaseUrl();
            _agentHealthPath = config.Agent.GetResolvedHealthPath();
        }

        public string AgentBaseUrl
        {
            get
            {
                var runtimeBaseUrl = _runtimeEndpointState.GetSnapshot().Agent.ActualUrl;
                return string.IsNullOrWhiteSpace(runtimeBaseUrl) ? _configuredAgentBaseUrl : runtimeBaseUrl;
            }
        }

        public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{AgentBaseUrl}{_agentHealthPath}");
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Agent 健康检查失败: {Message}", ex.Message);
                return false;
            }
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
                var url = $"{AgentBaseUrl}/api/agent/close";
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
        /// 通知 Agent 关闭指定项目下的所有窗口实例，释放项目目录及 Worktree 的 CWD 锁。
        /// </summary>
        public async Task<bool> CloseProjectAgentsAsync(string projectPath, int waitMs = 1000)
        {
            try
            {
                var url = $"{AgentBaseUrl}/api/agent/close-project";
                var json = JsonSerializer.Serialize(new { projectPath });
                using var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Agent 已关闭项目会话: {ProjectPath}", projectPath);
                    await Task.Delay(waitMs);
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("Agent 项目会话不存在或端点不可用: {ProjectPath}", projectPath);
                    await Task.Delay(waitMs);
                    return false;
                }

                _logger.LogWarning("关闭 Agent 项目会话失败: {ProjectPath}, 状态码: {StatusCode}", projectPath, response.StatusCode);
                return false;
            }
            catch (Exception ex)
            {
                // Agent 服务不可达（已退出），视为项目目录锁已释放
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

        public bool CloseProjectAgentsSync(string projectPath, int waitMs = 1000)
        {
            try
            {
                return CloseProjectAgentsAsync(projectPath, waitMs).GetAwaiter().GetResult();
            }
            catch
            {
                return false;
            }
        }
    }
}
