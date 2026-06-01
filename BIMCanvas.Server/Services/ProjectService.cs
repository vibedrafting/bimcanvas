using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Project;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 项目服务 - 负责项目加载、解压、初始化
    ///
    /// v3.2 架构简化：
    /// - 单仓库 + 多分支架构
    /// - schemes/ 目录直接存放策略文件（无子目录）
    /// - 策略切换通过 Git 分支实现
    /// - 并行任务通过 Git Worktree 实现
    /// </summary>
    public class ProjectService
    {
        internal sealed class ProjectLoadExecutionResult
        {
            public ProjectLoadExecutionResult(string projectPath)
            {
                ProjectPath = projectPath;
            }

            public string ProjectPath { get; }
            public List<string> Warnings { get; } = new List<string>();
        }

        internal const string ZonesBaselineChangedWarning =
            "已保留现有分区设计；baseline 已更新，分区可能与最新房间数据不一致，请检查。";

        private readonly ILogger<ProjectService> _logger;
        // v1.1 §4.9 R10:不再在 LoadProject / EnsureProjectAssets 内调 EnsureInitialized。
        // 保留字段是为不破坏 DI 注册签名;唯一活跃调用点已迁移到 ProjectController.BindScene → MountSceneScaffold。
#pragma warning disable IDE0052, CS0414 // 保留字段供未来可能的 plugin-scope 用途
        private readonly ProjectFixedFilesBootstrapService _projectFixedFilesBootstrapService;
#pragma warning restore IDE0052, CS0414
        private readonly ProjectDerivedBootstrapService _projectDerivedBootstrapService;
        private readonly BootstrapTemplateService _bootstrapTemplateService;
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 默认项目根目录：<BIMCANVAS_HOME>/Projects
        /// </summary>
        public static string DefaultProjectsRoot => Path.Combine(
            ConfigService.GetConfigDir(),
            "Projects");

        public ProjectService(
            ILogger<ProjectService> logger,
            ProjectFixedFilesBootstrapService projectFixedFilesBootstrapService,
            ProjectDerivedBootstrapService projectDerivedBootstrapService,
            BootstrapTemplateService bootstrapTemplateService)
        {
            _logger = logger;
            _projectFixedFilesBootstrapService = projectFixedFilesBootstrapService;
            _projectDerivedBootstrapService = projectDerivedBootstrapService;
            _bootstrapTemplateService = bootstrapTemplateService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Formatting = Formatting.Indented
            };
        }

        /// <summary>
        /// 检测项目文件夹是否存在冲突
        /// </summary>
        /// <param name="bcpFilePath">BCP 文件路径</param>
        /// <returns>冲突检测结果 (hasConflict, existingPath)</returns>
        public (bool HasConflict, string? ExistingPath) CheckProjectConflict(string bcpFilePath)
        {
            var bcpFileName = Path.GetFileNameWithoutExtension(bcpFilePath);
            var projectPath = Path.Combine(DefaultProjectsRoot, bcpFileName);

            if (Directory.Exists(projectPath))
            {
                return (true, projectPath);
            }
            return (false, null);
        }

        /// <summary>
        /// 加载项目（完整流程）
        /// </summary>
        /// <param name="bcpFilePath">.bcp 文件路径</param>
        /// <param name="overwrite">是否覆盖已存在的目录</param>
        /// <returns>解压后的项目上下文（含 warning）</returns>
        internal ProjectLoadExecutionResult LoadProject(string bcpFilePath, bool overwrite = false)
        {
            if (!File.Exists(bcpFilePath))
            {
                throw new FileNotFoundException($"BCP 文件不存在: {bcpFilePath}");
            }

            _logger.LogInformation("开始加载项目: {Path}, Overwrite: {Overwrite}", bcpFilePath, overwrite);

            // 1. 解压 .bcp 到工作目录
            var projectPath = ExtractBcpFile(bcpFilePath, overwrite);

            // v1.1 §4.9 R10 缓解 (用户决策"字面严格执行"):删除 EnsureInitialized 在 open / 解压时的调用。
            // 项目级模板物化改为 bind-time 由 ProjectFixedFilesBootstrapService.MountSceneScaffold 触发,
            // 唯一入口在 POST /api/project/scenes 端点内。

            // 组5 §5.A.7:平台级 baseline (README.md / .gitignore) 拷贝。
            // 与 plugin 系统解耦,任何 .bcp 项目都该有;幂等(已存在跳过),不违反 R10。
            _bootstrapTemplateService.EnsurePlatformBaseline(projectPath);

            // 2. 补齐条件派生产物（baseline/schemes/computed/zones/git 等）
            var bootstrapResult = _projectDerivedBootstrapService.EnsureInitialized(
                projectPath,
                refreshProjectMetadata: true);

            // 按当前激活 domain 插件初始化其 projectMount 到项目全局(modules/、references/)。
            // 仅缺失补齐,绝不覆盖用户改动(保留 R10 不静默覆盖的核心防御)。
            _projectFixedFilesBootstrapService.EnsureProjectMountInitialized(projectPath);

            var result = new ProjectLoadExecutionResult(projectPath);
            AddZoneBaselineWarningIfNeeded(result.Warnings, bootstrapResult);

            _logger.LogInformation("项目加载完成: {Path}", projectPath);
            return result;
        }

        /// <summary>
        /// 确保项目资源文件存在（用于打开已存在的项目）
        /// 包括：modules 文件夹、README.md 等
        /// </summary>
        public void EnsureProjectAssets(string projectPath)
        {
            if (!Directory.Exists(projectPath))
            {
                _logger.LogWarning("项目目录不存在，跳过资源检查: {Path}", projectPath);
                return;
            }

            // v1.1 §4.9 R10 缓解:不再补任何模板。如需 plugin projectMount 物化,
            // 走 POST /api/project/scenes 端点的 MountSceneScaffold 入口。
        }

        /// <summary>
        /// 缺失时从 computed/room_zones.json 初始化 schemes/zones.json。
        /// 一旦 schemes/zones.json 已存在，就保留现有分区设计，不再覆盖。
        /// </summary>
        internal void EnsureZonesInitializedFromComputed(string projectPath)
        {
            _projectDerivedBootstrapService.EnsureZonesInitializedFromComputed(projectPath);
        }

        /// <summary>
        /// 基于 schemes/zones.json 创建分区子目录
        /// 支持嵌套：有 SubZones 的 zone 为容器（不创建 modules.json），
        /// 只有叶子 zone 创建 modules.json
        /// </summary>
        internal void CreateZoneDirectories(string projectPath)
        {
            _projectDerivedBootstrapService.RefreshZoneDirectories(projectPath);
        }

        /// <summary>
        /// 解析 zoneId 到实际目录路径（支持嵌套分区）。
        /// 例如 dz_1 → schemes/rz_3/dz_1（搜索已有目录）。
        /// 静态方法，供各 Service/Controller 共用。
        /// </summary>
        internal static string ResolveZoneDirectory(string schemesPath, string zoneId)
        {
            if (ModuleFileTopologyService.TryResolveZoneDirectory(schemesPath, zoneId, out var canonicalZoneDir))
                return canonicalZoneDir;

            // 1. 检查一级目录
            var directPath = Path.Combine(schemesPath, zoneId);
            if (Directory.Exists(directPath))
                return directPath;

            // 2. 搜索嵌套目录（在父 zone 目录下查找 dz_*）
            if (Directory.Exists(schemesPath))
            {
                foreach (var parentDir in Directory.GetDirectories(schemesPath))
                {
                    var nestedPath = Path.Combine(parentDir, zoneId);
                    if (Directory.Exists(nestedPath))
                        return nestedPath;
                }
            }

            // 3. 回退到一级目录（新建场景）
            return directPath;
        }

        /// <summary>
        /// 递归查找 schemes 下所有叶子 zone 的 modules.json 文件路径。
        /// 静态方法，供各 Service/Controller 共用。
        /// </summary>
        /// <returns>(modulesFilePath, zoneId) 列表</returns>
        internal static List<(string FilePath, string ZoneId)> FindAllLeafModuleFiles(string schemesPath)
        {
            return FindAllLeafModuleFiles(schemesPath, requestedZoneIds: null, variantId: null);
        }

        /// <summary>
        /// 带 variantId 重载：解析指定方案 slug（候选/变体）的叶子 modules 文件。
        /// variantId 为空 → 与单参重载行为一致（解析 adopted 当前生效方案，零回归）。
        /// variantId 非空 → 解析 schemes/{dz}/{variantId}/[{leaf}/]modules.json，调用方必须显式给 requestedZoneIds
        /// （拓扑层不允许全分区变体扫描）。指针解析一律走 ModuleFileTopologyService，不裸拼路径。
        /// </summary>
        internal static List<(string FilePath, string ZoneId)> FindAllLeafModuleFiles(
            string schemesPath,
            IReadOnlyCollection<string>? requestedZoneIds,
            string? variantId)
        {
            return ModuleFileTopologyService.FindExistingCanonicalModuleFiles(schemesPath, requestedZoneIds, variantId)
                .Select(entry => (entry.FilePath, entry.ZoneId))
                .ToList();
        }

        /// <summary>
        /// 递归清空 schemes 下所有叶子 zone 的 modules.json（重写为空数组）。
        /// 静态方法，供 SaveAllModules 等场景使用。
        /// </summary>
        internal static void ClearAllLeafModuleFiles(string schemesPath)
        {
            if (!Directory.Exists(schemesPath))
                return;

            var moduleFiles = ModuleFileTopologyService.FindExistingCanonicalModuleFiles(schemesPath);
            foreach (var entry in moduleFiles)
            {
                // 移除只读属性
                var attrs = File.GetAttributes(entry.FilePath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(entry.FilePath, attrs & ~FileAttributes.ReadOnly);
                File.Delete(entry.FilePath);
            }
        }

        /// <summary>
        /// 解压 .bcp 文件到工作目录（不带时间戳）
        /// 支持 ZIP、RAR、7z 等多种压缩格式
        /// 自动检测并跳过单一顶层目录（避免 demo_1/demo_1 嵌套）
        /// </summary>
        /// <param name="bcpFilePath">BCP 文件路径</param>
        /// <param name="overwrite">是否覆盖已存在的目录</param>
        private string ExtractBcpFile(string bcpFilePath, bool overwrite = false)
        {
            // 确保根目录存在
            if (!Directory.Exists(DefaultProjectsRoot))
            {
                Directory.CreateDirectory(DefaultProjectsRoot);
                _logger.LogInformation("创建项目根目录: {Path}", DefaultProjectsRoot);
            }

            // 生成项目文件夹名：直接使用 bcp 文件名（不带时间戳）
            var bcpFileName = Path.GetFileNameWithoutExtension(bcpFilePath);
            var projectPath = Path.Combine(DefaultProjectsRoot, bcpFileName);

            // 检查是否已存在
            if (Directory.Exists(projectPath))
            {
                if (overwrite)
                {
                    _logger.LogInformation("覆盖已存在的项目目录: {Path}", projectPath);
                    Directory.Delete(projectPath, recursive: true);
                }
                else
                {
                    throw new InvalidOperationException($"项目目录已存在: {projectPath}");
                }
            }

            // 使用 SharpCompress 解压（自动检测格式：ZIP、RAR、7z 等）
            _logger.LogInformation("解压 BCP 到: {Path}", projectPath);
            Directory.CreateDirectory(projectPath);

            using (var archive = ArchiveFactory.Open(bcpFilePath))
            {
                var archiveType = archive.Type.ToString();
                _logger.LogInformation("检测到压缩格式: {Type}", archiveType);

                // 检测是否存在单一顶层目录（如 WinRAR 打包整个文件夹）
                var entries = archive.Entries.Where(e => !e.IsDirectory).ToList();
                var topLevelPrefix = DetectSingleTopLevelDirectory(entries);

                if (!string.IsNullOrEmpty(topLevelPrefix))
                {
                    _logger.LogInformation("检测到单一顶层目录: {Prefix}，将跳过此层", topLevelPrefix);
                }

                foreach (var entry in entries)
                {
                    // 计算目标路径，跳过顶层目录
                    var entryPath = NormalizeArchiveEntryPath(entry.Key);
                    if (!string.IsNullOrEmpty(topLevelPrefix) &&
                        entryPath.StartsWith(topLevelPrefix, StringComparison.Ordinal))
                    {
                        entryPath = entryPath.Substring(topLevelPrefix.Length);
                    }

                    if (string.IsNullOrEmpty(entryPath))
                        continue;

                    var destPath = ResolveArchiveDestinationPath(projectPath, entryPath);
                    var destDir = Path.GetDirectoryName(destPath);

                    if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    using (var entryStream = entry.OpenEntryStream())
                    using (var fileStream = File.Create(destPath))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }

            _logger.LogInformation("解压完成，已支持格式: ZIP/RAR/7z/TAR/GZ");
            return projectPath;
        }

        /// <summary>
        /// 检测压缩包是否有单一顶层目录
        /// 如果所有文件都以同一个目录名开头，返回该目录名（含尾部斜杠）
        /// </summary>
        private string? DetectSingleTopLevelDirectory(List<SharpCompress.Archives.IArchiveEntry> entries)
        {
            if (entries.Count == 0)
                return null;

            // 获取所有文件的第一级目录
            var topLevelDirs = new HashSet<string>();
            foreach (var entry in entries)
            {
                var key = NormalizeArchiveEntryPath(entry.Key);
                var slashIndex = key.IndexOf('/');
                if (slashIndex > 0)
                {
                    topLevelDirs.Add(key.Substring(0, slashIndex + 1));
                }
                else
                {
                    // 有文件在根目录，不是单一顶层目录
                    return null;
                }
            }

            // 只有一个顶层目录时返回它
            if (topLevelDirs.Count == 1)
            {
                return topLevelDirs.First();
            }

            return null;
        }

        private static string NormalizeArchiveEntryPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path
                .Replace('\\', '/')
                .TrimStart('/');
        }

        private static string ResolveArchiveDestinationPath(string projectPath, string entryPath)
        {
            var normalizedRelativePath = entryPath.Replace('/', Path.DirectorySeparatorChar);
            var combinedPath = Path.Combine(projectPath, normalizedRelativePath);
            var fullDestinationPath = Path.GetFullPath(combinedPath);
            var fullProjectPath = Path.GetFullPath(projectPath);

            if (!fullDestinationPath.StartsWith(fullProjectPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"压缩包条目路径非法: {entryPath}");
            }

            return fullDestinationPath;
        }

        /// <summary>
        /// 查找默认的 demo .bcp 文件
        /// </summary>
        /// <param name="baseDir">起始目录</param>
        /// <param name="demoName">demo 名称（如 "demo_1"）</param>
        /// <returns>.bcp 文件路径，未找到返回 null</returns>
        public string? FindDemoBcpFile(string baseDir, string demoName = "demo_1")
        {
            // 向上查找 demos 目录
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                var demosPath = Path.Combine(dir.FullName, "demos");
                var bcpPath = Path.Combine(demosPath, $"{demoName}.bcp");

                if (File.Exists(bcpPath))
                {
                    _logger.LogDebug("找到默认 BCP 文件: {Path}", bcpPath);
                    return bcpPath;
                }

                dir = dir.Parent;
            }

            _logger.LogWarning("未找到默认 BCP 文件: {Name}", demoName);
            return null;
        }

        /// <summary>
        /// 保存项目为 .bcp 文件
        /// </summary>
        public void SaveProject(string projectPath, string bcpOutputPath)
        {
            if (!Directory.Exists(projectPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {projectPath}");
            }

            _logger.LogInformation("保存项目到: {Path}", bcpOutputPath);

            // 如果目标文件已存在，先删除
            if (File.Exists(bcpOutputPath))
            {
                File.Delete(bcpOutputPath);
            }

            // 压缩为 ZIP
            ZipFile.CreateFromDirectory(projectPath, bcpOutputPath);
            _logger.LogInformation("项目保存完成");
        }

        /// <summary>
        /// 扫描 DefaultProjectsRoot 下的所有项目（轻量，只读 project.json）
        /// </summary>
        public List<Dtos.ProjectSummary> ScanProjects()
        {
            var result = new List<Dtos.ProjectSummary>();
            var root = DefaultProjectsRoot;

            if (!Directory.Exists(root))
                return result;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var projectJsonPath = Path.Combine(dir, "project.json");
                var summary = new Dtos.ProjectSummary
                {
                    Name = Path.GetFileName(dir),
                    FolderPath = dir
                };

                if (!File.Exists(projectJsonPath))
                {
                    summary.IsValid = false;
                    summary.ErrorMessage = "缺少 project.json";
                    result.Add(summary);
                    continue;
                }

                try
                {
                    var json = File.ReadAllText(projectJsonPath, Encoding.UTF8);
                    var project = JsonConvert.DeserializeObject<BIMCanvas.Core.Models.Project.Project>(json, _jsonSettings);
                    if (project == null)
                    {
                        summary.IsValid = false;
                        summary.ErrorMessage = "project.json 解析失败";
                    }
                    else
                    {
                        summary.CreatedAt = project.CreatedAt;
                        summary.UpdatedAt = project.UpdatedAt;
                        summary.SchemeCount = project.Schemes?.Count ?? 0;
                        summary.ActiveScheme = project.ActiveSchemeId;
                        summary.Version = project.Version;
                    }
                }
                catch (Exception ex)
                {
                    summary.IsValid = false;
                    summary.ErrorMessage = $"读取失败: {ex.Message}";
                }

                result.Add(summary);
            }

            return result;
        }

        /// <summary>
        /// 打开已存在的项目文件夹（验证 + 初始化链路）
        /// </summary>
        internal ProjectLoadExecutionResult OpenFolder(string folderPath)
        {
            // 路径穿越检查
            var normalizedFolder = Path.GetFullPath(folderPath);
            var normalizedRoot = Path.GetFullPath(DefaultProjectsRoot);
            if (!normalizedFolder.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"路径不在项目根目录下: {folderPath}");
            }

            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {folderPath}");
            }

            var projectJsonPath = Path.Combine(folderPath, "project.json");
            if (!File.Exists(projectJsonPath))
            {
                throw new FileNotFoundException($"project.json 不存在: {projectJsonPath}");
            }

            var baselinePath = Path.Combine(folderPath, "baseline");
            if (!Directory.Exists(baselinePath))
            {
                throw new DirectoryNotFoundException($"baseline 目录不存在: {baselinePath}");
            }

            EnsureProjectAssets(folderPath);
            var bootstrapResult = _projectDerivedBootstrapService.EnsureInitialized(
                folderPath,
                refreshProjectMetadata: false);

            // 按当前激活 domain 插件初始化其 projectMount 到项目全局(modules/、references/)。
            // 仅缺失补齐,绝不覆盖用户改动(保留 R10 不静默覆盖的核心防御)。
            _projectFixedFilesBootstrapService.EnsureProjectMountInitialized(folderPath);

            var result = new ProjectLoadExecutionResult(folderPath);
            AddZoneBaselineWarningIfNeeded(result.Warnings, bootstrapResult);

            _logger.LogInformation("项目文件夹打开完成: {Path}", folderPath);
            return result;
        }

        private void AddZoneBaselineWarningIfNeeded(
            List<string> warnings,
            ProjectDerivedBootstrapService.BootstrapResult bootstrapResult)
        {
            if (!bootstrapResult.WasComputedRegenerated ||
                !bootstrapResult.WasComputedRegeneratedBecauseBaselineChanged ||
                !bootstrapResult.ZonesExistedBeforeBootstrap)
            {
                return;
            }

            warnings.Add(ZonesBaselineChangedWarning);
            _logger.LogWarning("{Warning}", ZonesBaselineChangedWarning);
        }

        /// <summary>
        /// 删除项目（带路径穿越检查）
        /// </summary>
        public void DeleteProject(string projectName)
        {
            var projectPath = Path.Combine(DefaultProjectsRoot, projectName);
            var normalizedPath = Path.GetFullPath(projectPath);
            var normalizedRoot = Path.GetFullPath(DefaultProjectsRoot);

            if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"路径穿越检测: {projectName}");
            }

            if (!Directory.Exists(normalizedPath))
            {
                throw new DirectoryNotFoundException($"项目不存在: {projectName}");
            }

            // Git 对象文件默认只读，Directory.Delete 无法删除只读文件
            RemoveReadOnlyAttributes(normalizedPath);
            Directory.Delete(normalizedPath, recursive: true);
            _logger.LogInformation("项目已删除: {Name}", projectName);
        }

        /// <summary>
        /// 递归移除目录内所有文件的只读属性（Git 对象文件默认只读）
        /// </summary>
        private static void RemoveReadOnlyAttributes(string dirPath)
        {
            foreach (var file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
        }
    }
}
