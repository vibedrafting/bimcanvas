using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 模块布置变体（modules-alt-*.json）控制器。
    /// 仅服务于 module-relocation-agent 产出的变体方案与 Web 端的预览/采纳交互。
    /// 与 SchemeController（canonical modules.json 读写 + Worktree source 解析）解耦。
    /// </summary>
    [ApiController]
    [Route("api/scheme")]
    public class VariantController : ControllerBase
    {
        // 变体文件名 = "modules-{variantId}.json"
        // sidecar 文件名 = "modules-{variantId}.meta.json"
        private static readonly Regex VariantFilenameRegex = new Regex(
            @"^modules-(?<variantId>[A-Za-z0-9_\-]+)\.json$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ILogger<VariantController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly JsonSerializerSettings _jsonSettings;

        public VariantController(
            ILogger<VariantController> logger,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new Point2DConverter(), new FacingConverter() }
            };
        }

        /// <summary>
        /// 列出指定叶子分区下所有变体文件 + 其 sidecar metadata。
        /// 没有变体 → 返回空数组。
        /// </summary>
        [HttpGet("variants")]
        public ActionResult<List<VariantDescriptor>> ListVariants([FromQuery] string leafZonePath)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            if (!TryResolveZoneDirectory(leafZonePath, out var zoneDir, out var error))
                return BadRequest(new { error });

            if (!Directory.Exists(zoneDir))
                return Ok(new List<VariantDescriptor>());

            // v1.4：列举时主动清理"无效"变体文件（0 字节 OR 0 有效模块——SubAgent 修补失败的认输信号）。
            // parse 失败 / 结构不对的不删，照常报错让 bug 暴露。
            var descriptors = new List<VariantDescriptor>();
            foreach (var filePath in Directory.GetFiles(zoneDir, "modules-*.json", SearchOption.TopDirectoryOnly))
            {
                if (filePath.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileName = Path.GetFileName(filePath);
                var match = VariantFilenameRegex.Match(fileName);
                if (!match.Success)
                    continue;

                if (TrySweepUnhealthyVariant(filePath, out var summary))
                    continue; // 已自动清理（0 字节或 0 模块）→ 跳过

                descriptors.Add(new VariantDescriptor
                {
                    VariantId = match.Groups["variantId"].Value,
                    Filename = fileName,
                    LeafZonePath = NormalizeLeafZonePath(leafZonePath),
                    Summary = summary
                });
            }
            descriptors.Sort((a, b) => string.Compare(
                a.VariantId, b.VariantId, StringComparison.OrdinalIgnoreCase));
            return Ok(descriptors);
        }

        /// <summary>
        /// 读取指定变体的模块列表，用于 Web 端切换渲染。
        /// 返回结构对齐 SchemeController.GetModules 的 SchemeModulesResponse。
        /// </summary>
        [HttpGet("variant/{variantId}/modules")]
        public ActionResult<SchemeModulesResponse> GetVariantModules(
            string variantId,
            [FromQuery] string leafZonePath)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try
            {
                ModuleFileTopologyService.EnsureSafeVariantId(variantId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            if (!TryResolveZoneDirectory(leafZonePath, out var zoneDir, out var error))
                return BadRequest(new { error });

            var fileName = ModuleFileTopologyService.BuildVariantFilename(variantId);
            var filePath = Path.Combine(zoneDir, fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = $"变体文件不存在: {fileName}" });

            // v1.4：无效（0 字节 OR 0 模块）就删 + 404；健康才进入 parse 路径
            if (TrySweepUnhealthyVariant(filePath, out _))
                return NotFound(new { error = $"变体文件无效（已自动清理）: {fileName}" });

            try
            {
                var modules = LoadVariantModules(filePath);
                // 变体文件写入时 zoneId 通常被 normalize 服务剥成 null；
                // 这里按 leafZonePath 最后一段（叶子分区 ID）回填，保持与 canonical 读路径一致
                var leafZoneId = Path.GetFileName(zoneDir);
                foreach (var module in modules)
                {
                    if (string.IsNullOrEmpty(module.ZoneId))
                        module.ZoneId = leafZoneId;
                }
                return Ok(new SchemeModulesResponse
                {
                    Source = $"variant:{variantId}",
                    Branch = NormalizeLeafZonePath(leafZonePath),
                    Modules = modules
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取变体模块失败: {File}", filePath);
                return StatusCode(500, new { error = $"读取变体失败: {ex.Message}" });
            }
        }

        /// <summary>
        /// 采纳某变体：用变体内容覆写 canonical modules.json，并删除该叶子分区下所有 modules-alt-*.json + sidecar。
        /// </summary>
        [HttpPost("variant/adopt")]
        public async Task<IActionResult> AdoptVariant([FromBody] AdoptVariantRequest? request)
        {
            if (request == null)
                return BadRequest(new { error = "请求体无效" });

            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try
            {
                ModuleFileTopologyService.EnsureSafeVariantId(request.VariantId);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            if (!TryResolveZoneDirectory(request.LeafZonePath, out var zoneDir, out var error))
                return BadRequest(new { error });

            var variantFileName = ModuleFileTopologyService.BuildVariantFilename(request.VariantId);
            var variantPath = Path.Combine(zoneDir, variantFileName);
            if (!System.IO.File.Exists(variantPath))
                return NotFound(new { error = $"变体文件不存在: {variantFileName}" });

            var canonicalPath = Path.Combine(zoneDir, "modules.json");

            try
            {
                // 1) 读取变体内容并先校验合法（兼容 wrapper 形态 {summary, modules} 与 legacy 裸数组）
                //    采纳时只把 modules 数组写回 canonical，summary 字段不进 canonical（canonical 不带 wrapper）
                var variantContent = System.IO.File.ReadAllText(variantPath, Encoding.UTF8);
                var modulesArrayJson = ExtractModulesArrayJson(variantContent);

                // 2) 原子写入 canonical：先写 .tmp，再 Move 覆盖
                var tmpPath = canonicalPath + ".tmp";
                System.IO.File.WriteAllText(tmpPath, modulesArrayJson, Encoding.UTF8);
                System.IO.File.Move(tmpPath, canonicalPath, overwrite: true);

                // 3) 删除该 zone 下所有 modules-alt-*.json + 对应 sidecar
                var deletedFiles = new List<string>();
                foreach (var altFile in Directory.GetFiles(zoneDir, "modules-alt-*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        System.IO.File.Delete(altFile);
                        deletedFiles.Add(Path.GetFileName(altFile));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "删除变体文件失败: {File}", altFile);
                    }
                }

                _logger.LogInformation(
                    "[Variant.Adopt] 已采纳变体 {VariantId} → {Canonical}; 清理变体文件 {Count} 个",
                    request.VariantId, canonicalPath, deletedFiles.Count);

                // 4) 显式广播 modules.json 变化，缩短 Web 端等 FileWatcher 防抖的延迟
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                {
                    type = "file_changed",
                    file = "modules.json",
                    timestamp = DateTime.UtcNow,
                    action = "reload",
                    trigger = "variant-adopt"
                });

                return Ok(new
                {
                    success = true,
                    adopted = request.VariantId,
                    deletedVariants = deletedFiles
                });
            }
            catch (JsonReaderException ex)
            {
                _logger.LogError(ex, "采纳失败：变体文件不是合法 JSON");
                return StatusCode(500, new { error = "变体文件不是合法 JSON，已拒绝采纳" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "采纳变体失败");
                return StatusCode(500, new { error = $"采纳失败: {ex.Message}" });
            }
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>
        /// 把 leafZonePath（相对 schemes/ 的子路径，如 "rz_3/dz_1"）解析为绝对目录。
        /// 强校验：非空 / 不含 ".." / 解析后必须仍在 schemes/ 子树内。
        /// </summary>
        private bool TryResolveZoneDirectory(string? leafZonePath, out string zoneDir, out string error)
        {
            zoneDir = "";
            error = "";

            if (string.IsNullOrWhiteSpace(leafZonePath))
            {
                error = "leafZonePath 不能为空";
                return false;
            }

            // 拒绝路径穿越
            var normalized = leafZonePath.Replace('\\', '/').Trim('/');
            if (normalized.Contains("..") || normalized.Contains(":"))
            {
                error = "leafZonePath 包含非法字符";
                return false;
            }

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
            {
                error = "项目目录不存在";
                return false;
            }

            var schemesPath = Path.GetFullPath(Path.Combine(projectPath, "schemes"));
            var candidate = Path.GetFullPath(Path.Combine(schemesPath, normalized));

            // 二次校验：解析后必须仍在 schemes/ 子树内
            if (!candidate.StartsWith(
                    schemesPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                && !string.Equals(candidate, schemesPath, StringComparison.OrdinalIgnoreCase))
            {
                error = "leafZonePath 越界";
                return false;
            }

            zoneDir = candidate;
            return true;
        }

        private static string NormalizeLeafZonePath(string leafZonePath)
        {
            return string.IsNullOrWhiteSpace(leafZonePath)
                ? ""
                : leafZonePath.Replace('\\', '/').Trim('/');
        }

        /// <summary>
        /// v1.4 sweep：判定变体文件是否"无效"（0 字节 OR 0 有效模块）→ 删除并返回 true（caller 应跳过）。
        /// 健康 → 返回 false，summary 通过 out 返回（wrapper.summary 或空串）。
        /// parse 失败 / IO 失败 → 不删，返回 false 让上层暴露 bug（GetVariantModules 会返 500）。
        /// 一次 IO + parse 同时完成 sweep 判定 + summary 提取。
        /// </summary>
        private bool TrySweepUnhealthyVariant(string filePath, out string summary)
        {
            summary = string.Empty;
            try
            {
                if (!System.IO.File.Exists(filePath))
                    return false;
                if (new FileInfo(filePath).Length == 0)
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogWarning("[VariantSweep] 自动清理 0 字节变体文件: {File}", filePath);
                    return true;
                }
                var raw = System.IO.File.ReadAllText(filePath, Encoding.UTF8);
                var token = JToken.Parse(raw);
                JArray? modulesArray = null;
                if (token is JArray asArray)
                {
                    modulesArray = asArray;
                }
                else if (token is JObject obj)
                {
                    modulesArray = obj["modules"] as JArray;
                    summary = obj.Value<string>("summary") ?? string.Empty;
                }
                if (modulesArray == null || modulesArray.Count == 0)
                {
                    System.IO.File.Delete(filePath);
                    _logger.LogWarning("[VariantSweep] 自动清理 0 有效模块变体文件: {File}", filePath);
                    summary = string.Empty;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[VariantSweep] 检查变体文件失败 {File}", filePath);
                summary = string.Empty;
                return false;
            }
        }

        /// <summary>
        /// 读变体文件并兼容两种形态：
        ///   - v1.1 wrapper: { "summary": "...", "modules": [...] } → 取 modules
        ///   - legacy:        [ ... ] (裸数组) → 直接当 Module[]
        /// </summary>
        private List<Module> LoadVariantModules(string variantFilePath)
        {
            var raw = System.IO.File.ReadAllText(variantFilePath, Encoding.UTF8);
            var token = JToken.Parse(raw);
            JArray? modulesArray;
            if (token is JArray asArray)
            {
                modulesArray = asArray;
            }
            else if (token is JObject asObject && asObject["modules"] is JArray inner)
            {
                modulesArray = inner;
            }
            else
            {
                throw new InvalidOperationException(
                    $"变体文件结构不识别（既不是数组，也不是 {{summary, modules}} 包裹对象）: {variantFilePath}");
            }
            return modulesArray.ToObject<List<Module>>(JsonSerializer.Create(_jsonSettings)) ?? new List<Module>();
        }

        /// <summary>
        /// 把变体文件内容归一成"纯 modules 数组的 JSON 串"，供采纳时写入 canonical modules.json。
        /// 兼容 wrapper / 裸数组两种形态。
        /// </summary>
        private string ExtractModulesArrayJson(string variantContent)
        {
            var token = JToken.Parse(variantContent);
            JArray? modulesArray;
            if (token is JArray asArray)
            {
                modulesArray = asArray;
            }
            else if (token is JObject asObject && asObject["modules"] is JArray inner)
            {
                modulesArray = inner;
            }
            else
            {
                throw new InvalidOperationException("变体文件不是合法 modules 数组或 {summary, modules} 包裹对象");
            }
            return modulesArray.ToString(Formatting.Indented);
        }

        private T? ReadJson<T>(string path)
        {
            var json = System.IO.File.ReadAllText(path, Encoding.UTF8);
            return JsonConvert.DeserializeObject<T>(json, _jsonSettings);
        }
    }

    public class VariantDescriptor
    {
        public string VariantId { get; set; } = "";
        public string Filename { get; set; } = "";
        public string LeafZonePath { get; set; } = "";
        /// <summary>v1.1 wrapper.summary 字段（chip tooltip 用）。</summary>
        public string Summary { get; set; } = string.Empty;
    }

    public class AdoptVariantRequest
    {
        public string VariantId { get; set; } = "";
        public string LeafZonePath { get; set; } = "";
    }
}
