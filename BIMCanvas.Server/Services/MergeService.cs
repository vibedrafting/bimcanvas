using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 合并服务 - 计算分区级差异和执行选择性合并
    /// </summary>
    public class MergeService
    {
        private readonly ILogger<MergeService> _logger;
        private readonly GitWorktreeService _gitService;
        private readonly IWorktreeMetadataServiceFactory _metadataServiceFactory;

        public MergeService(
            ILogger<MergeService> logger,
            GitWorktreeService gitService,
            IWorktreeMetadataServiceFactory metadataServiceFactory)
        {
            _logger = logger;
            _gitService = gitService;
            _metadataServiceFactory = metadataServiceFactory;
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

                // 从源分支和目标分支读取分区列表（使用 git ls-tree）
                var sourceZones = GetZonesFromBranch(projectPath, sourceBranch);
                var targetZones = GetZonesFromBranch(projectPath, target);

                // 合并两个分支的分区列表（取并集）
                var zoneDirs = sourceZones.Union(targetZones).Distinct().ToList();

                _logger.LogInformation(
                    "找到 {Count} 个分区待比较（源分支: {SourceCount}, 目标分支: {TargetCount}）",
                    zoneDirs.Count, sourceZones.Count, targetZones.Count);

                if (zoneDirs.Count == 0)
                {
                    _logger.LogWarning("两个分支都没有分区数据");
                    return diffs;
                }

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
                return JsonConvert.DeserializeObject<List<Module>>(json) ?? new List<Module>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ParseModules] JSON反序列化失败,内容: {Json}", json);
                return new List<Module>();
            }
        }

        /// <summary>
        /// 比较两个模块是否相等
        /// </summary>
        private bool ModulesEqual(Module a, Module b)
        {
            // 简单比较关键属性（v3.4: 移除 ZoneId 比较，分区由 Server 自动计算）
            return a.Id == b.Id &&
                   a.ModuleId == b.ModuleId &&
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
        /// 执行覆盖合并（MVP v0.1）
        /// 将源分支的 schemes/ 数据完全覆盖到目标分支
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="sourceBranch">源分支</param>
        /// <param name="targetBranch">目标分支</param>
        /// <param name="branchesToCleanup">用户勾选要清理的分支列表（可选）</param>
        /// <returns>合并结果</returns>
        public async Task<OverwriteMergeResult> ExecuteOverwriteMergeAsync(
            string projectPath,
            string sourceBranch,
            string targetBranch,
            List<string>? branchesToCleanup = null)
        {
            try
            {
                _logger.LogInformation("执行覆盖合并: {Source} -> {Target}", sourceBranch, targetBranch);

                // 1. 验证分支存在
                var allBranches = _gitService.GetAllBranches(projectPath);
                if (!allBranches.Contains(sourceBranch))
                {
                    return new OverwriteMergeResult
                    {
                        Success = false,
                        Message = $"源分支 '{sourceBranch}' 不存在"
                    };
                }
                if (!allBranches.Contains(targetBranch))
                {
                    return new OverwriteMergeResult
                    {
                        Success = false,
                        Message = $"目标分支 '{targetBranch}' 不存在"
                    };
                }

                // 2. 如果目标分支不是当前分支，先切换到目标分支
                var currentBranch = _gitService.GetCurrentBranch(projectPath);
                if (currentBranch != targetBranch)
                {
                    _logger.LogInformation("切换到目标分支: {Target}", targetBranch);
                    _gitService.CheckoutBranch(projectPath, targetBranch);
                }

                // 2.5 自动存档未提交的改动(合并前存档)
                await AutoCommitUncommittedChangesAsync(projectPath, sourceBranch, targetBranch);

                // 3. 检测差异，如果两个分支内容相同则无需合并
                var diffs = await ComputeZoneDiffsAsync(projectPath, sourceBranch, targetBranch);
                if (diffs.Count == 0)
                {
                    _logger.LogInformation("两个分支内容相同，无需合并: {Source} -> {Target}", sourceBranch, targetBranch);
                    return new OverwriteMergeResult
                    {
                        Success = true,
                        MergedZoneCount = 0,
                        Message = "两个分支内容相同，无需合并"
                    };
                }

                // 3. 获取源分支的 schemes 目录内容
                var schemesPath = "schemes";
                var schemesDir = Path.Combine(projectPath, schemesPath);

                // 获取源分支的所有分区目录
                var sourceZones = GetZonesFromBranch(projectPath, sourceBranch);

                if (sourceZones.Count == 0)
                {
                    return new OverwriteMergeResult
                    {
                        Success = false,
                        Message = "源分支没有方案数据"
                    };
                }

                // 4. 清空目标分支的 schemes 目录（保留目录结构）
                if (Directory.Exists(schemesDir))
                {
                    foreach (var dir in Directory.GetDirectories(schemesDir))
                    {
                        var dirName = Path.GetFileName(dir);
                        if (dirName.StartsWith("rz_") || dirName.StartsWith("dz_"))
                        {
                            Directory.Delete(dir, recursive: true);
                        }
                    }
                }
                else
                {
                    Directory.CreateDirectory(schemesDir);
                }

                // 5. 从源分支复制所有分区数据
                foreach (var zoneId in sourceZones)
                {
                    await CopyZoneFromBranchAsync(projectPath, sourceBranch, zoneId);
                }

                // 6. 提交更改
                _gitService.Commit(projectPath,
                    $"Overwrite merge from {sourceBranch}: {sourceZones.Count} zones");

                _logger.LogInformation("覆盖合并完成: {Count} 个分区", sourceZones.Count);

                // 7. ✅ 合并成功后自动清理被合并的 worktree
                await CleanupMergedWorktreeAsync(projectPath, sourceBranch, branchesToCleanup);

                return new OverwriteMergeResult
                {
                    Success = true,
                    MergedZoneCount = sourceZones.Count,
                    Message = $"成功合并 {sourceZones.Count} 个分区"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "覆盖合并失败");
                return new OverwriteMergeResult
                {
                    Success = false,
                    Message = $"合并失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取分支中的所有分区 ID
        /// </summary>
        private List<string> GetZonesFromBranch(string projectPath, string branch)
        {
            var zones = new List<string>();

            try
            {
                // 使用 git ls-tree 获取分支中的 schemes 目录结构
                var arguments = $"ls-tree --name-only {branch}:schemes";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = projectPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                if (process == null)
                {
                    return zones;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(5000);

                if (process.ExitCode == 0)
                {
                    zones = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Where(name => name.StartsWith("rz_") || name.StartsWith("dz_"))
                        .Select(name => name.Trim())
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "获取分支 {Branch} 的分区列表失败", branch);
            }

            return zones;
        }

        /// <summary>
        /// 从源分支复制分区数据到当前工作目录
        /// </summary>
        private async Task CopyZoneFromBranchAsync(string projectPath, string sourceBranch, string zoneId)
        {
            var zonePath = $"schemes/{zoneId}";
            var targetZoneDir = Path.Combine(projectPath, zonePath);

            // 创建目标目录
            if (!Directory.Exists(targetZoneDir))
            {
                Directory.CreateDirectory(targetZoneDir);
            }

            // 获取分区下的所有文件
            var files = new[] { "modules.json", "zones.json", "finishes.json" };

            foreach (var fileName in files)
            {
                var filePath = $"{zonePath}/{fileName}";
                var content = GetFileContentFromBranch(projectPath, sourceBranch, filePath);

                if (!string.IsNullOrEmpty(content))
                {
                    var targetFilePath = Path.Combine(targetZoneDir, fileName);
                    await File.WriteAllTextAsync(targetFilePath, content);
                }
            }
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

        /// <summary>
        /// 自动提交未提交的改动(合并前存档)
        /// </summary>
        private async Task AutoCommitUncommittedChangesAsync(
            string projectPath,
            string sourceBranch,
            string targetBranch)
        {
            // 1. 检查源分支(通常是 worktree 临时分支)
            var (sourceOccupied, sourceWorktreePath) = _gitService.IsBranchOccupiedByWorktree(projectPath, sourceBranch);
            if (sourceOccupied && sourceWorktreePath != null)
            {
                if (_gitService.HasUncommittedChanges(sourceWorktreePath))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var message = $"Merge前{sourceBranch}的存档_{timestamp}";

                    _logger.LogInformation("源分支有未提交改动,自动存档: {Branch}", sourceBranch);
                    var committed = _gitService.TryCommit(sourceWorktreePath, message);
                    if (committed)
                    {
                        _logger.LogInformation("源分支自动存档成功: {Message}", message);
                    }
                    else
                    {
                        _logger.LogWarning("源分支自动存档失败: {Branch}", sourceBranch);
                    }
                }
            }

            // 2. 检查目标分支(通常是 master)
            var currentBranch = _gitService.GetCurrentBranch(projectPath);
            if (currentBranch == targetBranch)
            {
                if (_gitService.HasUncommittedChanges(projectPath))
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var message = $"Merge前{targetBranch}的存档_{timestamp}";

                    _logger.LogInformation("目标分支有未提交改动,自动存档: {Branch}", targetBranch);
                    var committed = _gitService.TryCommit(projectPath, message);
                    if (committed)
                    {
                        _logger.LogInformation("目标分支自动存档成功: {Message}", message);
                    }
                    else
                    {
                        _logger.LogWarning("目标分支自动存档失败: {Branch}", targetBranch);
                    }
                }
            }

            await Task.CompletedTask; // 满足 async 签名
        }

        /// <summary>
        /// 清理已合并的 Worktree（私有方法，被 ExecuteOverwriteMergeAsync 调用）
        /// </summary>
        private async Task CleanupMergedWorktreeAsync(
            string projectPath,
            string sourceBranch,
            List<string>? branchesToCleanup)
        {
            try
            {
                var metadataService = _metadataServiceFactory.Create(projectPath);
                var metadata = metadataService.Load();
                var entry = metadata.Worktrees.FirstOrDefault(e => e.BranchName == sourceBranch);

                if (entry != null)
                {
                    // ✅ 基于分支命名规则 + 用户勾选
                    bool shouldDeleteBranch = ShouldDeleteBranchByNamingRule(entry.BranchName)
                        && branchesToCleanup != null
                        && branchesToCleanup.Contains(entry.BranchName);

                    _logger.LogInformation("清理 worktree: {Name}, 删除分支={Delete}", entry.Name, shouldDeleteBranch);
                    _gitService.RemoveWorktree(projectPath, entry.Name, deleteBranch: shouldDeleteBranch);
                }
                else
                {
                    _logger.LogWarning("无法通过元数据找到 worktree (SourceBranch={SourceBranch})", sourceBranch);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理 worktree 失败 (SourceBranch={SourceBranch})", sourceBranch);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// 基于分支命名规则判断是否应删除分支
        /// </summary>
        /// <param name="branchName">分支名</param>
        /// <returns>true 表示是临时分支，应删除</returns>
        private bool ShouldDeleteBranchByNamingRule(string branchName)
        {
            // ✅ 临时分支前缀：temp/、feat/ai-、isolation/
            return branchName.StartsWith("temp/")
                || branchName.StartsWith("feat/ai-")
                || branchName.StartsWith("isolation/");
        }
    }

    #region DTOs

    /// <summary>
    /// 覆盖合并结果（MVP v0.1）
    /// </summary>
    public class OverwriteMergeResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 合并的分区数量
        /// </summary>
        public int MergedZoneCount { get; set; }

        /// <summary>
        /// 消息（成功或错误信息）
        /// </summary>
        public string? Message { get; set; }
    }

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
