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
    /// [Obsolete - 组5 §5.B.1] M1 阶段的 bind-time 物化占位路径
    /// <c>{projectPath}/_pluginMount/{sceneId}/</c>。M2 切换到按 plugin projectMount 子目录类型
    /// 分别物化到 SceneReferencesRoot / SceneModulesRoot 后,本常量不再被生产代码使用。
    /// 保留仅为单测复现历史 M1 行为。
    /// </summary>
    [System.Obsolete("组5 §5.B.1: M2 已切换到 SceneReferencesRoot / SceneModulesRoot 分别物化。仅保留供单测复现 M1 行为。")]
    public static string SceneScaffoldRoot(string projectPath, string sceneId)
        => Path.Combine(projectPath, "_pluginMount", sceneId);

    /// <summary>
    /// Plugin projectMount/references/* 物化目标:<c>{projectPath}/references/{sceneId}/</c>。
    /// (plugin 资源物化区;落点的进一步规整属后续工作,见回退执行计划 §6。)
    /// </summary>
    public static string SceneReferencesRoot(string projectPath, string sceneId)
        => Path.Combine(projectPath, "references", sceneId);

    /// <summary>
    /// Plugin projectMount/modules/* 物化目标:<c>{projectPath}/modules/{sceneId}/</c>。
    /// (plugin 资源物化区;同上,落点规整属后续工作。)
    /// </summary>
    public static string SceneModulesRoot(string projectPath, string sceneId)
        => Path.Combine(projectPath, "modules", sceneId);

    /// <summary>
    /// (组5 §5.B.2) Bind-time 其他 plugin 自定义子目录的兜底物化路径:
    /// <c>{projectPath}/_pluginMount/{sceneId}/</c>。
    /// 仅当 plugin projectMount/ 内有非 references/ / modules/ 的自定义子目录时使用。
    /// </summary>
    public static string SceneOtherMountRoot(string projectPath, string sceneId)
        => Path.Combine(projectPath, "_pluginMount", sceneId);

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
