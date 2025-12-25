using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// .manifest 文件读写服务
    /// 采用简单的键值对格式（每行 key=value）
    /// </summary>
    public class ManifestService
    {
        private readonly ILogger<ManifestService> _logger;

        public ManifestService(ILogger<ManifestService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 读取 manifest 文件
        /// </summary>
        /// <param name="manifestPath">manifest 文件路径</param>
        /// <returns>键值对字典，文件不存在或解析失败返回 null</returns>
        public Dictionary<string, string>? ReadManifest(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                _logger.LogDebug("Manifest 文件不存在: {Path}", manifestPath);
                return null;
            }

            try
            {
                var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var lines = File.ReadAllLines(manifestPath, Encoding.UTF8);

                foreach (var line in lines)
                {
                    // 跳过空行和注释行
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    // 解析 key=value
                    var equalIndex = trimmed.IndexOf('=');
                    if (equalIndex > 0)
                    {
                        var key = trimmed.Substring(0, equalIndex).Trim();
                        var value = trimmed.Substring(equalIndex + 1).Trim();
                        result[key] = value;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取 Manifest 失败: {Path}", manifestPath);
                return null;
            }
        }

        /// <summary>
        /// 写入 manifest 文件
        /// </summary>
        /// <param name="manifestPath">manifest 文件路径</param>
        /// <param name="data">键值对数据</param>
        public void WriteManifest(string manifestPath, Dictionary<string, string> data)
        {
            try
            {
                var lines = new List<string>();

                // 添加注释头
                lines.Add($"# Generated at {DateTime.Now:yyyy-MM-ddTHH:mm:ss}");

                foreach (var kvp in data)
                {
                    lines.Add($"{kvp.Key}={kvp.Value}");
                }

                // 确保目录存在
                var dir = Path.GetDirectoryName(manifestPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllLines(manifestPath, lines, Encoding.UTF8);
                _logger.LogInformation("Manifest 写入成功: {Path}", manifestPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写入 Manifest 失败: {Path}", manifestPath);
                throw;
            }
        }

        /// <summary>
        /// 创建 baseline.manifest
        /// </summary>
        /// <param name="baselinePath">baseline 目录路径</param>
        /// <param name="baselineHash">baseline 哈希值</param>
        public void WriteBaselineManifest(string baselinePath, string baselineHash)
        {
            var manifestPath = Path.Combine(baselinePath, "baseline.manifest");
            var data = new Dictionary<string, string>
            {
                ["version"] = "1",
                ["generatedAt"] = DateTime.Now.ToString("o"),
                ["baselineHash"] = baselineHash
            };
            WriteManifest(manifestPath, data);
        }

        /// <summary>
        /// 创建 computed.manifest
        /// </summary>
        /// <param name="computedPath">computed 目录路径</param>
        /// <param name="baselineHash">baseline 哈希值（用于验证计算数据是否过期）</param>
        public void WriteComputedManifest(string computedPath, string baselineHash)
        {
            var manifestPath = Path.Combine(computedPath, "computed.manifest");
            var data = new Dictionary<string, string>
            {
                ["version"] = "1",
                ["generatedAt"] = DateTime.Now.ToString("o"),
                ["baselineHash"] = baselineHash
            };
            WriteManifest(manifestPath, data);
        }

        /// <summary>
        /// 获取 baseline 哈希值
        /// </summary>
        /// <param name="baselinePath">baseline 目录路径</param>
        /// <returns>哈希值，不存在返回 null</returns>
        public string? GetBaselineHash(string baselinePath)
        {
            var manifestPath = Path.Combine(baselinePath, "baseline.manifest");
            var manifest = ReadManifest(manifestPath);
            if (manifest != null && manifest.TryGetValue("baselineHash", out var hash))
            {
                return hash;
            }
            return null;
        }

        /// <summary>
        /// 获取 computed 中记录的 baseline 哈希值
        /// </summary>
        /// <param name="computedPath">computed 目录路径</param>
        /// <returns>哈希值，不存在返回 null</returns>
        public string? GetComputedBaselineHash(string computedPath)
        {
            var manifestPath = Path.Combine(computedPath, "computed.manifest");
            var manifest = ReadManifest(manifestPath);
            if (manifest != null && manifest.TryGetValue("baselineHash", out var hash))
            {
                return hash;
            }
            return null;
        }
    }
}
