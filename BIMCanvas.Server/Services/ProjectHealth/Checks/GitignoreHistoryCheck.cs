using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BIMCanvas.Server.Services.ProjectHealth.Checks
{
    /// <summary>
    /// 维护项 check（非一次性 schema 迁移）：确保 .bcp 项目 .gitignore 忽略 <c>.history/</c>。
    /// <para>
    /// <c>.history/</c> 是历史对话的运行时落点(gitignored 基础设施,见 BIMCanvas.Agent history_persistence)。
    /// 新建项目由模板 .gitignore 自带该行;<b>存量项目</b>(模板拷贝幂等、不回填)缺这行,本 check 负责补齐。
    /// </para>
    /// Inspect 不写盘;Repair 原子追加(.tmp + rename),已含则跳过。仅文件系统操作,零 Core/Server 依赖。
    /// </summary>
    public class GitignoreHistoryCheck : IProjectHealthCheck
    {
        public string Id => "gitignore-history";
        public string Description => "确保 .gitignore 忽略 .history/（历史对话运行时落点）";

        private const string IgnoreLine = ".history/";

        public CheckInspectionResult Inspect(string projectPath)
        {
            var result = new CheckInspectionResult { CheckId = Id, CheckDescription = Description };
            try
            {
                if (!HasHistoryIgnore(projectPath, out var reason))
                {
                    result.Issues.Add(new HealthIssue
                    {
                        RelativePath = ".gitignore",
                        IssueType = "missing-history-ignore",
                        Description = reason
                    });
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($".gitignore: {ex.Message}");
            }
            return result;
        }

        public CheckRepairResult Repair(string projectPath)
        {
            var result = new CheckRepairResult { CheckId = Id, CheckDescription = Description };
            try
            {
                var gitignorePath = Path.Combine(projectPath, ".gitignore");
                if (HasHistoryIgnore(projectPath, out _))
                {
                    result.Skipped.Add(".gitignore（已忽略 .history/）");
                    return result;
                }

                var existing = File.Exists(gitignorePath)
                    ? File.ReadAllText(gitignorePath, Encoding.UTF8)
                    : string.Empty;

                var sb = new StringBuilder(existing);
                if (existing.Length > 0 && !existing.EndsWith("\n", StringComparison.Ordinal))
                    sb.Append('\n');
                sb.Append("\n# 历史对话（运行时落点，不入库）\n");
                sb.Append(IgnoreLine).Append('\n');

                WriteAtomicText(gitignorePath, sb.ToString());
                result.Migrated.Add(".gitignore（追加 .history/）");
            }
            catch (Exception ex)
            {
                result.Errors.Add($".gitignore: {ex.Message}");
            }
            return result;
        }

        /// <summary>.gitignore 是否已忽略 .history/（按行精确匹配 ".history/" 或 ".history"，忽略首尾空白）。</summary>
        private static bool HasHistoryIgnore(string projectPath, out string reason)
        {
            var gitignorePath = Path.Combine(projectPath, ".gitignore");
            if (!File.Exists(gitignorePath))
            {
                reason = ".gitignore 不存在，需创建并忽略 .history/";
                return false;
            }

            var lines = File.ReadAllLines(gitignorePath, Encoding.UTF8);
            var ignored = lines.Any(line =>
            {
                var trimmed = line.Trim();
                return trimmed == ".history/" || trimmed == ".history";
            });
            reason = ignored ? "" : ".gitignore 未忽略 .history/，需追加一行";
            return ignored;
        }

        private static void WriteAtomicText(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));
            File.Move(tmp, path, overwrite: true);
        }
    }
}
