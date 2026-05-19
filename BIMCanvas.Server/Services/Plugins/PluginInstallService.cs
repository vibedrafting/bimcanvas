using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Server.Exceptions;
using BIMCanvas.Server.Models.Plugins;
using BIMCanvas.Server.Services.PluginSecurity;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BIMCanvas.Server.Services.Plugins;

/// <summary>
/// install-time:git clone → StaticPluginValidator → 原子移到 plugins 目录 →
/// 写 plugins-state.json (trustState=Untrusted) (主真理源 v1.1 §2.1 步骤 5 / §3.12 / R1 / R9)。
/// <para>
/// <b>R1 红线</b>:本类绝不调用 <see cref="ExecutablePluginProbe"/>;
/// trust-time 才执行 Python 代码。
/// </para>
/// </summary>
public sealed class PluginInstallService
{
    private readonly StaticPluginValidator _validator;
    private readonly PluginTrustService _trustService;
    private readonly ILogger<PluginInstallService> _logger;

    private static readonly TimeSpan CloneTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RevParseTimeout = TimeSpan.FromSeconds(10);

    public PluginInstallService(
        StaticPluginValidator validator,
        PluginTrustService trustService,
        ILogger<PluginInstallService> logger)
    {
        _validator = validator;
        _trustService = trustService;
        _logger = logger;
    }

