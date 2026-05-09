using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using BIMCanvas.Server.Dtos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services;

/// <summary>
/// 直连 LLM 端点的连通性探测服务。绕过 Agent / SDK，单纯用 HttpClient 打一个最小请求，
/// 让用户在配置界面立刻知道 baseUrl/apiKey/model 这一组配置是否能从 Server 这台机器
/// 真实拿到响应。所有调用走相同的 15 秒硬超时，把"端点排队/吊死"的故障从"Agent 哑死"中剥离出来。
/// </summary>
public sealed class LlmEndpointTestService
{
    private const int TimeoutSeconds = 15;
    private const int SnippetMaxChars = 80;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly ILogger<LlmEndpointTestService> _logger;

    public LlmEndpointTestService(ILogger<LlmEndpointTestService> logger)
    {
        _logger = logger;
    }

    public async Task<LlmEndpointTestResultDto> TestAsync(
        LlmEndpointTestRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var provider = (request.RuntimeProvider ?? "").Trim().ToLowerInvariant();
        var baseUrl = (request.BaseUrl ?? "").Trim();
        var apiKey = request.ApiKey ?? "";
        var model = (request.Model ?? "").Trim();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return new LlmEndpointTestResultDto
            {
                Success = false,
                ErrorType = "bad_request",
                ErrorMessage = "baseUrl 为空"
            };
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new LlmEndpointTestResultDto
            {
                Success = false,
                ErrorType = "bad_request",
                ErrorMessage = "apiKey 为空",
                RequestUrl = baseUrl
            };
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return new LlmEndpointTestResultDto
            {
                Success = false,
                ErrorType = "bad_request",
                ErrorMessage = "model 为空"
            };
        }

        string requestUrl;
        try
        {
            requestUrl = BuildRequestUrl(provider, baseUrl);
        }
        catch (Exception ex)
        {
            return new LlmEndpointTestResultDto
            {
                Success = false,
                ErrorType = "bad_request",
                ErrorMessage = $"baseUrl 无法解析: {ex.Message}"
            };
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var httpRequest = BuildHttpRequest(provider, requestUrl, apiKey, model);
            using var response = await HttpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseContentRead,
                linkedCts.Token);

            stopwatch.Stop();
            var statusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

            if (response.IsSuccessStatusCode)
            {
                var snippet = ExtractSnippet(provider, body);
                return new LlmEndpointTestResultDto
                {
                    Success = true,
                    LatencyMs = stopwatch.ElapsedMilliseconds,
                    StatusCode = statusCode,
                    ErrorType = "ok",
                    ErrorMessage = "",
                    SampleResponseSnippet = snippet,
                    RequestUrl = requestUrl
                };
            }

            return new LlmEndpointTestResultDto
            {
                Success = false,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                StatusCode = statusCode,
                ErrorType = ClassifyHttpStatus(statusCode),
                ErrorMessage = TrimMessage(body),
                RequestUrl = requestUrl
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new LlmEndpointTestResultDto
            {
                Success = false,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                ErrorType = "timeout",
                ErrorMessage = $"{TimeoutSeconds} 秒内未收到响应",
                RequestUrl = requestUrl
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            var errorType = ex.InnerException is SocketException ? "network_unreachable" : "network_unreachable";
            return new LlmEndpointTestResultDto
            {
                Success = false,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                ErrorType = errorType,
                ErrorMessage = TrimMessage(ex.Message),
                RequestUrl = requestUrl
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "LLM 端点检测异常: {Url}", requestUrl);
            return new LlmEndpointTestResultDto
            {
                Success = false,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                ErrorType = "unknown",
                ErrorMessage = TrimMessage(ex.Message),
                RequestUrl = requestUrl
            };
        }
    }

    private static string BuildRequestUrl(string provider, string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        var path = provider == "openai" ? "v1/chat/completions" : "v1/messages";
        var basePart = new Uri(normalized + "/", UriKind.Absolute);
        return new Uri(basePart, path).ToString();
    }

    private static HttpRequestMessage BuildHttpRequest(string provider, string url, string apiKey, string model)
    {
        var payload = new JObject
        {
            ["model"] = model,
            ["max_tokens"] = 16,
            ["messages"] = new JArray
            {
                new JObject { ["role"] = "user", ["content"] = "hi" }
            }
        };

        var content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        if (provider == "openai")
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        else
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        }

        return request;
    }

    private static string ClassifyHttpStatus(int statusCode)
    {
        return statusCode switch
        {
            401 or 403 => "auth_failed",
            429 => "rate_limited",
            >= 500 and < 600 => "server_error",
            >= 400 and < 500 => "bad_request",
            _ => "unknown"
        };
    }

    private static string ExtractSnippet(string provider, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "";
        }

        try
        {
            var token = JToken.Parse(body);

            if (provider == "openai")
            {
                var text = token.SelectToken("choices[0].message.content")?.ToString();
                return TrimSnippet(text);
            }

            // anthropic: content 是 array of blocks，取第一个 text block
            var firstText = token.SelectToken("content[0].text")?.ToString();
            if (!string.IsNullOrEmpty(firstText))
            {
                return TrimSnippet(firstText);
            }

            return TrimSnippet(token.ToString(Formatting.None));
        }
        catch
        {
            return TrimSnippet(body);
        }
    }

    private static string TrimSnippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= SnippetMaxChars ? single : single[..SnippetMaxChars] + "…";
    }

    private static string TrimMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var trimmed = text.Trim();
        return trimmed.Length <= 400 ? trimmed : trimmed[..400] + "…";
    }
}
