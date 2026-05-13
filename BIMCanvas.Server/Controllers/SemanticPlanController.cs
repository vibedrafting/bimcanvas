using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        private static readonly HashSet<string> AllowedSemanticPlanTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "v0.1",
            "v0.2",
            "v0.2-meta",
            "v0.3"
        };

        private static readonly Regex ReferenceAnalysisTagPattern = new Regex(@"^v[1-9][0-9]*$", RegexOptions.Compiled);

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

            if (string.IsNullOrWhiteSpace(request.Tag) || !AllowedSemanticPlanTags.Contains(request.Tag))
            {
                var allowed = string.Join(", ", AllowedSemanticPlanTags);
                return BadRequest(new { message = $"非法 tag: {request.Tag ?? "(空)"}。合法值：{allowed}" });
            }

            var filePath = GetSemanticPlanPath(request.ZoneId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var document = ReadSemanticPlanDocument(filePath);

            // 新流程已经迁出 referenceAnalysis，保存 semantic_plan 时顺便清理旧嵌入字段。
            document.LegacyEmbeddedReferenceAnalysis = null;

            var entry = new SemanticPlanEntry
            {
                ZoneId = request.ZoneId,
                Tag = request.Tag,
                PlanType = normalizedPlanType,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o"),
                ReferenceAnalysisTag = NormalizeReferenceAnalysisTag(request.ReferenceAnalysisTag)
            };

            document.Entries ??= new List<SemanticPlanEntry>();
            document.Entries.RemoveAll(v => string.Equals(v.Tag, request.Tag, StringComparison.Ordinal));
            document.Entries.Add(entry);
            document.Entries.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.Ordinal));

            await WriteSemanticPlanDocumentAsync(filePath, document);

            await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
            {
                zoneId = request.ZoneId,
                tag = request.Tag,
                planType = entry.PlanType,
                referenceAnalysisTag = entry.ReferenceAnalysisTag,
                content = request.Content,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[SemanticPlan] 已保存 {ZoneId} {PlanType} {Tag}（reference={ReferenceAnalysisTag}）",
                request.ZoneId, entry.PlanType, request.Tag, entry.ReferenceAnalysisTag ?? "-");

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                planType = entry.PlanType,
                tag = request.Tag,
                referenceAnalysisTag = entry.ReferenceAnalysisTag
            });
        }

        [HttpPost("save-reference-analysis")]
        public async Task<ActionResult> SaveReferenceAnalysis([FromBody] SaveReferenceAnalysisRequest request)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(request.ZoneId))
                return BadRequest(new { message = "reference_analysis 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var filePath = GetReferenceAnalysisPath(request.ZoneId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            var entries = ReadReferenceAnalysisEntries(request.ZoneId);
            var nextTag = GetNextReferenceAnalysisTag(entries);
            var entry = new ReferenceAnalysisEntry
            {
                Tag = nextTag,
                SourceImageId = request.SourceImageId ?? string.Empty,
                Content = request.Content,
                Timestamp = DateTime.UtcNow.ToString("o")
            };

            entries.Add(entry);
            entries.Sort((a, b) => CompareReferenceAnalysisTag(a.Tag, b.Tag));

            await WriteReferenceAnalysisDocumentAsync(filePath, entries);
            await RemoveLegacyEmbeddedReferenceAnalysisAsync(request.ZoneId);

            await _hubContext.Clients.All.SendAsync("ReferenceAnalysisUpdated", new
            {
                zoneId = request.ZoneId,
                tag = entry.Tag,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[ReferenceAnalysis] 已保存 {ZoneId} {Tag}",
                request.ZoneId, entry.Tag);

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                tag = entry.Tag
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
            if (document.Entries == null || document.Entries.Count == 0)
                return NotFound(new { status = "missing", message = $"{zoneId} 的语义方案为空" });

            if (!TryResolvePlanType(document.Entries, out var planType))
            {
                return Conflict(new
                {
                    status = "ambiguous_legacy",
                    zoneId,
                    message = $"{zoneId} 的旧语义方案无法自动判定 planType，请重新规划或由主控 Agent 介入确认。"
                });
            }

            var target = document.Entries.LastOrDefault(v => string.Equals(v.Tag, "v0.3", StringComparison.Ordinal));
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
                effectiveTag = target.Tag,
                content = target.Content,
                timestamp = target.Timestamp,
                referenceAnalysisTag = target.ReferenceAnalysisTag
            });
        }

        [HttpGet("{zoneId}/reference-analysis")]
        public ActionResult LoadReferenceAnalysis(string zoneId, [FromQuery] string tag = null)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(zoneId))
                return BadRequest(new { message = "reference_analysis 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            var entries = ReadReferenceAnalysisEntries(zoneId);
            if (entries.Count == 0)
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    message = $"未找到 {zoneId} 的参考分析"
                });
            }

            ReferenceAnalysisEntry target;
            if (string.IsNullOrWhiteSpace(tag))
            {
                target = entries
                    .OrderBy(v => v.Tag, Comparer<string>.Create(CompareReferenceAnalysisTag))
                    .Last();
            }
            else
            {
                target = entries.LastOrDefault(v => string.Equals(v.Tag, tag, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                {
                    return NotFound(new
                    {
                        status = "missing",
                        zoneId,
                        tag,
                        message = $"未找到 {zoneId} 的参考分析 {tag}"
                    });
                }
            }

            return Ok(new
            {
                status = "ok",
                zoneId,
                tag = target.Tag,
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
            // Phase 0 起 schema 已从 `version` 全面切换为 `tag`，不再做向后兼容读取。
            // 旧版字段（versions / version / referenceAnalysisVersion）写的文件会反序列化为 Tag=null，
            // 进而被 TryResolvePlanType / Save 白名单识别为不可用并要求重新生成。
            if (!System.IO.File.Exists(filePath))
                return new SemanticPlanDocument { Entries = new List<SemanticPlanEntry>() };

            var existing = System.IO.File.ReadAllText(filePath);
            var doc = JsonConvert.DeserializeObject<SemanticPlanDocument>(existing)
                      ?? new SemanticPlanDocument();
            doc.Entries ??= new List<SemanticPlanEntry>();
            return doc;
        }

        private List<ReferenceAnalysisEntry> ReadReferenceAnalysisEntries(string zoneId)
        {
            // Phase 0 起 schema 已从 `version` 切换为 `tag`。旧 schema 仍能反序列化（字段类型相同），
            // 但 Tag 为 null 的条目对调用方而言相当于损坏数据；不主动迁移。
            // LegacyEmbeddedReferenceAnalysis（更老的内嵌格式）保留单一迁移点，作为最低限度的回退入口。
            var referenceAnalysisPath = GetReferenceAnalysisPath(zoneId);
            if (System.IO.File.Exists(referenceAnalysisPath))
            {
                var existing = System.IO.File.ReadAllText(referenceAnalysisPath);
                var entries = JsonConvert.DeserializeObject<List<ReferenceAnalysisEntry>>(existing);
                if (entries != null)
                    return entries;
            }

            var semanticPlanPath = GetSemanticPlanPath(zoneId);
            if (!System.IO.File.Exists(semanticPlanPath))
                return new List<ReferenceAnalysisEntry>();

            var document = ReadSemanticPlanDocument(semanticPlanPath);
            var legacy = document.LegacyEmbeddedReferenceAnalysis;
            if (legacy == null || string.IsNullOrWhiteSpace(legacy.Content))
                return new List<ReferenceAnalysisEntry>();

            return new List<ReferenceAnalysisEntry>
            {
                new ReferenceAnalysisEntry
                {
                    Tag = "v1",
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
            document.Entries ??= new List<SemanticPlanEntry>();
            var json = JsonConvert.SerializeObject(document, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }

        private static async Task WriteReferenceAnalysisDocumentAsync(string filePath, List<ReferenceAnalysisEntry> entries)
        {
            var json = JsonConvert.SerializeObject(entries, Formatting.Indented);
            await System.IO.File.WriteAllTextAsync(filePath, json);
        }

        private static bool IsDesignZoneId(string zoneId)
        {
            return !string.IsNullOrWhiteSpace(zoneId)
                   && !zoneId.StartsWith("dz_", StringComparison.OrdinalIgnoreCase);
        }

        private static string? NormalizeReferenceAnalysisTag(string? tag)
        {
            return string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
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
            IEnumerable<SemanticPlanEntry> entries,
            out string planType)
        {
            var normalizedTypes = entries
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

            var hasV03 = entries.Any(v => string.Equals(v.Tag, "v0.3", StringComparison.Ordinal));
            if (hasV03)
            {
                planType = PlanTypeDerived;
                return true;
            }

            var latestTag = entries
                .Select(v => v.Tag)
                .OrderBy(v => v, StringComparer.Ordinal)
                .LastOrDefault();

            if (string.Equals(latestTag, "v0.2", StringComparison.Ordinal))
            {
                var hasReferenceTitle = entries.Any(v =>
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

        private static string GetNextReferenceAnalysisTag(IEnumerable<ReferenceAnalysisEntry> entries)
        {
            var maxNumber = entries
                .Select(v => TryParseReferenceAnalysisTagNumber(v.Tag))
                .Where(v => v.HasValue)
                .Select(v => v.Value)
                .DefaultIfEmpty(0)
                .Max();

            return $"v{maxNumber + 1}";
        }

        private static int CompareReferenceAnalysisTag(string left, string right)
        {
            var leftNumber = TryParseReferenceAnalysisTagNumber(left);
            var rightNumber = TryParseReferenceAnalysisTagNumber(right);

            if (leftNumber.HasValue && rightNumber.HasValue)
                return leftNumber.Value.CompareTo(rightNumber.Value);

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int? TryParseReferenceAnalysisTagNumber(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var trimmed = tag.Trim();
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
        public string Tag { get; set; }
        public string PlanType { get; set; }
        public string Content { get; set; }
        // Nullable：NRT 启用下 ASP.NET Core 会把裸 string 视为 required；但"无参考图"
        // 流程下此字段语义上就该缺省（v0.1 不写 referenceAnalysisTag）。
        public string? ReferenceAnalysisTag { get; set; }
    }

    public class SaveReferenceAnalysisRequest
    {
        public string ZoneId { get; set; }
        public string SourceImageId { get; set; }
        public string Content { get; set; }
    }
}
