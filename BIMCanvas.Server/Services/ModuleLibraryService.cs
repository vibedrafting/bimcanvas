using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 模块库服务 - 管理当前项目的模块库资源
    /// 提供模块库 JSON 和 SVG 文件的读取、缓存功能
    /// </summary>
    public class ModuleLibraryService
    {
        private readonly ProjectContext _projectContext;
        private readonly ILogger<ModuleLibraryService> _logger;

        // 缓存
        private ModuleLibraryDto? _cachedLibrary;
        private string? _cachedProjectPath;
        private readonly ConcurrentDictionary<string, (string Content, string ETag)> _svgCache = new();

        private readonly JsonSerializerSettings _jsonSettings;

        public ModuleLibraryService(ProjectContext projectContext, ILogger<ModuleLibraryService> logger)
        {
            _projectContext = projectContext;
            _logger = logger;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };
        }

        /// <summary>
        /// 获取模块库（带缓存）
        /// svgPath 会被转换为 API 路径格式
        /// </summary>
        public ModuleLibraryDto? GetModuleLibrary()
        {
            if (!_projectContext.IsLoaded)
            {
                _logger.LogWarning("[ModuleLibraryService] 没有加载的项目");
                return null;
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            // 缓存命中检查
            if (_cachedLibrary != null && _cachedProjectPath == projectPath)
            {
                return _cachedLibrary;
            }

            var libraryPath = Path.Combine(projectPath, "modules", "module_library.json");
            if (!File.Exists(libraryPath))
            {
                _logger.LogWarning("[ModuleLibraryService] 模块库文件不存在: {Path}", libraryPath);
                return null;
            }

            try
            {
                var json = File.ReadAllText(libraryPath, Encoding.UTF8);
                var library = JsonConvert.DeserializeObject<ModuleLibraryDto>(json, _jsonSettings);

                if (library?.Modules != null)
                {
                    // 转换 svgPath 为 API 路径
                    foreach (var module in library.Modules)
                    {
                        module.SvgPath = $"/api/modules/svg/{module.Id}";
                    }
                }

                _cachedLibrary = library;
                _cachedProjectPath = projectPath;

                _logger.LogInformation("[ModuleLibraryService] 加载模块库成功，共 {Count} 个模块",
                    library?.Modules?.Count ?? 0);

                return library;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ModuleLibraryService] 读取模块库失败: {Path}", libraryPath);
                return null;
            }
        }

        /// <summary>
        /// 获取 SVG 文件内容（带缓存）
        /// </summary>
        /// <param name="moduleId">模块ID</param>
        /// <returns>(SVG内容, ETag) 或 (null, null)</returns>
        public (string? Content, string? ETag) GetSvgContent(string moduleId)
        {
            if (!_projectContext.IsLoaded)
            {
                return (null, null);
            }

            // 检查缓存
            if (_svgCache.TryGetValue(moduleId, out var cached))
            {
                return (cached.Content, cached.ETag);
            }

            var projectPath = _projectContext.CurrentProjectPath!;

            // 构建 SVG 文件路径
            // 约定：SVG 文件名与 moduleId 相同，位于 modules/assets/ 目录
            var svgFileName = $"{moduleId}.svg";
            var svgPath = Path.Combine(projectPath, "modules", "assets", svgFileName);

            if (!File.Exists(svgPath))
            {
                _logger.LogWarning("[ModuleLibraryService] SVG 文件不存在: {Path}", svgPath);
                return (null, null);
            }

            try
            {
                var content = File.ReadAllText(svgPath, Encoding.UTF8);
                var eTag = ComputeETag(content);

                // 加入缓存
                _svgCache[moduleId] = (content, eTag);

                _logger.LogDebug("[ModuleLibraryService] 加载 SVG: {ModuleId}", moduleId);
                return (content, eTag);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ModuleLibraryService] 读取 SVG 失败: {Path}", svgPath);
                return (null, null);
            }
        }

        /// <summary>
        /// 清除缓存（项目切换时调用）
        /// </summary>
        public void ClearCache()
        {
            _cachedLibrary = null;
            _cachedProjectPath = null;
            _svgCache.Clear();
            _logger.LogDebug("[ModuleLibraryService] 缓存已清除");
        }

        /// <summary>
        /// 计算内容的 ETag（用于 HTTP 缓存）
        /// </summary>
        private static string ComputeETag(string content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
            // 取前16个字符作为 ETag
            return $"\"{Convert.ToBase64String(hash).Substring(0, 16)}\"";
        }
    }

    #region DTOs

    /// <summary>
    /// 模块库 DTO
    /// </summary>
    public class ModuleLibraryDto
    {
        public string? Version { get; set; }
        public List<ModuleDefinitionDto>? Modules { get; set; }
    }

    /// <summary>
    /// 模块定义 DTO
    /// </summary>
    public class ModuleDefinitionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string>? Tags { get; set; }
        public ModuleSizeDto? Size { get; set; }
        public string? Description { get; set; }
        public string SvgPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 模块尺寸 DTO
    /// </summary>
    public class ModuleSizeDto
    {
        public double Width { get; set; }
        public double Depth { get; set; }
    }

    #endregion
}
