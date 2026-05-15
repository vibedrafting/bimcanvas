using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Core.Models.Shared;
using BIMCanvas.Server.Dtos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// 方案模块数据服务 - 支持跨分支/Worktree 的模块数据读写
    /// </summary>
    public class SchemeDataService
    {
        private readonly ILogger<SchemeDataService> _logger;
        private readonly ProjectContext _projectContext;
        private readonly GitWorktreeService _gitService;
        private readonly ModulesReaderService _modulesReader;
        private readonly ModulesWriterService _modulesWriter;
        private readonly JsonSerializerSettings _jsonSettings;

        public SchemeDataService(
            ILogger<SchemeDataService> logger,
            ProjectContext projectContext,
            GitWorktreeService gitService,
            ModulesReaderService modulesReader,
            ModulesWriterService modulesWriter)
        {
            _logger = logger;
            _projectContext = projectContext;
            _gitService = gitService;
            _modulesReader = modulesReader;
            _modulesWriter = modulesWriter;

            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = Formatting.Indented,
                Converters = { new Polygon2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 解析 source 参数为文件系统路径
        /// </summary>
        /// <param name="source">数据源标识：main, worktree:{name}, branch:{name}</param>
        /// <returns>文件系统路径</returns>
        public string ResolveSourcePath(string source)
        {
            if (string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                throw new InvalidOperationException("未加载项目");
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (source == "main" || string.IsNullOrEmpty(source))
            {
                return projectPath;
            }

            if (source.StartsWith("worktree:"))
            {
                var worktreeName = source.Substring("worktree:".Length);
                var worktreePath = Path.Combine(
                    _gitService.GetWorktreesDir(projectPath),
                    worktreeName);

                if (!Directory.Exists(worktreePath))
                {
                    throw new InvalidOperationException($"Worktree 不存在: {worktreeName}");
                }

                return worktreePath;
            }

            if (source.StartsWith("branch:"))
            {
                // 分支模式需要临时 checkout，性能较低，暂不支持
                throw new NotSupportedException("branch: 模式暂不支持，请使用 worktree: 模式");
            }

            throw new ArgumentException($"无法识别的 source 格式: {source}");
        }

        /// <summary>
        /// 获取 source 对应的分支名
        /// </summary>
        public string? GetBranchName(string source)
        {
            if (string.IsNullOrEmpty(_projectContext.CurrentProjectPath))
            {
                return null;
            }

            var projectPath = _projectContext.CurrentProjectPath;

            if (source == "main" || string.IsNullOrEmpty(source))
            {
                return _gitService.GetCurrentBranch(projectPath);
            }

            if (source.StartsWith("worktree:"))
            {
                var worktreeName = source.Substring("worktree:".Length);
                var worktrees = _gitService.GetWorktrees(projectPath);
                var worktree = worktrees.FirstOrDefault(w =>
                    w.Path.EndsWith(worktreeName, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(w.Path) == worktreeName);
                return worktree?.Branch;
            }

            if (source.StartsWith("branch:"))
            {
                return source.Substring("branch:".Length);
            }

            return null;
        }

        /// <summary>
        /// 读取所有分区的模块数据
        /// </summary>
        /// <param name="basePath">项目基础路径</param>
        /// <returns>合并后的模块列表</returns>
        public List<Module> LoadAllModules(string basePath)
        {
            var modules = new List<Module>();
            var schemesPath = Path.Combine(basePath, "schemes");

            if (!Directory.Exists(schemesPath))
            {
                _logger.LogDebug("schemes 目录不存在: {Path}", schemesPath);
                return modules;
            }

            // 递归遍历所有叶子 zone 的 modules.json（wrapper 形态，Phase 0b 起裸数组已淘汰）
            var leafFiles = ProjectService.FindAllLeafModuleFiles(schemesPath);
            foreach (var (filePath, zoneId) in leafFiles)
            {
                try
                {
                    var zoneModules = _modulesReader.ReadModulesOnly(filePath);
                    if (zoneModules != null)
                    {
                        foreach (var module in zoneModules)
                        {
                            module.ZoneId ??= zoneId;
                        }
                        modules.AddRange(zoneModules);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "读取模块文件失败: {Path}", filePath);
                }
            }

            _logger.LogDebug("从 {Path} 加载了 {Count} 个模块", schemesPath, modules.Count);
            return modules;
        }

        /// <summary>
        /// 按分区保存模块数据
        /// v3.4: 根据模块 bounds 位置自动计算分区
        /// </summary>
        /// <param name="basePath">项目基础路径</param>
        /// <param name="modules">要保存的模块列表</param>
        /// <returns>保存的模块数量</returns>
        public int SaveAllModules(string basePath, List<Module> modules)
        {
            return SaveAllModulesAsync(basePath, modules).GetAwaiter().GetResult();
        }

        public async System.Threading.Tasks.Task<int> SaveAllModulesAsync(string basePath, List<Module> modules)
        {
            var schemesPath = Path.Combine(basePath, "schemes");
            var facingErrors = ValidateFacingForServerWrite(modules);
            if (facingErrors.Count > 0)
            {
                throw new InvalidOperationException(string.Join("；", facingErrors));
            }

            // 确保 schemes 目录存在
            if (!Directory.Exists(schemesPath))
            {
                Directory.CreateDirectory(schemesPath);
            }

            // 读取分区边界
            var roomZones = LoadRoomZones(basePath);

            // 根据 bounds 位置分组
            var modulesByZone = new Dictionary<string, List<Module>>();
            var orphanCount = 0;

            foreach (var module in modules)
            {
                var zoneId = CalculateModuleZone(module, roomZones);

                if (string.IsNullOrEmpty(zoneId))
                {
                    orphanCount++;
                    zoneId = "_unzoned";
                    _logger.LogWarning("[SaveAllModules] 模块 {ModuleId} 不在任何分区内，归入 _unzoned", module.Id);
                }

                if (!modulesByZone.ContainsKey(zoneId))
                    modulesByZone[zoneId] = new List<Module>();
                modulesByZone[zoneId].Add(module);
            }

            var savedCount = 0;

            // 递归清空所有叶子 zone 的 modules.json（支持嵌套分区，防止残留）
            ProjectService.ClearAllLeafModuleFiles(schemesPath);

            // 写入新数据（全部走 ModulesWriterService，wrapper 形态）
            foreach (var kvp in modulesByZone)
            {
                var leafZoneId = kvp.Key;
                var designZoneId = ResolveDesignZoneIdForLeaf(schemesPath, leafZoneId);
                await _modulesWriter.WriteAsync(
                    basePath, designZoneId, leafZoneId,
                    variantId: null, pathMode: VariantPathMode.New, modules: kvp.Value);
                savedCount += kvp.Value.Count;
            }

            _logger.LogInformation("[SaveAllModules] 保存了 {Count} 个模块到 {Path}，{OrphanCount} 个孤立",
                savedCount, schemesPath, orphanCount);
            return savedCount;
        }

        /// <summary>
        /// 把叶子 zoneId 反查为其设计区祖先 ID（与 ProjectController 同款逻辑）。
        /// </summary>
        private static string ResolveDesignZoneIdForLeaf(string schemesPath, string leafZoneId)
        {
            if (string.Equals(leafZoneId, "_unzoned", StringComparison.OrdinalIgnoreCase))
                return leafZoneId;

            var zoneDir = ProjectService.ResolveZoneDirectory(schemesPath, leafZoneId);
            var relative = Path.GetRelativePath(schemesPath, zoneDir).Replace('\\', '/');
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 0 ? segments[0] : leafZoneId;
        }

        /// <summary>
        /// 读取 computed/room_zones.json
        /// </summary>
        private List<Zone> LoadRoomZones(string basePath)
        {
            var zonesPath = Path.Combine(basePath, "computed", "room_zones.json");
            if (!File.Exists(zonesPath))
            {
                _logger.LogWarning("[LoadRoomZones] room_zones.json 不存在: {Path}", zonesPath);
                return new List<Zone>();
            }

            try
            {
                var json = File.ReadAllText(zonesPath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<List<Zone>>(json, _jsonSettings) ?? new List<Zone>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LoadRoomZones] 读取 room_zones.json 失败");
                return new List<Zone>();
            }
        }

        /// <summary>
        /// 根据模块 bounds 中心点计算所属分区
        /// </summary>
        private string? CalculateModuleZone(Module module, List<Zone> roomZones)
        {
            if (module.Bounds == null || module.Bounds.Vertices.Length < 3)
                return null;

            // 计算 bounds 中心点
            var center = module.Bounds.ComputeCenter();

            // 遍历所有房间区域，找到包含该点的分区
            foreach (var zone in roomZones.Where(z => z.Type == ZoneType.Room))
            {
                var boundary = zone.ComputedBoundary ?? zone.RawBoundary;
                if (boundary != null && CollisionDetector.Contains(boundary, center))
                {
                    return zone.Id;
                }
            }

            return null;
        }

        private List<string> ValidateFacingForServerWrite(List<Module> modules)
        {
            var errors = new List<string>();

            foreach (var module in modules)
            {
                if (module.Facing.HasSemantic)
                {
                    errors.Add($"模块 {module.Id} 的 facing.semantic 必须为 null");
                    continue;
                }

                if (!module.Facing.Value.HasValue)
                {
                    errors.Add($"模块 {module.Id} 缺少 facing.value");
                    continue;
                }

                if (!module.Facing.HasFiniteValue() || module.Facing.HasZeroValue() || !module.Facing.TryGetNormalizedValue(out var normalizedValue))
                {
                    errors.Add($"模块 {module.Id} 的 facing.value 不是有效单位向量");
                    continue;
                }

                module.Facing = new Facing(normalizedValue, null);
            }

            return errors;
        }

        /// <summary>
        /// 确保文件可写（移除 ReadOnly 属性）
        /// </summary>
        private static void EnsureWritableFile(string path)
        {
            if (!File.Exists(path))
                return;

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }

        /// <summary>
        /// 获取模块数据（完整响应）
        /// </summary>
        public SchemeModulesResponse GetModules(string source)
        {
            var basePath = ResolveSourcePath(source);
            var modules = LoadAllModules(basePath);
            var branch = GetBranchName(source);

            return new SchemeModulesResponse
            {
                Source = source,
                Branch = branch,
                Modules = modules
            };
        }

        /// <summary>
        /// 保存模块数据
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="request">保存请求</param>
        /// <returns>保存结果</returns>
        public SaveSchemeModulesResponse SaveModules(string source, SaveSchemeModulesRequest request)
        {
            var basePath = ResolveSourcePath(source);
            var savedCount = SaveAllModules(basePath, request.Modules);

            CommitInfo? commitInfo = null;

            // 如果提供了 CommitMessage，自动提交
            if (!string.IsNullOrEmpty(request.CommitMessage))
            {
                try
                {
                    var committed = _gitService.TryCommit(basePath, request.CommitMessage);
                    if (committed)
                    {
                        commitInfo = _gitService.GetBranchCommit(
                            basePath,
                            _gitService.GetCurrentBranch(basePath));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "自动提交失败");
                }
            }

            return new SaveSchemeModulesResponse
            {
                Success = true,
                SavedCount = savedCount,
                Commit = commitInfo
            };
        }
    }
}
