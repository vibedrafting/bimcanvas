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
            "spatial-skeleton",
            "strategic-plan",
            "multi-plan-overview",
            "construction-brief"
        };

        // spatial-skeleton / multi-plan-overview 全局只在 canonical 出现，不允许变体覆盖。
        private static readonly HashSet<string> CanonicalOnlyTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "spatial-skeleton",
            "multi-plan-overview"
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

            // R3 写入 gate (V12a / 主真理源 §3.11 / §4.7):pending 状态拒绝写入
            var writeGate = _projectContext.CheckWriteAllowed();
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

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

            // variantId charset 校验：与 ModulesController.SaveSchemeModules 保持一致，
            // 避免路径穿越 / 不合法目录名。
            if (!string.IsNullOrWhiteSpace(request.VariantId))
            {
                try
                {
                    ModuleFileTopologyService.EnsureSafeVariantId(request.VariantId);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }
            }

            // canonical-only tag 单 owner 校验：spatial-skeleton / multi-plan-overview 不允许写入变体路径。
            if (CanonicalOnlyTags.Contains(request.Tag) && !string.IsNullOrWhiteSpace(request.VariantId))
            {
                return BadRequest(new
                {
                    message = $"tag={request.Tag} 是 canonical 单 owner（spatial-skeleton / multi-plan-overview 全局只在 canonical 出现），不能写入变体路径。请省略 variantId。"
                });
            }

            var filePath = GetSemanticPlanPath(request.ZoneId, request.VariantId);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var document = ReadSemanticPlanDocument(filePath);

            // 新流程已经迁出 referenceAnalysis，保存 semantic_plan 时顺便清理旧嵌入字段。
            // 仅在写入 canonical 时执行——变体目录从未有过 legacy embedded 数据，无需清理。
            if (string.IsNullOrWhiteSpace(request.VariantId))
            {
                document.LegacyEmbeddedReferenceAnalysis = null;
            }

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

            var variantIdForPayload = string.IsNullOrWhiteSpace(request.VariantId) ? null : request.VariantId;

            await _hubContext.Clients.All.SendAsync("SemanticPlanUpdated", new
            {
                zoneId = request.ZoneId,
                variantId = variantIdForPayload,
                tag = request.Tag,
                planType = entry.PlanType,
                referenceAnalysisTag = entry.ReferenceAnalysisTag,
                content = request.Content,
                timestamp = entry.Timestamp
            });

            _logger.LogInformation(
                "[SemanticPlan] 已保存 {ZoneId} {PlanType} {Tag}（variant={VariantId}, reference={ReferenceAnalysisTag}）",
                request.ZoneId, entry.PlanType, request.Tag, variantIdForPayload ?? "-", entry.ReferenceAnalysisTag ?? "-");

            return Ok(new
            {
                saved = true,
                zoneId = request.ZoneId,
                variantId = variantIdForPayload,
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

            // R3 写入 gate (V12a / 主真理源 §3.11 / §4.7):pending 状态拒绝写入
            var writeGate = _projectContext.CheckWriteAllowed();
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

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
        public ActionResult LoadSemanticPlan(string zoneId, [FromQuery] string? variantId = null)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { message = "没有加载的项目" });

            if (!IsDesignZoneId(zoneId))
                return BadRequest(new { message = "semantic_plan 只归属于设计区，不归属于子分区。请传入父设计区 zoneId。" });

            // variantId 非空时走 merge view 分支（canonical spatial-skeleton + 变体 entries）。
            if (!string.IsNullOrWhiteSpace(variantId))
            {
                try
                {
                    ModuleFileTopologyService.EnsureSafeVariantId(variantId);
                }
                catch (ArgumentException ex)
                {
                    return BadRequest(new { message = ex.Message });
                }

                return LoadSemanticPlanMergeView(zoneId, variantId);
            }

            var filePath = GetSemanticPlanPath(zoneId, null);
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

            var target = document.Entries.LastOrDefault(v => string.Equals(v.Tag, "construction-brief", StringComparison.Ordinal));
            if (target == null)
            {
                if (string.Equals(planType, PlanTypeReference, StringComparison.OrdinalIgnoreCase))
                {
                    return Conflict(new
                    {
                        status = "legacy_reference_requires_replan",
                        zoneId,
                        message = $"{zoneId} 当前仍是旧版 reference 工作流（缺少可施工的 construction-brief 自包含合同）。请重新执行规划。"
                    });
                }

                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    message = $"未找到 {zoneId} 的生效图纸 construction-brief"
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

        private ActionResult LoadSemanticPlanMergeView(string zoneId, string variantId)
        {
            // 1. 变体文件存在性：缺则 404 missing。
            var variantPath = GetSemanticPlanPath(zoneId, variantId);
            if (!System.IO.File.Exists(variantPath))
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    variantId,
                    message = $"未找到变体语义方案 schemes/{zoneId}/variants/{variantId}/semantic_plan.json"
                });
            }

            // 2. 读变体 doc + canonical doc，按硬约束 merge = canonical.spatial-skeleton + 变体的非 spatial-skeleton entries。
            var variantDoc = ReadSemanticPlanDocument(variantPath);
            var canonicalDoc = ReadSemanticPlanDocument(GetSemanticPlanPath(zoneId, null));

            var canonicalSkeleton = canonicalDoc.Entries?
                .LastOrDefault(e => string.Equals(e.Tag, "spatial-skeleton", StringComparison.Ordinal));

            var merge = new List<SemanticPlanEntry>();
            if (canonicalSkeleton != null) merge.Add(canonicalSkeleton);
            if (variantDoc.Entries != null)
            {
                // 防御性过滤：硬约束下变体不应承载 spatial-skeleton，但即便文件被手工写入也以 canonical 为准。
                merge.AddRange(variantDoc.Entries
                    .Where(e => !string.Equals(e.Tag, "spatial-skeleton", StringComparison.Ordinal)));
            }
            merge.Sort((a, b) => string.Compare(a.Tag, b.Tag, StringComparison.Ordinal));

            if (merge.Count == 0)
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    variantId,
                    message = $"变体 {variantId} 的语义方案为空（且 canonical 未提供 spatial-skeleton）"
                });
            }

            // 3. effectiveTag 按 construction-brief → strategic-plan → multi-plan-overview → spatial-skeleton 优先级解析。
            SemanticPlanEntry? target = null;
            foreach (var preferred in new[] { "construction-brief", "strategic-plan", "multi-plan-overview", "spatial-skeleton" })
            {
                target = merge.LastOrDefault(e => string.Equals(e.Tag, preferred, StringComparison.Ordinal));
                if (target != null) break;
            }
            if (target == null)
            {
                return NotFound(new
                {
                    status = "missing",
                    zoneId,
                    variantId,
                    message = $"变体 {variantId} 未提供任何已知 tag 的语义方案"
                });
            }

            // 4. planType 复用现有解析（合并列表统一处理）。
            if (!TryResolvePlanType(merge, out var planType))
            {
                return Conflict(new
                {
                    status = "ambiguous_legacy",
                    zoneId,
                    variantId,
                    message = $"{zoneId}/variants/{variantId} 的语义方案 planType 不一致，无法解析。"
                });
            }

            var warning = canonicalSkeleton == null ? "canonical spatial-skeleton missing" : null;

            return Ok(new
            {
                status = "ok",
                zoneId,
                variantId,
                planType,
                effectiveTag = target.Tag,
                content = target.Content,
                timestamp = target.Timestamp,
                referenceAnalysisTag = target.ReferenceAnalysisTag,
                entries = merge.Select(e => new
                {
                    zoneId = e.ZoneId,
                    tag = e.Tag,
                    planType = e.PlanType,
                    content = e.Content,
                    timestamp = e.Timestamp,
                    referenceAnalysisTag = e.ReferenceAnalysisTag
                }).ToList(),
                warning
            });
        }

        private string GetSemanticPlanPath(string zoneId, string? variantId)
        {
            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            if (string.IsNullOrWhiteSpace(variantId))
                return Path.Combine(projectPath, "schemes", zoneId, "semantic_plan.json");

            return Path.Combine(projectPath, "schemes", zoneId, "variants", variantId, "semantic_plan.json");
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

            // Legacy embedded reference 仅出现在 canonical 历史文件中，变体目录从不承载。
            var semanticPlanPath = GetSemanticPlanPath(zoneId, null);
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
            // Legacy 清理仅作用于 canonical 文件——变体目录无历史数据需要迁移。
            var semanticPlanPath = GetSemanticPlanPath(zoneId, null);
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

            var hasConstructionBrief = entries.Any(v => string.Equals(v.Tag, "construction-brief", StringComparison.Ordinal));
            if (hasConstructionBrief)
            {
                planType = PlanTypeDerived;
                return true;
            }

            var latestTag = entries
                .Select(v => v.Tag)
                .OrderBy(v => v, StringComparer.Ordinal)
                .LastOrDefault();

            if (string.Equals(latestTag, "strategic-plan", StringComparison.Ordinal))
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
        // 流程下此字段语义上就该缺省（spatial-skeleton 不写 referenceAnalysisTag）。
        public string? ReferenceAnalysisTag { get; set; }
        // 非空时落到 schemes/{zoneId}/variants/{variantId}/semantic_plan.json。
        // spatial-skeleton / multi-plan-overview 全局单 owner（canonical），传 variantId 会被 server 400 拒绝。
        public string? VariantId { get; set; }
    }

    public class SaveReferenceAnalysisRequest
    {
        public string ZoneId { get; set; }
        public string SourceImageId { get; set; }
        public string Content { get; set; }
    }
}
