using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 合并服务 - 计算分区级差异和执行选择性合并
    /// </summary>
    public class MergeService
    {
        private readonly ILogger<MergeService> _logger;
        private readonly GitWorktreeService _gitService;

        public MergeService(
            ILogger<MergeService> logger,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _gitService = gitService;
        }

        /// <summary>
        /// 计算两个分支之间的分区级差异
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="sourceBranch">源分支</param>
        /// <param name="targetBranch">目标分支（默认当前分支）</param>
        /// <returns>分区差异列表</returns>
        public async Task<List<ZoneDiff>> ComputeZoneDiffsAsync(
            string projectPath,
            string sourceBranch,
            string? targetBranch = null)
        {
            var diffs = new List<ZoneDiff>();

            try
            {
                // 获取目标分支（默认当前分支）
                var target = targetBranch ?? _gitService.GetCurrentBranch(projectPath);

                _logger.LogInformation("计算分区差异: {Source} -> {Target}", sourceBranch, target);

                // 获取 schemes 目录下的所有分区
                var schemesPath = Path.Combine(projectPath, "schemes");
                if (!Directory.Exists(schemesPath))
                {
                    _logger.LogWarning("schemes 目录不存在: {Path}", schemesPath);
                    return diffs;
                }

                // 查找所有分区目录
                var zoneDirs = Directory.GetDirectories(schemesPath)
                    .Where(d =>
                    {
                        var name = Path.GetFileName(d);
                        return name.StartsWith("rz_") || name.StartsWith("dz_");
                    })
                    .Select(d => Path.GetFileName(d))
                    .ToList();

                foreach (var zoneId in zoneDirs)
                {
                    var diff = await ComputeSingleZoneDiffAsync(
                        projectPath, zoneId, sourceBranch, target);
                    if (diff != null && diff.HasChanges)
                    {
                        diffs.Add(diff);
                    }
                }

                _logger.LogInformation("发现 {Count} 个分区有差异", diffs.Count);
                return diffs;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算分区差异失败");
                throw;
            }
        }

        /// <summary>
        /// 计算单个分区的差异
        /// </summary>
        private async Task<ZoneDiff?> ComputeSingleZoneDiffAsync(
            string projectPath,
            string zoneId,
            string sourceBranch,
            string targetBranch)
        {
            var modulesPath = $"schemes/{zoneId}/modules.json";

            try
            {
                // 使用 git show 获取两个分支的文件内容
                var sourceContent = GetFileContentFromBranch(projectPath, sourceBranch, modulesPath);
                var targetContent = GetFileContentFromBranch(projectPath, targetBranch, modulesPath);

                // 解析 JSON
                var sourceModules = ParseModules(sourceContent);
                var targetModules = ParseModules(targetContent);

                // 计算差异
                var diff = new ZoneDiff
                {
                    ZoneId = zoneId,
                    SourceBranch = sourceBranch,
                    TargetBranch = targetBranch
                };

                // 查找新增的模块
                foreach (var module in sourceModules)
                {
                    var existing = targetModules.FirstOrDefault(m => m.Id == module.Id);
                    if (existing == null)
                    {
                        diff.AddedModules.Add(module);
                    }
                    else if (!ModulesEqual(module, existing))
                    {
                        diff.ModifiedModules.Add(new ModuleChange
                        {
                            OldModule = existing,
                            NewModule = module
                        });
                    }
                }

                // 查找删除的模块
                foreach (var module in targetModules)
                {
                    if (!sourceModules.Any(m => m.Id == module.Id))
                    {
                        diff.RemovedModules.Add(module);
                    }
                }

                return diff;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "计算分区 {ZoneId} 差异失败", zoneId);
                return null;
            }
        }

        /// <summary>
        /// 从指定分支获取文件内容
        /// </summary>
        private string? GetFileContentFromBranch(string projectPath, string branch, string filePath)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"show {branch}:{filePath}",
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                return process.ExitCode == 0 ? output : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 解析模块 JSON
        /// </summary>
        private List<Module> ParseModules(string? json)
        {
            if (string.IsNullOrEmpty(json))
                return new List<Module>();

            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                return JsonSerializer.Deserialize<List<Module>>(json, options) ?? new List<Module>();
            }
            catch
            {
                return new List<Module>();
            }
        }

        /// <summary>
        /// 比较两个模块是否相等
        /// </summary>
        private bool ModulesEqual(Module a, Module b)
        {
            // 简单比较关键属性
            return a.Id == b.Id &&
                   a.ModuleId == b.ModuleId &&
                   a.ZoneId == b.ZoneId &&
                   BoundsEqual(a.Bounds, b.Bounds);
        }

        /// <summary>
        /// 比较边界是否相等
        /// </summary>
        private bool BoundsEqual(Polygon2D? a, Polygon2D? b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Vertices == null && b.Vertices == null) return true;
            if (a.Vertices == null || b.Vertices == null) return false;
            if (a.Vertices.Length != b.Vertices.Length) return false;

            for (int i = 0; i < a.Vertices.Length; i++)
            {
                if (Math.Abs(a.Vertices[i].X - b.Vertices[i].X) > 0.001) return false;
                if (Math.Abs(a.Vertices[i].Y - b.Vertices[i].Y) > 0.001) return false;
            }
            return true;
        }

        /// <summary>
        /// 执行选择性合并
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="sourceBranch">源分支</param>
        /// <param name="selectedZones">要合并的分区 ID 列表</param>
        /// <returns>合并结果</returns>
        public async Task<SelectiveMergeResult> ExecuteSelectiveMergeAsync(
            string projectPath,
            string sourceBranch,
            List<string> selectedZones)
        {
            var result = new SelectiveMergeResult();

            try
            {
                _logger.LogInformation("执行选择性合并: {Source} -> 当前分支, 分区: {Zones}",
                    sourceBranch, string.Join(", ", selectedZones));

                foreach (var zoneId in selectedZones)
                {
                    try
                    {
                        // 从源分支获取分区数据
                        var modulesPath = $"schemes/{zoneId}/modules.json";
                        var sourceContent = GetFileContentFromBranch(projectPath, sourceBranch, modulesPath);

                        if (string.IsNullOrEmpty(sourceContent))
                        {
                            result.FailedZones.Add(zoneId);
                            continue;
                        }

                        // 写入到当前分支
                        var targetPath = Path.Combine(projectPath, modulesPath);
                        var targetDir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }

                        await File.WriteAllTextAsync(targetPath, sourceContent);
                        result.MergedZones.Add(zoneId);

                        _logger.LogDebug("分区 {ZoneId} 合并成功", zoneId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "分区 {ZoneId} 合并失败", zoneId);
                        result.FailedZones.Add(zoneId);
                    }
                }

                // 提交更改
                if (result.MergedZones.Count > 0)
                {
                    _gitService.Commit(projectPath,
                        $"Selective merge from {sourceBranch}: {string.Join(", ", result.MergedZones)}");
                }

                result.Success = result.FailedZones.Count == 0;
                _logger.LogInformation("选择性合并完成: 成功 {Success}/{Total}",
                    result.MergedZones.Count, selectedZones.Count);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "选择性合并失败");
                result.Success = false;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }
    }

    #region DTOs

    /// <summary>
    /// 分区差异
    /// </summary>
    public class ZoneDiff
    {
        /// <summary>
        /// 分区 ID
        /// </summary>
        public string ZoneId { get; set; } = string.Empty;

        /// <summary>
        /// 源分支
        /// </summary>
        public string SourceBranch { get; set; } = string.Empty;

        /// <summary>
        /// 目标分支
        /// </summary>
        public string TargetBranch { get; set; } = string.Empty;

        /// <summary>
        /// 新增的模块
        /// </summary>
        public List<Module> AddedModules { get; set; } = new();

        /// <summary>
        /// 删除的模块
        /// </summary>
        public List<Module> RemovedModules { get; set; } = new();

        /// <summary>
        /// 修改的模块
        /// </summary>
        public List<ModuleChange> ModifiedModules { get; set; } = new();

        /// <summary>
        /// 是否有变化
        /// </summary>
        public bool HasChanges =>
            AddedModules.Count > 0 ||
            RemovedModules.Count > 0 ||
            ModifiedModules.Count > 0;

        /// <summary>
        /// 变化统计
        /// </summary>
        public string Summary =>
            $"+{AddedModules.Count} -{RemovedModules.Count} ~{ModifiedModules.Count}";
    }

    /// <summary>
    /// 模块变化
    /// </summary>
    public class ModuleChange
    {
        /// <summary>
        /// 旧模块
        /// </summary>
        public Module? OldModule { get; set; }

        /// <summary>
        /// 新模块
        /// </summary>
        public Module? NewModule { get; set; }
    }

    /// <summary>
    /// 选择性合并结果
    /// </summary>
    public class SelectiveMergeResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 成功合并的分区
        /// </summary>
        public List<string> MergedZones { get; set; } = new();

        /// <summary>
        /// 合并失败的分区
        /// </summary>
        public List<string> FailedZones { get; set; } = new();

        /// <summary>
        /// 错误消息
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    #endregion
}
