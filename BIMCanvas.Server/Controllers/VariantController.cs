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
        /// 批量摘要：遍历当前活动 worktree 下 schemes/ 全树，返回
        ///   { leafZonePath -> { count, variantIds[] } } 字典。
        /// variantIds 按字典序排序，与 ListVariants 输出顺序一致——这样前端就能用
        /// "active variant 在列表中的 index" 来算出 zone label 上的当前/总页码。
        /// 零变体的叶子不入字典；顺手沿用 TrySweepUnhealthyVariant 清除 0 字节 / 0 模块的死文件。
        /// </summary>
        [HttpGet("variants/summary")]
        public ActionResult<Dictionary<string, VariantSummaryEntry>> GetVariantsSummary()
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath;
            if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                return BadRequest(new { error = "项目目录不存在" });

            var schemesPath = Path.GetFullPath(Path.Combine(projectPath, "schemes"));
            var result = new Dictionary<string, VariantSummaryEntry>();
            if (!Directory.Exists(schemesPath))
                return Ok(result);

            // 全树扫描所有目录（含叶子 / 容器），逐目录数 modules-*.json
            // 容器目录通常不放变体文件，多扫几次成本极低；不增加 IsLeaf 判定逻辑保持简单
            foreach (var dir in Directory.EnumerateDirectories(schemesPath, "*", SearchOption.AllDirectories))
            {
                var variantIds = new List<string>();
                foreach (var filePath in Directory.EnumerateFiles(dir, "modules-*.json", SearchOption.TopDirectoryOnly))
                {
                    if (filePath.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var fileName = Path.GetFileName(filePath);
                    var match = VariantFilenameRegex.Match(fileName);
                    if (!match.Success)
                        continue;

                    if (TrySweepUnhealthyVariant(filePath, out _))
                        continue;

                    variantIds.Add(match.Groups["variantId"].Value);
                }

                if (variantIds.Count > 0)
                {
                    variantIds.Sort(StringComparer.OrdinalIgnoreCase);
                    var rel = Path.GetRelativePath(schemesPath, dir).Replace('\\', '/');
                    result[rel] = new VariantSummaryEntry
                    {
                        Count = variantIds.Count,
                        VariantIds = variantIds
                    };
                }
            }

            return Ok(result);
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
        /// 采纳某变体（轮换语义）：
        ///   1) 旧 canonical modules.json 内容降级为 modules-alt-prev-{yyyyMMddHHmmss}.json（带 wrapper.summary 标注归档时间）
        ///   2) 被采纳的 variant 内容写到 canonical modules.json
        ///   3) 被采纳的 variant 文件 + 其 sidecar 被删除（不再作为可选）
        ///   4) 其他 modules-alt-*.json 全部保留
        /// 这样用户可以"回退"——上一版正式方案仍以变体形式存在，可以再次采纳回去。
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
                // 1) 读取被采纳的变体内容并校验合法（兼容 wrapper 形态 {summary, modules} 与裸数组）
                var variantContent = System.IO.File.ReadAllText(variantPath, Encoding.UTF8);
                var modulesArrayJson = ExtractModulesArrayJson(variantContent);

                // 2) 旧 canonical 降级归档：写入 modules-alt-prev-{yyyyMMddHHmmss}.json
                //    带 wrapper.summary 标注"上一版正式方案，归档于 ..."，让 NavigatorBar 上能识别。
                //    canonical 缺失或空时跳过归档（首次采纳之前没东西可归档）。
                string? archivedVariantId = null;
                if (System.IO.File.Exists(canonicalPath))
                {
                    var canonicalContent = System.IO.File.ReadAllText(canonicalPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(canonicalContent))
                    {
                        var now = DateTime.Now;
                        archivedVariantId = $"alt-prev-{now:yyyyMMddHHmmss}";
                        var archiveFileName = ModuleFileTopologyService.BuildVariantFilename(archivedVariantId);
                        var archivePath = Path.Combine(zoneDir, archiveFileName);

                        // 用 wrapper 形态写入归档（summary 给 NavigatorBar 提示），modules 数组沿用旧 canonical 内容
                        var canonicalArrayJson = ExtractModulesArrayJson(canonicalContent);
                        var canonicalArray = JArray.Parse(canonicalArrayJson);
                        var wrapper = new JObject
                        {
                            ["summary"] = $"上一版已采纳方案，归档于 {now:yyyy/MM/dd HH:mm:ss}",
                            ["modules"] = canonicalArray
                        };

                        var archiveTmp = archivePath + ".tmp";
                        System.IO.File.WriteAllText(archiveTmp, wrapper.ToString(Formatting.Indented), Encoding.UTF8);
                        System.IO.File.Move(archiveTmp, archivePath, overwrite: true);
                    }
                }

                // 3) 原子写入新 canonical
                var tmpPath = canonicalPath + ".tmp";
                System.IO.File.WriteAllText(tmpPath, modulesArrayJson, Encoding.UTF8);
                System.IO.File.Move(tmpPath, canonicalPath, overwrite: true);

                // 4) 删除被采纳的那个变体文件 + 其 sidecar（不动其他 alt 文件）
                var deletedFiles = new List<string>();
                TryDeleteVariantPair(zoneDir, request.VariantId, deletedFiles);

                _logger.LogInformation(
                    "[Variant.Adopt] {VariantId} 晋升为 canonical；旧 canonical 归档为 {Archived}；删除 {Count} 个被采纳变体文件",
                    request.VariantId, archivedVariantId ?? "(无)", deletedFiles.Count);

                // 5) 显式广播 modules.json 变化，缩短 Web 端等 FileWatcher 防抖的延迟
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
                    archivedAs = archivedVariantId,
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

        /// <summary>
        /// 删除指定的可变方案文件（modules-{variantId}.json + sidecar）。
        /// 不动 canonical 与其他 alt 文件。
        /// </summary>
        [HttpDelete("variant/{variantId}")]
        public async Task<IActionResult> DeleteVariant(string variantId, [FromQuery] string leafZonePath)
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

            var variantFileName = ModuleFileTopologyService.BuildVariantFilename(variantId);
            var variantPath = Path.Combine(zoneDir, variantFileName);
            if (!System.IO.File.Exists(variantPath))
                return NotFound(new { error = $"变体文件不存在: {variantFileName}" });

            var deletedFiles = new List<string>();
            TryDeleteVariantPair(zoneDir, variantId, deletedFiles);

            _logger.LogInformation(
                "[Variant.Delete] 已删除变体 {VariantId}（{Count} 个文件）", variantId, deletedFiles.Count);

            // 广播：file 名以 modules-alt- 开头时 Web 端会进 variant-files-changed 分发，
            // 触发 variantInfoByZone refetch，让 NavigatorBar / zone label 同步刷新。
            await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
            {
                type = "file_changed",
                file = variantFileName,
                timestamp = DateTime.UtcNow,
                action = "reload",
                trigger = "variant-deleted"
            });

            return Ok(new
            {
                success = true,
                deleted = variantId,
                deletedFiles
            });
        }

        /// <summary>
        /// 尝试删除一个变体的主文件 + sidecar；成功的文件名追加到 deletedFiles。失败仅记日志不抛。
        /// </summary>
        private void TryDeleteVariantPair(string zoneDir, string variantId, List<string> deletedFiles)
        {
            var variantFileName = ModuleFileTopologyService.BuildVariantFilename(variantId);
            var variantPath = Path.Combine(zoneDir, variantFileName);
            var sidecarPath = Path.Combine(zoneDir, $"modules-{variantId}.meta.json");

            foreach (var path in new[] { variantPath, sidecarPath })
            {
                if (!System.IO.File.Exists(path)) continue;
                try
                {
                    System.IO.File.Delete(path);
                    deletedFiles.Add(Path.GetFileName(path));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "删除变体文件失败: {File}", path);
                }
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

    /// <summary>
    /// GetVariantsSummary 的字典值：count + 按字典序的 variantIds 列表。
    /// Web 端用 variantIds 来反查 active variant 在序列中的位置，渲染 zone label 上的 (current/total) 分页号。
    /// </summary>
    public class VariantSummaryEntry
    {
        public int Count { get; set; }
        public List<string> VariantIds { get; set; } = new List<string>();
    }
}
