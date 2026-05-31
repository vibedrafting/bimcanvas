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
    /// ⚠️ 指针模型（P1）后已**退化为 vestigial**：ResolveModulesPath 不再区分 New/Legacy，
    /// 统一走 schemes/{dz}/{slug}/[{leaf}/]modules.json（slug=显式或 adopted），无 adopted 时回落旧 canonical。
    /// 保留枚举与参数仅为避免改动全部调用方签名；完整退役（删枚举 + 清调用方）列为后续 cleanup。
    /// </summary>
    public enum VariantPathMode
    {
        New,
        Legacy
    }

    /// <summary>
    /// modules.json 的唯一写入入口。
    /// - schemeMetadata.summary 由调用方 passthrough（不再 Server 派生）
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
        /// <param name="designZoneId">设计区 ID</param>
        /// <param name="leafZoneId">叶子分区 ID</param>
        /// <param name="variantId">变体 slug；null 表示 canonical</param>
        /// <param name="pathMode">variantId 非空时决定写新路径还是旧 sibling 路径</param>
        /// <param name="modules">模块列表</param>
        /// <param name="summary">schemeMetadata.summary 值；不传则空字符串</param>
        public async Task WriteAsync(
            string projectPath,
            string designZoneId,
            string leafZoneId,
            string? variantId,
            VariantPathMode pathMode,
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
            _ = pathMode; // 指针模型下 VariantPathMode 已退化（New/Legacy 不再区分）；保留参数仅为签名兼容
            var schemesPath = Path.Combine(projectPath, "schemes");

            // 指针模型：variantId = 显式方案 slug；null = canonical = 父 DESIGN.md 的 adopted slug。
            // 无显式 slug 且无 adopted 指针 → 回落 legacy canonical 路径（存量项目 P2 迁移前仍可正常读写，零回归）。
            var slug = string.IsNullOrWhiteSpace(variantId)
                ? SchemeDesignDocService.ResolveAdoptedSlug(schemesPath, designZoneId)
                : variantId;
            var isTopLevelLeaf = string.Equals(designZoneId, leafZoneId, StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(slug))
            {
                // 指针布局：schemes/{dz}/{slug}/[{leaf}/]modules.json（slug 直接做 dz 下一级，无 variants/ 段）
                return isTopLevelLeaf
                    ? Path.Combine(schemesPath, designZoneId, slug, "modules.json")
                    : Path.Combine(schemesPath, designZoneId, slug, leafZoneId, "modules.json");
            }

            // legacy fallback：旧固定 canonical 路径（与改造前字节一致）
            return isTopLevelLeaf
                ? Path.Combine(schemesPath, designZoneId, "modules.json")
                : Path.Combine(schemesPath, designZoneId, leafZoneId, "modules.json");
        }

    }
}
