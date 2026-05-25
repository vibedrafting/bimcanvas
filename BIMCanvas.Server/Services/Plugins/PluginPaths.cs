using System;
using System.IO;

namespace BIMCanvas.Server.Services.Plugins;

/// <summary>
/// Plugin 相关路径常量集中处 (主真理源 v1.1 §3.13 / §4.4 / 模板 §4.x)。
/// 复用 <see cref="ConfigService.GetConfigDir"/> 作为 BIMCANVAS_HOME 解析的唯一真源,
/// 避免散落字符串拼接导致与 ConfigService 不一致。
/// </summary>
public static class PluginPaths
{
    /// <summary>已安装 plugin 根目录 <c>BIMCANVAS_HOME/plugins/</c>。</summary>
    public static string PluginsRoot => Path.Combine(ConfigService.GetConfigDir(), "plugins");

    /// <summary>
    /// 安装 staging 目录 <c>BIMCANVAS_HOME/plugins/.staging/</c>。
    /// PluginInstallService git-clone 先落到 staging/<![CDATA[<guid>]]>/,
    /// 通过 StaticPluginValidator 后才原子移到 <see cref="PluginRoot"/>。
    /// </summary>
    public static string StagingRoot => Path.Combine(PluginsRoot, ".staging");

    /// <summary>
    /// trust 元数据集中存储 <c>BIMCANVAS_HOME/plugins-state.json</c> (§3.13)。
    /// 仅 PluginTrustService 允许读写,plugin 代码完全无法触达。
    /// </summary>
    public static string PluginsStateFile => Path.Combine(ConfigService.GetConfigDir(), "plugins-state.json");

    /// <summary>运行时临时文件根 <c>BIMCANVAS_HOME/.runtime/</c>。</summary>
    public static string RuntimeRoot => Path.Combine(ConfigService.GetConfigDir(), ".runtime");

    /// <summary>
    /// LaunchContext JSON 文件路径 (§4.10) <c>.runtime/launch-context-{pid}.json</c>。
    /// Server 启动 / 项目 bind 时写入,Agent 子进程读完后由 Agent 端删除 (组3 责任)。
    /// </summary>
    public static string LaunchContextPath(int pid)
        => Path.Combine(RuntimeRoot, $"launch-context-{pid}.json");

    /// <summary>单个 plugin 根目录 <c>BIMCANVAS_HOME/plugins/{pluginId}/</c>。</summary>
    public static string PluginRoot(string pluginId)
        => Path.Combine(PluginsRoot, pluginId);

    /// <summary>plugin manifest 文件路径 <c>plugins/{pluginId}/bimcanvas-plugin.json</c>。</summary>
    public static string PluginManifestFile(string pluginId)
        => Path.Combine(PluginRoot(pluginId), "bimcanvas-plugin.json");

    /// <summary>plugin projectMount 根目录 (bind-time 复制源)。</summary>
    public static string PluginProjectMountRoot(string pluginId)
        => Path.Combine(PluginRoot(pluginId), "projectMount");

    /// <summary>plugin scaffold 输出根 <c>BIMCANVAS_HOME/plugin-scaffolds/</c>。</summary>
    public static string ScaffoldsRoot => Path.Combine(ConfigService.GetConfigDir(), "plugin-scaffolds");

    /// <summary>
    /// (§包2 ⑥) Bind-time 把 plugin projectMount/manifest.json 声明的 <paramref name="manifestTarget"/>
    /// 物化到项目侧、sceneId 命名空间化后的绝对路径。
    /// <para>
    /// 命名空间化规则:把 <paramref name="manifestTarget"/> 按首个 <c>/</c> 拆分,
    /// 将 <paramref name="sceneId"/> 插作**第 2 段**,再拼到 <paramref name="projectPath"/> 下。
    /// 平台对 plugin 声明的任意 target 一视同仁,**不认得** “references”/“modules” 的语义:
    /// <list type="bullet">
    /// <item><c>references</c> → <c>{projectPath}/references/{sceneId}</c></item>
    /// <item><c>modules</c> → <c>{projectPath}/modules/{sceneId}</c></item>
    /// <item><c>references/design_principles.md</c> → <c>{projectPath}/references/{sceneId}/design_principles.md</c></item>
    /// </list>
    /// </para>
    /// </summary>
    public static string SceneMountTarget(string projectPath, string sceneId, string manifestTarget)
    {
        if (string.IsNullOrWhiteSpace(manifestTarget))
            throw new ArgumentException("manifestTarget 必须非空", nameof(manifestTarget));

        // 统一分隔符,拆出首段与剩余段
        var normalized = manifestTarget.Replace('\\', '/').Trim('/');
        var slash = normalized.IndexOf('/');
        if (slash < 0)
        {
            // 单段 target(如 "references"):首段 + sceneId
            return Path.Combine(projectPath, normalized, sceneId);
        }

        var head = normalized.Substring(0, slash);
        var rest = normalized.Substring(slash + 1);
        // 多段 target:首段 / sceneId / 剩余子路径
        return Path.Combine(projectPath, head, sceneId, rest.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 某个 manifestTarget 命名空间化后的「顶层根目录」<c>{projectPath}/{首段}/{sceneId}</c>,
    /// 供 <c>MountSceneScaffold</c> 做幂等检查(任一已存在则跳过整体物化)。
    /// </summary>
    public static string SceneMountTargetRoot(string projectPath, string sceneId, string manifestTarget)
    {
        if (string.IsNullOrWhiteSpace(manifestTarget))
            throw new ArgumentException("manifestTarget 必须非空", nameof(manifestTarget));

        var normalized = manifestTarget.Replace('\\', '/').Trim('/');
        var slash = normalized.IndexOf('/');
        var head = slash < 0 ? normalized : normalized.Substring(0, slash);
        return Path.Combine(projectPath, head, sceneId);
    }

    /// <summary>plugins.lock.json 在项目根的路径。</summary>
    public static string ProjectPluginsLockFile(string projectPath)
        => Path.Combine(projectPath, "plugins.lock.json");

    // ─── Schema 资源 (随程序分发,docs/*.schema.json 通过 .csproj Content Include 复制) ───

    /// <summary>
    /// 进程内 manifest JSONSchema 文件路径 <c>{BaseDirectory}/Schemas/plugin-manifest-schema.json</c>。
    /// 校验逻辑由 StaticPluginValidator 加载 (§3.12 a)。
    /// </summary>
    public static string ManifestSchemaResourcePath
        => Path.Combine(AppContext.BaseDirectory, "Schemas", "plugin-manifest-schema.json");

    /// <summary>
    /// 进程内 bcp scenes JSONSchema 文件路径 <c>{BaseDirectory}/Schemas/bcp-scenes-schema.json</c>。
    /// </summary>
    public static string BcpScenesSchemaResourcePath
        => Path.Combine(AppContext.BaseDirectory, "Schemas", "bcp-scenes-schema.json");

    /// <summary>plugin 目录纯净检查的违禁文件 / 目录清单 (§3.12 b / §6.2)。</summary>
    public static readonly string[] ForbiddenEntries = new[]
    {
        "CLAUDE.md",
        "settings.local.json",
        ".claude",
        ".bimcanvas",
    };

    /// <summary>mcpNamespace 保留字 (§3.12 d) —— 不允许 plugin 占用。</summary>
    public static readonly string[] ReservedMcpNamespaces = new[] { "canvas" };
}