    /// <summary>
    /// 安装 plugin。成功返回 PluginInstallState (含 trustState=Untrusted);
    /// 任何步骤失败抛对应 <see cref="PluginException"/>,staging 目录已回滚清理。
    /// </summary>
    /// <param name="repoUrl">GitHub repo URL</param>
    /// <param name="gitRef">可选 git ref (tag / branch / commit);null 则用默认分支</param>
    public async Task<PluginInstallState> InstallAsync(string repoUrl, string? gitRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repoUrl))
            throw new ArgumentException("repoUrl 必须非空", nameof(repoUrl));

        // 输入规范化:Windows 裸路径 / 半成品 file: → 标准 file:///<正斜杠路径>
        repoUrl = NormalizeSourceUrl(repoUrl);

        // 本地路径预校验(避免延迟到 git clone 失败才报错,UX 更清晰)
        if (repoUrl.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            EnsureLocalGitRepository(repoUrl);
        }

        Directory.CreateDirectory(PluginPaths.StagingRoot);
        var stagingPath = Path.Combine(PluginPaths.StagingRoot, Guid.NewGuid().ToString("N"));

        try
        {
            // 1. git clone --depth 1 [--branch <ref>] <url> <staging>
            var cloneArgs = new List<string> { "clone", "--depth", "1" };
            if (!string.IsNullOrWhiteSpace(gitRef))
            {
                cloneArgs.Add("--branch");
                cloneArgs.Add(gitRef);
            }
            cloneArgs.Add(repoUrl);
            cloneArgs.Add(stagingPath);

            var cloneResult = RunGit(cloneArgs, workingDir: null, CloneTimeout);
            if (!cloneResult.Success)
            {
                throw new PluginCloneFailedException(
                    repoUrl,
                    cloneResult.StdErr,
                    $"git clone 失败 (exit={cloneResult.ExitCode}): {cloneResult.StdErr}");
            }

            // 2. 读 manifest 拿 pluginId
            var manifestPath = Path.Combine(stagingPath, "bimcanvas-plugin.json");
            if (!File.Exists(manifestPath))
                throw new SchemaValidationException(new[] { "仓库根缺失 bimcanvas-plugin.json,可能不是 BIMCanvas plugin" });

            // 3. StaticPluginValidator (会 JSONSchema 校验 + 目录纯净 + 路径不逃逸 + namespace 唯一 + overrides 合法)
            var alreadyInstalled = await BuildInstalledNamespaceInfoAsync(ct);
            var validatorContext = new ValidatorContext
            {
                AlreadyInstalled = alreadyInstalled,
            };
            var manifest = _validator.Validate(stagingPath, validatorContext);

            var pluginId = (string)manifest["name"]!;
            var version = (string)manifest["version"]!;
            var targetPath = PluginPaths.PluginRoot(pluginId);

            // 4. 计算 manifest checksum + git resolvedCommit
            var manifestChecksum = ComputeFileSha256(manifestPath);
            var resolvedCommit = TryGetGitHead(stagingPath);

            // 5. 原子移动:若目标已存在,先 delete (覆盖式重装语义)
            if (Directory.Exists(targetPath))
            {
                _logger.LogInformation("plugin '{Id}' 已存在,覆盖重装", pluginId);
                Directory.Delete(targetPath, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            Directory.Move(stagingPath, targetPath);

            // 6. 写入 plugins-state.json
            var state = new PluginInstallState(
                PluginId: pluginId,
                TrustState: TrustState.Untrusted,
                InstalledAt: DateTimeOffset.Now,
                TrustedAt: null,
                SourceUrl: repoUrl,
                ResolvedCommit: resolvedCommit,
                SourceKind: SourceKind.Github,
                ManifestChecksum: manifestChecksum,
                InstalledVersion: version
            );
            await _trustService.MarkInstalledAsync(state, ct);

            _logger.LogInformation(
                "plugin '{Id}' 安装成功:version={Version}, commit={Commit}, trustState=Untrusted",
                pluginId, version, resolvedCommit ?? "<unknown>");

            return state;
        }
        catch
        {
            // 任何失败 → 清理 staging
            try
            {
                if (Directory.Exists(stagingPath))
                    Directory.Delete(stagingPath, recursive: true);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "清理 staging 目录失败: {Staging}", stagingPath);
            }
            throw;
        }
    }

    /// <summary>
    /// 从已 installed plugin 构造 namespace 摘要列表 (StaticPluginValidator d 规则用)。
    /// </summary>
    private async Task<IReadOnlyList<InstalledNamespaceInfo>> BuildInstalledNamespaceInfoAsync(CancellationToken ct)
    {
        var states = await _trustService.GetAllStatesAsync(ct);
        var list = new List<InstalledNamespaceInfo>();
        foreach (var kv in states)
        {
            // 重新读 manifest 拿 mcpNamespace (PluginInstallState 不存 namespace,避免数据冗余)
            var manifestPath = PluginPaths.PluginManifestFile(kv.Key);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                var m = JObject.Parse(File.ReadAllText(manifestPath));
                var ns = (string?)m["mcpNamespace"] ?? (string?)m["name"] ?? kv.Key;
                list.Add(new InstalledNamespaceInfo(kv.Key, ns));
            }
            catch
            {
                // manifest 损坏 → 跳过该 plugin 的 namespace 校验贡献
            }
        }
        return list;
    }

    private static string? TryGetGitHead(string workingDir)
    {
        try
        {
            var result = RunGit(new[] { "rev-parse", "HEAD" }, workingDir, RevParseTimeout);
            if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
                return result.StdOut.Trim();
        }
        catch { /* 容错:resolvedCommit 留 null */ }
        return null;
    }

    private static string ComputeFileSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    // ─── git 进程包装 (复用 GitWorktreeService.RunGit 同形态:UTF8 + stdout/stderr 双向 + 超时) ───

    private readonly record struct GitResult(bool Success, int ExitCode, string StdOut, string StdErr);

    private static GitResult RunGit(IEnumerable<string> args, string? workingDir, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi);
        if (process is null)
            return new GitResult(false, -1, "", "无法启动 git 进程");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new GitResult(false, -2, stdout, "git 进程超时:" + stderr);
        }
        return new GitResult(process.ExitCode == 0, process.ExitCode, stdout, stderr);
    }

    // ─── sourceUrl 规范化 (支持本地路径 / file:// 自动补全;Git for Windows 实测 file:/// 可正常 clone) ───

    /// <summary>
    /// 规范化 plugin sourceUrl,统一输入形态:
    /// <list type="bullet">
    /// <item>远程 URL (https / http / ssh / git / user@host:path) → 保持原样</item>
    /// <item>已规范的 file:///&lt;path&gt; → 保持原样</item>
    /// <item>半成品 file: 形式 (file:E:\... / file://E:/... 等) → 修正为 file:///&lt;正斜杠路径&gt;</item>
    /// <item>Windows 裸路径 (E:\... 或 E:/...) → file:///E:/...</item>
    /// <item>UNC 路径 (\\server\share) → file:////server/share</item>
    /// <item>Unix 绝对路径 (/path) → file:///path</item>
    /// <item>其他不识别形式 → 保持原样,让下游 git clone 自己报错</item>
    /// </list>
    /// 反斜杠统一替换为正斜杠 (file:// URL 标准要求)。
    /// </summary>
    public static string NormalizeSourceUrl(string repoUrl)
    {
        if (string.IsNullOrWhiteSpace(repoUrl)) return repoUrl;
        var trimmed = repoUrl.Trim();

        // 1. 远程协议:https / http / ssh / git
        if (Regex.IsMatch(trimmed, @"^(https?|ssh|git)://", RegexOptions.IgnoreCase))
            return trimmed;

        // 2. SSH shorthand: user@host:path (例 git@github.com:owner/repo.git)
        if (Regex.IsMatch(trimmed, @"^[\w.\-]+@[\w.\-]+:"))
            return trimmed;

        // 3. file: 各种变体 → 规范化为 file:///<正斜杠路径>
        if (trimmed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var after = trimmed.Substring("file:".Length).Replace('\\', '/').TrimStart('/');
            return "file:///" + after;
        }

        // 4. Windows 盘符路径 (E:\... 或 E:/...)
        if (Regex.IsMatch(trimmed, @"^[A-Za-z]:[\\/]"))
            return "file:///" + trimmed.Replace('\\', '/');

        // 5. UNC 路径 \\server\share 或 //server/share
        if (trimmed.StartsWith(@"\\") || trimmed.StartsWith("//"))
            return "file://" + trimmed.Replace('\\', '/');

        // 6. Unix 绝对路径
        if (trimmed.StartsWith("/"))
            return "file://" + trimmed;

        // 7. 其他保持原样
        return trimmed;
    }

    /// <summary>
    /// 校验 file:// URL 指向的本地路径是有效的 git 仓库 (存在 + 含 .git 标记)。
    /// 失败抛 <see cref="PluginCloneFailedException"/>,在 git clone 启动之前提供清晰错误。
    /// </summary>
    private static void EnsureLocalGitRepository(string fileUrl)
    {
        string localPath;
        try
        {
            localPath = new Uri(fileUrl).LocalPath;
        }
        catch (Exception ex)
        {
            throw new PluginCloneFailedException(
                fileUrl, ex.Message,
                $"无法解析 file:// URL: {fileUrl} ({ex.Message})");
        }

        if (!Directory.Exists(localPath))
        {
            throw new PluginCloneFailedException(
                fileUrl, string.Empty,
                $"本地路径不存在或不是目录: {localPath}");
        }

        // .git 通常是目录;submodule / git worktree 场景下是文件 — 两者都接受
        var gitMarker = Path.Combine(localPath, ".git");
        if (!Directory.Exists(gitMarker) && !File.Exists(gitMarker))
        {
            throw new PluginCloneFailedException(
                fileUrl, string.Empty,
                $"本地路径不是 git 仓库 (缺少 .git): {localPath}");
        }
    }
}
