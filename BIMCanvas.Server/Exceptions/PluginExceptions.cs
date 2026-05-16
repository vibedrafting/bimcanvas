using System;
using System.Collections.Generic;

namespace BIMCanvas.Server.Exceptions;

/// <summary>
/// Plugin 安全 / 生命周期相关异常基类 (主真理源 v1.1 §3.12 / §3.13 / R9)。
/// Controller 层统一捕获后转 403 / 409 + <c>code</c> 字段;具体 code 在 PluginsController 映射。
/// </summary>
public abstract class PluginException : Exception
{
    /// <summary>稳定的错误 code,作为 HTTP 响应 <c>{code, message}</c> 的 code 字段值。</summary>
    public abstract string Code { get; }

    protected PluginException(string message) : base(message) { }
    protected PluginException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// JSONSchema 校验失败 (§3.12 a)。Errors 列出所有违规字段,供 plugin 作者调试。
/// V11 单测覆盖:必填字段缺失 / pattern 不匹配 / additionalProperties 出现等。
/// </summary>
public sealed class SchemaValidationException : PluginException
{
    public override string Code => "static_validation_failed";
    public IReadOnlyList<string> Errors { get; }

    public SchemaValidationException(IReadOnlyList<string> errors)
        : base($"plugin manifest 校验失败: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}

/// <summary>
/// 目录纯净检查失败 (§3.12 b / §6.2)。
/// 命中 <see cref="Services.Plugins.PluginPaths.ForbiddenEntries"/> 中任何条目即抛。
/// </summary>
public sealed class DirectoryNotPureException : PluginException
{
    public override string Code => "directory_not_pure";
    public IReadOnlyList<string> ForbiddenHits { get; }

    public DirectoryNotPureException(IReadOnlyList<string> forbiddenHits)
        : base($"plugin 目录含违禁文件 / 目录: {string.Join(", ", forbiddenHits)}")
    {
        ForbiddenHits = forbiddenHits;
    }
}

/// <summary>
/// mcpTools 路径逃逸 plugin root (§3.12 c / V11 T1)。
/// schema regex 是第一道防线,运行时 <see cref="System.IO.Path.GetFullPath(string)"/> 比对是第二道。
/// </summary>
public sealed class PathEscapeException : PluginException
{
    public override string Code => "path_escape";
    public string AttemptedPath { get; }

    public PathEscapeException(string attemptedPath)
        : base($"mcpTools 路径 '{attemptedPath}' 试图逃逸 plugin root")
    {
        AttemptedPath = attemptedPath;
    }
}

/// <summary>
/// mcpNamespace 与已 installed plugin 冲突或占用保留字 (§3.12 d / V11 T2)。
/// </summary>
public sealed class NamespaceConflictException : PluginException
{
    public override string Code => "namespace_conflict";
    public string Namespace { get; }
    public string ConflictWith { get; }

    public NamespaceConflictException(string ns, string conflictWith)
        : base($"mcpNamespace '{ns}' 与 '{conflictWith}' 冲突")
    {
        Namespace = ns;
        ConflictWith = conflictWith;
    }
}

/// <summary>
/// overrides 声明的同名条目在 core-base + 待 install plugin 中找不到 (§3.12 e)。
/// </summary>
public sealed class OverridesDeclarationException : PluginException
{
    public override string Code => "overrides_invalid";
    public IReadOnlyList<string> MissingTargets { get; }

    public OverridesDeclarationException(IReadOnlyList<string> missingTargets)
        : base($"overrides 声明的条目缺失被覆盖目标: {string.Join(", ", missingTargets)}")
    {
        MissingTargets = missingTargets;
    }
}

/// <summary>
/// git clone 失败 (网络 / 认证 / repo 不存在等)。
/// </summary>
public sealed class PluginCloneFailedException : PluginException
{
    public override string Code => "clone_failed";
    public string RepoUrl { get; }
    public string? GitStdErr { get; }

    public PluginCloneFailedException(string repoUrl, string? gitStdErr, string message)
        : base(message)
    {
        RepoUrl = repoUrl;
        GitStdErr = gitStdErr;
    }
}

/// <summary>
/// ExecutablePluginProbe 失败 (R9 缓解 / V13 T6c)。
/// plugin 保持 untrusted,不能进入 active。
/// </summary>
public sealed class PluginProbeFailedException : PluginException
{
    public override string Code => "probe_failed";
    public string? PythonStdErr { get; }

    public PluginProbeFailedException(string message, string? pythonStdErr = null)
        : base(message)
    {
        PythonStdErr = pythonStdErr;
    }
}

/// <summary>
/// 尝试激活 untrusted plugin (V13 T6b)。
/// PluginLifecycleService.Activate 必须先校验 trustState == Trusted。
/// </summary>
public sealed class PluginNotTrustedException : PluginException
{
    public override string Code => "plugin_not_trusted";
    public string PluginId { get; }

    public PluginNotTrustedException(string pluginId)
        : base($"plugin '{pluginId}' 未 trusted,不能激活")
    {
        PluginId = pluginId;
    }
}

/// <summary>
/// 试图操作不存在的 plugin (uninstall / activate / trust 等场景)。
/// </summary>
public sealed class PluginNotFoundException : PluginException
{
    public override string Code => "plugin_not_found";
    public string PluginId { get; }

    public PluginNotFoundException(string pluginId)
        : base($"plugin '{pluginId}' 不存在或未安装")
    {
        PluginId = pluginId;
    }
}

/// <summary>
/// project / scene 写入 gate 拒绝 (V12a / R3)。
/// ProjectContext.State != Bound|Launched 或 LaunchContext.Mode != ProjectBound 时返回。
/// </summary>
public sealed class ProjectPendingBindingException : PluginException
{
    public override string Code => "project_pending_binding";

    public ProjectPendingBindingException(string message)
        : base(message) { }
}
