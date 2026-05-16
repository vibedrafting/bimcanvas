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
        /// [已废弃 v1.1 §4.9] 旧"项目固定模板补齐"入口。
        /// 改造后:open / 解压 / 显式调用全链路均不再调本方法 (R10);
        /// 保留 internal 仅供向后兼容 + 单测复现,生产代码不应再调用。
        /// </summary>
        [Obsolete("v1.1 §4.9:打开 / 解压时不再补模板 (R10 缓解)。新代码用 MountSceneScaffold(projectPath, sceneId, pluginId)。")]
        internal void EnsureInitialized(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                throw new ArgumentException("项目路径不能为空。", nameof(projectPath));

            var projectName = Path.GetFileName(Path.GetFullPath(projectPath));
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["{PROJECT_NAME}"] = projectName,
                ["{EXPORT_DATE}"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ["{PROJECT_FOLDER}"] = projectName
            };

            _templateService.EnsureInitializedFromManifest(
                ManifestRelativePath,
                projectPath,
                replacements);
        }

        /// <summary>
        /// bind-time 把 plugin 的 <c>projectMount/</c> 物化到项目侧 sceneId 命名空间
        /// (主真理源 §3.9 + §4.2 + 模板 §4.9)。
        /// <para>
        /// <b>唯一调用入口</b>:<c>POST /api/project/scenes</c> 端点 (主真理源 §4.8)。
        /// 任何其他调用都违反 R10。
        /// </para>
        /// <para>
        /// M1 实现简化:不解析 plugin projectMount/manifest.json,直接递归复制全部内容到
        /// <see cref="PluginPaths.SceneScaffoldRoot"/> 占位路径 <c>_pluginMount/{sceneId}/</c>;
        /// M2 改 sceneId 真路径时只改 SceneScaffoldRoot 常量,API 已 plugin-aware。
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

            var target = PluginPaths.SceneScaffoldRoot(projectPath, sceneId);
            if (Directory.Exists(target))
            {
                _logger.LogInformation(
                    "sceneId 命名空间已存在,跳过物化 (幂等): {Target}", target);
                return;
            }

            Directory.CreateDirectory(target);
            CopyDirectoryRecursive(pluginMountSource, target);
            _logger.LogInformation(
                "plugin '{Plugin}' projectMount 已物化到 sceneId={Scene} → {Target}",
                pluginId, sceneId, target);
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
