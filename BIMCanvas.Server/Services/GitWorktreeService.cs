using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BIMCanvas.Server.Dtos;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// Git Worktree 服务 - 实现单仓库 + 多工作树的并行架构
    ///
    /// 架构说明：
    /// - 项目根目录是唯一的 Git 仓库（.git）
    /// - 不同策略/方案通过 Git 分支表示
    /// - 并行任务通过 Git Worktree 实现物理隔离
    ///
    /// 分支命名约定：
    /// - main: 用户当前接受的状态
    /// - scheme/{id}: 保存的设计方案（如 scheme/s1_Default）
    /// - feat/ai-{jobId}: AI 临时工作分支
    /// </summary>
    public class GitWorktreeService
    {
        private readonly ILogger<GitWorktreeService> _logger;

        /// <summary>
        /// Worktree 临时目录名
        /// </summary>
        public const string WorktreeDirName = ".worktrees";

        /// <summary>
        /// 方案分支前缀
        /// </summary>
        public const string SchemeBranchPrefix = "scheme/";

        /// <summary>
        /// AI 工作分支前缀
        /// </summary>
        public const string AiFeatureBranchPrefix = "feat/ai-";

        public GitWorktreeService(ILogger<GitWorktreeService> logger)
        {
            _logger = logger;
        }

        #region Git 仓库初始化

        /// <summary>
        /// 检查目录是否已初始化为 Git 仓库
        /// </summary>
        public bool IsGitRepository(string projectPath)
        {
            var gitDir = Path.Combine(projectPath, ".git");
            return Directory.Exists(gitDir) || File.Exists(gitDir); // .git 可能是文件（worktree）
        }

        /// <summary>
        /// 初始化 Git 仓库（如果尚未初始化）
        /// </summary>
        /// <returns>是否执行了初始化</returns>
        public bool InitializeRepository(string projectPath)
        {
            if (IsGitRepository(projectPath))
            {
                _logger.LogDebug("Git 仓库已存在: {Path}", projectPath);
                return false;
            }

            _logger.LogInformation("初始化 Git 仓库: {Path}", projectPath);

            // git init
            var initResult = RunGit(projectPath, "init");
            if (!initResult.Success)
            {
                throw new InvalidOperationException($"Git init 失败: {initResult.Error}");
            }

            // 配置用户信息（如果未配置）
            RunGit(projectPath, "config user.email \"bimcanvas@local\"");
            RunGit(projectPath, "config user.name \"BIMCanvas\"");

            // 创建初始提交
            RunGit(projectPath, "add .");
            var commitResult = RunGit(projectPath, "commit -m \"Initial commit: Project imported\"");

            if (commitResult.Success)
            {
                _logger.LogInformation("Git 仓库初始化完成，创建了初始提交");
            }

            return true;
        }

        #endregion

        #region 分支管理

        /// <summary>
        /// 获取当前分支名
        /// </summary>
        public string GetCurrentBranch(string projectPath)
        {
            var result = RunGit(projectPath, "branch --show-current");
            if (!result.Success)
            {
                throw new InvalidOperationException($"获取当前分支失败: {result.Error}");
            }
            return result.Output.Trim();
        }

        /// <summary>
        /// 获取所有分支列表
        /// </summary>
        public List<string> GetAllBranches(string projectPath)
        {
            var result = RunGit(projectPath, "branch --list");
            if (!result.Success)
            {
                return new List<string>();
            }

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(b => b.Trim().TrimStart('*', '+').Trim())  // * = 当前分支, + = worktree 关联分支
                .Where(b => !string.IsNullOrEmpty(b))
                .ToList();
        }

        /// <summary>
        /// 获取所有方案分支（scheme/* 开头）
        /// </summary>
        public List<string> GetSchemeBranches(string projectPath)
        {
            return GetAllBranches(projectPath)
                .Where(b => b.StartsWith(SchemeBranchPrefix))
                .ToList();
        }

        /// <summary>
        /// 获取所有 AI 工作分支（feat/ai-* 开头）
        /// </summary>
        public List<string> GetAiFeatureBranches(string projectPath)
        {
            return GetAllBranches(projectPath)
                .Where(b => b.StartsWith(AiFeatureBranchPrefix))
                .ToList();
        }

        /// <summary>
        /// 创建新分支（不切换）
        /// </summary>
        public void CreateBranch(string projectPath, string branchName, string? baseBranch = null)
        {
            var baseRef = baseBranch ?? "HEAD";
            var gitCommand = $"branch \"{branchName}\" {baseRef}";
            _logger.LogInformation("[CreateBranch] 执行 Git 命令: git {Command}", gitCommand);
            _logger.LogInformation("[CreateBranch] 参数: branchName={BranchName}, baseBranch={BaseBranch}, baseRef={BaseRef}",
                branchName, baseBranch ?? "(null)", baseRef);

            var result = RunGit(projectPath, gitCommand);
            if (!result.Success)
            {
                _logger.LogError("[CreateBranch] 失败: {Error}", result.Error);
                throw new InvalidOperationException($"创建分支 {branchName} 失败: {result.Error}");
            }
            _logger.LogInformation("[CreateBranch] 成功: {Branch} (基于 {Base})", branchName, baseRef);
        }

        /// <summary>
        /// 切换分支
        /// </summary>
        public void CheckoutBranch(string projectPath, string branchName)
        {
            var result = RunGit(projectPath, $"checkout \"{branchName}\"");
            if (!result.Success)
            {
                throw new InvalidOperationException($"切换到分支 {branchName} 失败: {result.Error}");
            }
            _logger.LogInformation("切换到分支: {Branch}", branchName);
        }

        /// <summary>
        /// 删除分支
        /// </summary>
        public void DeleteBranch(string projectPath, string branchName, bool force = false)
        {
            var flag = force ? "-D" : "-d";
            var result = RunGit(projectPath, $"branch {flag} \"{branchName}\"");
            if (!result.Success)
            {
                throw new InvalidOperationException($"删除分支 {branchName} 失败: {result.Error}");
            }
            _logger.LogInformation("删除分支: {Branch}", branchName);
        }

        /// <summary>
        /// 合并分支到当前分支
        /// </summary>
        public MergeResult MergeBranch(string projectPath, string branchName, string? commitMessage = null)
        {
            var message = commitMessage ?? $"Merge branch '{branchName}'";
            var result = RunGit(projectPath, $"merge \"{branchName}\" -m \"{message}\"");

            if (!result.Success)
            {
                // 检查是否有冲突
                if (result.Error.Contains("CONFLICT") || result.Output.Contains("CONFLICT"))
                {
                    return new MergeResult
                    {
                        Success = false,
                        HasConflicts = true,
                        Message = "合并存在冲突，需要手动解决"
                    };
                }

                return new MergeResult
                {
                    Success = false,
                    HasConflicts = false,
                    Message = result.Error
                };
            }

            _logger.LogInformation("合并分支 {Branch} 到当前分支", branchName);
            return new MergeResult { Success = true };
        }

        #endregion

        #region Worktree 管理

        /// <summary>
        /// 获取 Worktree 目录路径
        /// </summary>
        public string GetWorktreesDir(string projectPath)
        {
            return Path.Combine(projectPath, WorktreeDirName);
        }

        /// <summary>
        /// 创建 Worktree（用于并行任务）
        /// - 分支已存在：检出到 Worktree（场景 A：并行开发）
        /// - 分支不存在：基于 baseBranch 创建新分支（场景 B：隔离环境）
        /// </summary>
        /// <param name="projectPath">主仓库路径</param>
        /// <param name="worktreeName">工作树名称（如 "ai-job-1"）</param>
        /// <param name="branchName">目标分支名（如 "feat/ai-storage"）</param>
        /// <param name="baseBranch">基准分支（可选），分支不存在时作为创建起点，默认 HEAD</param>
        /// <returns>Worktree 的完整路径</returns>
        public string CreateWorktree(string projectPath, string worktreeName, string branchName, string? baseBranch = null)
        {
            var worktreesDir = GetWorktreesDir(projectPath);
            if (!Directory.Exists(worktreesDir))
            {
                Directory.CreateDirectory(worktreesDir);
            }

            var worktreePath = Path.Combine(worktreesDir, worktreeName);

            // 如果已存在，先删除
            if (Directory.Exists(worktreePath))
            {
                _logger.LogWarning("Worktree 已存在，将删除后重建: {Path}", worktreePath);
                RemoveWorktree(projectPath, worktreeName);
            }

            // 智能判断：分支是否存在
            var branches = GetAllBranches(projectPath);
            if (branches.Contains(branchName))
            {
                // 场景 A：分支已存在 → 直接检出
                var result = RunGit(projectPath, $"worktree add \"{worktreePath}\" \"{branchName}\"");
                if (!result.Success)
                {
                    throw new InvalidOperationException($"创建 Worktree 失败: {result.Error}");
                }
                _logger.LogInformation("创建 Worktree（检出已有分支）: {Name} -> {Branch} @ {Path}",
                    worktreeName, branchName, worktreePath);
            }
            else
            {
                // 场景 B：分支不存在 → 基于 baseBranch 创建新分支
                var startPoint = string.IsNullOrEmpty(baseBranch) ? "HEAD" : $"\"{baseBranch}\"";
                var result = RunGit(projectPath, $"worktree add -b \"{branchName}\" \"{worktreePath}\" {startPoint}");
                if (!result.Success)
                {
                    throw new InvalidOperationException($"创建 Worktree 失败: {result.Error}");
                }
                _logger.LogInformation("创建 Worktree（创建新分支）: {Name} -> {Branch} (基于 {Base}) @ {Path}",
                    worktreeName, branchName, baseBranch ?? "HEAD", worktreePath);
            }

            return worktreePath;
        }

        /// <summary>
        /// 删除 Worktree
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="worktreeName">Worktree 名称</param>
        /// <param name="deleteBranch">是否同时删除关联分支（场景 B：隔离环境使用）</param>
        public void RemoveWorktree(string projectPath, string worktreeName, bool deleteBranch = false)
        {
            var worktreePath = Path.Combine(GetWorktreesDir(projectPath), worktreeName);

            if (!Directory.Exists(worktreePath))
            {
                _logger.LogDebug("Worktree 不存在: {Path}", worktreePath);
                return;
            }

            // 如果需要删除分支，先获取 Worktree 关联的分支名
            string? branchToDelete = null;
            if (deleteBranch)
            {
                var worktrees = GetWorktrees(projectPath);
                var targetWorktree = worktrees.FirstOrDefault(w =>
                    w.Path.EndsWith(worktreeName, StringComparison.OrdinalIgnoreCase));
                if (targetWorktree != null)
                {
                    branchToDelete = targetWorktree.Branch;
                    _logger.LogDebug("将删除分支: {Branch}", branchToDelete);
                }
            }

            // 删除 Worktree
            var result = RunGit(projectPath, $"worktree remove \"{worktreePath}\" --force");
            if (!result.Success)
            {
                // 如果 git worktree remove 失败，尝试直接删除目录
                _logger.LogWarning("git worktree remove 失败，尝试直接删除: {Error}", result.Error);
                try
                {
                    Directory.Delete(worktreePath, recursive: true);
                    RunGit(projectPath, "worktree prune");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "删除 Worktree 目录失败");
                }
            }

            _logger.LogInformation("删除 Worktree: {Name}", worktreeName);

            // 删除关联分支（如果指定）
            if (deleteBranch && !string.IsNullOrEmpty(branchToDelete))
            {
                var deleteResult = RunGit(projectPath, $"branch -D \"{branchToDelete}\"");
                if (deleteResult.Success)
                {
                    _logger.LogInformation("删除关联分支: {Branch}", branchToDelete);
                }
                else
                {
                    _logger.LogWarning("删除分支失败: {Branch}, {Error}", branchToDelete, deleteResult.Error);
                }
            }
        }

        /// <summary>
        /// 获取所有活跃的 Worktree
        /// </summary>
        public List<WorktreeInfo> GetWorktrees(string projectPath)
        {
            var result = RunGit(projectPath, "worktree list --porcelain");
            if (!result.Success)
            {
                return new List<WorktreeInfo>();
            }

            var worktrees = new List<WorktreeInfo>();
            WorktreeInfo? current = null;

            foreach (var line in result.Output.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("worktree "))
                {
                    if (current != null) worktrees.Add(current);
                    current = new WorktreeInfo { Path = line.Substring(9).Trim() };
                }
                else if (line.StartsWith("HEAD ") && current != null)
                {
                    current.CommitHash = line.Substring(5).Trim();
                }
                else if (line.StartsWith("branch ") && current != null)
                {
                    current.Branch = line.Substring(7).Trim().Replace("refs/heads/", "");
                }
            }

            if (current != null) worktrees.Add(current);

            return worktrees;
        }

        /// <summary>
        /// 检查分支是否被任何 Worktree 占用
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="branchName">分支名</param>
        /// <returns>(是否被占用, 占用者 Worktree 路径)</returns>
        public (bool isOccupied, string? worktreePath) IsBranchOccupiedByWorktree(string projectPath, string branchName)
        {
            var worktrees = GetWorktrees(projectPath);

            foreach (var wt in worktrees)
            {
                if (wt.Branch == branchName)
                {
                    return (true, wt.Path);
                }
            }

            return (false, null);
        }

        /// <summary>
        /// 清理所有已完成的 AI 工作 Worktree
        /// </summary>
        public void CleanupAiWorktrees(string projectPath)
        {
            var worktrees = GetWorktrees(projectPath);
            var worktreesDir = GetWorktreesDir(projectPath);

            foreach (var wt in worktrees)
            {
                if (wt.Path.StartsWith(worktreesDir) &&
                    (wt.Branch?.StartsWith(AiFeatureBranchPrefix) ?? false))
                {
                    var name = Path.GetFileName(wt.Path);
                    RemoveWorktree(projectPath, name);
                }
            }

            _logger.LogInformation("清理完成所有 AI Worktree");
        }

        /// <summary>
        /// 清空所有 Worktree（Server 启动时调用）
        /// Server 重启后无活跃窗口，所有 worktree 都是残留
        /// </summary>
        public void CleanupAllWorktrees(string projectPath)
        {
            var worktrees = GetWorktrees(projectPath);
            var worktreesDir = Path.GetFullPath(GetWorktreesDir(projectPath));  // 标准化路径格式
            var count = 0;

            foreach (var wt in worktrees)
            {
                // 跳过主仓库（标准化路径后比较，Git 返回正斜杠，Windows 使用反斜杠）
                var wtPath = Path.GetFullPath(wt.Path);
                if (!wtPath.StartsWith(worktreesDir, StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = Path.GetFileName(wt.Path);
                _logger.LogInformation("清理 Worktree: {Name}", name);
                RemoveWorktree(projectPath, name, deleteBranch: false);
                count++;
            }

            if (count > 0)
                _logger.LogInformation("已清理 {Count} 个 Worktree", count);
        }

        #endregion

        #region 提交操作

        /// <summary>
        /// 提交当前更改
        /// </summary>
        public void Commit(string workingDir, string message)
        {
            RunGit(workingDir, "add .");
            var result = RunGit(workingDir, $"commit -m \"{EscapeMessage(message)}\"");

            // "nothing to commit" 可能在 stdout 或 stderr 中
            var combined = (result.Output ?? "") + (result.Error ?? "");
            if (!result.Success && !combined.Contains("nothing to commit"))
            {
                throw new InvalidOperationException($"提交失败: {result.Error}{result.Output}");
            }

            if (result.Success)
            {
                _logger.LogInformation("提交更改: {Message}", message);
            }
            else
            {
                _logger.LogDebug("无需提交（工作区干净）");
            }
        }

        /// <summary>
        /// 尝试提交当前更改（包括新文件）
        /// </summary>
        /// <returns>是否成功提交（false 表示没有需要提交的内容）</returns>
        public bool TryCommit(string workingDir, string message)
        {
            // 先 add 所有更改（包括新文件）
            RunGit(workingDir, "add .");

            // 执行 commit
            var result = RunGit(workingDir, $"commit -m \"{EscapeMessage(message)}\"");

            // "nothing to commit" 可能在 stdout 或 stderr 中
            var combined = (result.Output ?? "") + (result.Error ?? "");

            if (combined.Contains("nothing to commit"))
            {
                _logger.LogDebug("无需提交（工作区干净）");
                return false;
            }

            if (!result.Success)
            {
                throw new InvalidOperationException($"提交失败: {result.Error}{result.Output}");
            }

            _logger.LogInformation("提交更改: {Message}", message);
            return true;
        }

        /// <summary>
        /// 检查是否有未提交的更改（包括未跟踪的新文件）
        /// </summary>
        public bool HasUncommittedChanges(string workingDir)
        {
            // 不使用 -uno，这样既检测已跟踪文件的修改，也检测未跟踪的新文件
            var result = RunGit(workingDir, "status --porcelain");
            return !string.IsNullOrWhiteSpace(result.Output);
        }

        #endregion

        #region 还原操作

        /// <summary>
        /// 放弃所有未提交的更改（危险操作）
        /// </summary>
        public void DiscardChanges(string workingDir)
        {
            // 1. 还原已跟踪文件的修改
            var checkoutResult = RunGit(workingDir, "checkout .");
            if (!checkoutResult.Success)
            {
                throw new InvalidOperationException($"还原文件失败: {checkoutResult.Error}");
            }

            // 2. 删除未跟踪的文件和目录
            var cleanResult = RunGit(workingDir, "clean -fd");
            if (!cleanResult.Success)
            {
                throw new InvalidOperationException($"清理未跟踪文件失败: {cleanResult.Error}");
            }

            _logger.LogInformation("已放弃所有未提交的更改: {Path}", workingDir);
        }

        #endregion

        #region 分支详细信息

        /// <summary>
        /// 获取分支的最新提交信息
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <param name="branchName">分支名</param>
        /// <returns>提交信息</returns>
        public Dtos.CommitInfo? GetBranchCommit(string projectPath, string branchName)
        {
            // git log -1 --format="%h|%s|%ar|%an" branchName
            var result = RunGit(projectPath, $"log -1 --format=\"%h|%s|%ar|%an\" \"{branchName}\"");
            if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            {
                return null;
            }

            var parts = result.Output.Trim().Split('|');
            if (parts.Length < 4)
            {
                return null;
            }

            return new Dtos.CommitInfo
            {
                Hash = parts[0],
                Message = parts[1],
                Time = parts[2],
                Author = parts[3]
            };
        }

        /// <summary>
        /// 获取带详细信息的分支列表
        /// </summary>
        /// <param name="projectPath">项目路径</param>
        /// <returns>分支信息列表</returns>
        public List<GitBranchInfo> GetBranchesWithDetails(string projectPath)
        {
            var branches = new List<GitBranchInfo>();
            var currentBranch = GetCurrentBranch(projectPath);
            var allBranches = GetAllBranches(projectPath);

            foreach (var branchName in allBranches)
            {
                var commit = GetBranchCommit(projectPath, branchName);
                branches.Add(new GitBranchInfo
                {
                    Id = branchName,
                    Name = branchName,
                    IsCurrent = branchName == currentBranch,
                    Commit = commit
                });
            }

            // 当前分支排在最前面
            return branches
                .OrderByDescending(b => b.IsCurrent)
                .ThenBy(b => b.Name)
                .ToList();
        }

        #endregion

        #region 私有方法

        private GitResult RunGit(string workingDir, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return new GitResult { Success = false, Error = "无法启动 Git 进程" };
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                process.WaitForExit(30000); // 30秒超时

                var success = process.ExitCode == 0;

                if (!success)
                {
                    _logger.LogDebug("Git 命令失败: git {Args}\nOutput: {Output}\nError: {Error}",
                        arguments, output, error);
                }

                return new GitResult
                {
                    Success = success,
                    Output = output,
                    Error = error
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行 Git 命令异常: {Args}", arguments);
                return new GitResult { Success = false, Error = ex.Message };
            }
        }

        private static string EscapeMessage(string message)
        {
            return message.Replace("\"", "\\\"").Replace("\n", " ");
        }

        #endregion
    }

    #region 辅助类型

    /// <summary>
    /// Git 命令执行结果
    /// </summary>
    public class GitResult
    {
        public bool Success { get; set; }
        public string Output { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// 合并结果
    /// </summary>
    public class MergeResult
    {
        public bool Success { get; set; }
        public bool HasConflicts { get; set; }
        public string? Message { get; set; }
    }

    /// <summary>
    /// Worktree 信息
    /// </summary>
    public class WorktreeInfo
    {
        public string Path { get; set; } = string.Empty;
        public string? Branch { get; set; }
        public string? CommitHash { get; set; }
    }

    #endregion
}
