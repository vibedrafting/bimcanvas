using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BIMCanvas.Core.Algorithms.Spatial;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Computed;
using BIMCanvas.Core.Models.Project;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/spatial-marks")]
    public class SpatialMarksController : ControllerBase
    {
        private const int MaxCells = 5000;

        private readonly ILogger<SpatialMarksController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly JsonSerializerSettings _jsonSettings;

        public SpatialMarksController(
            ILogger<SpatialMarksController> logger,
            ProjectContext projectContext)
        {
            _logger = logger;
            _projectContext = projectContext;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters =
                {
                    new Polygon2DConverter(),
                    new AABBConverter()
                }
            };
        }

        [HttpPost("merge-grid-selection")]
        public ActionResult<MergeGridSelectionResponse> MergeGridSelection(
            [FromBody] MergeGridSelectionRequest? request)
        {
            var validationError = ValidateRequest(request);
            if (validationError != null)
                return BadRequest(new { message = validationError });

            if (!_projectContext.IsLoaded || string.IsNullOrWhiteSpace(_projectContext.CurrentProjectPath))
                return BadRequest(new { message = "未加载项目" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            if (!Directory.Exists(projectPath))
                return NotFound(new { message = $"当前项目目录或 active worktree 目录不存在: {projectPath}" });

            try
            {
                var project = ReadProject(projectPath);
                if (string.IsNullOrWhiteSpace(project.ActiveSchemeId))
                    return BadRequest(new { message = "项目没有 active scheme" });

                var zones = ReadTopLevelZones(projectPath);
                var zone = zones.FirstOrDefault(z => string.Equals(z.Id, request!.ZoneId, StringComparison.Ordinal));
                if (zone == null)
                    return BadRequest(new { message = $"zoneId 不在顶层 activeScheme.zones 中: {request!.ZoneId}" });

                var boundary = zone.ComputedBoundary ?? zone.RawBoundary;
                if (boundary == null || boundary.Vertices.Length < 3)
                    return BadRequest(new { message = $"目标 zone 缺少有效边界: {request!.ZoneId}" });

                var cells = request!.Cells.Select(c => (c.Col, c.Row));
                var gridOriginX = request.GridOriginX ?? 0;
                var gridOriginY = request.GridOriginY ?? 0;

                var geometry = GridSelectionMerger.MergeGridCells(
                    boundary,
                    gridOriginX,
                    gridOriginY,
                    request.CellSize,
                    cells,
                    0,
                    0);

                _logger.LogInformation(
                    "[SpatialMarks] 合并网格选择: Zone={ZoneId}, Cells={CellCount}, Geometry={GeometryCount}, Path={Path}",
                    request.ZoneId,
                    request.Cells.Count,
                    geometry.Count,
                    projectPath);

                return Ok(new MergeGridSelectionResponse
                {
                    ZoneId = request.ZoneId,
                    Geometry = geometry
                });
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[SpatialMarks] JSON 读取失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"JSON 读取失败: {ex.Message}" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SpatialMarks] 合并网格选择失败: {Path}", projectPath);
                return StatusCode(500, new { message = $"合并网格选择失败: {ex.Message}" });
            }
        }

        private static string? ValidateRequest(MergeGridSelectionRequest? request)
        {
            if (request == null)
                return "请求体不能为空";
            if (string.IsNullOrWhiteSpace(request.ZoneId))
                return "zoneId 不能为空";
            if (request.CellSize <= 0 || double.IsNaN(request.CellSize) || double.IsInfinity(request.CellSize))
                return "cellSize 必须大于 0";
            if (request.Cells == null || request.Cells.Count == 0)
                return "cells 不能为空";
            if (request.Cells.Count > MaxCells)
                return $"cells 数量不能超过 {MaxCells}，请调大网格尺寸";
            return null;
        }

        private Project ReadProject(string projectPath)
        {
            var projectJsonPath = Path.Combine(projectPath, "project.json");
            if (!System.IO.File.Exists(projectJsonPath))
                throw new FileNotFoundException("project.json 不存在", projectJsonPath);

            return ReadJson<Project>(projectJsonPath);
        }

        private List<Zone> ReadTopLevelZones(string projectPath)
        {
            var zonesPath = Path.Combine(projectPath, "schemes", "zones.json");
            if (!System.IO.File.Exists(zonesPath))
                return new List<Zone>();

            return ReadJson<List<Zone>>(zonesPath) ?? new List<Zone>();
        }

        private T ReadJson<T>(string path) where T : new()
        {
            var json = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings) ?? new T();
        }
    }
}
