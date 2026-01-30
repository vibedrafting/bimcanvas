using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BIMCanvas.Core.Converters.Json;
using BIMCanvas.Server.Dtos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BIMCanvas.Server.Services
{
    public class BackgroundScreenshotService : IAsyncDisposable
    {
        private const int DefaultViewportWidth = 1920;
        private const int DefaultViewportHeight = 1080;
        private const int MinViewportSide = 720;
        private const int MaxViewportSide = 4096;
        private const int MaxBatchParallelism = 5;
        private const double DefaultFullPadding = 1000;
        private const double DefaultViewPadding = 500;
        private const double PaddingRatio = 0.05;

        private readonly ProjectSnapshotService _snapshotService;
        private readonly ILogger<BackgroundScreenshotService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly SemaphoreSlim _semaphore;
        private readonly Lazy<Task<IPlaywright>> _playwright;
        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private int _contextScale = -1;
        private ColorScheme? _contextColorScheme;
        private int _currentViewportWidth;
        private int _currentViewportHeight;
        private bool _pageInitialized;
        private string? _pageProjectKey;
        private string? _cachedProjectKey;
        private ProjectData? _cachedProjectData;
        private readonly string _webBaseUrl;

        public BackgroundScreenshotService(
            ProjectSnapshotService snapshotService,
            IConfiguration configuration,
            ILogger<BackgroundScreenshotService> logger)
        {
            _snapshotService = snapshotService;
            _logger = logger;
            _semaphore = new SemaphoreSlim(1, 1);
            _playwright = new Lazy<Task<IPlaywright>>(Playwright.CreateAsync);

            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new Polygon2DConverter(), new FacingConverter() },
                Formatting = Formatting.None
            };

            _webBaseUrl = configuration["Web:BaseUrl"]
                          ?? configuration["BIMCANVAS_WEB_URL"]
                          ?? "http://localhost:5173";
        }

        public async Task<string> CaptureAsync(BackgroundScreenshotRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                throw new ArgumentException("projectPath 不能为空");
            }

            if (!Directory.Exists(request.ProjectPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {request.ProjectPath}");
            }

            if (request.Scale < 1 || request.Scale > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(request.Scale), "scale 必须在 1-4 之间");
            }

            var stopwatch = Stopwatch.StartNew();

            var projectKey = BuildProjectKey(request.ProjectPath, request.StrategyId);
            var projectData = GetProjectData(projectKey, request.ProjectPath, request.StrategyId);

            var viewMode = string.Equals(request.ViewMode, "ai", StringComparison.OrdinalIgnoreCase) ? "ai" : "human";
            var theme = string.Equals(request.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";

            ViewportConfig? viewport = null;
            if (request.Viewport != null)
            {
                viewport = new ViewportConfig
                {
                    Mode = string.IsNullOrWhiteSpace(request.Viewport.Mode)
                        ? "full"
                        : request.Viewport.Mode.ToLowerInvariant(),
                    RoomId = request.Viewport.RoomId,
                    ZoneId = request.Viewport.ZoneId,
                    Bounds = request.Viewport.Bounds
                };
            }

            var shouldReuseProject = string.Equals(_pageProjectKey, projectKey, StringComparison.Ordinal);
            var renderConfig = new RenderConfig
            {
                ProjectData = shouldReuseProject ? null : projectData,
                ProjectKey = projectKey,
                ViewMode = viewMode,
                Layers = request.Layers,
                LayerPreset = request.LayerPreset,
                LayerEnable = request.LayerEnable,
                LayerDisable = request.LayerDisable,
                Viewport = viewport,
                Theme = theme
            };

            var configJson = JsonConvert.SerializeObject(renderConfig, _jsonSettings);

            var autoFitViewport = request.AutoFitViewport ?? true;
            var viewportSize = autoFitViewport
                ? ResolveViewportSize(projectData, viewport)
                : new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight };

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var page = await GetPageAsync(viewportSize, request.Scale, renderConfig.Theme, cancellationToken);

                var imageData = await page.EvaluateAsync<string>(
                    "configJson => window.__renderAndCapture(JSON.parse(configJson))",
                    configJson);
                if (string.IsNullOrWhiteSpace(imageData))
                {
                    throw new InvalidOperationException("截图结果为空");
                }

                _pageProjectKey = projectKey;

                stopwatch.Stop();
                _logger.LogInformation("后台截图完成，耗时 {Elapsed}ms", stopwatch.ElapsedMilliseconds);

                return imageData;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<BackgroundScreenshotBatchResponse> CaptureBatchAsync(
            BackgroundScreenshotBatchRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectPath))
            {
                throw new ArgumentException("projectPath 不能为空");
            }

            if (!Directory.Exists(request.ProjectPath))
            {
                throw new DirectoryNotFoundException($"项目目录不存在: {request.ProjectPath}");
            }

            if (request.Scale < 1 || request.Scale > 4)
            {
                throw new ArgumentOutOfRangeException(nameof(request.Scale), "scale 必须在 1-4 之间");
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                throw new ArgumentException("items 不能为空");
            }

            var stopwatch = Stopwatch.StartNew();

            var projectKey = BuildProjectKey(request.ProjectPath, request.StrategyId);
            var projectData = GetProjectData(projectKey, request.ProjectPath, request.StrategyId);

            var theme = string.Equals(request.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
            var defaultAutoFit = request.AutoFitViewport ?? true;

            var contexts = new List<BatchItemContext>();
            for (var i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                var viewMode = string.Equals(item.ViewMode, "ai", StringComparison.OrdinalIgnoreCase) ? "ai" : "human";
                var viewport = NormalizeViewport(item.Viewport);
                var autoFit = item.AutoFitViewport ?? defaultAutoFit;
                var viewportSize = ResolveViewportSize(projectData, viewport, autoFit);

                var config = new RenderConfig
                {
                    ProjectData = null,
                    ProjectKey = projectKey,
                    ViewMode = viewMode,
                    Layers = item.Layers,
                    LayerPreset = item.LayerPreset,
                    LayerEnable = item.LayerEnable,
                    LayerDisable = item.LayerDisable,
                    Viewport = viewport,
                    Theme = theme
                };

                contexts.Add(new BatchItemContext(i, item.Name, config, viewportSize));
            }

            var response = new BackgroundScreenshotBatchResponse();
            var results = new BackgroundScreenshotBatchItemResult[contexts.Count];

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var parallelism = Math.Min(contexts.Count, MaxBatchParallelism);
                var queue = new Queue<BatchItemContext>(contexts);
                var queueLock = new object();
                var tasks = new List<Task>();
                var sessions = new List<BatchPageSession>();

                for (var i = 0; i < parallelism; i++)
                {
                    var session = new BatchPageSession();
                    sessions.Add(session);
                    tasks.Add(Task.Run(async () =>
                    {
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            BatchItemContext? item = null;
                            lock (queueLock)
                            {
                                if (queue.Count > 0)
                                {
                                    item = queue.Dequeue();
                                }
                            }

                            if (item == null)
                            {
                                break;
                            }

                            var itemWatch = Stopwatch.StartNew();
                            try
                            {
                                var page = await GetBatchPageAsync(session, item.ViewportSize, request.Scale, theme, cancellationToken);
                                var includeProjectData = !string.Equals(session.ProjectKey, projectKey, StringComparison.Ordinal);
                                if (includeProjectData)
                                {
                                    item.Config.ProjectData = projectData;
                                }

                                var configJson = JsonConvert.SerializeObject(item.Config, _jsonSettings);
                                var imageData = await page.EvaluateAsync<string>(
                                    "configJson => window.__renderAndCapture(JSON.parse(configJson))",
                                    configJson);

                                itemWatch.Stop();
                                results[item.Index] = new BackgroundScreenshotBatchItemResult
                                {
                                    Name = item.Name,
                                    ImageData = imageData,
                                    ElapsedMs = (int)itemWatch.ElapsedMilliseconds
                                };

                                session.ProjectKey = projectKey;
                            }
                            catch (Exception ex)
                            {
                                itemWatch.Stop();
                                results[item.Index] = new BackgroundScreenshotBatchItemResult
                                {
                                    Name = item.Name,
                                    Error = ex.Message,
                                    ElapsedMs = (int)itemWatch.ElapsedMilliseconds
                                };
                            }
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks);

                foreach (var session in sessions)
                {
                    await session.DisposeAsync();
                }
            }
            finally
            {
                _semaphore.Release();
            }

            foreach (var result in results)
            {
                if (result != null)
                {
                    response.Items.Add(result);
                }
            }

            stopwatch.Stop();
            _logger.LogInformation("后台批量截图完成，耗时 {Elapsed}ms，数量 {Count}", stopwatch.ElapsedMilliseconds, response.Items.Count);

            return response;
        }

        private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_browser != null && _browser.IsConnected)
            {
                return _browser;
            }

            var playwright = await _playwright.Value;

            _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Args = new[]
                {
                    "--use-angle=swiftshader",
                    "--disable-dev-shm-usage",
                    "--no-sandbox"
                }
            });

            return _browser;
        }

        private async Task<IPage> GetPageAsync(
            ViewportSize viewportSize,
            int scale,
            string theme,
            CancellationToken cancellationToken)
        {
            var browser = await GetBrowserAsync(cancellationToken);
            var colorScheme = theme == "light" ? ColorScheme.Light : ColorScheme.Dark;

            var needsNewContext = _context == null
                                  || _page == null
                                  || _page.IsClosed
                                  || _contextScale != scale
                                  || _contextColorScheme != colorScheme;

            if (needsNewContext)
            {
                await DisposePageAsync();

                _context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = viewportSize,
                    DeviceScaleFactor = scale,
                    ColorScheme = colorScheme
                });
                _contextScale = scale;
                _contextColorScheme = colorScheme;
                _page = await _context.NewPageAsync();
                _currentViewportWidth = viewportSize.Width;
                _currentViewportHeight = viewportSize.Height;
                _pageInitialized = false;
                _pageProjectKey = null;
            }
            else if (_page != null
                     && (_currentViewportWidth != viewportSize.Width || _currentViewportHeight != viewportSize.Height))
            {
                await _page.SetViewportSizeAsync(viewportSize.Width, viewportSize.Height);
                _currentViewportWidth = viewportSize.Width;
                _currentViewportHeight = viewportSize.Height;
            }

            if (_page == null)
            {
                throw new InvalidOperationException("Playwright 页面未初始化");
            }

            if (!_pageInitialized)
            {
                var url = $"{_webBaseUrl.TrimEnd('/')}/screenshot-render";
                await _page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 30000
                });

                await _page.WaitForFunctionAsync(
                    "() => window.__render !== undefined",
                    new PageWaitForFunctionOptions { Timeout = 30000 });

                _pageInitialized = true;
                _pageProjectKey = null;
            }

            return _page;
        }

        public async ValueTask DisposeAsync()
        {
            _semaphore.Dispose();

            await DisposePageAsync();

            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }

            if (_playwright.IsValueCreated)
            {
                var playwright = await _playwright.Value;
                playwright.Dispose();
            }
        }

        private async Task DisposePageAsync()
        {
            if (_page != null)
            {
                await _page.CloseAsync();
                _page = null;
            }

            if (_context != null)
            {
                await _context.CloseAsync();
                _context = null;
            }

            _pageInitialized = false;
            _currentViewportWidth = 0;
            _currentViewportHeight = 0;
        }

        private class RenderConfig
        {
            public ProjectData? ProjectData { get; set; }

            public string? ProjectKey { get; set; }

            public string ViewMode { get; set; } = "human";

            public int[]? Layers { get; set; }

            public string? LayerPreset { get; set; }

            public string[]? LayerEnable { get; set; }

            public string[]? LayerDisable { get; set; }

            public ViewportConfig? Viewport { get; set; }

            public string Theme { get; set; } = "dark";
        }

        private sealed class RenderBatchResult
        {
            public string? ImageData { get; set; }

            public string? Error { get; set; }

            public int? ElapsedMs { get; set; }
        }

        private sealed class BatchItemContext
        {
            public int Index { get; }

            public string? Name { get; }

            public RenderConfig Config { get; }

            public int ViewportWidth { get; }

            public int ViewportHeight { get; }

            public BatchItemContext(int index, string? name, RenderConfig config, ViewportSize viewportSize)
            {
                Index = index;
                Name = name;
                Config = config;
                ViewportWidth = viewportSize.Width;
                ViewportHeight = viewportSize.Height;
            }

            public ViewportSize ViewportSize => new ViewportSize
            {
                Width = ViewportWidth,
                Height = ViewportHeight
            };
        }

        private ProjectData GetProjectData(string projectKey, string projectPath, string? strategyId)
        {
            if (_cachedProjectKey == projectKey && _cachedProjectData != null)
            {
                return _cachedProjectData;
            }

            var projectData = _snapshotService.LoadProjectData(projectPath, strategyId);

            _cachedProjectKey = projectKey;
            _cachedProjectData = projectData;
            return projectData;
        }

        private static string BuildProjectKey(string projectPath, string? strategyId)
        {
            var lastWrite = Directory.GetLastWriteTimeUtc(projectPath).Ticks;
            var schemeId = string.IsNullOrWhiteSpace(strategyId) ? "default" : strategyId.Trim();
            return $"{projectPath}|{schemeId}|{lastWrite}";
        }

        private static ViewportSize ResolveViewportSize(ProjectData projectData, ViewportConfig? viewport)
        {
            var bounds = ComputeTargetBounds(projectData, viewport);
            if (bounds == null)
            {
                return new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight };
            }

            var mode = viewport?.Mode ?? "full";
            var padded = ExpandBounds(bounds, ComputePadding(bounds, mode));
            var width = padded.MaxX - padded.MinX;
            var height = padded.MaxY - padded.MinY;

            if (width <= 0 || height <= 0)
            {
                return new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight };
            }

            var aspect = width / height;
            if (!double.IsFinite(aspect) || aspect <= 0)
            {
                return new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight };
            }

            var baseArea = DefaultViewportWidth * DefaultViewportHeight;
            var targetWidth = Math.Sqrt(baseArea * aspect);
            var targetHeight = baseArea / targetWidth;

            var maxSide = Math.Max(targetWidth, targetHeight);
            var minSide = Math.Min(targetWidth, targetHeight);
            var scale = 1.0;

            if (minSide < MinViewportSide)
            {
                scale = MinViewportSide / minSide;
            }

            if (maxSide * scale > MaxViewportSide)
            {
                scale = MaxViewportSide / maxSide;
            }

            targetWidth = Math.Round(targetWidth * scale);
            targetHeight = Math.Round(targetHeight * scale);

            return new ViewportSize
            {
                Width = Math.Max(1, (int)targetWidth),
                Height = Math.Max(1, (int)targetHeight)
            };
        }

        private static ViewportSize ResolveViewportSize(ProjectData projectData, ViewportConfig? viewport, bool autoFit)
        {
            if (!autoFit)
            {
                return new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight };
            }
            return ResolveViewportSize(projectData, viewport);
        }

        private static ViewportConfig? NormalizeViewport(ViewportConfig? viewport)
        {
            if (viewport == null)
            {
                return null;
            }

            return new ViewportConfig
            {
                Mode = string.IsNullOrWhiteSpace(viewport.Mode)
                    ? "full"
                    : viewport.Mode.ToLowerInvariant(),
                RoomId = viewport.RoomId,
                ZoneId = viewport.ZoneId,
                Bounds = viewport.Bounds
            };
        }

        private sealed class BatchPageSession : IAsyncDisposable
        {
            public IBrowserContext? Context { get; set; }

            public IPage? Page { get; set; }

            public int Scale { get; set; } = -1;

            public ColorScheme? ColorScheme { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }

            public bool Initialized { get; set; }

            public string? ProjectKey { get; set; }

            public async ValueTask DisposeAsync()
            {
                if (Page != null)
                {
                    await Page.CloseAsync();
                    Page = null;
                }

                if (Context != null)
                {
                    await Context.CloseAsync();
                    Context = null;
                }

                Initialized = false;
                ProjectKey = null;
            }
        }

        private async Task<IPage> GetBatchPageAsync(
            BatchPageSession session,
            ViewportSize viewportSize,
            int scale,
            string theme,
            CancellationToken cancellationToken)
        {
            var browser = await GetBrowserAsync(cancellationToken);
            var colorScheme = theme == "light" ? ColorScheme.Light : ColorScheme.Dark;

            var needsNewContext = session.Context == null
                                  || session.Page == null
                                  || session.Page.IsClosed
                                  || session.Scale != scale
                                  || session.ColorScheme != colorScheme;

            if (needsNewContext)
            {
                await session.DisposeAsync();

                session.Context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = viewportSize,
                    DeviceScaleFactor = scale,
                    ColorScheme = colorScheme
                });
                session.Scale = scale;
                session.ColorScheme = colorScheme;
                session.Page = await session.Context.NewPageAsync();
                session.Width = viewportSize.Width;
                session.Height = viewportSize.Height;
                session.Initialized = false;
                session.ProjectKey = null;
            }
            else if (session.Page != null
                     && (session.Width != viewportSize.Width || session.Height != viewportSize.Height))
            {
                await session.Page.SetViewportSizeAsync(viewportSize.Width, viewportSize.Height);
                session.Width = viewportSize.Width;
                session.Height = viewportSize.Height;
            }

            if (session.Page == null)
            {
                throw new InvalidOperationException("Playwright 页面未初始化");
            }

            if (!session.Initialized)
            {
                var url = $"{_webBaseUrl.TrimEnd('/')}/screenshot-render";
                await session.Page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 30000
                });

                await session.Page.WaitForFunctionAsync(
                    "() => window.__render !== undefined",
                    new PageWaitForFunctionOptions { Timeout = 30000 });

                session.Initialized = true;
                session.ProjectKey = null;
            }

            return session.Page;
        }

        private static Bounds2D? ComputeTargetBounds(ProjectData projectData, ViewportConfig? viewport)
        {
            var mode = viewport?.Mode ?? "full";
            switch (mode)
            {
                case "bounds":
                    return viewport?.Bounds;
                case "room":
                    return ComputeRoomBounds(projectData, viewport?.RoomId);
                case "zone":
                    return ComputeZoneBounds(projectData, viewport?.ZoneId);
                default:
                    return ComputeProjectBounds(projectData);
            }
        }

        private static Bounds2D? ComputeRoomBounds(ProjectData projectData, string? roomId)
        {
            if (string.IsNullOrWhiteSpace(roomId))
            {
                return null;
            }

            var room = projectData.Baseline.Rooms
                .Find(r => string.Equals(r.Id, roomId, StringComparison.OrdinalIgnoreCase));
            var roomBounds = ToBounds(room?.Boundary);
            if (roomBounds != null)
            {
                return roomBounds;
            }

            return null;
        }

        private static Bounds2D? ComputeZoneBounds(ProjectData projectData, string? zoneId)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                return null;
            }

            var zone = projectData.ActiveScheme.Zones
                .Find(z => string.Equals(z.Id, zoneId, StringComparison.OrdinalIgnoreCase));

            var zoneBoundary = zone?.ComputedBoundary ?? zone?.RawBoundary;
            return ToBounds(zoneBoundary);
        }

        private static Bounds2D? ComputeProjectBounds(ProjectData projectData)
        {
            var minX = double.PositiveInfinity;
            var minY = double.PositiveInfinity;
            var maxX = double.NegativeInfinity;
            var maxY = double.NegativeInfinity;

            void AppendBounds(BIMCanvas.Core.Models.Geometry.AABB aabb)
            {
                minX = Math.Min(minX, aabb.MinX);
                minY = Math.Min(minY, aabb.MinY);
                maxX = Math.Max(maxX, aabb.MaxX);
                maxY = Math.Max(maxY, aabb.MaxY);
            }

            void AddPolygon(BIMCanvas.Core.Models.Geometry.Polygon2D? polygon)
            {
                if (polygon == null || polygon.VertexCount == 0)
                {
                    return;
                }

                AppendBounds(polygon.ComputeAABB());
            }

            projectData.Baseline.Walls.ForEach(wall => AddPolygon(wall.Polygon));
            projectData.Baseline.Columns.ForEach(column => AddPolygon(column.Polygon));
            projectData.Baseline.Rooms.ForEach(room => AddPolygon(room.Boundary));
            projectData.ActiveScheme.Modules.ForEach(module => AddPolygon(module.Bounds));

            if (!double.IsFinite(minX) || !double.IsFinite(minY))
            {
                return null;
            }

            return new Bounds2D
            {
                MinX = minX,
                MinY = minY,
                MaxX = maxX,
                MaxY = maxY
            };
        }

        private static Bounds2D? ToBounds(BIMCanvas.Core.Models.Geometry.Polygon2D? polygon)
        {
            if (polygon == null || polygon.VertexCount == 0)
            {
                return null;
            }

            var aabb = polygon.ComputeAABB();
            return new Bounds2D
            {
                MinX = aabb.MinX,
                MinY = aabb.MinY,
                MaxX = aabb.MaxX,
                MaxY = aabb.MaxY
            };
        }

        private static Bounds2D ExpandBounds(Bounds2D bounds, double padding)
        {
            return new Bounds2D
            {
                MinX = bounds.MinX - padding,
                MinY = bounds.MinY - padding,
                MaxX = bounds.MaxX + padding,
                MaxY = bounds.MaxY + padding
            };
        }

        private static double ComputePadding(Bounds2D bounds, string mode)
        {
            var width = bounds.MaxX - bounds.MinX;
            var height = bounds.MaxY - bounds.MinY;
            var maxSize = Math.Max(width, height);
            var minPadding = string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase)
                ? DefaultFullPadding
                : DefaultViewPadding;

            if (!double.IsFinite(maxSize) || maxSize <= 0)
            {
                return minPadding;
            }

            return Math.Max(minPadding, maxSize * PaddingRatio);
        }
    }
}
