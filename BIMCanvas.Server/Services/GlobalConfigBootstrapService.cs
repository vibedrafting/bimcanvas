using System;
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
            "generate-reference-placement",
            "generate-derived-placement"
        ];
        private static readonly string[] ObsoleteSkillReferenceOwners =
        [
            "generate-planning",
            "generate-placement"
        ];

        private readonly BootstrapTemplateService _templateService;

        public GlobalConfigBootstrapService(BootstrapTemplateService templateService)
        {
            _templateService = templateService;
        }

        public void EnsureInitialized()
        {
            var configDir = ConfigService.GetConfigDir();

            // 1. 平台级配置(server/* + agent/config.json)
            _templateService.EnsureInitializedFromManifest(
                ManifestRelativePath,
                configDir);

            // 2. 组5 §5.A.6 + v3.4:首启动 bootstrap core-base plugin 到 BIMCANVAS_HOME/plugins/core-base/
            // 提供通用 BIM 助手 prompt + Skills;Agent 启动时无 active plugin 也会以 core-base 为默认 prompt 基线。
            // v3.4:源路径已从 BIMCanvas.Server/Templates/plugins/core-base/ 迁到
            //       BIMCanvas.Agent/plugins/core-base/(Python 代码归 Python 项目,跟 mcp_tools/canvas.py 同根)。
            var agentRoot = BootstrapTemplateService.ResolveAgentProjectRoot();
            var coreBaseSource = Path.Combine(agentRoot, "plugins", "core-base");
            var coreBaseTarget = Path.Combine(configDir, "plugins", "core-base");
            _templateService.EnsurePluginInitializedFromAbsolute(coreBaseSource, coreBaseTarget);

            // 3. 清理 M1 / 之前的过期 skill 目录(向后兼容)
            RemoveObsoleteEmptySkillDirectories(configDir);
            RemoveObsoleteSkillReferenceDirectories(configDir);
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

        private static void RemoveObsoleteSkillReferenceDirectories(string configDir)
        {
            var skillsRoot = Path.Combine(configDir, "skills");
            if (!Directory.Exists(skillsRoot))
            {
                return;
            }

            var fullSkillsRoot = Path.GetFullPath(skillsRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            foreach (var skillName in ObsoleteSkillReferenceOwners)
            {
                var referencesPath = Path.Combine(skillsRoot, skillName, "references");
                if (!Directory.Exists(referencesPath))
                {
                    continue;
                }

                var fullReferencesPath = Path.GetFullPath(referencesPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (!fullReferencesPath.StartsWith(
                    fullSkillsRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"拒绝删除 skills 根目录之外的 references 目录: {fullReferencesPath}");
                }

                Directory.Delete(fullReferencesPath, recursive: true);
            }
        }
    }
}
