using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目级模板物化服务 (主真理源 v1.1 §4.2 / §4.9 R10 / 模板 §4.9 字面改造)。
    /// <para>
    /// <b>v1.1 改造前后语义差异</b>:
    /// - 改造前:<c>EnsureInitialized</c> 在 ProjectService.LoadProject / EnsureProjectAssets 内被调,
    ///   open .bcp / 解压时自动补"项目级固定模板"(README / license 等)。
    /// - 改造后 (R10 缓解 / 用户决策"字面严格执行"):
    ///   * <c>EnsureInitialized</c> 改 <see cref="Obsolete"/> + internal,所有外部调用已删除;
    ///   * 新增 <c>MountSceneScaffold</c>,只在 <c>POST /api/project/scenes</c> 端点内被调,
    ///     按 sceneId 命名空间物化 plugin 的 <c>projectMount/</c> 内容。
    /// </para>
    /// <para>
    /// <b>R10 不变量</b>:打开任何旧项目时不会写任何文件;切到不同 plugin 后打开旧项目
    /// 也不会被覆盖。projectMount 物化只发生在用户主动 bind scene 的瞬间。
    /// </para>
    /// </summary>
    public sealed class ProjectFixedFilesBootstrapService
    {
        /// <summary>projectMount/manifest.json 反序列化设置 (R10: 只用 Newtonsoft + camelCase resolver)。</summary>
        private static readonly JsonSerializerSettings ManifestJsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() }
        };

        private readonly BootstrapTemplateService _templateService;
        private readonly ILogger<ProjectFixedFilesBootstrapService> _logger;

        public ProjectFixedFilesBootstrapService(
            BootstrapTemplateService templateService,
            ILogger<ProjectFixedFilesBootstrapService> logger)
        {
            _templateService = templateService;
            _logger = logger;
        }

        /// <summary>
        /// [已废弃 v1.1 §4.9 + 组5 §5.A.5] 旧"项目固定模板补齐"入口。
        /// 改造后:open / 解压 / 显式调用全链路均不再调本方法 (R10);
        /// 组5 §5.A.5 删除了源 <c>Templates/project-fixed/</c> 目录:
        /// - README.md / .gitignore 迁到 <c>Templates/platform-config/project-baseline/</c>(由 ProjectService.LoadProject 调 BootstrapTemplateService.EnsurePlatformBaseline 物化)
        /// - modules/ + reference-templates/ 迁到 <c>Templates/plugins/indoor-layout/projectMount/</c>(bind scene 时由 MountSceneScaffold 物化)
        ///
        /// 本方法保留仅为不破坏 DI 注册签名;调用时 manifest 已不存在,会优雅降级 no-op 并记日志。
        /// </summary>
        [Obsolete("v1.1 §4.9 + 组5 §5.A.5:Templates/project-fixed/ 已迁出删除。新代码用 MountSceneScaffold(projectPath, sceneId, pluginId) + ProjectService.LoadProject 内部 baseline 拷贝。")]
        internal void EnsureInitialized(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("项目路径不能为空。", nameof(projectPath));

            // 组5 §5.A.5:Templates/project-fixed/ 已删除,manifest 不再存在;
            // 任何意外调用本 obsolete 方法,优雅降级为 no-op + 警告日志。
            _logger.LogWarning(
                "EnsureInitialized 已废弃(Templates/project-fixed/ 已删除);no-op。" +
                "如需 plugin projectMount 物化,走 POST /api/project/scenes → MountSceneScaffold。 projectPath={Path}",
                projectPath);
        }

        /// <summary>
        /// bind-time 把 plugin 的 <c>projectMount/</c> 物化到项目侧 sceneId 命名空间
        /// (主真理源 §3.9 + §4.2 / 组5 §5.B.2 真物化)。
        /// <para>
        /// <b>唯一调用入口</b>:<c>POST /api/project/scenes</c> 端点 (主真理源 §4.8)。
        /// 任何其他调用都违反 R10。
        /// </para>
        /// <para>
        /// <b>声明驱动(§包2 ⑥ 去 domain 化)</b>:物化由 plugin 的
        /// <c>projectMount/manifest.json</c> 的 <c>items[]</c> 声明驱动,平台**不再硬编码**
        /// 任何 domain 目录名(原 references / modules 特判已移除)。每项按 <c>type</c> 物化,
        /// <c>target</c> 经 <see cref="PluginPaths.SceneMountTarget"/> 做 sceneId 命名空间化:
        /// <list type="bullet">
        /// <item><c>directory</c>:递归拷 <c>projectMount/{name}</c> → 命名空间化 target</item>
        /// <item><c>template</c>:拷单文件 <c>projectMount/{name}</c> → 命名空间化 target</item>
        /// <item><c>empty-directory</c>:仅创建命名空间化 target 目录(name 可空,无源)</item>
        /// </list>
        /// manifest.json 缺失 / items 为空 → no-op(不回退硬编码遍历,避免重新引入 domain 名)。
        /// </para>
        /// <para>
        /// <b>幂等性</b>:任一启用项的「命名空间顶层根」已存在时跳过整体物化(保护用户已有数据,符合 R10 不变量)。
        /// </para>
        /// </summary>
        public void MountSceneScaffold(string projectPath, string sceneId, string pluginId)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("projectPath 必须非空", nameof(projectPath));
            if (string.IsNullOrWhiteSpace(sceneId))
                throw new ArgumentException("sceneId 必须非空", nameof(sceneId));
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("pluginId 必须非空", nameof(pluginId));

            var pluginMountSource = PluginPaths.PluginProjectMountRoot(pluginId);
            if (!Directory.Exists(pluginMountSource))
            {
                _logger.LogInformation(
                    "plugin '{Plugin}' 无 projectMount/ 目录,MountSceneScaffold no-op (sceneId={Scene})",
                    pluginId, sceneId);
                return;
            }

            // 声明式挂载清单:plugin 必须在 projectMount/manifest.json 显式声明 items[]
            var manifestPath = Path.Combine(pluginMountSource, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning(
                    "plugin '{Plugin}' projectMount/ 缺少 manifest.json,无挂载声明,MountSceneScaffold no-op (sceneId={Scene})",
                    pluginId, sceneId);
                return;
            }

            ProjectMountManifest? manifest;
            try
            {
                var json = File.ReadAllText(manifestPath, Encoding.UTF8);
                manifest = JsonConvert.DeserializeObject<ProjectMountManifest>(json, ManifestJsonSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "plugin '{Plugin}' projectMount/manifest.json 解析失败,跳过物化 (sceneId={Scene})",
                    pluginId, sceneId);
                return;
            }

            var enabledItems = manifest?.Items?
                .Where(i => i.Enabled && !string.IsNullOrWhiteSpace(i.Target))
                .ToList() ?? new List<ProjectMountItem>();
            if (enabledItems.Count == 0)
            {
                _logger.LogInformation(
                    "plugin '{Plugin}' projectMount/manifest.json 无启用项,sceneId={Scene} 无物化内容",
                    pluginId, sceneId);
                return;
            }

            // 幂等检查:任一启用项的命名空间顶层根已存在 → 跳过整体物化 (R10 不变量)
            foreach (var item in enabledItems)
            {
                var root = PluginPaths.SceneMountTargetRoot(projectPath, sceneId, item.Target!);
                if (Directory.Exists(root))
                {
                    _logger.LogInformation(
                        "sceneId='{Scene}' 命名空间根 {Root} 已存在,跳过物化 (幂等)", sceneId, root);
                    return;
                }
            }

            var fullMountSource = Path.GetFullPath(pluginMountSource);
            var fullProjectPath = Path.GetFullPath(projectPath);
            var mounted = 0;

            foreach (var item in enabledItems)
            {
                var target = PluginPaths.SceneMountTarget(projectPath, sceneId, item.Target!);
                var fullTarget = Path.GetFullPath(target);

                // 路径逃逸防护:目标必须落在 projectPath 内
                if (!IsWithin(fullProjectPath, fullTarget))
                {
                    _logger.LogError(
                        "plugin '{Plugin}' 声明的 target '{Target}' 逃逸出项目目录,已跳过", pluginId, item.Target);
                    continue;
                }

                var type = (item.Type ?? string.Empty).Trim().ToLowerInvariant();
                if (type == "empty-directory")
                {
                    Directory.CreateDirectory(fullTarget);
                    mounted++;
                    _logger.LogInformation("plugin '{Plugin}' empty-directory → {Target}", pluginId, fullTarget);
                    continue;
                }

                if (type != "directory" && type != "template")
                {
                    _logger.LogWarning(
                        "plugin '{Plugin}' 未知挂载 type '{Type}',已跳过 target={Target}", pluginId, item.Type, item.Target);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Name))
                {
                    _logger.LogError(
                        "plugin '{Plugin}' 的 {Type} 项缺少 name(源路径),已跳过 target={Target}", pluginId, type, item.Target);
                    continue;
                }

                var source = Path.Combine(
                    pluginMountSource, item.Name!.Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
                var fullSource = Path.GetFullPath(source);

                // 路径逃逸防护:源必须落在 projectMount 内
                if (!IsWithin(fullMountSource, fullSource))
                {
                    _logger.LogError(
                        "plugin '{Plugin}' 声明的 name '{Name}' 逃逸出 projectMount,已跳过", pluginId, item.Name);
                    continue;
                }

                if (type == "directory")
                {
                    if (!Directory.Exists(fullSource))
                    {
                        _logger.LogWarning(
                            "plugin '{Plugin}' directory 源不存在 {Source},已跳过", pluginId, fullSource);
                        continue;
                    }
                    CopyDirectoryRecursive(fullSource, fullTarget);
                }
                else // template
                {
                    if (!File.Exists(fullSource))
                    {
                        _logger.LogWarning(
                            "plugin '{Plugin}' template 源文件不存在 {Source},已跳过", pluginId, fullSource);
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(fullTarget)!);
                    File.Copy(fullSource, fullTarget, overwrite: false);
                }

                mounted++;
                _logger.LogInformation(
                    "plugin '{Plugin}' {Type} {Name} → {Target}", pluginId, type, item.Name, fullTarget);
            }

            _logger.LogInformation(
                "plugin '{Plugin}' projectMount 物化完成,sceneId={Scene},共 {Count} 项", pluginId, sceneId, mounted);
        }

        /// <summary>判断 <paramref name="candidate"/> 是否等于或位于 <paramref name="root"/> 之下(路径逃逸防护)。</summary>
        private static bool IsWithin(string root, string candidate)
        {
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            if (string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
            return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var relativeDir = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(target, relativeDir));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relativeFile = Path.GetRelativePath(source, file);
                var dest = Path.Combine(target, relativeFile);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: false);
            }
        }
    }

    /// <summary>
    /// plugin <c>projectMount/manifest.json</c> 的声明式挂载清单 (§包2 ⑥)。
    /// 平台只机械按 <see cref="Items"/> 物化,不解释 target 的 domain 含义。
    /// </summary>
    public sealed class ProjectMountManifest
    {
        public string? Version { get; set; }
        public List<ProjectMountItem> Items { get; set; } = new List<ProjectMountItem>();
    }

    /// <summary>单条挂载声明。</summary>
    public sealed class ProjectMountItem
    {
        /// <summary>源相对路径(相对 projectMount/);empty-directory 可为空。</summary>
        public string? Name { get; set; }

        /// <summary>目标相对路径(经平台 sceneId 命名空间化后落到项目侧)。</summary>
        public string? Target { get; set; }

        /// <summary>物化类型:directory / template / empty-directory。</summary>
        public string? Type { get; set; }

        /// <summary>是否启用;false 时跳过。</summary>
        public bool Enabled { get; set; }

        /// <summary>人类可读说明(仅文档用途)。</summary>
        public string? Description { get; set; }
    }
}
