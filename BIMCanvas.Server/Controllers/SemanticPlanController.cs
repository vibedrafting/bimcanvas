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

            // 新流程已经迁出 referenceAnalysis，保存 semantic_plan 时顺便清理旧嵌入字段。
            document.LegacyEmbeddedReferenceAnalysis = null;

            var entry = new SemanticPlanVersion
            {
                ZoneId = request.ZoneId,
                Version = request.Version,
                PlanType = normalizedPlanType,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o"),
                ReferenceAnalysisVersion = NormalizeReferenceAnalysisVersion(request.ReferenceAnalysisVersion)
            };

            document.Versions ??= new List<SemanticPlanVersion>();
            document.Versions.RemoveAll(v => string.Equals(v.Version, request.Version, StringComparison.Ordinal));
            document.Versions.Add(entry);
            document.Versions.Sort((a, b) => string.Compare(a.Version, b.Version, StringComparison.Ordinal));

            await WriteSemanticPlanDocumentAsync(filePath, document);

            await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
            {
                zoneId = request.ZoneId,
                version = request.Version,
                planType = entry.PlanType,
                referenceAnalysisVersion = entry.ReferenceAnalysisVersion,
                content = request.Content,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[SemanticPlan] 已保存 {ZoneId} {PlanType} {Version}（reference={ReferenceAnalysisVersion}）",
                request.ZoneId, entry.PlanType, request.Version, entry.ReferenceAnalysisVersion ?? "-");

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                planType = entry.PlanType,
                version = request.Version,
                referenceAnalysisVersion = entry.ReferenceAnalysisVersion
            });
        }

        [HttpPost("save-reference-analysis")]
        public async Task<ActionResult> SaveReferenceAnalysis([FromBody] SaveReferenceAnalysisRequest request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(request.ZoneId))
                return BadRequest(new { message = "reference_analysis 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            if (!IsSupportedRelevance(request.Relevance))
                return BadRequest(new { message = "relevance 必须是 partially_related 或 structurally_related" });

            var filePath = GetReferenceAnalysisPath(request.ZoneId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var versions = ReadReferenceAnalysisVersions(request.ZoneId);
            var nextVersion = GetNextReferenceAnalysisVersion(versions);
            var entry = new ReferenceAnalysisVersionEntry
            {
                Version = nextVersion,
                SourceImageId = request.SourceImageId ?? string.Empty,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            versions.Add(entry);
            versions.Sort((a, b) => CompareReferenceAnalysisVersion(a.Version, b.Version));

            await WriteReferenceAnalysisDocumentAsync(filePath, versions);
            await RemoveLegacyEmbeddedReferenceAnalysisAsync(request.ZoneId);

            await _hubContext.Clients.All.SendAsync("ReferenceAnalysisUpdated", new
            {
                zoneId = request.ZoneId,
                version = entry.Version,
                relevance = request.Relevance,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[ReferenceAnalysis] 已保存 {ZoneId} {Version}（{Relevance}）",
                request.ZoneId, entry.Version, request.Relevance);

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                version = entry.Version,
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
            if (document.Versions == null || document.Versions.Count == 0)
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

            var target = document.Versions.LastOrDefault(v => string.Equals(v.Version, "v0.3", StringComparison.Ordinal));
            if (target == null)
            {
                if (string.Equals(planType, PlanTypeReference, StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(new
                    {
                        status = "legacy_reference_requires_replan",
                        zoneId,
                        message = $"{zoneId} 当前仍是旧版 reference 工作流（缺少可施工的 v0.3 自包含合同）。请重新执行规划。"
                    });
                }

                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    message = $"未找到 {zoneId} 的生效图纸 v0.3"
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
                referenceAnalysisVersion = target.ReferenceAnalysisVersion
            });
        }

        [HttpGet("{zoneId}/reference-analysis")]
        public ActionResult LoadReferenceAnalysis(string zoneId, [FromQuery] string version = null)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(zoneId))
                return BadRequest(new { message = "reference_analysis 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var versions = ReadReferenceAnalysisVersions(zoneId);
            if (versions.Count == 0)
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    message = $"未找到 {zoneId} 的参考分析"
                });
            }

            ReferenceAnalysisVersionEntry target;
            if (string.IsNullOrWhiteSpace(version))
            {
                target = versions
                    .OrderBy(v => v.Version, Comparer<string>.Create(CompareReferenceAnalysisVersion))
                    .Last();
            }
            else
            {
                target = versions.LastOrDefault(v => string.Equals(v.Version, version, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    return NotFound(new
                    {
                        status = "missing",
                        zoneId,
                        version,
                        message = $"未找到 {zoneId} 的参考分析 {version}"
                    });
                }
            }

            return Ok(new
            {
                status = "ok",
                zoneId,
                version = target.Version,
                sourceImageId = target.SourceImageId,
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

        private string GetReferenceAnalysisPath(string zoneId)
        {
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            return Path.Combine(projectPath, "schemes", zoneId, "reference_analysis.json");
        }

        private static SemanticPlanDocument ReadSemanticPlanDocument(string filePath)
        {
            if (!System.IO.File.Exists(filePath))
                return new SemanticPlanDocument { Versions = new List<SemanticPlanVersion>() };

            var existing = System.IO.File.ReadAllText(filePath);

            try
            {
                var doc = JsonConvert.DeserializeObject<SemanticPlanDocument>(existing);
                if (doc?.Versions != null)
                {
                    doc.Versions ??= new List<SemanticPlanVersion>();
                    return doc;
                }
            }
            catch
            {
                // 忽略解析错误，继续尝试旧格式
            }

            try
            {
                var versions = JsonConvert.DeserializeObject<List<SemanticPlanVersion>>(existing);
                if (versions != null)
                {
                    return new SemanticPlanDocument
                    {
                        Versions = versions,
                        LegacyEmbeddedReferenceAnalysis = null
                    };
                }
            }
            catch
            {
                // 忽略解析错误
            }

            return new SemanticPlanDocument { Versions = new List<SemanticPlanVersion>() };
        }

        private List<ReferenceAnalysisVersionEntry> ReadReferenceAnalysisVersions(string zoneId)
        {
            var referenceAnalysisPath = GetReferenceAnalysisPath(zoneId);
            if (System.IO.File.Exists(referenceAnalysisPath))
            {
                try
                {
                    var existing = System.IO.File.ReadAllText(referenceAnalysisPath);
                    var versions = JsonConvert.DeserializeObject<List<ReferenceAnalysisVersionEntry>>(existing);
                    if (versions != null)
                        return versions;
                }
                catch
                {
                    // 忽略解析错误，尝试读取 legacy 数据
                }
            }

            var semanticPlanPath = GetSemanticPlanPath(zoneId);
            if (!System.IO.File.Exists(semanticPlanPath))
                return new List<ReferenceAnalysisVersionEntry>();

            var document = ReadSemanticPlanDocument(semanticPlanPath);
            var legacy = document.LegacyEmbeddedReferenceAnalysis;
            if (legacy == null || string.IsNullOrWhiteSpace(legacy.Content))
                return new List<ReferenceAnalysisVersionEntry>();

            return new List<ReferenceAnalysisVersionEntry>
            {
                new ReferenceAnalysisVersionEntry
                {
                    Version = "v1",
                    SourceImageId = legacy.SourceImageId ?? string.Empty,
                    Content = legacy.Content,
                    Timestamp = string.IsNullOrWhiteSpace(legacy.Timestamp)
                        ? DateTime.UtcNow.ToString("o")
                        : legacy.Timestamp
                }
            };
        }

        private async Task RemoveLegacyEmbeddedReferenceAnalysisAsync(string zoneId)
        {
            var semanticPlanPath = GetSemanticPlanPath(zoneId);
            if (!System.IO.File.Exists(semanticPlanPath))
                return;

            var document = ReadSemanticPlanDocument(semanticPlanPath);
            if (document.LegacyEmbeddedReferenceAnalysis == null)
                return;

            document.LegacyEmbeddedReferenceAnalysis = null;
            await WriteSemanticPlanDocumentAsync(semanticPlanPath, document);
        }

        private static async Task WriteSemanticPlanDocumentAsync(string filePath, SemanticPlanDocument document)
        {
            document.Versions ??= new List<SemanticPlanVersion>();
            var json = JsonConvert.SerializeObject(document, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }

        private static async Task WriteReferenceAnalysisDocumentAsync(string filePath, List<ReferenceAnalysisVersionEntry> versions)
        {
            var json = JsonConvert.SerializeObject(versions, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }

        private static bool IsDesignZoneId(string zoneId)
        {
            return !string.IsNullOrWhiteSpace(zoneId)
                   && !zoneId.StartsWith("dz_", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeReferenceAnalysisVersion(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }

        private static string NormalizePlanType(string planType)
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
            out string planType)
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

        private static bool IsSupportedRelevance(string relevance)
        {
            return string.Equals(relevance, "partially_related", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(relevance, "structurally_related", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetNextReferenceAnalysisVersion(IEnumerable<ReferenceAnalysisVersionEntry> versions)
        {
            var maxNumber = versions
                .Select(v => TryParseReferenceAnalysisVersionNumber(v.Version))
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .DefaultIfEmpty(0)
                .Max();

            return $"v{maxNumber + 1}";
        }

        private static int CompareReferenceAnalysisVersion(string left, string right)
        {
            var leftNumber = TryParseReferenceAnalysisVersionNumber(left);
            var rightNumber = TryParseReferenceAnalysisVersionNumber(right);

            if (leftNumber.HasValue && rightNumber.HasValue)
                return leftNumber.Value.CompareTo(rightNumber.Value);

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int? TryParseReferenceAnalysisVersionNumber(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            var trimmed = version.Trim();
            if (trimmed.Length < 2 || trimmed[0] != 'v')
                return null;

            if (int.TryParse(trimmed.Substring(1), out var number))
                return number;

            return null;
        }
    }

    public class SaveSemanticPlanRequest
    {
        public string ZoneId { get; set; }
        public string Version { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
        public string ReferenceAnalysisVersion { get; set; }
    }

    public class SaveReferenceAnalysisRequest
    {
        public string ZoneId { get; set; }
        public string SourceImageId { get; set; }
        public string Relevance { get; set; }
        public string Content { get; set; }
    }
}
