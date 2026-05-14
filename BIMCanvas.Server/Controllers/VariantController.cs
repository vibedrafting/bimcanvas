using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Core.Models.Layout;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace BIMCanvas.Server.Controllers
{
    /// <summary>
    /// 变体（schemes/{designZoneId}/variants/{slug}/）控制器。
    /// 协议按 designZone + variantSlug 索引；adopt 走"检测 → 降级 → 晋升"三阶段。
    /// </summary>
    [ApiController]
    [Route("api/scheme")]
    public class VariantController : ControllerBase
    {
        private readonly ILogger<VariantController> _logger;
        private readonly ProjectContext _projectContext;
        private readonly IHubContext<CanvasHub> _hubContext;
        private readonly ModulesWriterService _modulesWriter;
        private readonly ModulesReaderService _modulesReader;

        /// <summary>
        /// 按 designZoneId 串行化 Adopt / Delete 的多文件操作。
        /// BranchLockManager 是多窗口业务级互斥（branch→windowId），不保护同一窗口快速重复请求 / SignalR
        /// 重发等场景下的跨多文件竞态——这里用 SemaphoreSlim 兜底。
        /// 同 designZone 串行；不同 designZone 并发不互斥。
        /// </summary>
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _designZoneFileLocks = new();
        private static SemaphoreSlim GetDesignZoneLock(string designZoneId)
            => _designZoneFileLocks.GetOrAdd(designZoneId, _ => new SemaphoreSlim(1, 1));

        public VariantController(
            ILogger<VariantController> logger,
            ProjectContext projectContext,
            IHubContext<CanvasHub> hubContext,
            ModulesWriterService modulesWriter,
            ModulesReaderService modulesReader)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _modulesWriter = modulesWriter;
            _modulesReader = modulesReader;
        }

        // ─────────────────────────── ListVariants ───────────────────────────

        /// <summary>
        /// 列出指定 design zone 下所有变体（含 prev-* 降级目录）。
        /// 数据源：文件系统目录 + slug 前缀约定（prev-* → "prev-adopted"，其他 → "variant"）+
        /// 变体内 semantic_plan.json 派生的 summary（fallback canonical）。
        /// createdAt 取目录 mtime，按字典序（≈时间序）升序排序。
        /// </summary>
        [HttpGet("variants")]
        public ActionResult<VariantListResponse> ListVariants([FromQuery] string designZoneId)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            if (!TryResolveDesignZoneRoot(designZoneId, out var designZoneRoot, out var error))
                return NotFound(new { error });

            var variantsRoot = Path.Combine(designZoneRoot, "variants");
            var response = new VariantListResponse { DesignZoneId = designZoneId };
            if (!Directory.Exists(variantsRoot))
                return Ok(response);

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;

            foreach (var dir in Directory.EnumerateDirectories(variantsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var slug = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                var state = slug.StartsWith("prev-", StringComparison.OrdinalIgnoreCase)
                    ? "prev-adopted"
                    : "variant";
                var summary = SemanticPlanSummaryHelper.DeriveSummary(projectPath, designZoneId, slug);
                var createdAt = Directory.GetCreationTimeUtc(dir).ToString("o");

                response.Variants.Add(new VariantMetadata
                {
                    Slug = slug,
                    CreatedAt = createdAt,
                    State = state,
                    Summary = summary ?? string.Empty
                });
            }

            response.Variants.Sort((a, b) =>
                string.Compare(a.CreatedAt, b.CreatedAt, StringComparison.Ordinal));

            return Ok(response);
        }

        // ─────────────────────────── GetVariantsSummary ───────────────────────────

        /// <summary>
        /// 按 designZoneId 索引的变体计数摘要。
        /// 数据源：扫 schemes/{dz}/variants/{slug}/ 子目录，无需读 sidecar。
        /// 零变体的 design zone 不入字典。Web 端按 designZoneId 取 (current/total) 分页号。
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

            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            foreach (var designZoneDir in Directory.EnumerateDirectories(schemesPath, "*", SearchOption.TopDirectoryOnly))
            {
                var dzId = Path.GetFileName(designZoneDir);
                if (string.IsNullOrWhiteSpace(dzId) || !topology.IsDesignZoneId(dzId))
                    continue;

                var variantsRoot = Path.Combine(designZoneDir, "variants");
                if (!Directory.Exists(variantsRoot))
                    continue;

                var slugs = Directory.EnumerateDirectories(variantsRoot, "*", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Cast<string>()
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (slugs.Count > 0)
                {
                    result[dzId] = new VariantSummaryEntry
                    {
                        Count = slugs.Count,
                        VariantSlugs = slugs
                    };
                }
            }

            return Ok(result);
        }

        // ─────────────────────────── GetVariantModules ───────────────────────────

        /// <summary>
        /// 读取指定 design zone + variant slug + leaf zone 的 modules（New 路径）。
        /// 路径：schemes/{designZoneId}/variants/{variantSlug}/{leafZoneId}/modules.json
        /// </summary>
        [HttpGet("variants/{designZoneId}/{variantSlug}/modules")]
        public ActionResult<SchemeModulesResponse> GetVariantModules(
            string designZoneId,
            string variantSlug,
            [FromQuery] string leafZoneId)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try { ModuleFileTopologyService.EnsureSafeVariantId(variantSlug); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

            if (!TryResolveDesignZoneRoot(designZoneId, out _, out var dzError))
                return NotFound(new { error = dzError });

            if (string.IsNullOrWhiteSpace(leafZoneId))
                return BadRequest(new { error = "leafZoneId 不能为空" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            var filePath = _modulesWriter.ResolveModulesPath(
                projectPath, designZoneId, leafZoneId, variantSlug, VariantPathMode.New);

            if (!System.IO.File.Exists(filePath))
                return NotFound(new { error = $"变体 modules.json 不存在: {Path.GetRelativePath(projectPath, filePath)}" });

            try
            {
                var modules = _modulesReader.ReadModulesOnly(filePath) ?? new List<Module>();
                foreach (var module in modules)
                {
                    if (string.IsNullOrEmpty(module.ZoneId))
                        module.ZoneId = leafZoneId;
                }
                return Ok(new SchemeModulesResponse
                {
                    Source = $"variant:{designZoneId}/{variantSlug}",
                    Branch = leafZoneId,
                    Modules = modules
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取变体模块失败: {File}", filePath);
                return StatusCode(500, new { error = $"读取变体失败: {ex.Message}" });
            }
        }

        // ─────────────────────────── AdoptVariant ───────────────────────────

        /// <summary>
        /// 采纳变体：检测 canonical → 降级（如非空）→ 晋升 → 删除被采纳变体。
        /// 协议：POST {designZoneId, variantSlug}。
        /// </summary>
        [HttpPost("variant/adopt")]
        public async Task<IActionResult> AdoptVariant([FromBody] AdoptVariantRequest? request)
        {
            if (request == null)
                return BadRequest(new { error = "请求体无效" });
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try { ModuleFileTopologyService.EnsureSafeVariantId(request.VariantSlug); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

            if (!TryResolveDesignZoneRoot(request.DesignZoneId, out var designZoneRoot, out var dzError))
                return NotFound(new { error = dzError });

            var variantDir = Path.Combine(designZoneRoot, "variants", request.VariantSlug);
            if (!Directory.Exists(variantDir))
                return NotFound(new { error = $"变体目录不存在: variants/{request.VariantSlug}" });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            var schemesPath = Path.Combine(projectPath, "schemes");
            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            var leafZoneIds = topology.GetLeafZoneIds(request.DesignZoneId);
            if (leafZoneIds.Count == 0)
                return BadRequest(new { error = $"设计区 {request.DesignZoneId} 无叶子分区" });

            // 同 designZone 的 adopt/delete 串行化（防同一窗口快速重复请求 / SignalR 重发引发的多文件竞态）
            var adoptLock = GetDesignZoneLock(request.DesignZoneId);
            if (!await adoptLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return StatusCode(503, new { error = "设计区被其他变体操作占用，请稍后重试" });

            try
            {
                // 1) 加载所有叶子的 variant wrapper；必须至少一个 modules 非空才视为有效采纳
                var variantWrappersByLeaf = new Dictionary<string, ModulesWrapper>(StringComparer.OrdinalIgnoreCase);
                foreach (var leafId in leafZoneIds)
                {
                    var variantPath = _modulesWriter.ResolveModulesPath(
                        projectPath, request.DesignZoneId, leafId, request.VariantSlug, VariantPathMode.New);
                    if (!System.IO.File.Exists(variantPath))
                        continue;

                    var wrapper = _modulesReader.Read(variantPath);
                    if (wrapper != null)
                        variantWrappersByLeaf[leafId] = wrapper;
                }

                if (!variantWrappersByLeaf.Any(kv => kv.Value.Modules.Count > 0))
                    return BadRequest(new { error = "变体所有叶子 modules 均为空，无效采纳" });

                // 2) 检测 canonical 是否非空（触发降级条件）
                var canonicalWrappersByLeaf = new Dictionary<string, ModulesWrapper>(StringComparer.OrdinalIgnoreCase);
                foreach (var leafId in leafZoneIds)
                {
                    var canonicalPath = _modulesWriter.ResolveModulesPath(
                        projectPath, request.DesignZoneId, leafId, variantId: null, VariantPathMode.New);
                    if (!System.IO.File.Exists(canonicalPath))
                        continue;

                    var wrapper = _modulesReader.Read(canonicalPath);
                    if (wrapper != null && wrapper.Modules.Count > 0)
                        canonicalWrappersByLeaf[leafId] = wrapper;
                }

                // 3) 降级（如适用）：把当前 canonical 非空叶子写到 variants/prev-{ts}/
                string? demotedSlug = null;
                if (canonicalWrappersByLeaf.Count > 0)
                {
                    var now = DateTime.Now;
                    demotedSlug = $"prev-{now:yyyyMMddHHmmss}";

                    foreach (var (leafId, wrapper) in canonicalWrappersByLeaf)
                    {
                        await _modulesWriter.WriteAsync(
                            projectPath, request.DesignZoneId, leafId,
                            variantId: demotedSlug, pathMode: VariantPathMode.New,
                            modules: wrapper.Modules,
                            adoptedAt: now.ToUniversalTime(),
                            sourceWorkflowOverride: "prev-adopted");
                    }
                }

                // 4) 晋升：把被采纳 variant 的每个叶子写到 canonical（透传原 sourceWorkflow）
                var promoteNow = DateTime.UtcNow;
                foreach (var (leafId, wrapper) in variantWrappersByLeaf)
                {
                    var inheritedSourceWorkflow = string.IsNullOrWhiteSpace(wrapper.SchemeMetadata?.SourceWorkflow)
                        ? "unknown"
                        : wrapper.SchemeMetadata!.SourceWorkflow;

                    await _modulesWriter.WriteAsync(
                        projectPath, request.DesignZoneId, leafId,
                        variantId: null, pathMode: VariantPathMode.New,
                        modules: wrapper.Modules,
                        adoptedAt: promoteNow,
                        sourceWorkflowOverride: inheritedSourceWorkflow);
                }

                // 5) 删除被采纳变体整目录（含 semantic_plan.json / 各叶子 modules）
                try { Directory.Delete(variantDir, recursive: true); }
                catch (Exception ex) { _logger.LogWarning(ex, "删除被采纳变体目录失败: {Dir}", variantDir); }

                _logger.LogInformation(
                    "[Variant.Adopt] designZone={Dz} slug={Slug} 晋升为 canonical；降级 prev={Prev}（受影响叶子 {N}）",
                    request.DesignZoneId, request.VariantSlug, demotedSlug ?? "(无)", variantWrappersByLeaf.Count);

                // 6) SignalR 广播：trigger=variant-adopt，payload 含 designZoneId + adoptedSlug + demotedSlug
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                {
                    type = "file_changed",
                    file = "modules.json",
                    timestamp = DateTime.UtcNow,
                    action = "reload",
                    trigger = "variant-adopt",
                    designZoneId = request.DesignZoneId,
                    adoptedSlug = request.VariantSlug,
                    demotedSlug
                });

                return Ok(new
                {
                    success = true,
                    adopted = request.VariantSlug,
                    designZoneId = request.DesignZoneId,
                    demotedSlug
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "采纳变体失败");
                return StatusCode(500, new { error = $"采纳失败: {ex.Message}" });
            }
            finally
            {
                adoptLock.Release();
            }
        }

        // ─────────────────────────── CloneVariant ───────────────────────────

        /// <summary>
        /// 克隆设计区方案（canonical 或某变体）到一个或多个新变体目录。
        /// 协议：POST {designZoneId, sourceVariant, newVariantSlugs[], overwrite}。
        /// 复制范围：semantic_plan.json + reference_analysis.json + 各叶子 modules.json（字节级 File.Copy，
        /// 不重新派生 schemeMetadata）。state / summary / sourceWorkflow 由 Server 在 ListVariants /
        /// 各 modules.json wrapper 里按 slug 前缀 + semantic_plan 内容派生，磁盘上不再写 sidecar。
        /// 部分成功允许：单 slug 失败进 errors，已成功 slug 目录保留。
        /// </summary>
        [HttpPost("variant/clone")]
        public async Task<ActionResult<CloneVariantResponse>> CloneVariant([FromBody] CloneVariantRequest? request)
        {
            if (request == null)
                return BadRequest(new { error = "请求体无效" });
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });
            if (request.NewVariantSlugs == null || request.NewVariantSlugs.Count == 0)
                return BadRequest(new { error = "newVariantSlugs 不能为空" });

            if (!TryResolveDesignZoneRoot(request.DesignZoneId, out var designZoneRoot, out var dzError))
                return NotFound(new { error = dzError });

            // 规范化 sourceVariant：null/空/"canonical" → 视为 canonical
            var sourceIsCanonical = string.IsNullOrWhiteSpace(request.SourceVariant)
                || string.Equals(request.SourceVariant, "canonical", StringComparison.OrdinalIgnoreCase);
            string? safeSourceSlug = null;
            string srcRoot;
            string sourceLabel;

            if (sourceIsCanonical)
            {
                srcRoot = designZoneRoot;
                sourceLabel = "canonical";
            }
            else
            {
                try { ModuleFileTopologyService.EnsureSafeVariantId(request.SourceVariant!); }
                catch (ArgumentException ex) { return BadRequest(new { error = $"sourceVariant 非法: {ex.Message}" }); }

                safeSourceSlug = request.SourceVariant!;
                srcRoot = Path.Combine(designZoneRoot, "variants", safeSourceSlug);
                if (!Directory.Exists(srcRoot))
                    return BadRequest(new { error = $"source-not-found: variants/{safeSourceSlug}" });
                sourceLabel = safeSourceSlug;
            }

            // newVariantSlugs 预校验（含自我克隆冲突）
            var safeNewSlugs = new List<string>(request.NewVariantSlugs.Count);
            foreach (var rawSlug in request.NewVariantSlugs)
            {
                try { ModuleFileTopologyService.EnsureSafeVariantId(rawSlug); }
                catch (ArgumentException ex) { return BadRequest(new { error = $"newVariantSlugs 非法: {ex.Message}" }); }

                if (!sourceIsCanonical && string.Equals(rawSlug, safeSourceSlug, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = $"cannot-clone-onto-source: newVariantSlugs 含源 slug '{safeSourceSlug}'" });

                safeNewSlugs.Add(rawSlug);
            }

            // 与 adopt/delete 共享 designZone 锁，防多文件竞态
            var cloneLock = GetDesignZoneLock(request.DesignZoneId);
            if (!await cloneLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return StatusCode(503, new { error = "设计区被其他变体操作占用，请稍后重试" });

            var response = new CloneVariantResponse();
            try
            {
                // ── 锁内枚举源文件清单（绝对路径列表）──
                var projectPath = _projectContext.GetActiveWorktreePath()
                                  ?? _projectContext.CurrentProjectPath!;
                var schemesPath = Path.Combine(projectPath, "schemes");
                var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
                var leafZoneIds = topology.GetLeafZoneIds(request.DesignZoneId);

                var srcFiles = new List<string>();

                // semantic_plan.json + reference_analysis.json
                var srcSemanticPlan = Path.Combine(srcRoot, "semantic_plan.json");
                if (System.IO.File.Exists(srcSemanticPlan))
                    srcFiles.Add(srcSemanticPlan);
                var srcReferenceAnalysis = Path.Combine(srcRoot, "reference_analysis.json");
                if (System.IO.File.Exists(srcReferenceAnalysis))
                    srcFiles.Add(srcReferenceAnalysis);

                // 各叶子 modules.json
                foreach (var leafId in leafZoneIds)
                {
                    var leafModulesPath = _modulesWriter.ResolveModulesPath(
                        projectPath, request.DesignZoneId, leafId,
                        variantId: sourceIsCanonical ? null : safeSourceSlug,
                        pathMode: VariantPathMode.New);

                    if (System.IO.File.Exists(leafModulesPath))
                        srcFiles.Add(leafModulesPath);
                }

                // ── 循环每个 newSlug：复制源文件 ──
                foreach (var safeNew in safeNewSlugs)
                {
                    var dstRoot = Path.Combine(designZoneRoot, "variants", safeNew);
                    if (Directory.Exists(dstRoot))
                    {
                        if (!request.Overwrite)
                        {
                            response.Errors.Add(new CloneVariantError
                            {
                                Slug = safeNew,
                                Reason = "already-exists"
                            });
                            continue;
                        }
                        try { Directory.Delete(dstRoot, recursive: true); }
                        catch (Exception ex)
                        {
                            response.Errors.Add(new CloneVariantError
                            {
                                Slug = safeNew,
                                Reason = $"overwrite-delete-failed: {ex.Message}"
                            });
                            continue;
                        }
                    }

                    int fileCount = 0;
                    var ioFailed = false;
                    string? ioError = null;
                    try
                    {
                        Directory.CreateDirectory(dstRoot);
                        foreach (var src in srcFiles)
                        {
                            var relative = Path.GetRelativePath(srcRoot, src);
                            var dst = Path.Combine(dstRoot, relative);
                            var dstDir = Path.GetDirectoryName(dst)!;
                            Directory.CreateDirectory(dstDir);
                            System.IO.File.Copy(src, dst, overwrite: true);
                            fileCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        ioFailed = true;
                        ioError = ex.Message;
                    }

                    if (ioFailed)
                    {
                        response.Errors.Add(new CloneVariantError
                        {
                            Slug = safeNew,
                            Reason = $"copy-failed: {ioError}"
                        });
                        continue;
                    }

                    var dstRelative = Path.GetRelativePath(projectPath, dstRoot).Replace('\\', '/');
                    response.Created.Add(new CloneVariantResult
                    {
                        Slug = safeNew,
                        Path = dstRelative,
                        FileCount = fileCount
                    });
                }

                _logger.LogInformation(
                    "[Variant.Clone] designZone={Dz} source={Src} 创建 {Created} 个，失败 {Errors} 个",
                    request.DesignZoneId, sourceLabel, response.Created.Count, response.Errors.Count);

                // 单次 SignalR 广播（仅当有创建成功）
                if (response.Created.Count > 0)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                    {
                        type = "file_changed",
                        file = "modules.json",
                        timestamp = DateTime.UtcNow,
                        action = "reload",
                        trigger = "variant-cloned",
                        designZoneId = request.DesignZoneId,
                        createdSlugs = response.Created.Select(c => c.Slug).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "克隆变体失败");
                return StatusCode(500, new { error = $"克隆失败: {ex.Message}" });
            }
            finally
            {
                cloneLock.Release();
            }

            return Ok(response);
        }

        // ─────────────────────────── DeleteVariant ───────────────────────────

        /// <summary>
        /// 删除指定变体目录（schemes/{designZoneId}/variants/{variantSlug}/）。
        /// 不动 canonical 与其他变体。
        /// </summary>
        [HttpDelete("variant")]
        public async Task<IActionResult> DeleteVariant(
            [FromQuery] string designZoneId,
            [FromQuery] string variantSlug)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try { ModuleFileTopologyService.EnsureSafeVariantId(variantSlug); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

            if (!TryResolveDesignZoneRoot(designZoneId, out var designZoneRoot, out var dzError))
                return NotFound(new { error = dzError });

            var variantDir = Path.Combine(designZoneRoot, "variants", variantSlug);
            if (!Directory.Exists(variantDir))
                return NotFound(new { error = $"变体目录不存在: variants/{variantSlug}" });

            // 与 adopt 共享同一 designZone 锁——防"adopt 进行中另一边删变体"竞态
            var deleteLock = GetDesignZoneLock(designZoneId);
            if (!await deleteLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return StatusCode(503, new { error = "设计区被其他变体操作占用，请稍后重试" });

            try
            {
                // 锁内复查（adopt 可能刚把它清掉）
                if (!Directory.Exists(variantDir))
                    return NotFound(new { error = $"变体目录不存在: variants/{variantSlug}" });

                Directory.Delete(variantDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "删除变体目录失败: {Dir}", variantDir);
                return StatusCode(500, new { error = $"删除失败: {ex.Message}" });
            }
            finally
            {
                deleteLock.Release();
            }

            _logger.LogInformation(
                "[Variant.Delete] designZone={Dz} slug={Slug} 已删除", designZoneId, variantSlug);

            await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
            {
                type = "file_changed",
                file = "modules.json",
                timestamp = DateTime.UtcNow,
                action = "reload",
                trigger = "variant-deleted",
                designZoneId,
                variantSlug
            });

            return Ok(new { success = true, deleted = variantSlug, designZoneId });
        }

        // ───────────────────────── helpers ─────────────────────────

        /// <summary>
        /// 校验 designZoneId（合法名、是 design zone、目录存在），返回绝对路径 schemes/{designZoneId}/。
        /// </summary>
        private bool TryResolveDesignZoneRoot(string? designZoneId, out string designZoneRoot, out string error)
        {
            designZoneRoot = "";
            error = "";

            if (string.IsNullOrWhiteSpace(designZoneId))
            {
                error = "designZoneId 不能为空";
                return false;
            }
            // 防路径穿越：designZoneId 只能是单段标识符，不能含 / 或 ..
            if (designZoneId.Contains('/') || designZoneId.Contains('\\')
                || designZoneId.Contains("..") || designZoneId.Contains(':'))
            {
                error = "designZoneId 包含非法字符";
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
            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            if (!topology.IsDesignZoneId(designZoneId))
            {
                error = $"{designZoneId} 不是有效的 design zone";
                return false;
            }

            designZoneRoot = Path.GetFullPath(Path.Combine(schemesPath, designZoneId));
            if (!Directory.Exists(designZoneRoot))
            {
                error = $"设计区目录不存在: {designZoneId}";
                return false;
            }
            return true;
        }

    }

    public class AdoptVariantRequest
    {
        public string DesignZoneId { get; set; } = "";
        public string VariantSlug { get; set; } = "";
    }

    public class CloneVariantRequest
    {
        public string DesignZoneId { get; set; } = "";
        /// <summary>null/空/"canonical" 表示从 canonical 克隆；否则填某个已存在 variant slug。</summary>
        public string? SourceVariant { get; set; }
        public List<string> NewVariantSlugs { get; set; } = new List<string>();
        public bool Overwrite { get; set; } = false;
    }

    public class CloneVariantResponse
    {
        public List<CloneVariantResult> Created { get; set; } = new List<CloneVariantResult>();
        public List<CloneVariantError> Errors { get; set; } = new List<CloneVariantError>();
    }

    public class CloneVariantResult
    {
        public string Slug { get; set; } = "";
        public string Path { get; set; } = "";
        public int FileCount { get; set; }
    }

    public class CloneVariantError
    {
        public string Slug { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// GET /api/scheme/variants 的返回结构。
    /// </summary>
    public class VariantListResponse
    {
        public string DesignZoneId { get; set; } = "";
        public List<VariantMetadata> Variants { get; set; } = new List<VariantMetadata>();
    }

    /// <summary>
    /// GET /api/scheme/variants/summary 的字典值（designZone-level 索引）。
    /// </summary>
    public class VariantSummaryEntry
    {
        public int Count { get; set; }
        public List<string> VariantSlugs { get; set; } = new List<string>();
    }
}
