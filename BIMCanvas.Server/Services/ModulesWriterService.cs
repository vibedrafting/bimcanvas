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
            var schemesPath = Plugins.PluginPaths.ActiveSchemesRoot(projectPath);

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

    }
}
