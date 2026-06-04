using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Exceptions;
using BIMCanvas.Server.Models.Plugins;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.PluginSecurity;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Controllers;

/// <summary>
/// Plugin 管理 REST 端点 (模板 §4.6 / 主真理源 §4.2)。
/// <para>
/// 统一异常响应格式 <c>{ code, message, details? }</c>;HTTP 状态码与 <see cref="PluginException.Code"/>
/// 一一对应,组 4 Web 据此显示用户提示。
/// </para>
/// </summary>
[ApiController]
[Route("api/plugins")]
public sealed class PluginsController : ControllerBase
{
    private readonly PluginLifecycleService _lifecycle;
    private readonly PluginInstallService _installService;
    private readonly PluginTrustService _trustService;
    private readonly PluginScaffoldService _scaffoldService;
    private readonly StaticPluginValidator _validator;
    private readonly ILogger<PluginsController> _logger;

    public PluginsController(
        PluginLifecycleService lifecycle,
        PluginInstallService installService,
        PluginTrustService trustService,
        PluginScaffoldService scaffoldService,
        StaticPluginValidator validator,
        ILogger<PluginsController> logger)
    {
        _lifecycle = lifecycle;
        _installService = installService;
        _trustService = trustService;
        _scaffoldService = scaffoldService;
        _validator = validator;
        _logger = logger;
    }

    // ─── GET /api/plugins ──────────────────────────────────────────────────

    /// <summary>
    /// 列出已安装 plugin。返回 trustState / sourceUrl / installedVersion 等元数据,
    /// 以及当前 active plugin 标识 (供 Web 设置页渲染)。
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PluginListResponse>> List(CancellationToken ct)
    {
        var states = await _trustService.GetAllStatesAsync(ct);
        var serverConfig = ConfigService.Load();
        var activePluginId = string.IsNullOrWhiteSpace(serverConfig.Agent.ActivePlugin)
            ? null
            : serverConfig.Agent.ActivePlugin;

        var items = states.Select(kv =>
        {
            string? displayName = null;
            string? description = null;
            string? mcpNamespace = null;
            try
            {
                var manifestPath = PluginPaths.PluginManifestFile(kv.Key);
                if (System.IO.File.Exists(manifestPath))
                {
                    var manifest = JObject.Parse(System.IO.File.ReadAllText(manifestPath));
                    displayName = (string?)manifest["displayName"];
                    description = (string?)manifest["description"];
                    mcpNamespace = (string?)manifest["mcpNamespace"] ?? (string?)manifest["name"];
                }
            }
            catch { /* manifest 损坏不阻断列表 */ }

            return new PluginListItem
            {
                PluginId = kv.Key,
                DisplayName = displayName ?? kv.Key,
                Description = description,
                Version = kv.Value.InstalledVersion,
                McpNamespace = mcpNamespace,
                TrustState = kv.Value.TrustState,
                SourceUrl = kv.Value.SourceUrl,
                ResolvedCommit = kv.Value.ResolvedCommit,
                SourceKind = kv.Value.SourceKind,
                InstalledAt = kv.Value.InstalledAt,
                TrustedAt = kv.Value.TrustedAt,
                IsActive = string.Equals(activePluginId, kv.Key, StringComparison.OrdinalIgnoreCase),
            };
        }).ToList();

        return Ok(new PluginListResponse { Plugins = items, ActivePluginId = activePluginId });
    }

    // ─── POST /api/plugins/install ─────────────────────────────────────────

    /// <summary>
    /// 安装 plugin。只调 StaticPluginValidator,绝不执行 plugin Python 代码 (R1)。
    /// 成功响应含 trustState=Untrusted + 用户需要后续点 [信任并激活] 的提示。
    /// </summary>
    [HttpPost("install")]
    public async Task<IActionResult> Install([FromBody] InstallRequest request, CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new ErrorResponse("invalid_request", "请求体必须非空"));

        // source kind 分派:缺省 github(向后兼容,只认 repoUrl)。
        var kind = (request.SourceKind ?? "github").Trim().ToLowerInvariant();

