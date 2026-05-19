using System;
using System.Collections.Generic;
using System.IO;
using BIMCanvas.Server.Services.Plugins;
using Microsoft.Extensions.Logging;

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
        private const string ManifestRelativePath = "project-fixed/manifest.json";

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
        /// <b>M2 真实路径(组5 §5.B.2)</b>:按 plugin projectMount/ 内的子目录类型分别物化:
        /// <list type="bullet">
        /// <item><c>plugin/projectMount/references/*</c> → <c>{projectPath}/references/{sceneId}/*</c></item>
        /// <item><c>plugin/projectMount/modules/*</c> → <c>{projectPath}/modules/{sceneId}/*</c></item>
        /// <item>其他子目录(plugin 自定义,无 references / modules 语义)→ <c>{projectPath}/_pluginMount/{sceneId}/&lt;子目录&gt;</c> 兜底</item>
        /// </list>
        /// </para>
        /// <para>
        /// <b>幂等性</b>:三个目标路径有任一已存在时跳过整体物化(保护用户已有数据,符合 R10 不变量)。
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

            var referencesTarget = PluginPaths.SceneReferencesRoot(projectPath, sceneId);
            var modulesTarget = PluginPaths.SceneModulesRoot(projectPath, sceneId);
            var otherMountTarget = PluginPaths.SceneOtherMountRoot(projectPath, sceneId);

            // 幂等检查:三个目标任一已存在则跳过(避免覆盖用户已有数据,R10 不变量)
            if (Directory.Exists(referencesTarget) || Directory.Exists(modulesTarget) || Directory.Exists(otherMountTarget))
            {
                _logger.LogInformation(
                    "sceneId='{Scene}' 命名空间已存在(references/modules/_pluginMount 任一),跳过物化 (幂等)",
                    sceneId);
                return;
            }

            bool didMount = false;

            // references/* → {projectPath}/references/{sceneId}/*
            var pluginReferences = Path.Combine(pluginMountSource, "references");
            if (Directory.Exists(pluginReferences))
            {
                Directory.CreateDirectory(referencesTarget);
                CopyDirectoryRecursive(pluginReferences, referencesTarget);
                didMount = true;
                _logger.LogInformation(
                    "plugin '{Plugin}' references → {Target}", pluginId, referencesTarget);
            }

            // modules/* → {projectPath}/modules/{sceneId}/*
            var pluginModules = Path.Combine(pluginMountSource, "modules");
            if (Directory.Exists(pluginModules))
            {
                Directory.CreateDirectory(modulesTarget);
                CopyDirectoryRecursive(pluginModules, modulesTarget);
                didMount = true;
                _logger.LogInformation(
                    "plugin '{Plugin}' modules → {Target}", pluginId, modulesTarget);
            }

            // 其他子目录(非 references / modules / manifest.json)→ _pluginMount/{sceneId}/<sub>/
            foreach (var subDir in Directory.GetDirectories(pluginMountSource))
            {
                var subName = Path.GetFileName(subDir);
                if (subName.Equals("references", StringComparison.OrdinalIgnoreCase) ||
                    subName.Equals("modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 已分别物化
                }
                Directory.CreateDirectory(otherMountTarget);
                var subTarget = Path.Combine(otherMountTarget, subName);
                Directory.CreateDirectory(subTarget);
                CopyDirectoryRecursive(subDir, subTarget);
                didMount = true;
                _logger.LogInformation(
                    "plugin '{Plugin}' 自定义子目录 {Sub} → {Target}", pluginId, subName, subTarget);
            }

            if (!didMount)
            {
                _logger.LogInformation(
                    "plugin '{Plugin}' projectMount 为空(无 references / modules / 其他子目录),sceneId={Scene} 无物化内容",
                    pluginId, sceneId);
            }
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
}
