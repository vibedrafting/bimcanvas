using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Core.Models.Shared;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 策略管理服务
    ///
    /// v3.2 架构简化：
    /// - 单仓库 + 多分支架构
    /// - schemes/ 目录直接存放策略文件（无子目录）
    /// - 策略切换通过 Git 分支实现
    /// - 并行任务通过 Git Worktree 实现
    ///
    /// 目录结构：
    /// project/
    /// ├── .git/                # 单一 Git 仓库
    /// ├── schemes/             # 策略文件（直接存放）
    /// │   ├── strategy.json
    /// │   ├── zones.json
    /// │   ├── finishes.json
    /// │   └── modules.json
    /// └── .worktrees/          # 并行任务的 Worktree
    ///
    /// 分支命名：
    /// - main: 用户当前接受的状态
    /// - scheme/{id}: 保存的设计方案
    /// - feat/ai-{jobId}: AI 临时工作分支
    /// </summary>
    public class StrategyService
    {
        private readonly ILogger<StrategyService> _logger;
        private readonly GitWorktreeService _gitService;
        private readonly JsonSerializerSettings _jsonSettings;

        public StrategyService(
            ILogger<StrategyService> logger,
            GitWorktreeService gitService)
        {
            _logger = logger;
            _gitService = gitService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented
            };
        }

        #region 策略目录管理

        /// <summary>
        /// 创建默认策略
        /// v3.2: 文件直接存放在 schemes/ 目录下
        /// </summary>
        /// <param name="schemesPath">schemes 目录路径</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        /// <returns>策略 ID</returns>
        public string CreateDefaultStrategy(string schemesPath, string baselineHash)
        {
            var strategyId = "default";

            _logger.LogInformation("创建默认策略: {Path}", schemesPath);

            // 确保目录存在
            if (!Directory.Exists(schemesPath))
            {
                Directory.CreateDirectory(schemesPath);
            }

            // 创建 strategy.json
            var strategy = new Strategy
            {
                Id = strategyId,
                Name = "Default",
                Approach = StrategyApproach.CirculationFirst,
                Description = "默认策略",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Origin = null,
                LastValidatedBaselineHash = baselineHash,
                Status = StrategyStatus.Valid
            };
            WriteJsonFile(Path.Combine(schemesPath, "strategy.json"), strategy);

            // 创建空的 zones.json
            WriteJsonFile(Path.Combine(schemesPath, "zones.json"), new List<object>());

            // 创建空的 finishes.json
            WriteJsonFile(Path.Combine(schemesPath, "finishes.json"), new List<object>());

            // 创建空的 modules.json
            WriteJsonFile(Path.Combine(schemesPath, "modules.json"), new List<object>());

            _logger.LogInformation("默认策略创建完成: {Id}", strategyId);
            return strategyId;
        }

        /// <summary>
        /// 检查策略是否存在
        /// </summary>
        public bool StrategyExists(string schemesPath, string strategyId)
        {
            var strategyJsonPath = Path.Combine(schemesPath, "strategy.json");
            if (!File.Exists(strategyJsonPath))
                return false;

            // 验证 strategy.json 中的 ID
            var json = File.ReadAllText(strategyJsonPath, Encoding.UTF8);
            var strategy = JsonConvert.DeserializeObject<Strategy>(json);
            return strategy?.Id == strategyId;
        }

        /// <summary>
        /// 获取所有策略 ID（从 Git 分支获取）
        /// </summary>
        public List<string> GetAllStrategyIds(string schemesPath)
        {
            var projectPath = Directory.GetParent(schemesPath)?.FullName;
            if (projectPath == null || !_gitService.IsGitRepository(projectPath))
            {
                // 回退到直接读取 strategy.json
                return GetCurrentStrategyId(schemesPath);
            }

            // 从分支获取
            var branches = _gitService.GetSchemeBranches(projectPath);
            var strategyIds = branches
                .Select(b => b.Replace(GitWorktreeService.SchemeBranchPrefix, ""))
                .ToList();

            // 添加当前活跃策略
            var strategyJsonPath = Path.Combine(schemesPath, "strategy.json");
            if (File.Exists(strategyJsonPath))
            {
                var json = File.ReadAllText(strategyJsonPath, Encoding.UTF8);
                var strategy = JsonConvert.DeserializeObject<Strategy>(json);
                if (strategy != null && !strategyIds.Contains(strategy.Id))
                {
                    strategyIds.Insert(0, strategy.Id);
                }
            }

            return strategyIds;
        }

        /// <summary>
        /// 获取当前策略 ID
        /// </summary>
        private List<string> GetCurrentStrategyId(string schemesPath)
        {
            var result = new List<string>();
            var strategyJsonPath = Path.Combine(schemesPath, "strategy.json");

            if (File.Exists(strategyJsonPath))
            {
                var json = File.ReadAllText(strategyJsonPath, Encoding.UTF8);
                var strategy = JsonConvert.DeserializeObject<Strategy>(json);
                if (strategy != null)
                {
                    result.Add(strategy.Id);
                }
            }

            return result;
        }

        #endregion

        #region Git 分支策略管理

        /// <summary>
        /// 保存当前策略到分支
        /// </summary>
        public void SaveStrategyToBranch(string projectPath, string strategyId, string? commitMessage = null)
        {
            if (!_gitService.IsGitRepository(projectPath))
            {
                _logger.LogWarning("项目未初始化 Git，跳过分支保存");
                return;
            }

            var branchName = $"{GitWorktreeService.SchemeBranchPrefix}{strategyId}";
            var message = commitMessage ?? $"Save strategy: {strategyId}";

            // 提交当前更改
            if (_gitService.HasUncommittedChanges(projectPath))
            {
                _gitService.Commit(projectPath, message);
            }

            // 创建/更新分支指向当前提交
            var branches = _gitService.GetAllBranches(projectPath);
            if (branches.Contains(branchName))
            {
                _gitService.DeleteBranch(projectPath, branchName, force: true);
            }
            _gitService.CreateBranch(projectPath, branchName);

            _logger.LogInformation("策略已保存到分支: {Branch}", branchName);
        }

        /// <summary>
        /// 从分支加载策略
        /// </summary>
        public void LoadStrategyFromBranch(string projectPath, string strategyId)
        {
            if (!_gitService.IsGitRepository(projectPath))
            {
                throw new InvalidOperationException("项目未初始化 Git");
            }

            var branchName = $"{GitWorktreeService.SchemeBranchPrefix}{strategyId}";
            var branches = _gitService.GetAllBranches(projectPath);

            if (!branches.Contains(branchName))
            {
                throw new InvalidOperationException($"策略分支不存在: {branchName}");
            }

            if (_gitService.HasUncommittedChanges(projectPath))
            {
                throw new InvalidOperationException("存在未提交的更改，请先保存当前策略");
            }

            _gitService.CheckoutBranch(projectPath, branchName);
            _logger.LogInformation("已加载策略: {Id} (分支: {Branch})", strategyId, branchName);
        }

        /// <summary>
        /// 创建新策略（基于当前状态创建分支）
        /// </summary>
        public string CreateStrategy(string projectPath, string name, StrategyApproach approach, string baselineHash)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");

            // 生成策略 ID
            var existingIds = GetAllStrategyIds(schemesPath);
            var nextNum = existingIds
                .Select(id =>
                {
                    var match = System.Text.RegularExpressions.Regex.Match(id, @"^s(\d+)");
                    return match.Success ? int.Parse(match.Groups[1].Value) : 0;
                })
                .DefaultIfEmpty(0)
                .Max() + 1;

            var strategyId = $"s{nextNum}_{SanitizeName(name)}";

            _logger.LogInformation("创建策略: {Id} ({Name})", strategyId, name);

            // 更新 schemes/ 目录中的 strategy.json
            var strategy = new Strategy
            {
                Id = strategyId,
                Name = name,
                Approach = approach,
                Description = null,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Origin = null,
                LastValidatedBaselineHash = baselineHash,
                Status = StrategyStatus.Valid
            };
            WriteJsonFile(Path.Combine(schemesPath, "strategy.json"), strategy);

            // 如果有 Git，保存到分支
            if (_gitService.IsGitRepository(projectPath))
            {
                SaveStrategyToBranch(projectPath, strategyId,
                    $"Create strategy: {name} ({approach})");
            }

            _logger.LogInformation("策略创建完成: {Id}", strategyId);
            return strategyId;
        }

        /// <summary>
        /// 删除策略（删除分支）
        /// </summary>
        public void DeleteStrategy(string projectPath, string strategyId)
        {
            if (!_gitService.IsGitRepository(projectPath))
            {
                _logger.LogWarning("项目未初始化 Git，无法删除策略分支");
                return;
            }

            var branchName = $"{GitWorktreeService.SchemeBranchPrefix}{strategyId}";
            var branches = _gitService.GetAllBranches(projectPath);

            if (branches.Contains(branchName))
            {
                _gitService.DeleteBranch(projectPath, branchName, force: true);
                _logger.LogInformation("已删除策略: {Id}", strategyId);
            }
        }

        #endregion

        #region 并行策略生成

        /// <summary>
        /// 创建并行策略分叉（场景 A：策略分叉）
        /// </summary>
        public Dictionary<string, string> CreateParallelStrategies(
            string projectPath,
            List<ParallelStrategyRequest> strategies)
        {
            if (!_gitService.IsGitRepository(projectPath))
            {
                throw new InvalidOperationException("项目未初始化 Git，无法创建并行策略");
            }

            var result = new Dictionary<string, string>();

            // 先提交当前状态
            if (_gitService.HasUncommittedChanges(projectPath))
            {
                _gitService.Commit(projectPath, "Save state before parallel generation");
            }

            foreach (var strategyReq in strategies)
            {
                var jobId = strategyReq.JobId ?? SanitizeName(strategyReq.Name);
                var worktreePath = _gitService.CreateAiJobWorktree(
                    projectPath, jobId, SanitizeName(strategyReq.Name));

                // 在 worktree 中更新策略配置
                var schemesPath = Path.Combine(worktreePath, "schemes");
                var strategyJsonPath = Path.Combine(schemesPath, "strategy.json");

                if (File.Exists(strategyJsonPath))
                {
                    var json = File.ReadAllText(strategyJsonPath, Encoding.UTF8);
                    var strategy = JsonConvert.DeserializeObject<Strategy>(json);
                    if (strategy != null)
                    {
                        strategy.Name = strategyReq.Name;
                        strategy.Approach = strategyReq.Approach;
                        strategy.Description = strategyReq.Description;
                        strategy.UpdatedAt = DateTime.Now;
                        WriteJsonFile(strategyJsonPath, strategy);
                    }
                }

                result[jobId] = worktreePath;
                _logger.LogInformation("创建并行策略: {Name} -> {Path}", strategyReq.Name, worktreePath);
            }

            return result;
        }

        /// <summary>
        /// 接受并行策略结果
        /// </summary>
        public MergeResult AcceptParallelStrategy(string projectPath, string jobId)
        {
            return _gitService.AcceptAiJob(projectPath, jobId);
        }

        /// <summary>
        /// 清理所有并行策略
        /// </summary>
        public void CleanupParallelStrategies(string projectPath)
        {
            _gitService.CleanupAiWorktrees(projectPath);
        }

        #endregion

        #region 私有方法

        private void WriteJsonFile(string path, object data)
        {
            var json = JsonConvert.SerializeObject(data, _jsonSettings);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var result = new StringBuilder();
            foreach (var c in name)
            {
                if (Array.IndexOf(invalid, c) >= 0 || c == ' ')
                    result.Append('_');
                else
                    result.Append(c);
            }
            return result.ToString();
        }

        #endregion
    }

    #region 请求类型

    /// <summary>
    /// 并行策略请求
    /// </summary>
    public class ParallelStrategyRequest
    {
        public string? JobId { get; set; }
        public string Name { get; set; } = string.Empty;
        public StrategyApproach Approach { get; set; }
        public string? Description { get; set; }
        public Dictionary<string, object>? Parameters { get; set; }
    }

    #endregion
}
