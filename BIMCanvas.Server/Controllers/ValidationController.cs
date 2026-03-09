using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Core.Models.Revit;
using BIMCanvas.Core.Validation;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ValidationController : ControllerBase
    {
        private readonly ILogger<ValidationController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly JsonSerializerSettings _jsonSettings;

        public ValidationController(
            ILogger<ValidationController> logger,
            ProjectContext projectContext)
        {
            _logger = logger;
            _projectContext = projectContext;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new FacingConverter() }
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
        public ActionResult<SchemeValidationReport> ValidateLayout([FromBody] ValidateLayoutRequest request)
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

                // 3. 读取当前方案的所有模块
                var modules = LoadAllModules(projectPath);

                // 3.5 持久化模块（确保 [OnDeserialized] 自动生成的 Id 写回文件）
                PersistModules(projectPath, modules);

                // 3.6 按 zoneIds 过滤（可选）
                if (request?.ZoneIds is { Count: > 0 } filterIds)
                {
                    var filterSet = new HashSet<string>(filterIds);
                    modules = modules.Where(m => filterSet.Contains(m.ZoneId ?? "_unzoned")).ToList();
                    _logger.LogInformation("[Validation] 按分区过滤: {ZoneIds} → {Count} 个模块",
                        string.Join(", ", filterIds), modules.Count);
                }

                // 4. 调用 Core 层验证
                var report = SchemeValidator.Validate(
                    modules, designZones, exclusionZones, walls, columns);

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
            var schemesZonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (System.IO.File.Exists(schemesZonesPath))
            {
                var schemeZones = ReadJson<List<Zone>>(schemesZonesPath) ?? new List<Zone>();
                designZones.AddRange(schemeZones);
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
        /// 读取所有模块（支持分区子目录格式）
        /// </summary>
        private List<Module> LoadAllModules(string projectPath)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");
            var allModules = new List<Module>();

            if (!Directory.Exists(schemesPath))
            {
                return allModules;
            }

            // 新格式：从分区子目录读取 (rz_*, dz_*, _unzoned)
            var zoneDirs = Directory.GetDirectories(schemesPath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.StartsWith("rz_") || name.StartsWith("dz_") || name == "_unzoned";
                })
                .ToList();

            if (zoneDirs.Count > 0)
            {
                foreach (var zoneDir in zoneDirs)
                {
                    var zoneId = Path.GetFileName(zoneDir);
                    var modulesPath = Path.Combine(zoneDir, "modules.json");
                    if (System.IO.File.Exists(modulesPath))
                    {
                        var modules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
                        foreach (var module in modules)
                        {
                            module.ZoneId ??= zoneId;
                        }
                        allModules.AddRange(modules);
                    }
                }
            }
            else
            {
                // 旧格式：单一 modules.json
                var modulesPath = Path.Combine(schemesPath, "modules.json");
                if (System.IO.File.Exists(modulesPath))
                {
                    allModules = ReadJson<List<Module>>(modulesPath) ?? new List<Module>();
                }
            }

            _logger.LogDebug("[Validation] 加载 {Count} 个模块", allModules.Count);
            return allModules;
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
        /// 将模块按 ZoneId 分组写回对应 schemes/{zoneId}/modules.json
        /// 确保反序列化时自动生成的 Id 被持久化
        /// </summary>
        private void PersistModules(string projectPath, List<Module> modules)
        {
            if (modules.Count == 0) return;

            var byZone = modules
                .GroupBy(m => m.ZoneId ?? "_unzoned")
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in byZone)
            {
                var zoneDir = Path.Combine(projectPath, "schemes", kvp.Key);
                if (!Directory.Exists(zoneDir))
                    Directory.CreateDirectory(zoneDir);

                var modulesPath = Path.Combine(zoneDir, "modules.json");

                // 写入时清理运行时字段 ZoneId
                var toSave = kvp.Value.Select(m =>
                {
                    m.ZoneId = null;
                    return m;
                }).ToList();

                WriteJson(modulesPath, toSave);

                // 恢复 ZoneId 以供后续验证使用
                foreach (var m in kvp.Value)
                    m.ZoneId = kvp.Key;
            }

            _logger.LogDebug("[Validation] 持久化 {Count} 个模块的 Id", modules.Count);
        }
    }

    /// <summary>
    /// validate_layout 请求体（所有字段可选，向后兼容空 body）
    /// </summary>
    public class ValidateLayoutRequest
    {
        /// <summary>仅验证这些 Zone 内的模块（为空或 null 时验证全部）</summary>
        public List<string> ZoneIds { get; set; }
    }
}
