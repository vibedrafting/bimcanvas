using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 通用模板初始化服务。
    /// 负责定位 Templates 根目录、读取 manifest，并按“仅缺失时补齐”规则复制模板。
    /// </summary>
    public sealed class BootstrapTemplateService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly string _templatesRoot;

        public BootstrapTemplateService()
        {
            _templatesRoot = ResolveTemplatesRoot();
        }

        public string TemplatesRoot => _templatesRoot;

        /// <summary>
        /// 按 manifest 初始化目标目录。
        /// 规则：仅在目标缺失时创建，不覆盖、不修复已存在内容。
        /// </summary>
        public void EnsureInitializedFromManifest(
            string manifestRelativePath,
            string targetRoot,
            IReadOnlyDictionary<string, string>? replacements = null)
        {
            if (string.IsNullOrWhiteSpace(targetRoot))
            {
                throw new ArgumentException("目标根目录不能为空。", nameof(targetRoot));
            }

            var normalizedManifestPath = NormalizeRelativePath(manifestRelativePath);
            var manifestPath = Path.Combine(_templatesRoot, normalizedManifestPath);
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"模板清单不存在: {manifestPath}", manifestPath);
            }

            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonSerializer.Deserialize<BootstrapManifest>(manifestJson, JsonOptions);
            if (manifest?.Items == null || manifest.Items.Count == 0)
            {
                throw new InvalidOperationException($"模板清单为空或无效: {manifestPath}");
            }

            var manifestRoot = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException($"无法解析 manifest 所在目录: {manifestPath}");

            Directory.CreateDirectory(targetRoot);

            foreach (var item in manifest.Items)
            {
                if (!item.Enabled)
                {
                    continue;
                }

                EnsureItem(manifestRoot, targetRoot, item, replacements);
            }
        }

        private static void EnsureItem(
            string manifestRoot,
            string targetRoot,
            BootstrapManifestItem item,
            IReadOnlyDictionary<string, string>? replacements)
        {
            var sourcePath = Path.Combine(manifestRoot, NormalizeRelativePath(item.Name));
            var targetPath = Path.Combine(targetRoot, NormalizeRelativePath(item.Target));
            var itemType = item.Type?.Trim().ToLowerInvariant() ?? "template";

            switch (itemType)
            {
                case "directory":
                    EnsureDirectoryItem(sourcePath, targetPath);
                    return;
                case "template":
                    EnsureTemplateItem(sourcePath, targetPath, replacements);
                    return;
                default:
                    throw new InvalidOperationException(
                        $"不支持的模板项类型: {item.Type} (name={item.Name}, target={item.Target})");
            }
        }

        private static void EnsureDirectoryItem(string sourcePath, string targetPath)
        {
            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"模板源目录不存在: {sourcePath}");
            }

            if (File.Exists(targetPath))
            {
                throw new InvalidOperationException($"目标路径已存在同名文件，无法初始化目录: {targetPath}");
            }

            if (Directory.Exists(targetPath))
            {
                return;
            }

            CopyDirectory(sourcePath, targetPath);
        }

        private static void EnsureTemplateItem(
            string sourcePath,
            string targetPath,
            IReadOnlyDictionary<string, string>? replacements)
        {
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException($"模板源文件不存在: {sourcePath}", sourcePath);
            }

            if (Directory.Exists(targetPath))
            {
                throw new InvalidOperationException($"目标路径已存在同名目录，无法初始化文件: {targetPath}");
            }

            if (File.Exists(targetPath))
            {
                return;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var content = File.ReadAllText(sourcePath, Encoding.UTF8);
            if (replacements != null)
            {
                foreach (var replacement in replacements)
                {
                    content = content.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
                }
            }

            File.WriteAllText(targetPath, content, Utf8NoBom);
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                File.Copy(file, Path.Combine(targetDir, fileName), overwrite: false);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                var subDirName = Path.GetFileName(subDir);
                CopyDirectory(subDir, Path.Combine(targetDir, subDirName));
            }
        }

        private static string ResolveTemplatesRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var directPath = Path.Combine(baseDir, "Templates");
            if (Directory.Exists(directPath))
            {
                return directPath;
            }

            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var tryPath = Path.Combine(dir.FullName, "BIMCanvas.Server", "Templates");
                if (Directory.Exists(tryPath))
                {
                    return tryPath;
                }
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("未找到 BIMCanvas.Server/Templates 目录。");
        }

        private static string NormalizeRelativePath(string relativePath)
        {
            return relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
        }

        private sealed class BootstrapManifest
        {
            public string Version { get; set; } = "1.0";
            public List<BootstrapManifestItem> Items { get; set; } = new();
        }

        private sealed class BootstrapManifestItem
        {
            public string Name { get; set; } = "";
            public string Target { get; set; } = "";
            public string Type { get; set; } = "template";
            public bool Enabled { get; set; } = true;
            public string? Description { get; set; }
        }
    }
}
