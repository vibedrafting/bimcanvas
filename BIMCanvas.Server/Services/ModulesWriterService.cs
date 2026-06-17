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
    /// modules.json 的唯一写入入口。
    /// - schemeMetadata.summary 由调用方 passthrough（不再 Server 派生）
    /// - 合成 wrapper 形态
    /// - 原子写入（.tmp → rename）
    /// </summary>
    public class ModulesWriterService
    {
        private readonly ILogger<ModulesWriterService> _logger;
        private readonly SchemeDesignDocService _designDoc;
        private readonly JsonSerializerSettings _jsonSettings;

        public ModulesWriterService(ILogger<ModulesWriterService> logger, SchemeDesignDocService designDoc)
        {
            _logger = logger;
            _designDoc = designDoc;
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
        /// <param name="designZoneId">设计区 ID</param>
        /// <param name="leafZoneId">叶子分区 ID</param>
        /// <param name="variantId">变体 slug；null 表示 canonical</param>
        /// <param name="modules">模块列表</param>
        /// <param name="summary">schemeMetadata.summary 值；不传则空字符串</param>
        public async Task WriteAsync(
            string projectPath,
            string designZoneId,
            string leafZoneId,
            string? variantId,
            List<Module> modules,
            string summary = "")
        {
            if (string.IsNullOrWhiteSpace(designZoneId))
                throw new ArgumentException("designZoneId 必填", nameof(designZoneId));
            if (string.IsNullOrWhiteSpace(leafZoneId))
                throw new ArgumentException("leafZoneId 必填", nameof(leafZoneId));
            if (modules == null)
                throw new ArgumentNullException(nameof(modules));

            if (!string.IsNullOrWhiteSpace(variantId))
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);

            // 路A：写 canonical（variantId 空）但该设计区还没 adopted 指针。
            // 分两种：
            //   ① 真·无任何方案（新项目首次手动布置）→ bootstrap "main" 方案 + 父 adopted:main，落进指针结构、不产 legacy 路径。
            //   ② 已有候选方案目录但无 adopted（多方案待采纳）→ 绝不凭空造 main：那会污染出一个被采纳的 main 方案，
            //      且每次 canonical 回落/重启都复活（隐患根因）。此时 canonical 写入是调用方契约错误，抛错要求带 variantId 定向写。
            // （场景① workflow 候选走显式 variantId=_cand-x、经 Write 工具直写，不经此入口、不触发 bootstrap。）
            var schemesPath = Path.Combine(projectPath, "schemes");
            if (string.IsNullOrWhiteSpace(variantId)
                && string.IsNullOrEmpty(SchemeDesignDocService.ResolveAdoptedSlug(schemesPath, designZoneId)))
            {
                var dzDir = ModuleFileTopologyService.CombineSegments(schemesPath, designZoneId);
                var hasCandidateSchemes = Directory.Exists(dzDir) && Directory.EnumerateDirectories(dzDir).Any();
                if (hasCandidateSchemes)
                {
                    throw new InvalidOperationException(
                        $"设计区 {designZoneId} 已有候选方案但无 adopted 指针（多方案待采纳）；canonical 写入会凭空 bootstrap main 污染——" +
                        "请带 variantId 定向写入目标变体，或先采纳一个方案。");
                }

                _designDoc.WriteAdoptedSlug(schemesPath, designZoneId, "main");
                _logger.LogInformation(
                    "[ModulesWriter] 设计区 {Dz} 无任何方案，bootstrap main 方案（路A·新项目首次布置）", designZoneId);
            }

            var filePath = ResolveModulesPath(projectPath, designZoneId, leafZoneId, variantId);
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
                    Summary = summary ?? string.Empty
                },
                Modules = modulesToSave
            };

            var json = JsonConvert.SerializeObject(wrapper, Formatting.Indented, _jsonSettings);
            var tmpPath = filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, Encoding.UTF8);
            File.Move(tmpPath, filePath, overwrite: true);

            _logger.LogDebug(
                "[ModulesWriter] 写入 {Count} 个模块到 {Path}（variantId={VariantId}）",
                modulesToSave.Count, filePath, variantId ?? "(canonical)");
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
        /// 指针布局：顶层叶子（dz == leaf）不重复 designZoneId，嵌套叶子按 dz/leaf 两段嵌套，
        /// 与 canonical 路径完全镜像（采纳=翻指针，方案目录与 canonical 结构一致）。
        /// </summary>
        public string ResolveModulesPath(
            string projectPath,
            string designZoneId,
            string leafZoneId,
            string? variantId)
        {
            var schemesPath = Path.Combine(projectPath, "schemes");

            // 指针模型：variantId = 显式方案 slug；null = canonical = 父 DESIGN.md 的 adopted slug。
            // 不回头看：删 legacy 回落——无 slug（既无显式 variantId 又无 adopted 指针）即调用方契约错误，抛错。
            // （Write() bootstrap 已保证 canonical 写入前必有 adopted 指针，slug 不应为空。）
            var slug = string.IsNullOrWhiteSpace(variantId)
                ? SchemeDesignDocService.ResolveAdoptedSlug(schemesPath, designZoneId)
                : variantId;
            if (string.IsNullOrWhiteSpace(slug))
                throw new InvalidOperationException(
                    $"无法解析设计区 {designZoneId} 的方案 slug（既无显式 variantId 又无 adopted 指针）；指针模型下不再回落 legacy 路径");

            var isTopLevelLeaf = string.Equals(designZoneId, leafZoneId, StringComparison.OrdinalIgnoreCase);

            // 指针布局：schemes/{dz}/{slug}/[{leaf}/]modules.json
            // （dz 可多段 rz_6/dz_客厅，用 CombineSegments 安全拼接，不裸 Path.Combine 含 '/' 字符串）
            var schemeDir = ModuleFileTopologyService.CombineSegments(schemesPath, designZoneId, slug);
            return isTopLevelLeaf
                ? Path.Combine(schemeDir, "modules.json")
                : Path.Combine(schemeDir, leafZoneId, "modules.json");
        }

    }
}
