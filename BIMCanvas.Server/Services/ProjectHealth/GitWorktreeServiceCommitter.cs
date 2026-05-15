using System.Diagnostics;

namespace BIMCanvas.Server.Services.ProjectHealth
{
    /// <summary>
    /// IGitCommitter 的 Server 实现：委托给 GitWorktreeService.TryCommit；
    /// commit 成功后另跑 `git rev-parse HEAD` 拿 hash（拿不到不影响修复结果）。
    /// CLI 不使用此类，直接传 null IGitCommitter 即可。
    /// </summary>
    public class GitWorktreeServiceCommitter : IGitCommitter
    {
        private readonly GitWorktreeService _git;

        public GitWorktreeServiceCommitter(GitWorktreeService git)
        {
            _git = git;
        }

        public bool TryCommit(string workingDir, string message, out string? commitHash)
        {
            commitHash = null;
            var didCommit = _git.TryCommit(workingDir, message);
            if (didCommit)
                commitHash = TryGetHead(workingDir);
            return didCommit;
        }

        private static string? TryGetHead(string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "rev-parse HEAD",
                    WorkingDirectory = workingDir,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(5000);
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }
    }
}