        try
        {
            PluginInstallState state;
            switch (kind)
            {
                case "github":
                    if (string.IsNullOrWhiteSpace(request.RepoUrl))
                        return BadRequest(new ErrorResponse("invalid_request", "sourceKind=github 时 repoUrl 必须非空"));
                    state = await _lifecycle.InstallAsync(request.RepoUrl, request.Ref, ct);
                    break;

                case "local":
                    if (string.IsNullOrWhiteSpace(request.Path))
                        return BadRequest(new ErrorResponse("invalid_request", "sourceKind=local 时 path 必须非空"));
                    // link 缺省 true(软链,改源码即时生效)
                    state = await _lifecycle.InstallFromLocalAsync(request.Path, request.Link ?? true, ct);
                    break;

                default:
                    return BadRequest(new ErrorResponse("invalid_request",
                        $"不支持的 sourceKind: '{kind}'(支持 github / local)"));
            }

            return Ok(new
            {
                pluginId = state.PluginId,
                trustState = state.TrustState,
                installedVersion = state.InstalledVersion,
                sourceUrl = state.SourceUrl,
                sourceKind = state.SourceKind,
                resolvedCommit = state.ResolvedCommit,
                nextStep = "请点击 [信任并激活] 完成首次激活 (将执行该 plugin 的 Python 代码)",
            });
        }
        catch (PluginException ex)
        {
            return MapPluginException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "plugin 安装失败: kind={Kind}, repoUrl={Url}, path={Path}",
                kind, request.RepoUrl, request.Path);
            return StatusCode(500, new ErrorResponse("internal_error", ex.Message));
        }
    }

    // ─── POST /api/plugins/{id}/trust-and-activate ─────────────────────────

    /// <summary>
    /// 首次激活专用 (主真理源 §2.1 步骤 7)。
    /// 连续调 Trust(id) → Activate(id);响应含"请重启程序"提示。
    /// </summary>
    [HttpPost("{pluginId}/trust-and-activate")]
    public async Task<IActionResult> TrustAndActivate(string pluginId, CancellationToken ct)
    {
        try
        {
            await _lifecycle.TrustAndActivateAsync(pluginId, ct);
            return Ok(new
            {
                pluginId,
                trustState = TrustState.Trusted,
                activated = true,
                restartRequired = true,
                message = "激活成功,请重启 BIMCanvas 让 Agent 加载新 plugin",
            });
        }
        catch (PluginException ex)
        {
            return MapPluginException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "plugin trust-and-activate 失败: {Id}", pluginId);
            return StatusCode(500, new ErrorResponse("internal_error", ex.Message));
        }
    }

    // ─── POST /api/plugins/active ──────────────────────────────────────────

    /// <summary>
    /// 后续切换 active plugin。对 untrusted plugin 抛 403 + code=plugin_not_trusted (V13 T6b)。
    /// </summary>
    [HttpPost("active")]
    public async Task<IActionResult> SetActive([FromBody] SetActiveRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PluginId))
            return BadRequest(new ErrorResponse("invalid_request", "pluginId 必须非空"));

        try
        {
            await _lifecycle.ActivateAsync(request.PluginId, ct);
            return Ok(new
            {
                pluginId = request.PluginId,
                activated = true,
                restartRequired = true,
            });
        }
        catch (PluginException ex)
        {
            return MapPluginException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "set active 失败: {Id}", request.PluginId);
            return StatusCode(500, new ErrorResponse("internal_error", ex.Message));
        }
    }

    // ─── DELETE /api/plugins/{id} ──────────────────────────────────────────

    [HttpDelete("{pluginId}")]
    public async Task<IActionResult> Uninstall(string pluginId, CancellationToken ct)
    {
        try
        {
            await _lifecycle.UninstallAsync(pluginId, ct);
            return Ok(new { pluginId, uninstalled = true });
        }
        catch (PluginException ex)
        {
            return MapPluginException(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "uninstall 失败: {Id}", pluginId);
            return StatusCode(500, new ErrorResponse("internal_error", ex.Message));
        }
    }

    // ─── POST /api/plugins/{id}/validate ───────────────────────────────────

    /// <summary>
    /// 对已安装 plugin 重新跑 StaticPluginValidator,返回校验报告 (供 plugin 作者 dev 用)。
    /// 不修改 trustState,不执行 Python 代码。
    /// </summary>
    [HttpPost("{pluginId}/validate")]
    public async Task<IActionResult> Validate(string pluginId, CancellationToken ct)
    {
        var pluginRoot = PluginPaths.PluginRoot(pluginId);
        if (!Directory.Exists(pluginRoot))
            return NotFound(new ErrorResponse("plugin_not_found", $"plugin '{pluginId}' 不存在"));

        // 构造校验上下文 (排除自己,namespace 唯一性比对其他)
        var allStates = await _trustService.GetAllStatesAsync(ct);
        var alreadyInstalled = new List<InstalledNamespaceInfo>();
        foreach (var other in allStates.Keys.Where(k => !string.Equals(k, pluginId, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var m = JObject.Parse(System.IO.File.ReadAllText(PluginPaths.PluginManifestFile(other)));
                var ns = (string?)m["mcpNamespace"] ?? (string?)m["name"] ?? other;
                alreadyInstalled.Add(new InstalledNamespaceInfo(other, ns));
            }
            catch { }
        }

        try
        {
            _validator.Validate(pluginRoot, new ValidatorContext { AlreadyInstalled = alreadyInstalled });
            return Ok(new { pluginId, valid = true, errors = Array.Empty<string>() });
        }
        catch (SchemaValidationException ex)
        {
            return Ok(new { pluginId, valid = false, code = ex.Code, errors = ex.Errors });
        }
        catch (PluginException ex)
        {
            return Ok(new { pluginId, valid = false, code = ex.Code, errors = new[] { ex.Message } });
        }
    }

    // ─── POST /api/plugins/scaffold ────────────────────────────────────────

    [HttpPost("scaffold")]
    public async Task<IActionResult> Scaffold([FromBody] ScaffoldRequest request, CancellationToken ct)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.PluginId))
            return BadRequest(new ErrorResponse("invalid_request", "pluginId 必须非空"));

        try
        {
            var path = await _scaffoldService.ScaffoldAsync(
                request.PluginId,
                request.DisplayName ?? request.PluginId,
                request.BaseTemplate ?? "blank",
                ct);
            return Ok(new { pluginId = request.PluginId, scaffoldPath = path });
        }
        catch (NotImplementedException ex)
        {
            return StatusCode(501, new ErrorResponse("not_implemented", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "scaffold 失败: {Id}", request.PluginId);
            return StatusCode(500, new ErrorResponse("internal_error", ex.Message));
        }
    }

    // ─── 异常映射 ──────────────────────────────────────────────────────────

    private IActionResult MapPluginException(PluginException ex)
    {
        _logger.LogWarning("plugin 操作失败: code={Code}, message={Message}", ex.Code, ex.Message);

        var status = ex switch
        {
            PluginNotFoundException => 404,
            PluginNotTrustedException => 403,
            ProjectPendingBindingException => 403,
            SchemaValidationException => 400,
            PathEscapeException => 400,
            DirectoryNotPureException => 400,
            NamespaceConflictException => 409,
            PluginCloneFailedException => 502,
            PluginInstallSourceException => 400,
            PluginProbeFailedException => 422,
            _ => 400,
        };

        object payload = ex switch
        {
            SchemaValidationException s => new ErrorResponse(s.Code, s.Message) { Details = s.Errors.Cast<object>().ToList() },
            DirectoryNotPureException d => new ErrorResponse(d.Code, d.Message) { Details = d.ForbiddenHits.Cast<object>().ToList() },
            _ => new ErrorResponse(ex.Code, ex.Message),
        };
        return StatusCode(status, payload);
    }
}

