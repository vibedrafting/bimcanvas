using System;
using System.Collections.Generic;
using System.IO;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目固定模板文件初始化。
    /// 仅补齐缺失项，不覆盖已有内容。
    /// </summary>
    public sealed class ProjectFixedFilesBootstrapService
    {
        private const string ManifestRelativePath = "project-fixed/manifest.json";

        private readonly BootstrapTemplateService _templateService;

        public ProjectFixedFilesBootstrapService(BootstrapTemplateService templateService)
        {
            _templateService = templateService;
        }

        public void EnsureInitialized(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("项目路径不能为空。", nameof(projectPath));
            }

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
    }
}
