using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Geometry;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Models.Semantic;
using BIMCanvas.Core.Validation;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ValidationController : ControllerBase
    {
        private const double BoundsCoordinateToleranceMm = 0.001;

        private readonly ILogger<ValidationController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly ZoneBoundaryService _zoneBoundaryService;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ModuleLibraryService _moduleLibraryService;
        private readonly JsonSerializerSettings _jsonSettings;

        public ValidationController(
            ILogger<ValidationController> logger,
            ProjectContext projectContext,
            ZoneBoundaryService zoneBoundaryService,
            IHubContext<CanvasHub> hubContext,
            ModuleLibraryService moduleLibraryService)
        {
            _logger = logger;
            _projectContext = projectContext;
            _zoneBoundaryService = zoneBoundaryService;
            _hubContext = hubContext;
            _moduleLibraryService = moduleLibraryService;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 验证当前方案的布局合法性（布局编译器）
        /// 检查所有模块的三类错误：
        /// 1. 模块超出所有设计区域
        /// 2. 模块与墙体/柱子/禁区重叠
        /// 3. 模块之间互相重叠
        /// 可选 zoneIds 参数：仅验证指定分区内的模块
        /// </summary>
        [HttpPost("layout")]
        public ActionResult<SchemeValidationReport> ValidateLayout([FromBody] ValidateLayoutRequest? request)
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            // 支持 Worktree 隔离
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(projectPath))
            {
                return NotFound(new { message = $"项目目录不存在: {projectPath}" });
            }

            try
            {
                _logger.LogInformation("[Validation] 开始验证布局: {Path}", projectPath);

                // 1. 读取 baseline 建筑数据（墙体、柱子）
                var (walls, columns) = LoadArchitectureData(projectPath);

                // 2. 读取 computed 数据（设计区、禁区）
                var (designZones, exclusionZones) = LoadZoneData(projectPath);

                // 3. 按 zoneIds 选择性读取模块（先选文件，再反序列化，不读取无关 zone）
                var requestedZoneIds = request?.ZoneIds;
                var targetSet = requestedZoneIds is { Count: > 0 }
                    ? new HashSet<string>(requestedZoneIds)
                    : null;
                var (modules, structureDiagnostics) = LoadAllModules(projectPath, targetSet);

                // 3.5 校验 facing：validate_layout 只做诊断，不再消费 semantic 或写回文件
                var allDiagnostics = new List<Diagnostic>(structureDiagnostics);
                var facingDiagnostics = ValidateModuleFacings(modules);
                allDiagnostics.AddRange(facingDiagnostics);
                if (facingDiagnostics.Count > 0)
                    _logger.LogWarning("[Validation] facing 预检发现 {Count} 个诊断，继续执行后续检查", facingDiagnostics.Count);

                // 3.6 校验 moduleId 是否在模块库中
                var moduleIdDiagnostics = ValidateModuleIds(modules);
                allDiagnostics.AddRange(moduleIdDiagnostics);

                if (targetSet != null)
                    _logger.LogInformation("[Validation] 按分区加载: {ZoneIds} → {Count} 个模块",
                        string.Join(", ", targetSet), modules.Count);

                // 4. 调用 Core 层验证
                var report = SchemeValidator.Validate(
                    modules, designZones, exclusionZones, walls, columns);

                // 合并所有诊断，TotalModules 包含结构预检过滤掉的模块数
                allDiagnostics.AddRange(report.Diagnostics);
                var totalModules = report.TotalModules + structureDiagnostics.Count;
                report = new SchemeValidationReport
                {
                    TotalModules = totalModules,
                    ErrorCount = allDiagnostics.Count(d => d.Severity == "error"),
                    WarningCount = allDiagnostics.Count(d => d.Severity == "warning"),
                    Diagnostics = allDiagnostics,
                    ElapsedMs = report.ElapsedMs
                };

                _logger.LogInformation(
                    "[Validation] 验证完成: {Total} 个模块, {Errors} 个错误, {ElapsedMs}ms",
                    report.TotalModules, report.ErrorCount, report.ElapsedMs);

                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Validation] 验证失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"验证失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 读取建筑数据（墙体 + 柱子）
        /// </summary>
        private (List<Wall> walls, List<Column> columns) LoadArchitectureData(string projectPath)
        {
            var architecturePath = Path.Combine(projectPath, "baseline", "architecture.json");
            if (!System.IO.File.Exists(architecturePath))
            {
                _logger.LogDebug("[Validation] architecture.json 不存在，跳过墙柱检查");
                return (new List<Wall>(), new List<Column>());
            }

            var arch = ReadJson<Architecture>(architecturePath);
            return (arch.Walls ?? new List<Wall>(), arch.Columns ?? new List<Column>());
        }

        /// <summary>
        /// 读取区域数据（设计区 + 禁区）
        /// </summary>
        private (List<Zone> designZones, List<Zone> exclusionZones) LoadZoneData(string projectPath)
        {
            var designZones = new List<Zone>();
            var exclusionZones = new List<Zone>();

            // room_zones.json（房间区域，Type = Room）
            var roomZonesPath = Path.Combine(projectPath, "computed", "room_zones.json");
            if (System.IO.File.Exists(roomZonesPath))
            {
                var roomZones = ReadJson<List<Zone>>(roomZonesPath) ?? new List<Zone>();
                designZones.AddRange(roomZones);
            }

            // schemes/zones.json（设计区，Type = Designable）
            // 支持嵌套：展平 SubZones，只取叶子 zone 参与验证
            var schemesZonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (System.IO.File.Exists(schemesZonesPath))
            {
                var schemeZones = ReadJson<List<Zone>>(schemesZonesPath) ?? new List<Zone>();
                var leafZones = FlattenToLeafZones(schemeZones);
                designZones.AddRange(leafZones);
            }

            // exclusions.json（禁区，Type = Exclusion）
            var exclusionsPath = Path.Combine(projectPath, "computed", "exclusions.json");
            if (System.IO.File.Exists(exclusionsPath))
            {
                exclusionZones = ReadJson<List<Zone>>(exclusionsPath) ?? new List<Zone>();
            }

            _logger.LogDebug("[Validation] 区域数据: {Design} 个设计区, {Exclusion} 个禁区",
                designZones.Count, exclusionZones.Count);

            return (designZones, exclusionZones);
        }

        /// <summary>
        /// 读取模块（支持分区子目录格式）
        /// 当 targetZoneIds 不为 null 时，只读取目标 zone 的文件，跳过其他 zone
        /// </summary>
        private (List<Module> modules, List<Diagnostic> structureDiagnostics) LoadAllModules(string projectPath, HashSet<string>? targetZoneIds = null)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");
            var allModules = new List<Module>();
            var allStructureDiagnostics = new List<Diagnostic>();

            if (!Directory.Exists(schemesPath))
            {
                return (allModules, allStructureDiagnostics);
            }

            IEnumerable<string> moduleFiles;

            if (targetZoneIds is { Count: > 0 })
            {
                // 精确定位目标 zone 的文件，跳过无关 zone 的反序列化
                var targeted = new List<string>();
                foreach (var zoneId in targetZoneIds)
                {
                    var zoneDir = ResolveZoneDirectory(schemesPath, zoneId);
                    var path = Path.Combine(zoneDir, "modules.json");
                    if (System.IO.File.Exists(path)) targeted.Add(path);
                }
                moduleFiles = targeted;
            }
            else
            {
                // 全量加载：递归查找所有 zone 子目录中的 modules.json
                var zoneFiles = Directory.GetFiles(schemesPath, "modules.json", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var dir = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
                        return dir.StartsWith("rz_") || dir.StartsWith("dz_") || dir == "_unzoned";
                    })
                    .ToList();

                if (zoneFiles.Count > 0)
                {
                    moduleFiles = zoneFiles;
                }
                else
                {
                    // 旧格式：单一 schemes/modules.json
                    var legacyPath = Path.Combine(schemesPath, "modules.json");
                    moduleFiles = System.IO.File.Exists(legacyPath)
                        ? new[] { legacyPath }
                        : Enumerable.Empty<string>();
                }
            }

            foreach (var modulesPath in moduleFiles)
            {
                var zoneId = Path.GetFileName(Path.GetDirectoryName(modulesPath)!);
                try
                {
                    var modules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
                    foreach (var module in modules)
                    {
                        module.ZoneId ??= zoneId;
                    }

                    // validate_layout 的 bounds 结构预检：非法模块直接报结构错误，跳过几何验证。
                    // 避免退化多边形流入 SchemeValidator 产生误诊（如把三角形误报为 E001_OUT_OF_BOUNDS）。
                    var (validModules, structDiagnostics) = FilterInvalidBounds(modules);
                    allStructureDiagnostics.AddRange(structDiagnostics);
                    allModules.AddRange(validModules);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"模块数据解析失败 | 文件: {modulesPath} | Zone: {zoneId} | 原始错误: {ex.Message}", ex);
                }
            }

            _logger.LogDebug("[Validation] 加载 {Count} 个模块（结构预检后）", allModules.Count);
            return (allModules, allStructureDiagnostics);
        }

        /// <summary>
        /// validate_layout bounds 结构预检。
        /// bounds 为 null 的模块报 E006_MISSING_BOUNDS；非 4 顶点、重复顶点、非法坐标或零面积报 E012_INVALID_BOUNDS。
        /// 结构非法模块不进入 SchemeValidator，防止退化多边形在几何计算中产生误诊。
        /// </summary>
        private static (List<Module> valid, List<Diagnostic> diagnostics) FilterInvalidBounds(List<Module> modules)
        {
            var valid = new List<Module>();
            var diagnostics = new List<Diagnostic>();

            foreach (var module in modules)
            {
                var boundsError = GetBoundsStructureError(module);
                if (boundsError != null)
                {
                    var error = boundsError.Value;
                    diagnostics.Add(new Diagnostic(
                        error.Code,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 的 bounds 结构非法：{error.Message}",
                        module.Id,
                        module.ModuleName));
                }
                else
                {
                    valid.Add(module);
                }
            }

            return (valid, diagnostics);
        }

        private static (string Code, string Message)? GetBoundsStructureError(Module module)
        {
            if (module.Bounds == null)
                return (DiagnosticCodes.MissingBounds, "缺少 bounds 定义");

            var vertices = module.Bounds.Vertices;
            if (vertices.Length != 4)
                return (DiagnosticCodes.InvalidBounds, $"顶点数不符合模块规范（{vertices.Length} 个，需要 4 个矩形顶点）");

            if (vertices.Any(v => double.IsNaN(v.X) || double.IsNaN(v.Y) ||
                                  double.IsInfinity(v.X) || double.IsInfinity(v.Y)))
                return (DiagnosticCodes.InvalidBounds, "包含非法坐标值（NaN 或 Infinity）");

            var distinctCount = CountDistinctVertices(vertices);
            if (distinctCount != 4)
                return (DiagnosticCodes.InvalidBounds, $"包含重复顶点，实际有效顶点数 {distinctCount} 个，需要 4 个互不重复的矩形顶点");

            var area = Math.Abs(ComputeSignedArea(vertices));
            if (area <= BoundsCoordinateToleranceMm)
                return (DiagnosticCodes.InvalidBounds, "面积为 0，无法形成有效模块轮廓");

            return null;
        }

        private static int CountDistinctVertices(Point2D[] vertices)
        {
            var distinct = new List<Point2D>();
            foreach (var vertex in vertices)
            {
                if (!distinct.Any(existing => AreSamePoint(existing, vertex)))
                    distinct.Add(vertex);
            }

            return distinct.Count;
        }

        private static bool AreSamePoint(Point2D a, Point2D b)
        {
            return Math.Abs(a.X - b.X) <= BoundsCoordinateToleranceMm &&
                   Math.Abs(a.Y - b.Y) <= BoundsCoordinateToleranceMm;
        }

        private static double ComputeSignedArea(Point2D[] vertices)
        {
            var sum = 0.0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var current = vertices[i];
                var next = vertices[(i + 1) % vertices.Length];
                sum += current.X * next.Y - next.X * current.Y;
            }

            return sum / 2.0;
        }

        /// <summary>
        /// 校验模块 facing 数据。
        /// validate_layout 不消费 semantic；Agent 写入 semantic 后应先调用 normalize_modules。
        /// </summary>
        private static List<Diagnostic> ValidateModuleFacings(List<Module> modules)
        {
            var diagnostics = new List<Diagnostic>();

            foreach (var module in modules)
            {
                if (!module.Facing.Value.HasValue)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.MissingFacingValue,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 缺少 facing.value；若使用 facing.semantic，请先调用 normalize_modules",
                        module.Id,
                        module.ModuleName));
                    continue;
                }

                if (!module.Facing.HasFiniteValue() || module.Facing.HasZeroValue() || !module.Facing.TryGetNormalizedValue(out var normalizedValue))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.InvalidFacingValue,
                        "error",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 的 facing.value 不是有效单位向量",
                        module.Id,
                        module.ModuleName));
                    continue;
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// 校验模块 moduleId 是否存在于模块库中。
        /// 若模块库文件不存在则跳过（降级，不报错）。
        /// </summary>
        private List<Diagnostic> ValidateModuleIds(List<Module> modules)
        {
            var diagnostics = new List<Diagnostic>();

            var library = _moduleLibraryService.GetModuleLibrary();
            if (library?.Modules == null)
                return diagnostics;

            var validIds = new HashSet<string>(library.Modules.Select(m => m.Id), StringComparer.OrdinalIgnoreCase);

            foreach (var module in modules)
            {
                if (string.IsNullOrEmpty(module.ModuleId))
                    continue; // 缺失 moduleId 由 Load 质检闸门处理，这里跳过

                if (!validIds.Contains(module.ModuleId))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.InvalidModuleId,
                        "warning",
                        $"模块 {module.Id} ({module.ModuleName ?? "未命名"}) 的 moduleId '{module.ModuleId}' 不在模块库中",
                        module.Id,
                        module.ModuleName));
                }
            }

            return diagnostics;
        }

        /// <summary>
        /// 递归展平嵌套 zone，只返回叶子 zone（SubZones 为 null 或空）
        /// </summary>
        private List<Zone> FlattenToLeafZones(List<Zone> zones)
        {
            var result = new List<Zone>();
            foreach (var zone in zones)
            {
                if (zone.SubZones != null && zone.SubZones.Count > 0)
                    result.AddRange(FlattenToLeafZones(zone.SubZones));
                else
                    result.Add(zone);
            }
            return result;
        }

        /// <summary>
        /// 解析 zoneId 到实际目录路径（支持嵌套分区）
        /// 例如 dz_1 → schemes/rz_3/dz_1（搜索已有目录）
        /// </summary>
        private string ResolveZoneDirectory(string schemesPath, string zoneId)
        {
            // 1. 检查一级目录
            var directPath = Path.Combine(schemesPath, zoneId);
            if (Directory.Exists(directPath))
                return directPath;

            // 2. 搜索嵌套目录（在 rz_* 父目录下查找）
            if (Directory.Exists(schemesPath))
            {
                foreach (var parentDir in Directory.GetDirectories(schemesPath))
                {
                    var nestedPath = Path.Combine(parentDir, zoneId);
                    if (Directory.Exists(nestedPath))
                        return nestedPath;
                }
            }

            // 3. 回退到一级目录（新建场景）
            return directPath;
        }

        /// <summary>
        /// 读取 JSON 文件并反序列化
        /// </summary>
        private T ReadJson<T>(string path) where T : new()
        {
            var json = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }

        /// <summary>
        /// 序列化并写入 JSON 文件
        /// </summary>
        private void WriteJson<T>(string path, T data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented, _jsonSettings);
            System.IO.File.WriteAllText(path, json, Encoding.UTF8);
        }

        /// <summary>
        /// 获取 Zone 边界语义数据（wall/passage/door/window 段）
        /// Agent 按需调用，实时计算不存文件
        /// </summary>
        [HttpPost("zone-boundaries")]
        public async Task<ActionResult<List<ZoneBoundaryData>>> GetZoneBoundaries(
            [FromBody] ZoneBoundaryRequest request)
        {
            if (!_projectContext.IsLoaded)
            {
                return BadRequest(new { message = "没有加载的项目" });
            }

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(projectPath))
            {
                return NotFound(new { message = $"项目目录不存在: {projectPath}" });
            }

            try
            {
                _logger.LogInformation("[ZoneBoundary] 计算边界段: {Path}", projectPath);

                // 1. 读取 zones（优先 schemes/zones.json，回退 computed/room_zones.json）
                var allZones = LoadAllZones(projectPath);
                if (allZones.Count == 0)
                {
                    return Ok(new List<ZoneBoundaryData>());
                }

                // 2. 读取 openings
                var openings = LoadOpenings(projectPath);

                // 3. 计算
                var results = _zoneBoundaryService.CalculateBoundarySegments(
                    allZones, openings, request?.ZoneIds);

                _logger.LogInformation("[ZoneBoundary] 计算完成: {Count} 个 zone 的边界段", results.Count);

                // 4. Debug 模式：通过 SignalR 推送给 Web 端可视化
                if (request?.Debug == true)
                {
                    var payload = JsonConvert.SerializeObject(results, _jsonSettings);
                    await _hubContext.Clients.All.SendAsync("BoundaryDebugData", payload);
                    _logger.LogInformation("[ZoneBoundary] Debug 模式：已推送 {Count} 个 zone 边界数据到 Web", results.Count);
                }

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ZoneBoundary] 计算失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"边界段计算失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 读取所有 zones（优先 schemes/zones.json，回退 computed/room_zones.json）
        /// </summary>
        private List<Zone> LoadAllZones(string projectPath)
        {
            var schemesZonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (System.IO.File.Exists(schemesZonesPath))
            {
                var zones = ReadJson<List<Zone>>(schemesZonesPath);
                if (zones != null && zones.Count > 0)
                    return zones;
            }

            var roomZonesPath = Path.Combine(projectPath, "computed", "room_zones.json");
            if (System.IO.File.Exists(roomZonesPath))
                return ReadJson<List<Zone>>(roomZonesPath) ?? new List<Zone>();

            return new List<Zone>();
        }

        /// <summary>
        /// 读取 openings
        /// </summary>
        private List<Opening> LoadOpenings(string projectPath)
        {
            var path = Path.Combine(projectPath, "baseline", "openings.json");
            if (!System.IO.File.Exists(path)) return new List<Opening>();
            return ReadJson<List<Opening>>(path) ?? new List<Opening>();
        }
    }

    /// <summary>
    /// validate_layout 请求体（所有字段可选，向后兼容空 body）
    /// </summary>
    public class ValidateLayoutRequest
    {
        /// <summary>仅验证这些 Zone 内的模块（为空或 null 时验证全部）</summary>
        public List<string>? ZoneIds { get; set; }
    }

    /// <summary>
    /// get_zone_boundaries 请求体
    /// </summary>
    public class ZoneBoundaryRequest
    {
        /// <summary>可选，指定要计算的 Zone ID 列表。不传则返回所有叶子 zone。</summary>
        public List<string> ZoneIds { get; set; }

        /// <summary>调试模式：为 true 时通过 SignalR 推送数据给 Web 端进行可视化调试</summary>
        public bool Debug { get; set; }
    }
}
