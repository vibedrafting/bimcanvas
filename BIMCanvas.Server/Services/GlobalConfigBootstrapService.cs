using System.IO;
using System.Linq;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// BIMCANVAS_HOME 下全局配置资产初始化。
    /// 仅补齐缺失项，不覆盖已有内容。
    /// </summary>
    public sealed class GlobalConfigBootstrapService
    {
        private const string ManifestRelativePath = "global-config/manifest.json";
        private static readonly string[] ObsoleteSkillDirectories =
        [
            "generate-derived-planning",
            "generate-reference-translation",
            "generate-reference-placement",
            "generate-derived-placement"
        ];

        private readonly BootstrapTemplateService _templateService;

        public GlobalConfigBootstrapService(BootstrapTemplateService templateService)
        {
            _templateService = templateService;
        }

        public void EnsureInitialized()
        {
            var configDir = ConfigService.GetConfigDir();

            _templateService.EnsureInitializedFromManifest(
                ManifestRelativePath,
                configDir);

            RemoveObsoleteEmptySkillDirectories(configDir);
        }

        private static void RemoveObsoleteEmptySkillDirectories(string configDir)
        {
            var skillsRoot = Path.Combine(configDir, "skills");
            if (!Directory.Exists(skillsRoot))
            {
                return;
            }

            foreach (var skillName in ObsoleteSkillDirectories)
            {
                var skillPath = Path.Combine(skillsRoot, skillName);
                if (!Directory.Exists(skillPath))
                {
                    continue;
                }

                if (Directory.EnumerateFileSystemEntries(skillPath).Any())
                {
                    continue;
                }

                Directory.Delete(skillPath, recursive: false);
            }
        }
    }
}
