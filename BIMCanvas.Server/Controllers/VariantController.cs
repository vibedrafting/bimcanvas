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
        /// 变体各叶子 modules.json 的 schemeMetadata.summary 取第一个非空（由 AI 在 register_variant 时写入）。
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
            var schemesPath = Path.Combine(projectPath, "schemes");
            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            var leafZoneIds = topology.GetLeafZoneIds(designZoneId);

            foreach (var dir in Directory.EnumerateDirectories(variantsRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var slug = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;

                var state = slug.StartsWith("prev-", StringComparison.OrdinalIgnoreCase)
                    ? "prev-adopted"
                    : "variant";
                var summary = DeriveVariantSummaryFromModules(projectPath, designZoneId, slug, leafZoneIds);
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

                // 排序基必须与 ListVariants 一致（按目录创建时间升序），
                // 否则 VariantNavigatorBar (createdAt) 和 Zone label 角标 (此处) 的页码会错位。
                var slugs = Directory.EnumerateDirectories(variantsRoot, "*", SearchOption.TopDirectoryOnly)
                    .Where(dir => !string.IsNullOrWhiteSpace(Path.GetFileName(dir)))
                    .OrderBy(dir => Directory.GetCreationTimeUtc(dir))
                    .Select(dir => Path.GetFileName(dir)!)
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

            // R3 写入 gate (V12a / 主真理源 §3.11 / §4.7):pending 状态拒绝写入
            var writeGate = _projectContext.CheckWriteAllowed();
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

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

                // 3) 降级（如适用）：把当前 canonical 非空叶子写到 variants/prev-{ts}/，透传原 summary
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
                            summary: wrapper.SchemeMetadata?.Summary ?? string.Empty);
                    }
                }

                // 4) 晋升：把被采纳 variant 的每个叶子写到 canonical，透传 variant 的 summary
                foreach (var (leafId, wrapper) in variantWrappersByLeaf)
                {
                    await _modulesWriter.WriteAsync(
                        projectPath, request.DesignZoneId, leafId,
                        variantId: null, pathMode: VariantPathMode.New,
                        modules: wrapper.Modules,
                        summary: wrapper.SchemeMetadata?.Summary ?? string.Empty);
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

        // ─────────────────────────── RegisterVariants ───────────────────────────

        /// <summary>
        /// 注册一个或多个变体（申请制）。统一变体目录创建入口，承担三种来源：
        ///   mode=blank             —— 从空白创建（variant-design-agent 用）
        ///   mode=clone-from-canonical —— 从 canonical 复制（module-relocation-agent 用）
        ///   mode=clone-from-variant   —— 从某已存在变体复制
        /// 行为：mkdir + 按 zones.json 拓扑创建各叶子子目录 + 写入 modules.json 含 schemeMetadata.summary
        /// （summary 由 AI 在调用时显式提交；clone 模式下复制源文件后替换每份 modules.json 的 summary）。
        /// 部分成功允许：单 slug 失败进 errors，已成功 slug 目录保留。
        /// </summary>
        [HttpPost("variant/register")]
        public async Task<ActionResult<RegisterVariantsResponse>> RegisterVariants([FromBody] RegisterVariantsRequest? request)
        {
            if (request == null)
                return BadRequest(new { error = "请求体无效" });
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            // R3 写入 gate (V12a / 主真理源 §3.11 / §4.7): pending 状态拒绝写入
            var writeGate = _projectContext.CheckWriteAllowed();
            if (!writeGate.Allowed)
                return StatusCode(403, new { code = writeGate.Code, message = writeGate.Message });

            if (request.Slugs == null || request.Slugs.Count == 0)
                return BadRequest(new { error = "slugs 不能为空" });
            if (string.IsNullOrWhiteSpace(request.Mode))
                return BadRequest(new { error = "mode 必填" });

            var mode = request.Mode.Trim().ToLowerInvariant();
            if (mode != "blank" && mode != "clone-from-canonical" && mode != "clone-from-variant")
                return BadRequest(new { error = $"mode 非法 '{request.Mode}'（应为 blank / clone-from-canonical / clone-from-variant）" });

            if (!TryResolveDesignZoneRoot(request.DesignZoneId, out var designZoneRoot, out var dzError))
                return NotFound(new { error = dzError });

            // 解析源（clone 模式才需要）
            string? safeSourceSlug = null;
            string? srcRoot = null;
            if (mode == "clone-from-variant")
            {
                if (string.IsNullOrWhiteSpace(request.SourceVariant))
                    return BadRequest(new { error = "mode=clone-from-variant 时 sourceVariant 必填" });
                try { ModuleFileTopologyService.EnsureSafeVariantId(request.SourceVariant); }
                catch (ArgumentException ex) { return BadRequest(new { error = $"sourceVariant 非法: {ex.Message}" }); }
                safeSourceSlug = request.SourceVariant;
                srcRoot = Path.Combine(designZoneRoot, "variants", safeSourceSlug);
                if (!Directory.Exists(srcRoot))
                    return BadRequest(new { error = $"source-not-found: variants/{safeSourceSlug}" });
            }
            else if (mode == "clone-from-canonical")
            {
                srcRoot = designZoneRoot;
            }

            // 校验所有 slugs charset + 自我克隆冲突
            var safeNewSlugs = new List<string>(request.Slugs.Count);
            foreach (var rawSlug in request.Slugs)
            {
                try { ModuleFileTopologyService.EnsureSafeVariantId(rawSlug); }
                catch (ArgumentException ex) { return BadRequest(new { error = $"slug 非法 '{rawSlug}': {ex.Message}" }); }
                if (mode == "clone-from-variant"
                    && string.Equals(rawSlug, safeSourceSlug, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = $"cannot-clone-onto-source: slugs 含源 slug '{safeSourceSlug}'" });
                safeNewSlugs.Add(rawSlug);
            }

            var summary = request.Summary ?? string.Empty;
            var overwrite = request.Overwrite;

            // 与 adopt/delete 共享 designZone 锁，防多文件竞态
            var registerLock = GetDesignZoneLock(request.DesignZoneId);
            if (!await registerLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return StatusCode(503, new { error = "设计区被其他变体操作占用，请稍后重试" });

            var response = new RegisterVariantsResponse();
            try
            {
                var projectPath = _projectContext.GetActiveWorktreePath()
                                  ?? _projectContext.CurrentProjectPath!;
                var schemesPath = Path.Combine(projectPath, "schemes");
                var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
                var leafZoneIds = topology.GetLeafZoneIds(request.DesignZoneId);
                if (leafZoneIds.Count == 0)
                    return BadRequest(new { error = $"设计区 {request.DesignZoneId} 无叶子分区" });

                // clone 模式：锁内枚举源文件清单
                var srcFiles = new List<string>();
                if (mode != "blank")
                {
                    var srcSemanticPlan = Path.Combine(srcRoot!, "semantic_plan.json");
                    if (System.IO.File.Exists(srcSemanticPlan)) srcFiles.Add(srcSemanticPlan);
                    var srcReferenceAnalysis = Path.Combine(srcRoot!, "reference_analysis.json");
                    if (System.IO.File.Exists(srcReferenceAnalysis)) srcFiles.Add(srcReferenceAnalysis);
                    foreach (var leafId in leafZoneIds)
                    {
                        var leafModulesPath = _modulesWriter.ResolveModulesPath(
                            projectPath, request.DesignZoneId, leafId,
                            variantId: safeSourceSlug, // null 表示 canonical
                            pathMode: VariantPathMode.New);
                        if (System.IO.File.Exists(leafModulesPath))
                            srcFiles.Add(leafModulesPath);
                    }
                }

                // 循环每个新 slug
                foreach (var safeNew in safeNewSlugs)
                {
                    var dstRoot = Path.Combine(designZoneRoot, "variants", safeNew);
                    if (Directory.Exists(dstRoot))
                    {
                        if (!overwrite)
                        {
                            response.Errors.Add(new RegisterVariantError { Slug = safeNew, Reason = "already-exists" });
                            continue;
                        }
                        try { Directory.Delete(dstRoot, recursive: true); }
                        catch (Exception ex)
                        {
                            response.Errors.Add(new RegisterVariantError
                            {
                                Slug = safeNew,
                                Reason = $"overwrite-delete-failed: {ex.Message}"
                            });
                            continue;
                        }
                    }

                    try
                    {
                        Directory.CreateDirectory(dstRoot);
                        var leafPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (mode == "blank")
                        {
                            // 为每个叶子写空 modules.json（带 summary）
                            foreach (var leafId in leafZoneIds)
                            {
                                await _modulesWriter.WriteAsync(
                                    projectPath, request.DesignZoneId, leafId,
                                    variantId: safeNew, pathMode: VariantPathMode.New,
                                    modules: new List<Module>(),
                                    summary: summary);

                                var leafPath = _modulesWriter.ResolveModulesPath(
                                    projectPath, request.DesignZoneId, leafId,
                                    safeNew, VariantPathMode.New);
                                leafPaths[leafId] = leafPath;
                            }
                        }
                        else
                        {
                            // clone 模式：复制源文件 → 重写每份 modules.json 的 summary
                            foreach (var src in srcFiles)
                            {
                                var relative = Path.GetRelativePath(srcRoot!, src);
                                var dst = Path.Combine(dstRoot, relative);
                                var dstDir = Path.GetDirectoryName(dst)!;
                                Directory.CreateDirectory(dstDir);
                                System.IO.File.Copy(src, dst, overwrite: true);
                            }

                            // 替换每份 modules.json 的 summary
                            foreach (var leafId in leafZoneIds)
                            {
                                var leafPath = _modulesWriter.ResolveModulesPath(
                                    projectPath, request.DesignZoneId, leafId,
                                    safeNew, VariantPathMode.New);
                                if (!System.IO.File.Exists(leafPath)) continue;

                                var wrapper = _modulesReader.Read(leafPath);
                                if (wrapper == null) continue;
                                wrapper.SchemeMetadata ??= new SchemeMetadata();
                                wrapper.SchemeMetadata.Summary = summary;
                                await _modulesWriter.WriteWrapperAsync(leafPath, wrapper);

                                leafPaths[leafId] = leafPath;
                            }
                        }

                        var dstRelative = Path.GetRelativePath(projectPath, dstRoot).Replace('\\', '/');
                        response.Created.Add(new RegisterVariantResult
                        {
                            Slug = safeNew,
                            Workdir = dstRoot,
                            WorkdirRelative = dstRelative,
                            LeafPaths = leafPaths
                        });
                    }
                    catch (Exception ex)
                    {
                        response.Errors.Add(new RegisterVariantError
                        {
                            Slug = safeNew,
                            Reason = $"register-failed: {ex.Message}"
                        });
                    }
                }

                _logger.LogInformation(
                    "[Variant.Register] designZone={Dz} mode={Mode} 创建 {Created} 个，失败 {Errors} 个",
                    request.DesignZoneId, mode, response.Created.Count, response.Errors.Count);

                if (response.Created.Count > 0)
                {
                    await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                    {
                        type = "file_changed",
                        file = "modules.json",
                        timestamp = DateTime.UtcNow,
                        action = "reload",
                        trigger = "variant-registered",
                        designZoneId = request.DesignZoneId,
                        mode,
                        createdSlugs = response.Created.Select(c => c.Slug).ToList()
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "注册变体失败");
                return StatusCode(500, new { error = $"注册失败: {ex.Message}" });
            }
            finally
            {
                registerLock.Release();
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

            // 变体 / canonical 业务数据根 = schemes/{active_scene}/{designZoneId}/(zones.json 仍共享 schemes/)。
            designZoneRoot = Path.GetFullPath(
                Path.Combine(BIMCanvas.Server.Services.Plugins.PluginPaths.ActiveSchemesRoot(projectPath), designZoneId));
            if (!Directory.Exists(designZoneRoot))
            {
                error = $"设计区目录不存在: {designZoneId}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 派生变体的 summary：遍历该变体所有叶子 modules.json，取第一个非空的 schemeMetadata.summary。
        /// 用于 ListVariants 显示。AI 在 register_variant 时已把 summary 复制到各叶子 modules.json。
        /// </summary>
        private string DeriveVariantSummaryFromModules(
            string projectPath, string designZoneId, string variantSlug, IReadOnlyList<string> leafZoneIds)
        {
            try
            {
                foreach (var leafId in leafZoneIds)
                {
                    var modulesPath = _modulesWriter.ResolveModulesPath(
                        projectPath, designZoneId, leafId, variantSlug, VariantPathMode.New);
                    if (!System.IO.File.Exists(modulesPath))
                        continue;
                    var wrapper = _modulesReader.Read(modulesPath);
                    var summary = wrapper?.SchemeMetadata?.Summary;
                    if (!string.IsNullOrWhiteSpace(summary))
                        return summary;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DeriveVariantSummaryFromModules 失败 designZone={Dz} slug={Slug}", designZoneId, variantSlug);
            }
            return string.Empty;
        }

    }

    public class AdoptVariantRequest
    {
        public string DesignZoneId { get; set; } = "";
        public string VariantSlug { get; set; } = "";
    }

    /// <summary>
    /// POST /api/scheme/variant/register 请求体。
    /// 三种 mode：blank / clone-from-canonical / clone-from-variant。
    /// </summary>
    public class RegisterVariantsRequest
    {
        public string DesignZoneId { get; set; } = "";
        /// <summary>批量注册的 slug 列表（每个变体一个 slug）。</summary>
        public List<string> Slugs { get; set; } = new List<string>();
        /// <summary>"blank" | "clone-from-canonical" | "clone-from-variant"</summary>
        public string Mode { get; set; } = "";
        /// <summary>设计意图，写到每个叶子 modules.json schemeMetadata.summary。</summary>
        public string Summary { get; set; } = "";
        /// <summary>mode=clone-from-variant 时必填。</summary>
        public string? SourceVariant { get; set; }
        /// <summary>同名 slug 是否覆盖。</summary>
        public bool Overwrite { get; set; } = false;
    }

    public class RegisterVariantsResponse
    {
        public List<RegisterVariantResult> Created { get; set; } = new List<RegisterVariantResult>();
        public List<RegisterVariantError> Errors { get; set; } = new List<RegisterVariantError>();
    }

    public class RegisterVariantResult
    {
        public string Slug { get; set; } = "";
        /// <summary>变体根目录绝对路径（host 文件系统）。</summary>
        public string Workdir { get; set; } = "";
        /// <summary>变体根目录相对项目根的路径（Agent 提示词友好）。</summary>
        public string WorkdirRelative { get; set; } = "";
        /// <summary>{ leafZoneId: 该叶子 modules.json 绝对路径 }。</summary>
        public Dictionary<string, string> LeafPaths { get; set; } = new Dictionary<string, string>();
    }

    public class RegisterVariantError
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
