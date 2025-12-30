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
    /// v3.1 架构升级：
    /// - 从"每个策略独立 Git 仓库"改为"单仓库 + 多分支"
    /// - 策略通过 Git 分支管理（scheme/{id}）
    /// - 并行任务通过 Git Worktree 实现
    ///
    /// 目录结构：
    /// project/
    /// ├── .git/                # 单一 Git 仓库
    /// ├── schemes/
    /// │   └── active/          # 当前激活策略的工作目录
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

        /// <summary>
        /// 活跃策略目录名（替代原来的 s1_Default 等）
        /// </summary>
        public const string ActiveSchemeDirName = "active";

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

        #region 策略目录管理（兼容层）

        /// <summary>
        /// 创建默认策略
        /// </summary>
        /// <param name="schemesPath">schemes 目录路径</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        /// <returns>策略 ID</returns>
        public string CreateDefaultStrategy(string schemesPath, string baselineHash)
        {
            var strategyId = "default";
            var strategyPath = Path.Combine(schemesPath, ActiveSchemeDirName);

            _logger.LogInformation("创建默认策略: {Path}", strategyPath);

            // 创建策略目录
            if (!Directory.Exists(strategyPath))
            {
                Directory.CreateDirectory(strategyPath);
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
            WriteJsonFile(Path.Combine(strategyPath, "strategy.json"), strategy);

            // 创建空的 zones.json
            WriteJsonFile(Path.Combine(strategyPath, "zones.json"), new List<object>());

            // 创建空的 finishes.json
            WriteJsonFile(Path.Combine(strategyPath, "finishes.json"), new List<object>());

            // 创建空的 modules.json
            WriteJsonFile(Path.Combine(strategyPath, "modules.json"), new List<object>());

            _logger.LogInformation("默认策略创建完成: {Id}", strategyId);
            return strategyId;
        }

        /// <summary>
        /// 检查策略是否存在（检查活跃目录）
        /// </summary>
        public bool StrategyExists(string schemesPath, string strategyId)
        {
            // 新架构：只检查 active 目录
            var activePath = Path.Combine(schemesPath, ActiveSchemeDirName);
            if (!Directory.Exists(activePath))
                return false;

            var strategyJsonPath = Path.Combine(activePath, "strategy.json");
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
                // 回退到目录扫描（兼容旧项目）
                return GetAllStrategyIdsFromDirectories(schemesPath);
            }

            // 从分支获取
            var branches = _gitService.GetSchemeBranches(projectPath);
            var strategyIds = branches
                .Select(b => b.Replace(GitWorktreeService.SchemeBranchPrefix, ""))
                .ToList();

            // 添加当前活跃策略
            var activePath = Path.Combine(schemesPath, ActiveSchemeDirName, "strategy.json");
            if (File.Exists(activePath))
            {
                var json = File.ReadAllText(activePath, Encoding.UTF8);
                var strategy = JsonConvert.DeserializeObject<Strategy>(json);
                if (strategy != null && !strategyIds.Contains(strategy.Id))
                {
                    strategyIds.Insert(0, strategy.Id);
                }
            }

            return strategyIds;
        }

        /// <summary>
        /// 从目录获取策略 ID（兼容旧项目）
        /// </summary>
        private List<string> GetAllStrategyIdsFromDirectories(string schemesPath)
        {
            if (!Directory.Exists(schemesPath))
                return new List<string>();

            var result = new List<string>();
            foreach (var dir in Directory.GetDirectories(schemesPath))
            {
                var strategyJsonPath = Path.Combine(dir, "strategy.json");
                if (File.Exists(strategyJsonPath))
                {
                    result.Add(Path.GetFileName(dir));
                }
            }
            return result;
        }

        #endregion

        #region Git 分支策略管理

        /// <summary>
        /// 保存当前策略到分支
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="strategyId">策略 ID</param>
        /// <param name="commitMessage">提交信息</param>
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
                // 分支已存在，强制更新（通过删除重建）
                _gitService.DeleteBranch(projectPath, branchName, force: true);
            }
            _gitService.CreateBranch(projectPath, branchName);

            _logger.LogInformation("策略已保存到分支: {Branch}", branchName);
        }

        /// <summary>
        /// 从分支加载策略
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="strategyId">策略 ID</param>
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

            // 检查是否有未提交更改
            if (_gitService.HasUncommittedChanges(projectPath))
            {
                throw new InvalidOperationException("存在未提交的更改，请先保存当前策略");
            }

            // 切换到目标分支
            _gitService.CheckoutBranch(projectPath, branchName);

            _logger.LogInformation("已加载策略: {Id} (分支: {Branch})", strategyId, branchName);
        }

        /// <summary>
        /// 创建新策略（基于当前状态创建分支）
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="name">策略名称</param>
        /// <param name="approach">设计方法</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        /// <returns>策略 ID</returns>
        public string CreateStrategy(string projectPath, string name, StrategyApproach approach, string baselineHash)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");
            var activePath = Path.Combine(schemesPath, ActiveSchemeDirName);

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

            // 更新 active 目录中的 strategy.json
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
            WriteJsonFile(Path.Combine(activePath, "strategy.json"), strategy);

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
        /// <param name="projectPath">项目路径</param>
        /// <param name="strategies">策略配置列表</param>
        /// <returns>各策略的 Worktree 路径</returns>
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
                var activePath = Path.Combine(worktreePath, "schemes", ActiveSchemeDirName);
                if (Directory.Exists(activePath))
                {
                    var strategyJsonPath = Path.Combine(activePath, "strategy.json");
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
        /// <summary>
        /// 任务 ID（可选，默认使用 Name 生成）
        /// </summary>
        public string? JobId { get; set; }

        /// <summary>
        /// 策略名称
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 设计方法
        /// </summary>
        public StrategyApproach Approach { get; set; }

        /// <summary>
        /// 描述
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 策略参数（如 storage_weight, flow_weight 等）
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }
    }

    #endregion
}
