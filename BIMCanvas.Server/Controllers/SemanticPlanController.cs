using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Models;
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
            var document = ReadSemanticPlanDocument(filePath);

            var entry = new SemanticPlanVersion
            {
                ZoneId = request.ZoneId,
                Version = request.Version,
                PlanType = normalizedPlanType,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            document.Versions.RemoveAll(v => v.Version == request.Version);
            document.Versions.Add(entry);
            document.Versions.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.Ordinal));

            var json = JsonConvert.SerializeObject(document, Formatting.Indented);
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

        [HttpPost("save-reference-analysis")]
        public async Task<ActionResult> SaveReferenceAnalysis([FromBody] SaveReferenceAnalysisRequest request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(request.ZoneId))
                return BadRequest(new { message = "referenceAnalysis 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var filePath = GetSemanticPlanPath(request.ZoneId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var document = ReadSemanticPlanDocument(filePath);

            document.ReferenceAnalysis = new ReferenceAnalysis
            {
                SourceImageId = request.SourceImageId ?? "",
                Relevance = request.Relevance,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            var json = JsonConvert.SerializeObject(document, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);

            await _hubContext.Clients.All.SendAsync("ReferenceAnalysisUpdated", new
            {
                zoneId = request.ZoneId,
                relevance = request.Relevance,
                timestamp = document.ReferenceAnalysis.Timestamp
            });

            _logger.LogInformation(
                "[ReferenceAnalysis] 已保存 {ZoneId} {Relevance}",
                request.ZoneId, request.Relevance);

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                relevance = request.Relevance
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

            var document = ReadSemanticPlanDocument(filePath);
            if (document.Versions.Count == 0)
                return NotFound(new { status = "missing", message = $"{zoneId} 的语义方案为空" });

            if (!TryResolvePlanType(document.Versions, out var planType))
            {
                return Conflict(new
                {
                    status = "ambiguous_legacy",
                    zoneId,
                    message = $"{zoneId} 的旧语义方案无法自动判定 planType，请重新规划或由主控 Agent 介入确认。"
                });
            }

            var effectiveVersion = GetEffectiveVersion(planType!);
            var target = document.Versions.LastOrDefault(v => string.Equals(v.Version, effectiveVersion, StringComparison.Ordinal));
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
                timestamp = target.Timestamp,
                referenceAnalysis = document.ReferenceAnalysis
            });
        }

        private string GetSemanticPlanPath(string zoneId)
        {
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            return Path.Combine(projectPath, "schemes", zoneId, "semantic_plan.json");
        }

        private static SemanticPlanDocument ReadSemanticPlanDocument(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return new SemanticPlanDocument { Versions = new List<SemanticPlanVersion>() };

            var existing = System.IO.File.ReadAllText(filePath);

            // 尝试解析新格式（SemanticPlanDocument）
            try
            {
                var doc = JsonConvert.DeserializeObject<SemanticPlanDocument>(existing);
                if (doc?.Versions != null)
                    return doc;
            }
            catch
            {
                // 忽略解析错误，尝试旧格式
            }

            // 向后兼容：尝试解析旧格式（List<SemanticPlanVersion>）
            try
            {
                var versions = JsonConvert.DeserializeObject<List<SemanticPlanVersion>>(existing);
                if (versions != null)
                {
                    return new SemanticPlanDocument
                    {
                        Versions = versions,
                        ReferenceAnalysis = null
                    };
                }
            }
            catch
            {
                // 忽略解析错误
            }

            return new SemanticPlanDocument { Versions = new List<SemanticPlanVersion>() };
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

    public class SaveReferenceAnalysisRequest
    {
        public string ZoneId { get; set; }
        public string SourceImageId { get; set; }
        public string Relevance { get; set; }
        public string Content { get; set; }
    }
}
