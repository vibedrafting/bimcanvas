using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    /// <summary>
    /// variantId 路径解析模式。
    /// New: schemes/{designZoneId}/variants/{slug}/{leafZoneId}/modules.json —— Phase 1+ 的标准
    /// Legacy: schemes/{designZoneId}/modules-{slug}.json sibling —— ProjectController.SaveModules / VariantController 暂时还在用
    /// </summary>
    public enum VariantPathMode
    {
        New,
        Legacy
    }

    /// <summary>
    /// modules.json 的唯一写入入口。
    /// - 派生 schemeMetadata（Server 全权决定，忽略外部传入）
    /// - 合成 wrapper 形态
    /// - 原子写入（.tmp → rename）
    /// </summary>
    public class ModulesWriterService
    {
        private readonly ILogger<ModulesWriterService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;

        public ModulesWriterService(ILogger<ModulesWriterService> logger)
        {
            _logger = logger;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 写入 modules.json wrapper。
        /// </summary>
        /// <param name="projectPath">项目根目录（含 schemes/）</param>
        /// <param name="designZoneId">设计区 ID（用于 schemeMetadata.summary 派生）</param>
        /// <param name="leafZoneId">叶子分区 ID</param>
        /// <param name="variantId">变体 slug；null 表示 canonical</param>
        /// <param name="pathMode">variantId 非空时决定写新路径还是旧 sibling 路径</param>
        /// <param name="modules">模块列表</param>
        /// <param name="adoptedAt">采纳时间戳；adopt 路径填 now，常规 save 留 null</param>
        /// <param name="sourceWorkflowOverride">显式指定 sourceWorkflow（如 adopt 路径用 "prev-adopted"）；null 时按默认规则派生</param>
        public async Task WriteAsync(
            string projectPath,
            string designZoneId,
            string leafZoneId,
            string? variantId,
            VariantPathMode pathMode,
            List<Module> modules,
            DateTime? adoptedAt = null,
            string? sourceWorkflowOverride = null)
        {
            if (string.IsNullOrWhiteSpace(designZoneId))
                throw new ArgumentException("designZoneId 必填", nameof(designZoneId));
            if (string.IsNullOrWhiteSpace(leafZoneId))
                throw new ArgumentException("leafZoneId 必填", nameof(leafZoneId));
            if (modules == null)
                throw new ArgumentNullException(nameof(modules));

            if (!string.IsNullOrWhiteSpace(variantId))
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);

            var filePath = ResolveModulesPath(projectPath, designZoneId, leafZoneId, variantId, pathMode);
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);

            // 清理运行时字段（ZoneId 不写入文件）
            var modulesToSave = modules.Select(m =>
            {
                m.ZoneId = null;
                return m;
            }).ToList();

            var wrapper = new ModulesWrapper
            {
                SchemeMetadata = new SchemeMetadata
                {
                    Summary = SemanticPlanSummaryHelper.DeriveSummary(projectPath, designZoneId, variantId),
                    VariantSlug = string.IsNullOrWhiteSpace(variantId) ? null : variantId,
                    AdoptedAt = adoptedAt?.ToUniversalTime().ToString("o"),
                    SourceWorkflow = sourceWorkflowOverride ?? DeriveSourceWorkflow(variantId)
                },
                Modules = modulesToSave
            };

            var json = JsonConvert.SerializeObject(wrapper, Formatting.Indented, _jsonSettings);
            var tmpPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, filePath, overwrite: true);

            _logger.LogDebug(
                "[ModulesWriter] 写入 {Count} 个模块到 {Path}（variantSlug={VariantSlug}, workflow={Workflow}）",
                modulesToSave.Count, filePath, wrapper.SchemeMetadata.VariantSlug ?? "-", wrapper.SchemeMetadata.SourceWorkflow);
        }

        /// <summary>
        /// 低层写入：直接持久化已合成的 wrapper（不重新派生 schemeMetadata）。
        /// 用于 normalize / migrate 等需保留原 metadata 的场景。原子写入（.tmp → rename）。
        /// </summary>
        public async Task WriteWrapperAsync(string filePath, Models.ModulesWrapper wrapper)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("filePath 必填", nameof(filePath));
            if (wrapper == null)
                throw new ArgumentNullException(nameof(wrapper));

            wrapper.SchemeMetadata ??= new Models.SchemeMetadata();
            wrapper.Modules ??= new List<Module>();

            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);

            var json = JsonConvert.SerializeObject(wrapper, Formatting.Indented, _jsonSettings);
            var tmpPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, filePath, overwrite: true);
        }

        /// <summary>
        /// 仅算文件路径，不写入。供 file watcher / list / 调试用。
        /// variants/New 路径对齐 canonical 结构——顶层叶子（dz == leaf）不重复 designZoneId，
        /// 嵌套叶子按 dz/leaf 两段嵌套，与 canonical 路径完全镜像
        /// （adopt 晋升/降级时变体目录与 canonical 字节级一致，move 即可）。
        /// </summary>
        public string ResolveModulesPath(
            string projectPath,
            string designZoneId,
            string leafZoneId,
            string? variantId,
            VariantPathMode pathMode)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");

            if (string.IsNullOrWhiteSpace(variantId))
            {
                // canonical 路径：通过拓扑解析（容器分区 → 叶子）
                // designZoneId == leafZoneId 时（顶层叶子）→ schemes/{designZoneId}/modules.json
                // designZoneId 是容器时 → schemes/{designZoneId}/{leafZoneId}/modules.json
                if (string.Equals(designZoneId, leafZoneId, StringComparison.OrdinalIgnoreCase))
                    return Path.Combine(schemesPath, designZoneId, "modules.json");
                return Path.Combine(schemesPath, designZoneId, leafZoneId, "modules.json");
            }

            return pathMode switch
            {
                VariantPathMode.New =>
                    string.Equals(designZoneId, leafZoneId, StringComparison.OrdinalIgnoreCase)
                        ? Path.Combine(schemesPath, designZoneId, "variants", variantId, "modules.json")
                        : Path.Combine(schemesPath, designZoneId, "variants", variantId, leafZoneId, "modules.json"),
                VariantPathMode.Legacy =>
                    Path.Combine(
                        ProjectService.ResolveZoneDirectory(schemesPath, leafZoneId),
                        ModuleFileTopologyService.BuildVariantFilename(variantId)),
                _ => throw new ArgumentOutOfRangeException(nameof(pathMode))
            };
        }

        /// <summary>
        /// 由 variantId slug 约定派生 sourceWorkflow：
        ///   null/空 → "single-plan"
        ///   "prev-" 前缀（adopt 降级保留的上一版方案）→ "prev-adopted"
        ///   其他 → "variant"（合并自原 multi-plan-explore / relocation；UI 无差异化展示）
        /// </summary>
        private static string DeriveSourceWorkflow(string? variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
                return "single-plan";
            if (variantId.StartsWith("prev-", StringComparison.OrdinalIgnoreCase))
                return "prev-adopted";
            return "variant";
        }
    }

    /// <summary>
    /// 从 semantic_plan.json 派生 schemeMetadata.summary 的小 helper。
    /// 优先变体内的 semantic_plan（variantId 非空时），回落 canonical；
    /// 同一份计划内优先 tag=construction-brief，再回落 tag=strategic-plan，再回落空串。
    /// </summary>
    public static class SemanticPlanSummaryHelper
    {
        public static string DeriveSummary(string projectPath, string designZoneId, string? variantId = null)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(designZoneId))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(variantId))
            {
                var variantPlanPath = Path.Combine(
                    projectPath, "schemes", designZoneId, "variants", variantId, "semantic_plan.json");
                var fromVariant = TryDeriveFromPlanFile(variantPlanPath);
                if (!string.IsNullOrWhiteSpace(fromVariant))
                    return fromVariant;
                // variant 缺 semantic_plan（如 prev-adopted）或无 brief/strategic → fallback canonical
            }

            var canonicalPlanPath = Path.Combine(projectPath, "schemes", designZoneId, "semantic_plan.json");
            return TryDeriveFromPlanFile(canonicalPlanPath);
        }

        private static string TryDeriveFromPlanFile(string planPath)
        {
            if (!File.Exists(planPath))
                return string.Empty;

            try
            {
                var json = File.ReadAllText(planPath, Encoding.UTF8);
                var doc = JsonConvert.DeserializeObject<Models.SemanticPlanDocument>(json);
                if (doc?.Entries == null || doc.Entries.Count == 0)
                    return string.Empty;

                var constructionBrief = doc.Entries.LastOrDefault(e => string.Equals(e.Tag, "construction-brief", StringComparison.Ordinal));
                if (constructionBrief != null && !string.IsNullOrWhiteSpace(constructionBrief.Content))
                    return FirstSentence(constructionBrief.Content);

                var strategicPlan = doc.Entries.LastOrDefault(e => string.Equals(e.Tag, "strategic-plan", StringComparison.Ordinal));
                if (strategicPlan != null && !string.IsNullOrWhiteSpace(strategicPlan.Content))
                    return FirstSentence(strategicPlan.Content);

                return string.Empty;
            }
            catch
            {
                // semantic_plan.json 读取失败不应阻塞 modules 写入
                return string.Empty;
            }
        }

        private static string FirstSentence(string content)
        {
            // 取第一行非空内容；去掉前导 markdown 标记（#, -, *）
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.TrimStart('#', ' ', '-', '*', '\t').Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // 进一步截取到第一个句号 / 换行
                var idx = line.IndexOfAny(new[] { '。', '.', '\r' });
                if (idx > 0)
                    return line.Substring(0, idx).Trim();
                return line.Length > 120 ? line.Substring(0, 120) + "..." : line;
            }
            return string.Empty;
        }
    }
}
