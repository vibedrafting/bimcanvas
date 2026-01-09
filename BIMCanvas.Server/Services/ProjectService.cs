using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
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

            // 3. 创建 context/ 目录
            CreateContextDirectory(projectPath);

            // 4. 创建 schemes/ 和默认策略
            var defaultStrategyId = EnsureSchemesDirectory(projectPath, baselineHash);

            // 5. 更新 project.json
            UpdateProjectJson(projectPath, defaultStrategyId);

            // 6. 验证并生成 computed 数据
            EnsureComputedData(projectPath);

            // 7. 初始化 Git 仓库（v3.1 新增：单仓库 + 多分支架构）
            InitializeGitRepository(projectPath);

            _logger.LogInformation("项目加载完成: {Path}", projectPath);
            return projectPath;
        }

        /// <summary>
        /// 初始化项目 Git 仓库
        /// v3.1 架构：单仓库 + 多分支，支持 Worktree 并行任务
        /// </summary>
        private void InitializeGitRepository(string projectPath)
        {
            try
            {
                // 创建 .gitignore
                var gitignorePath = Path.Combine(projectPath, ".gitignore");
                if (!File.Exists(gitignorePath))
                {
                    var gitignoreContent = @"# BIMCanvas Project .gitignore

# Worktree 临时目录
.worktrees/

# 系统文件
.DS_Store
Thumbs.db

# IDE / 开发工具
.idea/
.vscode/
.vs/
*.suo
*.user
*.sln
*.csproj
";
                    File.WriteAllText(gitignorePath, gitignoreContent, Encoding.UTF8);
                    _logger.LogDebug("创建 .gitignore");
                }

                // 初始化 Git 仓库
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
        /// 创建 context/ 目录和 requirements.md
        /// </summary>
        private void CreateContextDirectory(string projectPath)
        {
            var contextPath = Path.Combine(projectPath, "context");

            if (Directory.Exists(contextPath))
            {
                _logger.LogDebug("context/ 目录已存在，跳过创建");
                return;
            }

            Directory.CreateDirectory(contextPath);
            _logger.LogInformation("创建 context/ 目录");

            // 创建 requirements.md 模板
            var requirementsPath = Path.Combine(contextPath, "requirements.md");
            var content = @"# 设计需求

## 项目概述

（在此描述项目的基本情况）

## 功能需求

-

## 风格偏好

-

## 特殊要求

-

";
            File.WriteAllText(requirementsPath, content, Encoding.UTF8);
            _logger.LogInformation("创建 requirements.md 模板");
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
            // 向上查找 BIMCanvas.Web/demos 目录
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                var webPath = Path.Combine(dir.FullName, "BIMCanvas.Web", "demos");
                var bcpPath = Path.Combine(webPath, $"{demoName}.bcp");

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
