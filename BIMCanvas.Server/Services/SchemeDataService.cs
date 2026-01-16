using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Dtos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 方案模块数据服务 - 支持跨分支/Worktree 的模块数据读写
    /// </summary>
    public class SchemeDataService
    {
        private readonly ILogger<SchemeDataService> _logger;
        private readonly ProjectContext _projectContext;
        private readonly GitWorktreeService _gitService;
        private readonly JsonSerializerSettings _jsonSettings;

        public SchemeDataService(
            ILogger<SchemeDataService> logger,
            ProjectContext projectContext,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _gitService = gitService;

            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                Converters = { new Polygon2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 解析 source 参数为文件系统路径
        /// </summary>
        /// <param name="source">数据源标识：main, worktree:{name}, branch:{name}</param>
        /// <returns>文件系统路径</returns>
        public string ResolveSourcePath(string source)
        {
            if (string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                throw new InvalidOperationException("未加载项目");
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (source == "main" || string.IsNullOrEmpty(source))
            {
                return projectPath;
            }

            if (source.StartsWith("worktree:"))
            {
                var worktreeName = source.Substring("worktree:".Length);
                var worktreePath = Path.Combine(
                    _gitService.GetWorktreesDir(projectPath),
                    worktreeName);

                if (!Directory.Exists(worktreePath))
                {
                    throw new InvalidOperationException($"Worktree 不存在: {worktreeName}");
                }

                return worktreePath;
            }

            if (source.StartsWith("branch:"))
            {
                // 分支模式需要临时 checkout，性能较低，暂不支持
                throw new NotSupportedException("branch: 模式暂不支持，请使用 worktree: 模式");
            }

            throw new ArgumentException($"无法识别的 source 格式: {source}");
        }

        /// <summary>
        /// 获取 source 对应的分支名
        /// </summary>
        public string? GetBranchName(string source)
        {
            if (string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                return null;
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (source == "main" || string.IsNullOrEmpty(source))
            {
                return _gitService.GetCurrentBranch(projectPath);
            }

            if (source.StartsWith("worktree:"))
            {
                var worktreeName = source.Substring("worktree:".Length);
                var worktrees = _gitService.GetWorktrees(projectPath);
                var worktree = worktrees.FirstOrDefault(w =>
                    w.Path.EndsWith(worktreeName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(w.Path) == worktreeName);
                return worktree?.Branch;
            }

            if (source.StartsWith("branch:"))
            {
                return source.Substring("branch:".Length);
            }

            return null;
        }

        /// <summary>
        /// 读取所有分区的模块数据
        /// </summary>
        /// <param name="basePath">项目基础路径</param>
        /// <returns>合并后的模块列表</returns>
        public List<Module> LoadAllModules(string basePath)
        {
            var modules = new List<Module>();
            var schemesPath = Path.Combine(basePath, "schemes");

            if (!Directory.Exists(schemesPath))
            {
                _logger.LogDebug("schemes 目录不存在: {Path}", schemesPath);
                return modules;
            }

            // 遍历所有分区子目录
            foreach (var zoneDir in Directory.GetDirectories(schemesPath))
            {
                var modulesFile = Path.Combine(zoneDir, "modules.json");
                if (File.Exists(modulesFile))
                {
                    try
                    {
                        var json = File.ReadAllText(modulesFile);
                        var zoneModules = JsonConvert.DeserializeObject<List<Module>>(json, _jsonSettings);
                        if (zoneModules != null)
                        {
                            modules.AddRange(zoneModules);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "读取模块文件失败: {Path}", modulesFile);
                    }
                }
            }

            _logger.LogDebug("从 {Path} 加载了 {Count} 个模块", schemesPath, modules.Count);
            return modules;
        }

        /// <summary>
        /// 按分区保存模块数据
        /// </summary>
        /// <param name="basePath">项目基础路径</param>
        /// <param name="modules">要保存的模块列表</param>
        /// <returns>保存的模块数量</returns>
        public int SaveAllModules(string basePath, List<Module> modules)
        {
            var schemesPath = Path.Combine(basePath, "schemes");

            // 确保 schemes 目录存在
            if (!Directory.Exists(schemesPath))
            {
                Directory.CreateDirectory(schemesPath);
            }

            // 按 ZoneId 分组
            var modulesByZone = modules
                .GroupBy(m => m.ZoneId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var savedCount = 0;

            // 清空现有分区目录中的 modules.json（避免残留数据）
            foreach (var zoneDir in Directory.GetDirectories(schemesPath))
            {
                var modulesFile = Path.Combine(zoneDir, "modules.json");
                var zoneName = Path.GetFileName(zoneDir);

                if (modulesByZone.ContainsKey(zoneName))
                {
                    // 写入新数据
                    var zoneModules = modulesByZone[zoneName];
                    var json = JsonConvert.SerializeObject(zoneModules, _jsonSettings);
                    File.WriteAllText(modulesFile, json);
                    savedCount += zoneModules.Count;
                    modulesByZone.Remove(zoneName);
                }
                else if (File.Exists(modulesFile))
                {
                    // 删除不再有模块的分区文件
                    File.Delete(modulesFile);
                }
            }

            // 处理新增的分区
            foreach (var kvp in modulesByZone)
            {
                var zoneDir = Path.Combine(schemesPath, kvp.Key);
                if (!Directory.Exists(zoneDir))
                {
                    Directory.CreateDirectory(zoneDir);
                }

                var modulesFile = Path.Combine(zoneDir, "modules.json");
                var json = JsonConvert.SerializeObject(kvp.Value, _jsonSettings);
                File.WriteAllText(modulesFile, json);
                savedCount += kvp.Value.Count;
            }

            _logger.LogInformation("保存了 {Count} 个模块到 {Path}", savedCount, schemesPath);
            return savedCount;
        }

        /// <summary>
        /// 获取模块数据（完整响应）
        /// </summary>
        public SchemeModulesResponse GetModules(string source)
        {
            var basePath = ResolveSourcePath(source);
            var modules = LoadAllModules(basePath);
            var branch = GetBranchName(source);

            return new SchemeModulesResponse
            {
                Source = source,
                Branch = branch,
                Modules = modules
            };
        }

        /// <summary>
        /// 保存模块数据
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="request">保存请求</param>
        /// <returns>保存结果</returns>
        public SaveSchemeModulesResponse SaveModules(string source, SaveSchemeModulesRequest request)
        {
            var basePath = ResolveSourcePath(source);
            var savedCount = SaveAllModules(basePath, request.Modules);

            CommitInfo? commitInfo = null;

            // 如果提供了 CommitMessage，自动提交
            if (!string.IsNullOrEmpty(request.CommitMessage))
            {
                try
                {
                    var committed = _gitService.TryCommit(basePath, request.CommitMessage);
                    if (committed)
                    {
                        commitInfo = _gitService.GetBranchCommit(
                            basePath,
                            _gitService.GetCurrentBranch(basePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "自动提交失败");
                }
            }

            return new SaveSchemeModulesResponse
            {
                Success = true,
                SavedCount = savedCount,
                Commit = commitInfo
            };
        }
    }
}
