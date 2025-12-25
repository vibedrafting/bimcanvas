using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BIMCanvas.Core.Services
{
    /// <summary>
    /// Baseline 哈希计算服务
    /// 用于验证策略与 baseline 的一致性
    /// </summary>
    public static class BaselineHashService
    {
        /// <summary>
        /// Baseline 文件列表（按固定顺序）
        /// </summary>
        private static readonly string[] BaselineFiles =
        {
            "metadata.json",
            "architecture.json",
            "rooms.json",
            "openings.json",
            "location_lines.json"
        };

        /// <summary>
        /// 计算 baseline 目录的联合哈希值
        /// </summary>
        /// <param name="baselinePath">baseline 目录路径</param>
        /// <returns>格式：sha256:{hash}</returns>
        public static string ComputeBaselineHash(string baselinePath)
        {
            if (!Directory.Exists(baselinePath))
            {
                throw new DirectoryNotFoundException($"Baseline directory not found: {baselinePath}");
            }

            using (var sha256 = SHA256.Create())
            {
                var combinedContent = new StringBuilder();

                foreach (var file in BaselineFiles)
                {
                    var filePath = Path.Combine(baselinePath, file);
                    if (File.Exists(filePath))
                    {
                        var content = File.ReadAllText(filePath, Encoding.UTF8);
                        // 规范化：移除空白字符差异
                        var normalized = NormalizeJson(content);
                        combinedContent.Append(normalized);
                    }
                }

                var bytes = Encoding.UTF8.GetBytes(combinedContent.ToString());
                var hash = sha256.ComputeHash(bytes);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

                return $"sha256:{hashString}";
            }
        }

        /// <summary>
        /// 验证策略是否与当前 baseline 一致
        /// </summary>
        /// <param name="strategyHash">策略记录的哈希值</param>
        /// <param name="baselinePath">baseline 目录路径</param>
        /// <returns>是否一致</returns>
        public static bool ValidateBaselineConsistency(string strategyHash, string baselinePath)
        {
            var currentHash = ComputeBaselineHash(baselinePath);
            return string.Equals(strategyHash, currentHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 规范化 JSON 字符串（移除格式差异）
        /// </summary>
        private static string NormalizeJson(string json)
        {
            // 移除所有空白字符（简单实现）
            return Regex.Replace(json, @"\s+", "");
        }
    }
}
