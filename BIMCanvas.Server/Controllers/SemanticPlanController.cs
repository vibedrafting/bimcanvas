using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BIMCanvas.Server.Controllers
{
    [ApiController]
    [Route("api/semantic-plan")]
    public class SemanticPlanController : ControllerBase
    {
        private const string PlanTypeDerived = "derived";
        private const string PlanTypeReference = "reference";

        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ILogger<SemanticPlanController> _logger;

        public SemanticPlanController(
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            ILogger<SemanticPlanController> logger)
        {
            _projectContext = projectContext;
            _hubContext = hubContext;
            _logger = logger;
        }

        [HttpPost("save")]
        public async Task<ActionResult> SaveSemanticPlan([FromBody] SaveSemanticPlanRequest request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(request.ZoneId))
                return BadRequest(new { message = "semantic_plan 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var normalizedPlanType = NormalizePlanType(request.PlanType);
            if (normalizedPlanType == null)
                return BadRequest(new { message = "planType 必须是 derived 或 reference" });

            var filePath = GetSemanticPlanPath(request.ZoneId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var versions = ReadSemanticPlanVersions(filePath);

            var entry = new SemanticPlanVersion
            {
                ZoneId = request.ZoneId,
                Version = request.Version,
                PlanType = normalizedPlanType,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            versions.RemoveAll(v => v.Version == request.Version);
            versions.Add(entry);
            versions.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.Ordinal));

            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);

            await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
            {
                zoneId = request.ZoneId,
                version = request.Version,
                planType = entry.PlanType,
                content = request.Content,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[SemanticPlan] 已保存 {ZoneId} {PlanType} {Version}",
                request.ZoneId, entry.PlanType, request.Version);

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                planType = entry.PlanType,
                version = request.Version
            });
        }

        [HttpGet("{zoneId}")]
        public ActionResult LoadSemanticPlan(string zoneId)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(zoneId))
                return BadRequest(new { message = "semantic_plan 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var filePath = GetSemanticPlanPath(zoneId);
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { status = "missing", message = $"未找到 {zoneId} 的语义方案" });

            var versions = ReadSemanticPlanVersions(filePath);
            if (versions.Count == 0)
                return NotFound(new { status = "missing", message = $"{zoneId} 的语义方案为空" });

            if (!TryResolvePlanType(versions, out var planType))
            {
                return Conflict(new
                {
                    status = "ambiguous_legacy",
                    zoneId,
                    message = $"{zoneId} 的旧语义方案无法自动判定 planType，请重新规划或由主控 Agent 介入确认。"
                });
            }

            var effectiveVersion = GetEffectiveVersion(planType!);
            var target = versions.LastOrDefault(v => string.Equals(v.Version, effectiveVersion, StringComparison.Ordinal));
            if (target == null)
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    message = $"未找到 {zoneId} 的生效图纸 {effectiveVersion}"
                });
            }

            return Ok(new
            {
                status = "ok",
                zoneId = target.ZoneId,
                planType,
                effectiveVersion = target.Version,
                content = target.Content,
                timestamp = target.Timestamp
            });
        }

        private string GetSemanticPlanPath(string zoneId)
        {
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            return Path.Combine(projectPath, "schemes", zoneId, "semantic_plan.json");
        }

        private static List<SemanticPlanVersion> ReadSemanticPlanVersions(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return new List<SemanticPlanVersion>();

            var existing = System.IO.File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<List<SemanticPlanVersion>>(existing)
                   ?? new List<SemanticPlanVersion>();
        }

        private static bool IsDesignZoneId(string zoneId)
        {
            return !string.IsNullOrWhiteSpace(zoneId)
                   && !zoneId.StartsWith("dz_", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizePlanType(string? planType)
        {
            if (string.IsNullOrWhiteSpace(planType))
                return null;

            if (string.Equals(planType, PlanTypeDerived, StringComparison.OrdinalIgnoreCase))
                return PlanTypeDerived;

            if (string.Equals(planType, PlanTypeReference, StringComparison.OrdinalIgnoreCase))
                return PlanTypeReference;

            return null;
        }

        private static bool TryResolvePlanType(
            IEnumerable<SemanticPlanVersion> versions,
            out string? planType)
        {
            var normalizedTypes = versions
                .Select(v => NormalizePlanType(v.PlanType))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalizedTypes.Count == 1)
            {
                planType = normalizedTypes[0];
                return true;
            }

            if (normalizedTypes.Count > 1)
            {
                planType = null;
                return false;
            }

            var hasV03 = versions.Any(v => string.Equals(v.Version, "v0.3", StringComparison.Ordinal));
            if (hasV03)
            {
                planType = PlanTypeDerived;
                return true;
            }

            var latestVersion = versions
                .Select(v => v.Version)
                .OrderBy(v => v, StringComparer.Ordinal)
                .LastOrDefault();

            if (string.Equals(latestVersion, "v0.2", StringComparison.Ordinal))
            {
                var hasReferenceTitle = versions.Any(v =>
                    !string.IsNullOrWhiteSpace(v.Content)
                    && v.Content.IndexOf("识别方案", StringComparison.OrdinalIgnoreCase) >= 0);

                if (hasReferenceTitle)
                {
                    planType = PlanTypeReference;
                    return true;
                }
            }

            planType = null;
            return false;
        }

        private static string GetEffectiveVersion(string planType)
        {
            return string.Equals(planType, PlanTypeReference, StringComparison.OrdinalIgnoreCase)
                ? "v0.2"
                : "v0.3";
        }
    }

    public class SaveSemanticPlanRequest
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
    }

    public class SemanticPlanVersion
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
        public string Timestamp { get; set; }
    }
}
