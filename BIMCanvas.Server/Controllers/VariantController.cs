using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Core.Models.Computed;
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
    /// 方案控制器（指针模型：schemes/{designZoneId}/{slug}/ 平级方案，无 variants/ 层、无固定 canonical）。
    /// 协议按 designZone + slug 索引；adopt = 翻父 {zoneId}/DESIGN.md 的 adopted 指针（零复制 / 零删除 / 零降级、可逆）。
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
        private readonly SchemeDesignDocService _designDoc;

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
            ModulesReaderService modulesReader,
            SchemeDesignDocService designDoc)
        {
            _logger = logger;
            _projectContext = projectContext;
            _hubContext = hubContext;
            _modulesWriter = modulesWriter;
            _modulesReader = modulesReader;
            _designDoc = designDoc;
        }

        // ─────────────────────────── ListVariants ───────────────────────────

        /// <summary>
        /// 列出指定 design zone 下所有方案（按 schemes/{dz}/ 子目录枚举）。
        /// 数据源：文件系统目录 + slug/adopted 指针约定（adopted 指向 → "adopted"，_ 前缀 → "hidden"，其他 → "variant"）+
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

            var response = new VariantListResponse { DesignZoneId = designZoneId };
            if (!Directory.Exists(designZoneRoot))
                return Ok(response);

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            var schemesPath = Path.Combine(projectPath, "schemes");
            var adopted = _designDoc.ReadAdoptedSlug(schemesPath, designZoneId);
            var topology = ModuleFileTopologyService.BuildFromSchemesPath(schemesPath);
            var leafZoneIds = topology.GetLeafZoneIds(designZoneId);

            // 指针模型：方案 = schemes/{dz}/ 下的子目录（每个一个 slug）；adopted 指针标生效；_ 前缀=隐藏候选。
            foreach (var dir in Directory.EnumerateDirectories(designZoneRoot, "*", SearchOption.TopDirectoryOnly))
            {
                var slug = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(slug))
                    continue;
                if (string.Equals(slug, "variants", StringComparison.OrdinalIgnoreCase))
                    continue; // 跳过存量遗留 variants/ 目录（未迁移项目）

                var state = string.Equals(slug, adopted, StringComparison.OrdinalIgnoreCase)
                    ? "adopted"
                    : slug.StartsWith("_", StringComparison.Ordinal)
                        ? "hidden"
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
        /// 按 designZoneId 索引的可显示方案计数摘要。
        /// 数据源：扫 schemes/{dz}/{slug}/ 子目录（仅不以 _ 开头的可显示方案，§3.4），无需读 sidecar。
        /// 零方案的 design zone 不入字典。Web 端按 designZoneId 取 (current/total) 分页号。
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

                // 角标口径必须与 ListVariants/VariantNavigatorBar 的 visibleVariants 一致：
                // 排除 adopted（它由前端 canonical 槽「已采纳方案」代表，Web 端 total=count+1 的 +1 即它）、
                // _ 隐藏候选、存量 variants/。若把 adopted 也计入 count，前端会多算一档——每个已采纳设计区的
                // Zone label 虚显角标（单方案显示 (1/2)），多方案页码与导航条漂移。
                var adopted = _designDoc.ReadAdoptedSlug(schemesPath, dzId);
                var slugs = Directory.EnumerateDirectories(designZoneDir, "*", SearchOption.TopDirectoryOnly)
                    .Where(dir =>
                    {
                        var name = Path.GetFileName(dir);
                        return !string.IsNullOrWhiteSpace(name)
                            && !string.Equals(name, "variants", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(name, adopted, StringComparison.OrdinalIgnoreCase)
                            && !name.StartsWith("_", StringComparison.Ordinal);
                    })
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
        /// 读取指定 design zone + 方案 slug + leaf zone 的 modules（指针模型路径）。
        /// 路径：schemes/{designZoneId}/{variantSlug}/{leafZoneId}/modules.json（无 variants/ 层）
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
                projectPath, designZoneId, leafZoneId, variantSlug);

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

        // ─────────────────────────── GetVariantZones ───────────────────────────

        /// <summary>
        /// 读取指定 design zone + 方案 slug 的有效分区（SubZones），供 Web 实时切换候选方案时让分区线跟随该方案。
        /// 与 GetVariantModules 对称（modules ↔ zones）。
        /// 塑形**必须复用 BuildEffectiveZoneView(by variantId)**——与首屏 adopted / 截图路径同一塑形源，
        /// 禁裸读 {dz}/{slug}/zones.json 自塑形（否则三者分区线漂移，毁 P3 单一塑形源 PerSchemeZoneTreeBuilder）。
        /// scope 仅限本设计区：只该 dz 用 variantSlug 重算，其余设计区保持 adopted（不蔓延）。
        /// </summary>
        [HttpGet("variants/{designZoneId}/{variantSlug}/zones")]
        public ActionResult<VariantZonesResponse> GetVariantZones(string designZoneId, string variantSlug)
        {
            if (!_projectContext.IsLoaded)
                return BadRequest(new { error = "未加载项目" });

            try { ModuleFileTopologyService.EnsureSafeVariantId(variantSlug); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }

            if (!TryResolveDesignZoneRoot(designZoneId, out _, out var dzError))
                return NotFound(new { error = dzError });

            var projectPath = _projectContext.GetActiveWorktreePath()
                              ?? _projectContext.CurrentProjectPath!;
            var schemesPath = Path.Combine(projectPath, "schemes");

            // 复用同一塑形源；scope=[designZoneId] 使仅该 dz 用候选 slug，其余仍 adopted。
            var roots = ProjectService.BuildEffectiveZoneView(
                schemesPath, variantSlug, new[] { designZoneId });
            var dz = roots.FirstOrDefault(z =>
                string.Equals(z.Id, designZoneId, StringComparison.OrdinalIgnoreCase));

            // 单叶子候选（无 {slug}/zones.json）→ SubZones 为空，前端按"无内部分区"渲染（正确）。
            return Ok(new VariantZonesResponse
            {
                DesignZoneId = designZoneId,
                VariantSlug = variantSlug,
                SubZones = dz?.SubZones ?? new List<Zone>()
            });
        }

        // ─────────────────────────── AdoptVariant ───────────────────────────

        /// <summary>
        /// 采纳方案 = 翻指针：校验非空 →（候选以 _ 前缀隐藏则去前缀转正）→ 写父 {zoneId}/DESIGN.md adopted。
        /// 零复制 / 零删除 / 零降级、可逆；不再有"晋升 canonical / 降级 prev / 删变体目录"。
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

            var variantDir = Path.Combine(designZoneRoot, request.VariantSlug);
            if (!Directory.Exists(variantDir))
                return NotFound(new { error = $"方案目录不存在: {request.VariantSlug}" });

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
                // 1) 校验被采纳方案至少一个叶子有非空 modules。
                //    必补-1：候选叶子结构必须读**该候选自身**的 {slug}/zones.json（EnumerateSchemeLeaves），
                //    不能用上面 GetLeafZoneIds —— 后者按设计区当前 adopted slug 解析叶子。首次采纳时 adopted
                //    为空，topology 回落单叶子占位 [designZoneId]，而候选自身是 AI 多叶子（dz_1/dz_2），
                //    ResolveModulesPath 据占位拼出的 {slug}/modules.json 顶层路径不存在（真实在 {slug}/{dz}/modules.json）
                //    → 误判全空（首采纳 chicken-and-egg：用未来 adopted 的结构校验正要采纳的第一个候选）。
                var hasModules = false;
                foreach (var entry in ModuleFileTopologyService.EnumerateSchemeLeaves(
                             schemesPath, request.DesignZoneId, request.VariantSlug))
                {
                    if (!System.IO.File.Exists(entry.FilePath))
                        continue;
                    var wrapper = _modulesReader.Read(entry.FilePath);
                    if (wrapper != null && wrapper.Modules.Count > 0) { hasModules = true; break; }
                }
                if (!hasModules)
                    return BadRequest(new { error = "方案所有叶子 modules 均为空，无效采纳" });

                // 2) 转正：候选若以 _ 前缀隐藏，去前缀重命名目录（_slug → slug），使其在 Web 可见
                var adoptedSlug = request.VariantSlug;
                if (adoptedSlug.StartsWith("_", StringComparison.Ordinal))
                {
                    var promoted = adoptedSlug.TrimStart('_');
                    if (string.IsNullOrWhiteSpace(promoted))
                        return BadRequest(new { error = $"非法 slug：{request.VariantSlug}" });
                    var promotedDir = Path.Combine(designZoneRoot, promoted);
                    if (Directory.Exists(promotedDir))
                        return Conflict(new { error = $"转正目标已存在：{promoted}" });
                    Directory.Move(variantDir, promotedDir);
                    adoptedSlug = promoted;
                }

                // 3) 翻指针：写父 DESIGN.md adopted（零复制 / 零删除 / 零降级 / 可逆，根除"采纳即删设计意图"）
                _designDoc.WriteAdoptedSlug(schemesPath, request.DesignZoneId, adoptedSlug);

                _logger.LogInformation(
                    "[Variant.Adopt] designZone={Dz} slug={Slug} → 翻指针 adopted={Adopted}（叶子 {N}）",
                    request.DesignZoneId, request.VariantSlug, adoptedSlug, leafZoneIds.Count);

                // 4) SignalR 广播（指针模型无降级 prev，去 demotedSlug 语义）
                await _hubContext.Clients.All.SendAsync("ReceiveUpdate", new
                {
                    type = "file_changed",
                    file = "modules.json",
                    timestamp = DateTime.UtcNow,
                    action = "reload",
                    trigger = "variant-adopt",
                    designZoneId = request.DesignZoneId,
                    adoptedSlug
                });

                return Ok(new
                {
                    success = true,
                    adopted = adoptedSlug,
                    designZoneId = request.DesignZoneId
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
                srcRoot = Path.Combine(designZoneRoot, safeSourceSlug);
                if (!Directory.Exists(srcRoot))
                    return BadRequest(new { error = $"source-not-found: {safeSourceSlug}" });
            }
            else if (mode == "clone-from-canonical")
            {
                // 指针模型：canonical = 当前 adopted 方案目录。无指针（存量未迁移）时拒绝——
                // 从设计区根整克隆会把兄弟方案目录 / dstRoot 自身递归复制进来（自包含 bug），故要求先有 adopted。
                var schemesRootForClone = Path.GetDirectoryName(designZoneRoot)!;
                var adoptedForClone = _designDoc.ReadAdoptedSlug(schemesRootForClone, request.DesignZoneId);
                if (string.IsNullOrEmpty(adoptedForClone))
                    return BadRequest(new { error = "clone-from-canonical 需先有 adopted 指针（存量项目请先迁移或采纳一个方案）" });
                srcRoot = Path.Combine(designZoneRoot, adoptedForClone);
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

                // clone 模式不再枚举命名文件——整子树递归复制（见 per-slug 循环内 clone 分支）。

                // 循环每个新 slug
                foreach (var safeNew in safeNewSlugs)
                {
                    var dstRoot = Path.Combine(designZoneRoot, safeNew);
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
                                    variantId: safeNew,
                                    modules: new List<Module>(),
                                    summary: summary);

                                var leafPath = _modulesWriter.ResolveModulesPath(
                                    projectPath, request.DesignZoneId, leafId,
                                    safeNew);
                                leafPaths[leafId] = leafPath;
                            }
                        }
                        else
                        {
                            // clone 模式：整子树递归复制（domain-agnostic，不枚举任何具体文件名）。
                            // 指针模型下 src 恒为某方案目录 schemes/{dz}/{slug}/（clone-from-canonical 取 adopted、
                            // clone-from-variant 取源 slug），与 dstRoot schemes/{dz}/{safeNew}/ 互为兄弟，无自包含风险，无需排除。
                            CopyDirectoryTree(srcRoot!, dstRoot, excludeTopLevelDirs: null);

                            // 复制后重写每份叶子 modules.json 的 summary——modules 是平台 reserved
                            // kind，summary 注入是平台职责，不属 domain 耦合，保留原逻辑不动。
                            foreach (var leafId in leafZoneIds)
                            {
                                var leafPath = _modulesWriter.ResolveModulesPath(
                                    projectPath, request.DesignZoneId, leafId,
                                    safeNew);
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
        /// 删除指定方案目录（schemes/{designZoneId}/{variantSlug}/）。
        /// 不动其他方案与父 adopted 指针。
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

            var variantDir = Path.Combine(designZoneRoot, variantSlug);
            if (!Directory.Exists(variantDir))
                return NotFound(new { error = $"方案目录不存在: {variantSlug}" });

            // 与 adopt 共享同一 designZone 锁——防"adopt 进行中另一边删方案"竞态
            var deleteLock = GetDesignZoneLock(designZoneId);
            if (!await deleteLock.WaitAsync(TimeSpan.FromSeconds(30)))
                return StatusCode(503, new { error = "设计区被其他变体操作占用，请稍后重试" });

            try
            {
                // 锁内复查（adopt 可能刚把它清掉）
                if (!Directory.Exists(variantDir))
                    return NotFound(new { error = $"方案目录不存在: {variantSlug}" });

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

            // 变体 / canonical 业务数据根 = schemes/{designZoneId}/。
            designZoneRoot = Path.GetFullPath(Path.Combine(schemesPath, designZoneId));
            if (!Directory.Exists(designZoneRoot))
            {
                error = $"设计区目录不存在: {designZoneId}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// 递归复制目录树。excludeTopLevelDirs 仅在第一层生效（保留参数；指针模型下 clone 源恒为某方案目录
        /// schemes/{dz}/{slug}/、与 dstRoot 互为兄弟，一般传 null）。整树复制让 clone domain-agnostic：
        /// 方案目录下新增任何 domain 文件都自动随克隆。
        /// </summary>
        private static void CopyDirectoryTree(string srcDir, string dstDir, ISet<string>? excludeTopLevelDirs)
        {
            Directory.CreateDirectory(dstDir);
            foreach (var file in Directory.EnumerateFiles(srcDir))
                System.IO.File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.EnumerateDirectories(srcDir))
            {
                var name = Path.GetFileName(dir);
                if (excludeTopLevelDirs != null && excludeTopLevelDirs.Contains(name))
                    continue;
                CopyDirectoryTree(dir, Path.Combine(dstDir, name), excludeTopLevelDirs: null); // 排除仅顶层
            }
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
                        projectPath, designZoneId, leafId, variantSlug);
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

    /// <summary>GET /api/scheme/variants/{dz}/{slug}/zones 响应体（变体分区线，供 Web 实时切换跟随）。</summary>
    public class VariantZonesResponse
    {
        public string DesignZoneId { get; set; } = "";
        public string VariantSlug { get; set; } = "";
        public List<Zone> SubZones { get; set; } = new List<Zone>();
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
