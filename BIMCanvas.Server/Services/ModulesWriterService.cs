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
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
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
                    Summary = SemanticPlanSummaryHelper.DeriveSummary(projectPath, designZoneId),
                    VariantSlug = string.IsNullOrWhiteSpace(variantId) ? null : variantId,
                    AdoptedAt = adoptedAt?.ToUniversalTime().ToString("o"),
                    SourceWorkflow = sourceWorkflowOverride ?? DeriveSourceWorkflow(variantId, projectPath, designZoneId)
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
                    Path.Combine(schemesPath, designZoneId, "variants", variantId, leafZoneId, "modules.json"),
                VariantPathMode.Legacy =>
                    Path.Combine(
                        ProjectService.ResolveZoneDirectory(schemesPath, leafZoneId),
                        ModuleFileTopologyService.BuildVariantFilename(variantId)),
                _ => throw new ArgumentOutOfRangeException(nameof(pathMode))
            };
        }

        private static string DeriveSourceWorkflow(string? variantId, string projectPath, string designZoneId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
                return "single-plan";

            var variantJsonPath = Path.Combine(
                projectPath, "schemes", designZoneId, "variants", variantId, "variant.json");
            if (!File.Exists(variantJsonPath))
                return "unknown";

            try
            {
                var json = File.ReadAllText(variantJsonPath, Encoding.UTF8);
                var meta = JsonConvert.DeserializeObject<Models.VariantMetadata>(json);
                return meta?.State switch
                {
                    "explore-generated" => "multi-plan-explore",
                    "relocation" => "relocation",
                    "prev-adopted" => "prev-adopted",
                    _ => "unknown"
                };
            }
            catch
            {
                return "unknown";
            }
        }
    }

    /// <summary>
    /// 从 semantic_plan.json 派生 schemeMetadata.summary 的小 helper。
    /// 优先 tag=v0.3，回落 tag=v0.2，再回落空串。
    /// </summary>
    internal static class SemanticPlanSummaryHelper
    {
        public static string DeriveSummary(string projectPath, string designZoneId)
        {
            if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(designZoneId))
                return string.Empty;

            var planPath = Path.Combine(projectPath, "schemes", designZoneId, "semantic_plan.json");
            if (!File.Exists(planPath))
                return string.Empty;

            try
            {
                var json = File.ReadAllText(planPath, Encoding.UTF8);
                var doc = JsonConvert.DeserializeObject<Models.SemanticPlanDocument>(json);
                if (doc?.Entries == null || doc.Entries.Count == 0)
                    return string.Empty;

                var v03 = doc.Entries.LastOrDefault(e => string.Equals(e.Tag, "v0.3", StringComparison.Ordinal));
                if (v03 != null && !string.IsNullOrWhiteSpace(v03.Content))
                    return FirstSentence(v03.Content);

                var v02 = doc.Entries.LastOrDefault(e => string.Equals(e.Tag, "v0.2", StringComparison.Ordinal));
                if (v02 != null && !string.IsNullOrWhiteSpace(v02.Content))
                    return FirstSentence(v02.Content);

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
