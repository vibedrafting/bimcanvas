using System;
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

            var projectData = _snapshotService.LoadProjectData(request.ProjectPath, request.StrategyId);

            if (!string.IsNullOrWhiteSpace(request.StrategyId))
            {
                projectData.Project.ActiveSchemeId = request.StrategyId;
            }

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

            var renderConfig = new RenderConfig
            {
                ProjectData = projectData,
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

                await page.EvaluateAsync("configJson => window.__render(JSON.parse(configJson))", configJson);

                await page.WaitForFunctionAsync(
                    "() => window.__renderReady === true || window.__renderError",
                    new PageWaitForFunctionOptions { Timeout = 30000 });

                var error = await page.EvaluateAsync<string?>("() => window.__renderError || null");
                if (!string.IsNullOrWhiteSpace(error))
                {
                    throw new InvalidOperationException(error);
                }

                var imageData = await page.EvaluateAsync<string>("() => window.__capture()");
                if (string.IsNullOrWhiteSpace(imageData))
                {
                    throw new InvalidOperationException("截图结果为空");
                }

                stopwatch.Stop();
                _logger.LogInformation("后台截图完成，耗时 {Elapsed}ms", stopwatch.ElapsedMilliseconds);

                return imageData;
            }
            finally
            {
                _semaphore.Release();
            }
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
            public ProjectData ProjectData { get; set; } = new ProjectData();

            public string ViewMode { get; set; } = "human";

            public int[]? Layers { get; set; }

            public string? LayerPreset { get; set; }

            public string[]? LayerEnable { get; set; }

            public string[]? LayerDisable { get; set; }

            public ViewportConfig? Viewport { get; set; }

            public string Theme { get; set; } = "dark";
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
