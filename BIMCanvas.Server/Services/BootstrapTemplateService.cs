using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 通用模板初始化服务。
    /// 负责定位 Templates 根目录、读取 manifest，并按“仅缺失时补齐”规则复制模板。
    /// 序列化栈:Newtonsoft.Json + <see cref="DefaultContractResolver"/> +
    /// <see cref="CamelCaseNamingStrategy"/>(只转 C# 属性名,不转 Dictionary key;详见 CLAUDE.md §10)。
    /// </summary>
    public sealed class BootstrapTemplateService
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private static readonly JsonSerializerSettings JsonSettings = new()
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
        };

        private readonly string _templatesRoot;

        public BootstrapTemplateService()
        {
            _templatesRoot = ResolveTemplatesRoot();
        }

        public string TemplatesRoot => _templatesRoot;

        /// <summary>
        /// (组5 §5.A.7) 项目创建时拷贝平台级 baseline 文件 (README.md / .gitignore) 到项目根。
        /// <para>
        /// <b>语义边界</b>:这两个文件是"任何 .bcp 项目都该有"的平台基线,与 plugin 系统解耦。
        /// 源:<c>Templates/platform-config/project-baseline/*</c>;
        /// 目标:<paramref name="projectPath"/> 项目根。
        /// </para>
        /// <para>
        /// <b>R10 不变量</b>:**只在新建项目(LoadProject 解压新 .bcp 后)调用**,
        /// 不在 OpenProject / bind scene 时调用。文件已存在则跳过(幂等)。
        /// 切到不同 plugin 后打开已有项目,baseline 不会被覆盖。
        /// </para>
        /// </summary>
        public void EnsurePlatformBaseline(string projectPath)
        {
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                throw new ArgumentException("项目路径不能为空。", nameof(projectPath));
            }

            var sourceDir = Path.Combine(_templatesRoot, "platform-config", "project-baseline");
            if (!Directory.Exists(sourceDir))
            {
                // platform-config/project-baseline/ 缺失不阻塞项目创建,只是无 baseline 可拷贝。
                return;
            }

            Directory.CreateDirectory(projectPath);
            foreach (var sourceFile in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(sourceFile);
                var targetPath = Path.Combine(projectPath, fileName);
                if (File.Exists(targetPath))
                {
                    continue; // 幂等:已存在跳过
                }
                File.Copy(sourceFile, targetPath, overwrite: false);
            }
        }

        /// <summary>
        /// (组5 §5.A.6) 平台首启动 / plugin 安装时把 plugin 模板从 Templates/plugins/{pluginName}/
        /// 物化到 <c>BIMCANVAS_HOME/plugins/{pluginName}/</c>。
        /// <para>
        /// <b>语义</b>:对 core-base 等 platform-shipped plugin,这是首启动 bootstrap 入口。
        /// 第三方 plugin 走 PluginInstallService 的 git clone 路径,不经本方法。
        /// </para>
        /// <para>
        /// <b>幂等性</b>:目标目录已存在时跳过整体拷贝(不做"补齐缺失文件",避免覆盖用户手改)。
        /// </para>
        /// </summary>
        public void EnsurePluginInitialized(string pluginName, string targetPluginRoot)
        {
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException("pluginName 不能为空。", nameof(pluginName));
            }
            if (string.IsNullOrWhiteSpace(targetPluginRoot))
            {
                throw new ArgumentException("targetPluginRoot 不能为空。", nameof(targetPluginRoot));
            }

            var sourceDir = Path.Combine(_templatesRoot, "plugins", pluginName);
            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException(
                    $"Templates/plugins/{pluginName} 不存在,无法 bootstrap plugin: {sourceDir}");
            }

            if (Directory.Exists(targetPluginRoot))
            {
                // 幂等:已 bootstrap 过则跳过,不补齐
                return;
            }

            CopyDirectory(sourceDir, targetPluginRoot);
        }

        /// <summary>
        /// (v3.4) 从指定绝对源路径 bootstrap plugin 到目标位置。
        /// 用于 core-base —— 其源已从 BIMCanvas.Server/Templates 迁到 BIMCanvas.Agent/plugins/。
        /// 第三方 plugin 仍走 PluginInstallService 的 git clone 路径,不经本方法。
        /// 幂等性:目标目录已存在时跳过(同 EnsurePluginInitialized)。
        /// </summary>
        public void EnsurePluginInitializedFromAbsolute(string sourceDir, string targetPluginRoot)
        {
            if (string.IsNullOrWhiteSpace(sourceDir))
            {
                throw new ArgumentException("sourceDir 不能为空。", nameof(sourceDir));
            }
            if (string.IsNullOrWhiteSpace(targetPluginRoot))
            {
                throw new ArgumentException("targetPluginRoot 不能为空。", nameof(targetPluginRoot));
            }

            if (!Directory.Exists(sourceDir))
            {
                throw new DirectoryNotFoundException(
                    $"源目录不存在,无法 bootstrap plugin: {sourceDir}");
            }

            if (Directory.Exists(targetPluginRoot))
            {
                return; // 幂等
            }

            CopyDirectory(sourceDir, targetPluginRoot);
        }

        /// <summary>
        /// (v3.4) 定位 BIMCanvas.Agent 项目根目录,跨项目读 plugin 源。
        /// 复用与 <see cref="ResolveTemplatesRoot"/> 同款向上回溯逻辑。
        /// </summary>
        public static string ResolveAgentProjectRoot()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dir != null; i++)
            {
                var tryPath = Path.Combine(dir.FullName, "BIMCanvas.Agent");
                if (Directory.Exists(tryPath))
                {
                    return tryPath;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("未找到 BIMCanvas.Agent 目录。");
        }

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

            // 平台配置清单要求非空(空 = 配置错误,显式抛错)。
            ProcessManifest(manifestPath, targetRoot, replacements, requireNonEmpty: true);
        }

        /// <summary>
        /// 按**绝对路径** manifest 初始化目标目录。与 <see cref="EnsureInitializedFromManifest"/>
        /// 共享同一套"仅缺失补齐、绝不覆盖"程序;唯一区别:manifest 路径不拼 Templates 根,
        /// 且 items 为空时安静 no-op(plugin projectMount 允许声明空挂载清单)。
        /// 供 plugin projectMount 初始化复用(<see cref="ProjectFixedFilesBootstrapService.EnsureProjectMountInitialized"/>)。
        /// </summary>
        public void EnsureInitializedFromManifestAbsolute(
            string manifestPath,
            string targetRoot,
            IReadOnlyDictionary<string, string>? replacements = null)
        {
            if (string.IsNullOrWhiteSpace(targetRoot))
            {
                throw new ArgumentException("目标根目录不能为空。", nameof(targetRoot));
            }
            if (!File.Exists(manifestPath))
            {
                throw new FileNotFoundException($"模板清单不存在: {manifestPath}", manifestPath);
            }

            ProcessManifest(manifestPath, targetRoot, replacements, requireNonEmpty: false);
        }

        /// <summary>读 manifest 并逐项"仅缺失补齐"。两个公共入口共享的核心循环。</summary>
        private static void ProcessManifest(
            string manifestPath,
            string targetRoot,
            IReadOnlyDictionary<string, string>? replacements,
            bool requireNonEmpty)
        {
            var manifestJson = File.ReadAllText(manifestPath, Encoding.UTF8);
            var manifest = JsonConvert.DeserializeObject<BootstrapManifest>(manifestJson, JsonSettings);
            if (manifest?.Items == null || manifest.Items.Count == 0)
            {
                if (requireNonEmpty)
                {
                    throw new InvalidOperationException($"模板清单为空或无效: {manifestPath}");
                }
                return;
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
            var targetPath = Path.Combine(targetRoot, NormalizeRelativePath(item.Target));
            var itemType = item.Type?.Trim().ToLowerInvariant() ?? "template";

            switch (itemType)
            {
                case "empty-directory":
                    EnsureEmptyDirectoryItem(targetPath);
                    return;
                case "directory":
                {
                    var sourcePath = Path.Combine(manifestRoot, NormalizeRelativePath(item.Name));
                    EnsureDirectoryItem(sourcePath, targetPath);
                    return;
                }
                case "template":
                {
                    var sourcePath = Path.Combine(manifestRoot, NormalizeRelativePath(item.Name));
                    EnsureTemplateItem(sourcePath, targetPath, replacements);
                    return;
                }
                default:
                    throw new InvalidOperationException(
                        $"不支持的模板项类型: {item.Type} (name={item.Name}, target={item.Target})");
            }
        }

        private static void EnsureEmptyDirectoryItem(string targetPath)
        {
            if (File.Exists(targetPath))
            {
                throw new InvalidOperationException($"目标路径已存在同名文件，无法初始化目录: {targetPath}");
            }

            if (Directory.Exists(targetPath))
            {
                return;
            }

            Directory.CreateDirectory(targetPath);
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

            // 目标目录可能已部分存在:递归"仅缺失补齐"(CopyDirectory 跳过已存在文件),绝不覆盖用户改动。
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
                var dest = Path.Combine(targetDir, fileName);
                if (File.Exists(dest))
                {
                    continue; // 仅缺失补齐:已存在文件不覆盖
                }
                File.Copy(file, dest, overwrite: false);
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

            var directPath = Path.Combine(baseDir, "Templates");
            if (Directory.Exists(directPath))
            {
                return directPath;
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
