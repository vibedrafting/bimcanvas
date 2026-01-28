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

        private readonly ProjectSnapshotService _snapshotService;
        private readonly ILogger<BackgroundScreenshotService> _logger;
        private readonly JsonSerializerSettings _jsonSettings;
        private readonly SemaphoreSlim _semaphore;
        private readonly Lazy<Task<IPlaywright>> _playwright;
        private IBrowser? _browser;
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
                    Bounds = request.Viewport.Bounds
                };
            }

            var renderConfig = new RenderConfig
            {
                ProjectData = projectData,
                ViewMode = viewMode,
                Layers = request.Layers,
                Viewport = viewport,
                Theme = theme
            };

            var configJson = JsonConvert.SerializeObject(renderConfig, _jsonSettings);

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var browser = await GetBrowserAsync(cancellationToken);
                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = DefaultViewportWidth, Height = DefaultViewportHeight },
                    DeviceScaleFactor = request.Scale,
                    ColorScheme = renderConfig.Theme == "light" ? ColorScheme.Light : ColorScheme.Dark
                });

                var page = await context.NewPageAsync();
                await page.AddInitScriptAsync($"window.__renderConfig = {configJson};");

                var url = $"{_webBaseUrl.TrimEnd('/')}/screenshot-render";
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = 30000
                });

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

        public async ValueTask DisposeAsync()
        {
            _semaphore.Dispose();

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

        private class RenderConfig
        {
            public ProjectData ProjectData { get; set; } = new ProjectData();

            public string ViewMode { get; set; } = "human";

            public int[]? Layers { get; set; }

            public ViewportConfig? Viewport { get; set; }

            public string Theme { get; set; } = "dark";
        }
    }
}