// ─── 请求 / 响应 DTO ───────────────────────────────────────────────────────

public sealed class InstallRequest
{
    /// <summary>source 类型:github(默认) / local。</summary>
    public string? SourceKind { get; set; }

    // ─ github source ─
    public string RepoUrl { get; set; } = "";
    public string? Ref { get; set; }

    // ─ local source ─
    /// <summary>本地 plugin 目录绝对路径(sourceKind=local)。</summary>
    public string? Path { get; set; }
    /// <summary>local 模式:true(默认)=软链,改源码即时生效;false=复制快照。</summary>
    public bool? Link { get; set; }
}

public sealed class SetActiveRequest
{
    public string PluginId { get; set; } = "";
}

public sealed class ScaffoldRequest
{
    public string PluginId { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? BaseTemplate { get; set; }
}

public sealed class PluginListResponse
{
    public List<PluginListItem> Plugins { get; set; } = new();
    public string? ActivePluginId { get; set; }
}

public sealed class PluginListItem
{
    public string PluginId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string Version { get; set; } = "";
    public string? McpNamespace { get; set; }
    public TrustState TrustState { get; set; }
    public string? SourceUrl { get; set; }
    public string? ResolvedCommit { get; set; }
    public SourceKind SourceKind { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public DateTimeOffset? TrustedAt { get; set; }
    public bool IsActive { get; set; }
}

public sealed class ErrorResponse
{
    public string Code { get; set; }
    public string Message { get; set; }
    public List<object>? Details { get; set; }

    public ErrorResponse(string code, string message)
    {
        Code = code;
        Message = message;
    }
}
