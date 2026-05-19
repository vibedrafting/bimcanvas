using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BIMCanvas.Server.Exceptions;
using BIMCanvas.Server.Services.Plugins;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace BIMCanvas.Server.Services.PluginSecurity;

/// <summary>
/// install-time 纯文本校验 (主真理源 v1.1 §3.12 / R1 / R9)。
/// <para>
/// <b>R1 红线</b>:本类绝不 import / exec / 解析 Python AST 中可触发代码部分;
/// 仅做 JSON / 路径 / 字符串校验。任何 dry-import 必须放到 trust-time
/// <see cref="ExecutablePluginProbe"/>。
/// </para>
/// <para>
/// 校验项 (§3.12 a-d):
/// (a) JSONSchema 校验 <c>bimcanvas-plugin.json</c>
/// (b) 目录纯净检查 (拒绝 CLAUDE.md / settings.local.json / .claude/ / .bimcanvas/)
/// (c) mcpTools 路径不能 ".." 逃逸 plugin root
/// (d) mcpNamespace 唯一性 (与已 installed plugin 比对) + 非 "canvas" + 格式合法
/// (v3.7 移除 overrides 显式声明校验:plugin 同名 agent/skill 默认覆盖 core-base,
/// 覆盖决定由 Agent 端 loader.py 用 logger.info 记录,详见主真理源 §3.6 v3.7 修订)
/// </para>
/// </summary>
public sealed class StaticPluginValidator
{
    private readonly Lazy<JSchema> _manifestSchema = new(LoadManifestSchema, LazyThreadSafetyMode.ExecutionAndPublication);

    private static JSchema LoadManifestSchema()
    {
        var path = PluginPaths.ManifestSchemaResourcePath;
        if (!File.Exists(path))
            throw new InvalidOperationException(
                $"plugin manifest schema 资源缺失: {path}。检查 BIMCanvas.Server.csproj 是否包含 docs/plugin-manifest-schema.json Content Include。");
        return JSchema.Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// 对 pluginRoot 目录执行五项静态校验。任何一项失败抛对应
    /// <see cref="PluginException"/>;全部通过返回解析后的 manifest <see cref="JObject"/>
    /// (调用方后续要计算 manifestChecksum 等)。
    /// </summary>
    /// <param name="pluginRoot">plugin 根目录绝对路径 (staging 或已安装目录)</param>
    /// <param name="context">已安装 plugin 上下文 (mcpNamespace 唯一性校验用)</param>
    public JObject Validate(string pluginRoot, ValidatorContext context)
    {
        if (string.IsNullOrWhiteSpace(pluginRoot))
            throw new ArgumentException("pluginRoot 必须非空", nameof(pluginRoot));
        if (!Directory.Exists(pluginRoot))
            throw new DirectoryNotFoundException($"plugin 目录不存在: {pluginRoot}");

        var manifestPath = Path.Combine(pluginRoot, "bimcanvas-plugin.json");
        if (!File.Exists(manifestPath))
            throw new SchemaValidationException(new[] { "缺失 bimcanvas-plugin.json" });

        JObject manifest;
        try
        {
            manifest = JObject.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            throw new SchemaValidationException(new[] { $"bimcanvas-plugin.json 不是合法 JSON: {ex.Message}" });
        }

        // (a) JSONSchema
        if (!manifest.IsValid(_manifestSchema.Value, out IList<string> schemaErrors))
            throw new SchemaValidationException(schemaErrors.ToList());

        // (b) 目录纯净
        var forbiddenHits = new List<string>();
        foreach (var entry in PluginPaths.ForbiddenEntries)
        {
            var fullEntry = Path.Combine(pluginRoot, entry);
            if (File.Exists(fullEntry) || Directory.Exists(fullEntry))
                forbiddenHits.Add(entry);
        }
        if (forbiddenHits.Count > 0)
            throw new DirectoryNotPureException(forbiddenHits);

        // (c) mcpTools 路径不逃逸 (schema regex 已查 "..",这里做运行时 Path 比对作第二道防线)
        var mcpTools = (string?)manifest["mcpTools"];
        if (!string.IsNullOrEmpty(mcpTools))
        {
            string combined;
            try
            {
                combined = Path.GetFullPath(Path.Combine(pluginRoot, mcpTools));
            }
            catch (Exception)
            {
                throw new PathEscapeException(mcpTools);
            }
            var rootFull = Path.GetFullPath(pluginRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rootWithSep = rootFull + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
                !combined.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new PathEscapeException(mcpTools);
            }
        }

        // (d) mcpNamespace 唯一性 + 非保留字 (格式由 schema regex 保证)
        var pluginName = (string?)manifest["name"]
            ?? throw new SchemaValidationException(new[] { "name 字段缺失" });
        var ns = (string?)manifest["mcpNamespace"] ?? pluginName; // 缺省时用 name 作 ns
        foreach (var reserved in PluginPaths.ReservedMcpNamespaces)
        {
            if (string.Equals(ns, reserved, StringComparison.OrdinalIgnoreCase))
                throw new NamespaceConflictException(ns, $"保留字 '{reserved}'");
        }
        foreach (var installed in context.AlreadyInstalled)
        {
            if (string.Equals(installed.PluginId, pluginName, StringComparison.OrdinalIgnoreCase))
                continue; // 同 pluginId 重新 install 不算 namespace 冲突
            if (string.Equals(installed.McpNamespace, ns, StringComparison.OrdinalIgnoreCase))
                throw new NamespaceConflictException(ns, $"已安装 plugin '{installed.PluginId}'");
        }

        return manifest;
    }
}

/// <summary>
/// StaticPluginValidator 调用上下文 —— 由 caller (PluginInstallService) 注入已 installed
/// plugin 与 core-base 元数据。Validator 自身保持无状态。
/// </summary>
public sealed class ValidatorContext
{
    /// <summary>已 installed plugin 的 (pluginId, mcpNamespace) 摘要,用于 namespace 唯一性。</summary>
    public IReadOnlyList<InstalledNamespaceInfo> AlreadyInstalled { get; init; } = Array.Empty<InstalledNamespaceInfo>();

    public static ValidatorContext Empty { get; } = new();
}

/// <summary>已安装 plugin namespace 摘要 (跨 Service 传递,不直接依赖 PluginInstallState)。</summary>
public sealed record InstalledNamespaceInfo(string PluginId, string McpNamespace);
