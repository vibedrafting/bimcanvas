using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 指针式平级模型的 DESIGN.md frontmatter 读写（平台级通用约定）。
    ///
    /// 父 <c>schemes/{designZoneId}/DESIGN.md</c> 的 frontmatter 唯一字段 <c>adopted: {slug}</c>
    /// 标记当前生效方案；采纳 = 翻这个指针（零复制 / 零删除 / 零降级 / 可逆）。
    ///
    /// 【domain-agnostic 边界（指挥部裁决 2026-05-31）】Server 只读 / 写这个**通用指针字段**，
    /// **绝不解析 DESIGN.md 正文**（正文=空间骨架 / 战略 / 简报等 plugin 业务，由 agent / workflow 读写）。
    ///
    /// frontmatter 块约定：文件首行 "---" 起、到下一行 "---" 止，之间为 YAML。
    /// </summary>
    public class SchemeDesignDocService
    {
        public const string DesignDocFileName = "DESIGN.md";
        private const string AdoptedKey = "adopted";
        private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder().Build();

        private readonly ILogger<SchemeDesignDocService>? _logger;

        public SchemeDesignDocService(ILogger<SchemeDesignDocService>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>父 DESIGN.md 的物理路径：schemes/{designZoneId}/DESIGN.md。</summary>
        public string DesignDocPath(string schemesPath, string designZoneId)
            => Path.Combine(schemesPath, designZoneId, DesignDocFileName);

        /// <summary>
        /// 读父 DESIGN.md 的 adopted slug。
        /// 无 DESIGN.md / 无 frontmatter / 无 adopted 字段 → 返回 <c>null</c>
        /// （调用方据 null **回落 legacy canonical 路径**，保证存量项目在 P2 迁移前仍可正常渲染）。
        /// </summary>
        public string? ReadAdoptedSlug(string schemesPath, string designZoneId)
        {
            if (string.IsNullOrWhiteSpace(designZoneId))
                return null;

            var path = DesignDocPath(schemesPath, designZoneId);
            if (!File.Exists(path))
                return null;

            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                SplitFrontmatterAndBody(text, out var frontmatter, out _);
                if (string.IsNullOrEmpty(frontmatter))
                    return null;

                var map = YamlDeserializer.Deserialize<Dictionary<string, object>>(frontmatter);
                if (map != null && map.TryGetValue(AdoptedKey, out var value))
                {
                    var slug = value?.ToString();
                    if (!string.IsNullOrWhiteSpace(slug))
                        return slug.Trim();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[DesignDoc] 解析 adopted 失败，回落 legacy：{Path}", path);
            }

            return null;
        }

        /// <summary>
        /// 写父 DESIGN.md 的 adopted slug（采纳=翻指针）。
        /// 保留正文与其它 frontmatter 字段；DESIGN.md 不存在时新建（仅 frontmatter + 占位正文）。原子写入（.tmp → rename）。
        /// </summary>
        public void WriteAdoptedSlug(string schemesPath, string designZoneId, string slug)
        {
            if (string.IsNullOrWhiteSpace(designZoneId))
                throw new ArgumentException("designZoneId 必填", nameof(designZoneId));
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("slug 必填", nameof(slug));

            var path = DesignDocPath(schemesPath, designZoneId);
            var body = string.Empty;
            var frontmatterLines = new List<string>();
            var adoptedWritten = false;

            if (File.Exists(path))
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                SplitFrontmatterAndBody(text, out var fm, out body);
                if (!string.IsNullOrEmpty(fm))
                {
                    foreach (var rawLine in fm.Split('\n'))
                    {
                        var line = rawLine.TrimEnd('\r');
                        if (line.TrimStart().StartsWith(AdoptedKey + ":", StringComparison.OrdinalIgnoreCase))
                        {
                            frontmatterLines.Add($"{AdoptedKey}: {slug}");
                            adoptedWritten = true;
                        }
                        else if (line.Trim().Length > 0)
                        {
                            frontmatterLines.Add(line);
                        }
                    }
                }
            }

            if (!adoptedWritten)
                frontmatterLines.Insert(0, $"{AdoptedKey}: {slug}");

            var sb = new StringBuilder();
            sb.Append("---\n");
            foreach (var line in frontmatterLines)
                sb.Append(line).Append('\n');
            sb.Append("---\n");
            if (!string.IsNullOrEmpty(body))
            {
                sb.Append('\n');
                sb.Append(body.TrimStart('\n'));
            }

            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            var tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(false));
            File.Move(tmpPath, path, overwrite: true);
            _logger?.LogDebug("[DesignDoc] 写 adopted={Slug} → {Path}", slug, path);
        }

        /// <summary>
        /// 切分 frontmatter 与正文。首行非 "---" → 无 frontmatter（frontmatter=null、body=原文）。
        /// </summary>
        private static void SplitFrontmatterAndBody(string text, out string? frontmatter, out string body)
        {
            frontmatter = null;
            body = text ?? string.Empty;
            if (string.IsNullOrEmpty(text))
                return;

            var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---")
                return;

            var close = -1;
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    close = i;
                    break;
                }
            }

            if (close < 0)
                return;

            frontmatter = string.Join("\n", lines.Skip(1).Take(close - 1));
            body = string.Join("\n", lines.Skip(close + 1));
        }
    }
}
