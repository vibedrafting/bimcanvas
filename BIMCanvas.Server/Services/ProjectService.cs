using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Services;
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
        private readonly ILogger<ProjectService> _logger;
        private readonly ManifestService _manifestService;
        private readonly StrategyService _strategyService;
        private readonly ComputedDataService _computedDataService;
        private readonly GitWorktreeService _gitService;
        private readonly JsonSerializerSettings _jsonSettings;

        /// <summary>
        /// 默认项目根目录：用户文档/BIMCanvas/Projects
        /// </summary>
        public static string DefaultProjectsRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BIMCanvas",
            "Projects");

        public ProjectService(
            ILogger<ProjectService> logger,
            ManifestService manifestService,
            StrategyService strategyService,
            ComputedDataService computedDataService,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _manifestService = manifestService;
            _strategyService = strategyService;
            _computedDataService = computedDataService;
            _gitService = gitService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
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
        /// <returns>解压后的项目文件夹路径</returns>
        public string LoadProject(string bcpFilePath, bool overwrite = false)
        {
            if (!File.Exists(bcpFilePath))
            {
                throw new FileNotFoundException($"BCP 文件不存在: {bcpFilePath}");
            }

            _logger.LogInformation("开始加载项目: {Path}, Overwrite: {Overwrite}", bcpFilePath, overwrite);

            // 1. 解压 .bcp 到工作目录
            var projectPath = ExtractBcpFile(bcpFilePath, overwrite);

            // 2. 计算 baseline 哈希并写入 baseline.manifest
            var baselineHash = EnsureBaselineManifest(projectPath);

            // 3. 创建 schemes/ 和默认策略
            var defaultStrategyId = EnsureSchemesDirectory(projectPath, baselineHash);

            // 4. 更新 project.json
            UpdateProjectJson(projectPath, defaultStrategyId);

            // 5. 验证并生成 computed 数据
            EnsureComputedData(projectPath);

            // 6. 从 computed/room_zones.json 初始化 schemes/zones.json（MVP简化）
            InitializeZonesFromComputed(projectPath);

            // 7. 基于 schemes/zones.json 创建分区子目录
            CreateZoneDirectories(projectPath);

            // 8. 从 Templates 统一初始化资源文件（modules、knowledge、README、.gitignore 等）
            InitializeFromTemplates(projectPath);

            // 9. 初始化 Git 仓库（单仓库 + 多分支架构）
            InitializeGitRepository(projectPath);

            _logger.LogInformation("项目加载完成: {Path}", projectPath);
            return projectPath;
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

            // 从 Templates 统一初始化资源文件
            InitializeFromTemplates(projectPath);

            // 确保 Git 仓库状态完整（修复无 commit 的僵死状态）
            InitializeGitRepository(projectPath);
        }

        /// <summary>
        /// 从 computed/room_zones.json 初始化 schemes/zones.json
        /// MVP 阶段：直接复制，跳过分区设计流程
        /// </summary>
        private void InitializeZonesFromComputed(string projectPath)
        {
            var roomZonesPath = Path.Combine(projectPath, "computed", "room_zones.json");
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");

            if (!File.Exists(roomZonesPath))
            {
                // 如果没有 room_zones，创建空数组
                File.WriteAllText(zonesPath, "[]", Encoding.UTF8);
                _logger.LogWarning("computed/room_zones.json 不存在，创建空的 schemes/zones.json");
                return;
            }

            // MVP: 直接复制 room_zones.json → zones.json
            var roomZonesJson = File.ReadAllText(roomZonesPath, Encoding.UTF8);
            File.WriteAllText(zonesPath, roomZonesJson, Encoding.UTF8);

            _logger.LogInformation("从 computed/room_zones.json 初始化 schemes/zones.json");
        }

        /// <summary>
        /// 基于 schemes/zones.json 创建分区子目录
        /// 支持嵌套：有 SubZones 的 zone 为容器（不创建 modules.json），
        /// 只有叶子 zone 创建 modules.json
        /// </summary>
        internal void CreateZoneDirectories(string projectPath)
        {
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            var schemesPath = Path.Combine(projectPath, "schemes");

            if (!File.Exists(zonesPath))
            {
                _logger.LogWarning("schemes/zones.json 不存在，跳过分区目录创建");
                return;
            }

            try
            {
                var zonesJson = File.ReadAllText(zonesPath, Encoding.UTF8);
                var zones = JsonConvert.DeserializeObject<List<Zone>>(zonesJson) ?? new List<Zone>();

                var createdCount = 0;
                foreach (var zone in zones)
                {
                    if (string.IsNullOrEmpty(zone.Id))
                        continue;

                    createdCount += CreateZoneDirectory(schemesPath, zone.Id, zone);
                }

                _logger.LogInformation("创建/刷新了 {Count} 个分区目录", createdCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "创建分区目录失败");
            }
        }

        /// <summary>
        /// 为单个 zone 创建目录（递归处理 SubZones）
        /// </summary>
        private int CreateZoneDirectory(string parentDir, string zoneId, Zone zone)
        {
            var zoneDir = Path.Combine(parentDir, zoneId);
            var count = 0;

            if (zone.SubZones != null && zone.SubZones.Count > 0)
            {
                // 容器 zone：创建目录但不创建 modules.json
                Directory.CreateDirectory(zoneDir);
                _logger.LogDebug("创建容器分区目录: {ZoneId}", zoneId);

                foreach (var subZone in zone.SubZones)
                {
                    if (!string.IsNullOrEmpty(subZone.Id))
                    {
                        count += CreateZoneDirectory(zoneDir, subZone.Id, subZone);
                    }
                }
            }
            else
            {
                // 叶子 zone：创建目录 + modules.json
                if (!Directory.Exists(zoneDir))
                {
                    Directory.CreateDirectory(zoneDir);
                }

                var modulesPath = Path.Combine(zoneDir, "modules.json");
                if (!File.Exists(modulesPath))
                {
                    File.WriteAllText(modulesPath, "[]", Encoding.UTF8);
                }

                count++;
                _logger.LogDebug("创建叶子分区目录: {ZoneId}", zoneId);
            }

            return count;
        }

        /// <summary>
        /// 初始化项目 Git 仓库
        /// v3.1 架构：单仓库 + 多分支，支持 Worktree 并行任务
        /// </summary>
        private void InitializeGitRepository(string projectPath)
        {
            try
            {
                var initialized = _gitService.InitializeRepository(projectPath);
                if (initialized)
                {
                    _logger.LogInformation("Git 仓库初始化完成（单仓库 + 多分支架构）");
                }
            }
            catch (Exception ex)
            {
                // Git 初始化失败不阻塞项目加载
                _logger.LogWarning(ex, "Git 仓库初始化失败（非致命错误）");
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
                    var entryPath = entry.Key ?? string.Empty;
                    if (!string.IsNullOrEmpty(topLevelPrefix) && entryPath.StartsWith(topLevelPrefix))
                    {
                        entryPath = entryPath.Substring(topLevelPrefix.Length);
                    }

                    if (string.IsNullOrEmpty(entryPath))
                        continue;

                    var destPath = Path.Combine(projectPath, entryPath.Replace('/', Path.DirectorySeparatorChar));
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
                var key = entry.Key ?? string.Empty;
                var slashIndex = key.IndexOfAny(new[] { '/', '\\' });
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

        /// <summary>
        /// 确保 baseline.manifest 存在并包含哈希值
        /// </summary>
        /// <returns>baseline 哈希值</returns>
        private string EnsureBaselineManifest(string projectPath)
        {
            var baselinePath = Path.Combine(projectPath, "baseline");

            if (!Directory.Exists(baselinePath))
            {
                throw new DirectoryNotFoundException($"baseline 目录不存在: {baselinePath}");
            }

            // 检查是否已存在有效的 manifest
            var existingHash = _manifestService.GetBaselineHash(baselinePath);
            if (!string.IsNullOrEmpty(existingHash))
            {
                _logger.LogDebug("baseline.manifest 已存在，hash = {Hash}", existingHash);
                return existingHash;
            }

            // 计算 baseline 哈希
            _logger.LogInformation("计算 baseline 哈希...");
            var hash = BaselineHashService.ComputeBaselineHash(baselinePath);
            _logger.LogInformation("Baseline Hash: {Hash}", hash);

            // 写入 manifest
            _manifestService.WriteBaselineManifest(baselinePath, hash);

            return hash;
        }

        /// <summary>
        /// 确保 schemes/ 目录和默认策略存在
        /// v3.2: 策略文件直接存放在 schemes/ 目录下（无子目录）
        /// </summary>
        /// <returns>默认策略 ID</returns>
        private string EnsureSchemesDirectory(string projectPath, string baselineHash)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");

            // 确保目录存在
            if (!Directory.Exists(schemesPath))
            {
                Directory.CreateDirectory(schemesPath);
                _logger.LogInformation("创建 schemes/ 目录");
            }

            // 检查是否已有策略
            var existingStrategies = _strategyService.GetAllStrategyIds(schemesPath);
            if (existingStrategies.Count > 0)
            {
                _logger.LogDebug("已存在 {Count} 个策略，跳过默认策略创建", existingStrategies.Count);
                return existingStrategies[0]; // 返回第一个作为默认
            }

            // 创建默认策略
            return _strategyService.CreateDefaultStrategy(schemesPath, baselineHash);
        }

        /// <summary>
        /// 更新 project.json
        /// </summary>
        private void UpdateProjectJson(string projectPath, string activeStrategyId)
        {
            var projectJsonPath = Path.Combine(projectPath, "project.json");

            Project project;
            if (File.Exists(projectJsonPath))
            {
                // 读取现有 project.json
                var json = File.ReadAllText(projectJsonPath, Encoding.UTF8);
                project = JsonConvert.DeserializeObject<Project>(json) ?? new Project();
            }
            else
            {
                // 创建新的 project
                project = new Project
                {
                    Id = $"proj_{Path.GetFileName(projectPath)}",
                    Name = Path.GetFileName(projectPath),
                    Version = "3.0",
                    CreatedAt = DateTime.Now,
                    CoordinateSystem = "cartesian_mm_yUp"
                };
            }

            // 更新策略列表
            var schemesPath = Path.Combine(projectPath, "schemes");
            var strategyIds = _strategyService.GetAllStrategyIds(schemesPath);

            // v3.2: 策略文件直接存放在 schemes/ 目录下，所有策略共用同一路径
            project.Schemes = new List<SchemeRef>();
            foreach (var id in strategyIds)
            {
                project.Schemes.Add(new SchemeRef
                {
                    Id = id,
                    Path = "./schemes",  // v3.2: 统一路径
                    Name = id.Contains("_") ? id.Substring(id.IndexOf('_') + 1) : id
                });
            }

            // 设置激活的策略
            project.ActiveSchemeId = activeStrategyId;
            project.UpdatedAt = DateTime.Now;

            // 写入 project.json
            var updatedJson = JsonConvert.SerializeObject(project, _jsonSettings);
            File.WriteAllText(projectJsonPath, updatedJson, Encoding.UTF8);
            _logger.LogInformation("更新 project.json: ActiveSchemeId = {Id}, Schemes.Count = {Count}",
                activeStrategyId, project.Schemes.Count);
        }

        /// <summary>
        /// 递归复制目录
        /// </summary>
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            // 复制文件
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, overwrite: true);
            }

            // 递归复制子目录
            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var subDirName = Path.GetFileName(subDir);
                CopyDirectory(subDir, Path.Combine(targetDir, subDirName));
            }
        }

        #region 统一模板初始化

        /// <summary>
        /// 查找 Templates 根目录
        /// 优先查编译输出目录，再向上 8 级查开发目录
        /// </summary>
        private string? FindTemplatesRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 方法1：编译输出目录（bin/Debug/net8.0/Templates/）
            var directPath = Path.Combine(baseDir, "Templates");
            if (Directory.Exists(directPath) &&
                File.Exists(Path.Combine(directPath, "init_manifest.json")))
            {
                return directPath;
            }

            // 方法2：向上查找 BIMCanvas.Server/Templates（开发目录）
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var tryPath = Path.Combine(dir.FullName, "BIMCanvas.Server", "Templates");
                if (Directory.Exists(tryPath) &&
                    File.Exists(Path.Combine(tryPath, "init_manifest.json")))
                {
                    return tryPath;
                }
                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 统一从 Templates 目录初始化项目资源
        /// 读取 init_manifest.json，按配置逐项初始化
        /// </summary>
        private void InitializeFromTemplates(string projectPath)
        {
            var templatesRoot = FindTemplatesRoot();
            if (string.IsNullOrEmpty(templatesRoot))
            {
                _logger.LogWarning("未找到 Templates 目录，跳过模板初始化");
                return;
            }

            // 读取 init_manifest.json
            var manifestPath = Path.Combine(templatesRoot, "init_manifest.json");
            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonConvert.DeserializeObject<InitManifest>(manifestJson);

            if (manifest?.Items == null || manifest.Items.Count == 0)
            {
                _logger.LogWarning("init_manifest.json 为空或无效，跳过模板初始化");
                return;
            }

            var projectName = Path.GetFileName(projectPath);

            foreach (var item in manifest.Items)
            {
                if (!item.Enabled)
                {
                    _logger.LogDebug("跳过禁用项: {Name}", item.Name);
                    continue;
                }

                var sourcePath = Path.Combine(templatesRoot, item.Name);
                var targetPath = Path.Combine(projectPath, item.Target);

                try
                {
                    if (item.Type == "directory")
                    {
                        if (Directory.Exists(targetPath))
                        {
                            _logger.LogDebug("{Target} 已存在，跳过", item.Target);
                            continue;
                        }

                        if (!Directory.Exists(sourcePath))
                        {
                            _logger.LogWarning("模板源目录不存在: {Path}", sourcePath);
                            continue;
                        }

                        CopyDirectory(sourcePath, targetPath);
                        _logger.LogInformation("初始化目录: {Target}", item.Target);
                    }
                    else if (item.Type == "template")
                    {
                        if (File.Exists(targetPath))
                        {
                            _logger.LogDebug("{Target} 已存在，跳过", item.Target);
                            continue;
                        }

                        if (!File.Exists(sourcePath))
                        {
                            _logger.LogWarning("模板源文件不存在: {Path}", sourcePath);
                            continue;
                        }

                        // 读取并替换占位符
                        var content = File.ReadAllText(sourcePath, Encoding.UTF8);
                        content = content.Replace("{PROJECT_NAME}", projectName);
                        content = content.Replace("{EXPORT_DATE}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        content = content.Replace("{PROJECT_FOLDER}", projectName);

                        // 确保目标目录存在
                        var targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        File.WriteAllText(targetPath, content, Encoding.UTF8);
                        _logger.LogInformation("初始化模板: {Target}", item.Target);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "初始化模板项失败: {Name}", item.Name);
                }
            }
        }

        /// <summary>初始化清单</summary>
        private class InitManifest
        {
            public string Version { get; set; } = "1.0";
            public List<InitItem> Items { get; set; } = new();
        }

        /// <summary>初始化清单项</summary>
        private class InitItem
        {
            public string Name { get; set; } = "";
            public string Target { get; set; } = "";
            public string Type { get; set; } = "directory";
            public bool Enabled { get; set; } = true;
            public string? Description { get; set; }
        }

        #endregion

        /// <summary>
        /// 确保 computed 数据有效
        /// </summary>
        private void EnsureComputedData(string projectPath)
        {
            if (_computedDataService.ValidateComputedData(projectPath))
            {
                _logger.LogDebug("computed 数据有效，跳过生成");
                return;
            }

            _computedDataService.GenerateComputedData(projectPath);
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
    }
}
