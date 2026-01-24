using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BIMCanvas.Server.Models;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Worktree 元数据管理服务
    /// 负责读写 {projectPath}\.worktrees\worktrees.json 文件
    /// 用于精准判断删除 worktree 时是否应同时删除分支
    /// </summary>
    public class WorktreeMetadataService
    {
        private readonly string _projectPath;
        private readonly string _metadataFile;
        private readonly ILogger<WorktreeMetadataService>? _logger;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="projectPath">项目根路径</param>
        /// <param name="logger">可选的日志记录器</param>
        public WorktreeMetadataService(string projectPath, ILogger<WorktreeMetadataService>? logger = null)
        {
            _projectPath = projectPath;
            _metadataFile = Path.Combine(projectPath, ".worktrees", "worktrees.json");
            _logger = logger;
        }

        /// <summary>
        /// 读取元数据文件
        /// </summary>
        /// <returns>元数据对象（文件不存在时返回空对象）</returns>
        public WorktreeMetadata Load()
        {
            if (!File.Exists(_metadataFile))
            {
                _logger?.LogDebug("[WorktreeMetadata] 元数据文件不存在: {Path}", _metadataFile);
                return new WorktreeMetadata();
            }

            try
            {
                var json = File.ReadAllText(_metadataFile);
                var metadata = JsonSerializer.Deserialize<WorktreeMetadata>(json);

                if (metadata == null)
                {
                    _logger?.LogWarning("[WorktreeMetadata] 反序列化失败，返回空对象");
                    return new WorktreeMetadata();
                }

                _logger?.LogDebug("[WorktreeMetadata] 加载成功，共 {Count} 条记录", metadata.Worktrees.Count);
                return metadata;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[WorktreeMetadata] 读取元数据文件失败");
                return new WorktreeMetadata();
            }
        }

        /// <summary>
        /// 保存元数据文件
        /// </summary>
        /// <param name="metadata">元数据对象</param>
        public void Save(WorktreeMetadata metadata)
        {
            try
            {
                var dir = Path.GetDirectoryName(_metadataFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(_metadataFile, json);
                _logger?.LogDebug("[WorktreeMetadata] 保存成功，共 {Count} 条记录", metadata.Worktrees.Count);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[WorktreeMetadata] 保存元数据文件失败");
                throw;
            }
        }

        /// <summary>
        /// 添加 worktree 记录
        /// </summary>
        /// <param name="name">Worktree 名称</param>
        /// <param name="branchName">Git 分支名称</param>
        /// <param name="intent">创建意图（"isolation" 或 "parallel"）</param>
        /// <param name="baseBranch">基准分支</param>
        /// <param name="createdBy">创建者（默认 "Agent"）</param>
        public void AddWorktree(string name, string branchName, string intent,
                                string baseBranch, string createdBy = "Agent")
        {
            var metadata = Load();

            // 检查是否已存在同名记录（避免重复）
            var existing = metadata.Worktrees.FirstOrDefault(w => w.Name == name);
            if (existing != null)
            {
                _logger?.LogWarning("[WorktreeMetadata] 已存在同名记录，将覆盖: {Name}", name);
                metadata.Worktrees.Remove(existing);
            }

            metadata.Worktrees.Add(new WorktreeEntry
            {
                Name = name,
                BranchName = branchName,
                Intent = intent,
                BaseBranch = baseBranch,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdBy
            });

            Save(metadata);
            _logger?.LogInformation("[WorktreeMetadata] 添加记录: {Name} -> {Branch} (intent: {Intent})",
                name, branchName, intent);
        }

        /// <summary>
        /// 移除 worktree 记录
        /// </summary>
        /// <param name="name">Worktree 名称</param>
        /// <returns>被移除的条目（未找到时返回 null）</returns>
        public WorktreeEntry? RemoveWorktree(string name)
        {
            var metadata = Load();
            var entry = metadata.Worktrees.FirstOrDefault(w => w.Name == name);

            if (entry != null)
            {
                metadata.Worktrees.Remove(entry);
                Save(metadata);
                _logger?.LogInformation("[WorktreeMetadata] 移除记录: {Name}", name);
            }
            else
            {
                _logger?.LogDebug("[WorktreeMetadata] 未找到记录: {Name}", name);
            }

            return entry;
        }

        /// <summary>
        /// 判断是否应删除分支
        /// </summary>
        /// <param name="worktreeName">Worktree 名称</param>
        /// <returns>true 表示应删除分支（隔离意图），false 表示保留分支（并行意图或未找到记录）</returns>
        public bool ShouldDeleteBranch(string worktreeName)
        {
            var metadata = Load();
            var entry = metadata.Worktrees.FirstOrDefault(w => w.Name == worktreeName);

            if (entry == null)
            {
                _logger?.LogWarning("[WorktreeMetadata] 未找到元数据记录，默认不删除分支: {Name}", worktreeName);
                return false;
            }

            var shouldDelete = entry.Intent == "isolation";
            _logger?.LogDebug("[WorktreeMetadata] 判断结果: {Name} -> {ShouldDelete} (intent: {Intent})",
                worktreeName, shouldDelete, entry.Intent);

            return shouldDelete;
        }

        /// <summary>
        /// 同步元数据与实际 worktree 列表
        /// 清理元数据中不存在的 worktree 记录
        /// </summary>
        /// <param name="actualWorktreeNames">实际存在的 worktree 名称集合</param>
        /// <returns>被清理的过期记录数量</returns>
        public int SyncWithActualWorktrees(IEnumerable<string> actualWorktreeNames)
        {
            var metadata = Load();
            var actualSet = actualWorktreeNames.ToHashSet();

            var staleEntries = metadata.Worktrees
                .Where(e => !actualSet.Contains(e.Name))
                .ToList();

            foreach (var entry in staleEntries)
            {
                metadata.Worktrees.Remove(entry);
                _logger?.LogInformation("[WorktreeMetadata] 清理过期元数据: {Name}", entry.Name);
            }

            if (staleEntries.Any())
            {
                Save(metadata);
            }

            return staleEntries.Count;
        }

        /// <summary>
        /// 获取指定 worktree 的元数据条目
        /// </summary>
        /// <param name="worktreeName">Worktree 名称</param>
        /// <returns>元数据条目（未找到时返回 null）</returns>
        public WorktreeEntry? GetWorktreeEntry(string worktreeName)
        {
            var metadata = Load();
            return metadata.Worktrees.FirstOrDefault(w => w.Name == worktreeName);
        }
    }
}
