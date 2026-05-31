using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BIMCanvas.Server.Dtos;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Logging;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Git;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
// Windows API for enabling ANSI escape sequences (Virtual Terminal Processing)
// ─────────────────────────────────────────────────────────────────────────────
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

[DllImport("kernel32.dll", SetLastError = true)]
static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

[DllImport("kernel32.dll", SetLastError = true)]
static extern IntPtr GetStdHandle(int nStdHandle);

static void EnableVirtualTerminalProcessing()
{
    const int STD_OUTPUT_HANDLE = -11;
    const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    var handle = GetStdHandle(STD_OUTPUT_HANDLE);
    if (GetConsoleMode(handle, out uint mode))
    {
        SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Console output helper: colored prefix, passthrough message (preserves ANSI)
// ─────────────────────────────────────────────────────────────────────────────
static void WriteWithColoredPrefix(string prefix, string message, ConsoleColor prefixColor)
{
    var originalColor = Console.ForegroundColor;
    // 时间戳（灰色）
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    // 前缀（保持现有颜色）
    Console.ForegroundColor = prefixColor;
    Console.Write(prefix);
    // 消息（恢复默认颜色）
    Console.ForegroundColor = originalColor;
    Console.WriteLine($" {message}");
}

// ─────────────────────────────────────────────────────────────────────────────
// Console output helper: timestamp only (for Agent stdout where prefix comes from Python)
// ─────────────────────────────────────────────────────────────────────────────
static void WriteWithTimestampOnly(string message, ConsoleColor messageColor)
{
    var originalColor = Console.ForegroundColor;
    // 时间戳（灰色）
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    // 消息（指定颜色，保留 ANSI 序列）
    Console.ForegroundColor = messageColor;
    Console.WriteLine(message);
    Console.ForegroundColor = originalColor;

    // 对话日志持久化
    BIMCanvas.Server.Logging.ConversationLogger.ProcessLine(message);
}

// ─────────────────────────────────────────────────────────────────────────────
// Initialize console: UTF-8 encoding + ANSI support
// ─────────────────────────────────────────────────────────────────────────────
Console.OutputEncoding = Encoding.UTF8;
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    EnableVirtualTerminalProcessing();
}

var agentProxyHttpClient = new HttpClient(new SocketsHttpHandler
{
    UseProxy = false,
    AllowAutoRedirect = false
})
{
    Timeout = Timeout.InfiniteTimeSpan
};

var hopByHopHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "Connection",
    "Keep-Alive",
    "Proxy-Authenticate",
    "Proxy-Authorization",
    "TE",
    "Trailer",
    "Transfer-Encoding",
    "Upgrade",
    "Host"
};

var builder = WebApplication.CreateBuilder(args);
var isProduction = builder.Environment.IsProduction();
var isDevelopment = builder.Environment.IsDevelopment();
var configDir = ConfigService.GetConfigDir();
var agentConfigExistsBeforeBootstrap = ConfigService.SectionExists(ConfigService.SectionAgent);
var ccrConfigExistsBeforeBootstrap = ConfigService.SectionExists(ConfigService.SectionCcr);

// 配置统一日志格式（替换默认 Console Logger 格式）
builder.Logging.ClearProviders();
builder.Logging.AddServerConsoleFormatter();

// 统一初始化 BIMCANVAS_HOME 下的全局配置资产（Server + Agent）
var templateBootstrapService = new BootstrapTemplateService();
var globalConfigBootstrapService = new GlobalConfigBootstrapService(templateBootstrapService);
globalConfigBootstrapService.EnsureInitialized();

DevelopmentLocalConfigBootstrapService? developmentLocalConfigBootstrapService = null;
if (isDevelopment)
{
    developmentLocalConfigBootstrapService = new DevelopmentLocalConfigBootstrapService(templateBootstrapService);
    developmentLocalConfigBootstrapService.EnsureInitialized(
        initializeAgentRuntime: !agentConfigExistsBeforeBootstrap,
        initializeCcrRuntime: !ccrConfigExistsBeforeBootstrap);
}

// 加载用户配置（提前到 DI 注册前，供 AgentClientService 等服务使用）
var config = ConfigService.Load();
var baseDir = AppContext.BaseDirectory;
var agentProjectPath = FindAgentProjectPath(baseDir);
var webProjectPath = FindWebProjectPath(baseDir);
var agentAutoStart = config.Agent.AutoStart;
var configuredWebPort = config.Web.Port > 0 ? config.Web.Port : 5173;
var agentPort = config.Agent.GetResolvedPort();
var agentBaseUrl = config.Agent.GetResolvedBaseUrl();
var agentManagedByServer = agentAutoStart && IsLocalDevelopmentOrigin(agentBaseUrl);
var pythonCommand = "python"; // Python 解释器命令已硬编码;非 PATH/python3/venv 环境需在此处改
var runtimeEndpointState = new RuntimeEndpointState();
var configuredServerBinding = ResolveConfiguredServerBinding(builder.Configuration, config.Server.Port);
builder.Configuration["BIMCANVAS_WEB_URL"] = BuildUrl(Uri.UriSchemeHttp, "localhost", configuredWebPort);
var resolvedServerPort = ResolveManagedPort(
    "Server",
    configuredServerBinding.ListenHost,
    configuredServerBinding.PreferredPort,
    (_, occupant) => ClassifyBIMCanvasServerOccupant(occupant.ProcessId, baseDir));
var serverListenUrl = BuildUrl(configuredServerBinding.Scheme, configuredServerBinding.ListenHost, resolvedServerPort.ActualPort);
var serverBaseUrl = BuildUrl(configuredServerBinding.Scheme, configuredServerBinding.BrowserHost, resolvedServerPort.ActualPort);
builder.Configuration["urls"] = serverListenUrl;
builder.Configuration["Kestrel:Endpoints:Http:Url"] = serverListenUrl;
builder.Configuration["Web:BaseUrl"] = serverBaseUrl;
builder.WebHost.UseUrls(serverListenUrl);
runtimeEndpointState.SetServer(CreateRuntimeEndpoint(
    "server",
    "Server",
    managedByServer: true,
    autoShifted: resolvedServerPort.AutoShifted,
    configuredUrl: configuredServerBinding.DisplayUrl,
    actualUrl: serverBaseUrl,
    configuredPort: configuredServerBinding.PreferredPort,
    actualPort: resolvedServerPort.ActualPort));
var startupErrors = new List<string>();
var productionWebDistPath = isProduction ? ResolveWebDistPath(baseDir) : null;
if (isProduction && productionWebDistPath == null)
{
    startupErrors.Add("生产模式未找到可用的 Web dist 目录，请设置 BIMCANVAS_WEB_DIST 或先构建 BIMCanvas.Web。");
}

// 注册配置 + Agent HTTP 客户端
builder.Services.AddSingleton(templateBootstrapService);
builder.Services.AddSingleton(globalConfigBootstrapService);
if (developmentLocalConfigBootstrapService != null)
{
    builder.Services.AddSingleton(developmentLocalConfigBootstrapService);
}
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(runtimeEndpointState);
builder.Services.AddSingleton<AgentClientService>();

// 配置 JSON 序列化选项（本项目统一使用 Newtonsoft.Json，禁止引入 System.Text.Json，见 CLAUDE.md "Newtonsoft.Json 单一序列化栈"）
// ContractResolver: DefaultContractResolver + CamelCaseNamingStrategy(只转 C# 属性名,不转 Dictionary key)。
// enum 序列化默认整数;需字符串的 enum(TrustState / SourceKind / LaunchMode 等 plugin enum)
// 在 enum 类型上显式标 [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]。
// **禁止全局 StringEnumConverter** —— 会波及业务 enum(OpeningType/RoomType/ZoneType)
// 让前端整数比较失败(详见 CLAUDE.md §10)。
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() };
        options.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });

// 注册服务
// TODO: 以下服务需要迁移到 v3.0 文件结构
// builder.Services.AddSingleton<CanvasStateManager>();
// builder.Services.AddSingleton<ZoneCalculator>();

// v3.0 项目管理服务
builder.Services.AddSingleton<ManifestService>();
builder.Services.AddSingleton<RoomTypeTagMappingService>();
builder.Services.AddSingleton<ComputedDataService>();
builder.Services.AddSingleton<PlacementService>();
builder.Services.AddSingleton<ZoneBoundaryService>();
builder.Services.AddSingleton<ProjectFixedFilesBootstrapService>();
builder.Services.AddSingleton<ProjectDerivedBootstrapService>();

// v1.1 平台化改造 · 组 2:Plugin 安全 + 生命周期 (主真理源 §3.12 / §3.13 / §4.2)
builder.Services.AddSingleton<BIMCanvas.Server.Services.PluginSecurity.StaticPluginValidator>();
builder.Services.AddSingleton(sp => new BIMCanvas.Server.Services.PluginSecurity.ExecutablePluginProbe(
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BIMCanvas.Server.Services.PluginSecurity.ExecutablePluginProbe>>(),
    agentProjectPath));
builder.Services.AddSingleton<BIMCanvas.Server.Services.Plugins.PluginTrustService>();
// 包A · 按需调用 active plugin 的 validators/ 校验脚本（与 ExecutablePluginProbe 对称：注入 Agent 根）
builder.Services.AddSingleton(sp => new BIMCanvas.Server.Services.PluginSecurity.PluginValidatorRuntime(
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BIMCanvas.Server.Services.PluginSecurity.PluginValidatorRuntime>>(),
    agentProjectPath,
    sp.GetRequiredService<BIMCanvas.Server.Services.Plugins.PluginTrustService>()));
builder.Services.AddSingleton<BIMCanvas.Server.Services.PluginSecurity.PluginValidatorOrchestrator>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.Plugins.PluginInstallService>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.Plugins.PluginLifecycleService>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.Plugins.PluginScaffoldService>();

// v3.1 Git Worktree 架构服务（单仓库 + 多分支 + Worktree 并行）
builder.Services.AddSingleton<GitWorktreeService>();
builder.Services.AddSingleton<IWorktreeMetadataServiceFactory, WorktreeMetadataServiceFactory>();  // ✅ 工厂模式
builder.Services.AddSingleton<StrategyService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<ProjectContext>();  // 单项目模式上下文
builder.Services.AddSingleton<ModuleLibraryService>();  // 模块库服务
builder.Services.AddSingleton<ModuleFileTopologyService>();  // modules.json 拓扑解析服务
builder.Services.AddSingleton<SchemeDesignDocService>();  // DESIGN.md frontmatter(adopted 指针) 读写（指针模型）
builder.Services.AddSingleton<ModuleNormalizationService>();  // 模块数据规范化服务
builder.Services.AddSingleton<ModulesReaderService>();  // modules.json wrapper 读取
builder.Services.AddSingleton<ModulesWriterService>();  // modules.json wrapper 写入（含 schemeMetadata 派生）

// 项目健康检查 + 修复（schema 迁移工具的服务化入口）
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IProjectHealthCheck,
    BIMCanvas.Server.Services.ProjectHealth.Checks.SemanticPlanTagCheck>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IProjectHealthCheck,
    BIMCanvas.Server.Services.ProjectHealth.Checks.ModulesWrapperCheck>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IProjectHealthCheck,
    BIMCanvas.Server.Services.ProjectHealth.Checks.SchemeMetadataSlimCheck>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IProjectHealthCheck,
    BIMCanvas.Server.Services.ProjectHealth.Checks.SemanticPlanTagValueCheck>();
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IProjectHealthCheck,
    BIMCanvas.Server.Services.ProjectHealth.Checks.PointerModelMigrateCheck>();  // 指针模型迁移：末位注册=末位执行（依赖前序 wrapper/tag 已就位）；幂等+Web repair 入口有 git 兜底
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.IGitCommitter>(sp =>
    new BIMCanvas.Server.Services.ProjectHealth.GitWorktreeServiceCommitter(
        sp.GetRequiredService<GitWorktreeService>()));
builder.Services.AddSingleton<BIMCanvas.Server.Services.ProjectHealth.ProjectHealthService>();
builder.Services.AddSingleton<ProjectSnapshotService>();
builder.Services.AddSingleton<BackgroundScreenshotService>();
builder.Services.AddSingleton<ChatAttachmentService>();

// v3.3 多窗口并行架构服务
builder.Services.AddSingleton<BranchLockManager>();  // 分支锁管理（多窗口互斥）
builder.Services.AddSingleton<MergeService>();       // 可视化合并服务

// v3.4 可视化 Diff 服务
builder.Services.AddSingleton<SchemeDataService>();  // 跨分支/Worktree 模块数据读写

// 首页项目管理服务
builder.Services.AddSingleton<RecentProjectsService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<SettingsRestartService>();
builder.Services.AddSingleton<LlmEndpointTestService>();

// v3.2 实时通信服务（使用 Newtonsoft.Json 避免 JsonElement 序列化问题）
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol(options =>
    {
        options.PayloadSerializerSettings.ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() };
        options.PayloadSerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
    });
builder.Services.AddSingleton<ProjectWatcherService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ProjectWatcherService>());

var configuredCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()?
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim())
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray()
    ?? Array.Empty<string>();

// 配置 CORS - 允许 Web 前端跨域访问
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        if (configuredCorsOrigins.Length > 0)
        {
            policy.WithOrigins(configuredCorsOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
            return;
        }

        policy.SetIsOriginAllowed(IsLocalDevelopmentOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 启用 CORS
app.UseCors("AllowWebClient");

if (isProduction && productionWebDistPath != null)
{
    var webDistProvider = new PhysicalFileProvider(productionWebDistPath);
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = webDistProvider
    });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = webDistProvider
    });
}

// 启用控制器路由
app.MapControllers();

// SignalR Hub 路由
app.MapHub<CanvasHub>("/hubs/canvas");

// 健康检查端点
app.MapGet("/health", async (AgentClientService agentClient, CancellationToken cancellationToken) =>
{
    var agentHealthy = await agentClient.CheckHealthAsync(cancellationToken);
    if (agentHealthy)
    {
        return Results.Ok(new
        {
            status = "healthy",
            agent = "healthy",
            timestamp = DateTime.UtcNow
        });
    }

    return Results.Json(
        new
        {
            status = "degraded",
            agent = "unavailable",
            timestamp = DateTime.UtcNow
        },
        statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Map("/agent", agentApp =>
{
    agentApp.Run(async context =>
    {
        var agentClient = context.RequestServices.GetRequiredService<AgentClientService>();
        await ProxyToAgentAsync(context, agentClient.AgentBaseUrl);
    });
});

if (isProduction && productionWebDistPath != null)
{
    app.MapFallbackToFile(
        "index.html",
        new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(productionWebDistPath)
        });
}

// config 已在 DI 注册阶段加载（line 85 附近）
WriteWithColoredPrefix("[Server]", "BIMCanvas.Server 启动中...", ConsoleColor.White);
WriteWithColoredPrefix(
    "[Server]",
    $"Server 监听端口: {configuredServerBinding.PreferredPort} → {resolvedServerPort.ActualPort}",
    ConsoleColor.White);

// 空启动模式：不自动加载项目，等待用户通过首页选择
WriteWithColoredPrefix("[Server]", "空启动模式，等待用户选择项目", ConsoleColor.White);

// ─── 环境检测阶段 ───
var agentReady = true;
var agentRuntimeReady = false;
var webReady = true;
var playwrightReady = true;
{
    WriteWithColoredPrefix("[Server]", "环境检测中...", ConsoleColor.White);

    if (!agentManagedByServer)
    {
        if (agentAutoStart)
        {
            WriteWithColoredPrefix(
                "[Server:WARN]",
                $"检测到 Agent 配置冲突：autoStart=true 但 baseUrl 指向外部地址，已按外部依赖模式处理 ({agentBaseUrl})",
                ConsoleColor.DarkYellow);
        }

        WriteWithColoredPrefix("[Server]", $"Agent 服务模式: 外部依赖 ({agentBaseUrl})", ConsoleColor.White);
    }
    else if (!Directory.Exists(agentProjectPath))
    {
        WriteWithColoredPrefix("[Server:WARN]", $"Agent 项目目录不存在: {agentProjectPath}", ConsoleColor.DarkYellow);
        agentReady = false;
        if (isProduction)
        {
            startupErrors.Add($"生产模式缺少 Agent 项目目录: {agentProjectPath}");
        }
    }
    else if (!IsPythonAvailable(pythonCommand))
    {
        WriteWithColoredPrefix("[Server:WARN]", "未检测到 Python，Agent 服务将不启动", ConsoleColor.DarkYellow);
        WriteWithColoredPrefix("[Server:WARN]", "提示: 请安装 Python 3.10+ 并添加到 PATH", ConsoleColor.DarkYellow);
        agentReady = false;
        if (isProduction)
        {
            startupErrors.Add("生产模式未检测到 Python，无法启动 Agent。");
        }
    }
    else if (!IsAgentDependencyReady(pythonCommand))
    {
        if (isProduction)
        {
            agentReady = false;
            startupErrors.Add("生产模式检测到 Agent 依赖缺失，请先完成 Agent 依赖安装。");
        }
        else
        {
            agentReady = TryInstallAgentDependencies(agentProjectPath, pythonCommand);
        }
    }

    playwrightReady = IsPlaywrightChromiumInstalled();
    if (!playwrightReady)
    {
        if (isProduction)
        {
            startupErrors.Add("生产模式未检测到 Playwright Chromium，后台截图功能不可用，启动已终止。");
        }
        else
        {
            TryInstallPlaywrightChromium();
        }
    }

    if (!IsRipgrepAvailable())
    {
        if (isProduction)
        {
            WriteWithColoredPrefix("[Server:WARN]", "未检测到 ripgrep (rg)，OpenAI Runtime 的 Glob/Grep 工具将不可用", ConsoleColor.DarkYellow);
        }
        else
        {
            TryInstallRipgrep();
        }
    }

    // Web 环境检测：开发模式依赖 Node/Vite，生产模式依赖 dist 静态产物
    if (!isProduction && !IsNodeAvailable())
    {
        WriteWithColoredPrefix("[Server:WARN]", "未检测到 Node.js，Web 服务将不启动", ConsoleColor.DarkYellow);
        WriteWithColoredPrefix("[Server:WARN]", "提示: 请安装 Node.js 18+ 并添加到 PATH", ConsoleColor.DarkYellow);
        webReady = false;
    }
    else if (!isProduction && !Directory.Exists(Path.Combine(webProjectPath, "node_modules")))
    {
        webReady = TryInstallWebDependencies(webProjectPath);
    }
}

// ─── 自动启动 Agent 和 Web 服务 ───
Process? agentProcess = null;
Process? webProcess = null;
Task<bool>? agentReadyTask = null;
TaskCompletionSource<string>? webReadyUrlSource = null;
string? webBaseUrl = null;
Process? ccrProcess = null;
{
    // 1. 启动 CCR（唯一 API 网关）
    bool ccrRuntimeReady = false;
    var configuredCcrUrl = BuildUrl(Uri.UriSchemeHttp, GetReachableLocalHost(config.Ccr.Host), config.Ccr.Port);
    runtimeEndpointState.SetCcr(CreateRuntimeEndpoint(
        "ccr",
        "CCR",
        managedByServer: false,
        autoShifted: false,
        configuredUrl: configuredCcrUrl,
        actualUrl: config.Ccr.Enabled && !config.Ccr.AutoStart ? configuredCcrUrl : string.Empty,
        configuredPort: config.Ccr.Port,
        actualPort: config.Ccr.Enabled && !config.Ccr.AutoStart ? config.Ccr.Port : null));
    if (config.Ccr.Enabled && config.Ccr.AutoStart)
    {
        // CCR 依赖检查：检测 ccr 命令是否可用
        if (!IsCcrAvailable())
        {
            if (isProduction)
            {
                startupErrors.Add("生产模式未检测到 CCR (claude-code-router)，启动已终止。");
                goto CcrSkipped;
            }
            if (!IsNodeAvailable())
            {
                WriteWithColoredPrefix("[Server:WARN]", "未检测到 CCR 且 Node.js 不可用，CCR 服务将不启动", ConsoleColor.DarkYellow);
                WriteWithColoredPrefix("[Server:WARN]", "提示: 请先安装 Node.js 18+ 并添加到 PATH，再运行 npm install -g claude-code-router", ConsoleColor.DarkYellow);
                goto CcrSkipped;
            }
            if (!TryInstallCcr())
            {
                goto CcrSkipped;
            }
        }

        // CCR 守护进程配置来自统一配置文件的 ccr 段（合并前是独立 ccr_config.json）。
        // CCR 默认从 ~/.claude-code-router/config.json 读取,需把 ccr 段投影成 CCR 原生 config.json 写过去。
        var ccrSection = ConfigService.LoadSection(ConfigService.SectionCcr);
        var ccrHomeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-code-router");
        Directory.CreateDirectory(ccrHomeDir);
        var ccrHomeConfigPath = Path.Combine(ccrHomeDir, "config.json");
        if (ccrSection.HasValues)
        {
            File.WriteAllText(ccrHomeConfigPath, ccrSection.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        var resolvedCcrPort = ResolveManagedPort(
            "CCR",
            config.Ccr.Host,
            config.Ccr.Port,
            (_, occupant) => IsBIMCanvasCcrProcess(occupant.ProcessId)
                ? PortOccupantOwnership.OwnedManaged
                : PortOccupantOwnership.ExternalProcess);
        config.Ccr.Port = resolvedCcrPort.ActualPort;
        var ccrRuntimeBaseUrl = BuildUrl(Uri.UriSchemeHttp, GetReachableLocalHost(config.Ccr.Host), config.Ccr.Port);
        runtimeEndpointState.SetCcr(CreateRuntimeEndpoint(
            "ccr",
            "CCR",
            managedByServer: true,
            autoShifted: resolvedCcrPort.AutoShifted,
            configuredUrl: configuredCcrUrl,
            actualUrl: ccrRuntimeBaseUrl,
            configuredPort: resolvedCcrPort.PreferredPort,
            actualPort: resolvedCcrPort.ActualPort));
        WriteWithColoredPrefix(
            "[Server]",
            $"CCR 监听端口: {resolvedCcrPort.PreferredPort} → {resolvedCcrPort.ActualPort}",
            ConsoleColor.White);

        WriteWithColoredPrefix("[Server]", "CCR 服务启动中...", ConsoleColor.White);
        ccrProcess = StartCcrProcess(config, ccrHomeConfigPath);
        if (ccrProcess != null)
        {
            ccrRuntimeReady = await WaitForServiceReadyAsync(GetReachableLocalHost(config.Ccr.Host), config.Ccr.Port, timeoutMs: 15000);
            if (ccrRuntimeReady)
                WriteWithColoredPrefix("[CCR]", $"CCR 已就绪: {ccrRuntimeBaseUrl}", ConsoleColor.Magenta);
            else
            {
                WriteWithColoredPrefix("[Server:WARN]", "CCR 未在预期时间内就绪", ConsoleColor.DarkYellow);
                if (isProduction)
                {
                    startupErrors.Add("生产模式下 CCR 未在预期时间内就绪。");
                }
            }
        }
        else if (isProduction)
        {
            startupErrors.Add("生产模式下 CCR 启动失败。");
        }
    }
    CcrSkipped:

    // 2. 启动 Agent 服务（仅托管模式）
    if (agentReady && agentManagedByServer)
    {
        var resolvedAgentPort = ResolveManagedPort(
            "Agent",
            "127.0.0.1",
            agentPort,
            (port, occupant) => ClassifyBIMCanvasAgentOccupant(
                port,
                occupant.ProcessId,
                agentProjectPath,
                configDir));
        agentPort = resolvedAgentPort.ActualPort;
        agentBaseUrl = BuildUrl(Uri.UriSchemeHttp, "127.0.0.1", agentPort);
        runtimeEndpointState.SetAgent(CreateRuntimeEndpoint(
            "agent",
            "Agent",
            managedByServer: true,
            autoShifted: resolvedAgentPort.AutoShifted,
            configuredUrl: config.Agent.GetResolvedBaseUrl(),
            actualUrl: agentBaseUrl,
            configuredPort: resolvedAgentPort.PreferredPort,
            actualPort: resolvedAgentPort.ActualPort));
        WriteWithColoredPrefix(
            "[Server]",
            $"Agent 监听端口: {resolvedAgentPort.PreferredPort} → {resolvedAgentPort.ActualPort}",
            ConsoleColor.White);

        WriteWithColoredPrefix("[Server]", "Agent 服务启动中...", ConsoleColor.White);
        try
        {
            agentProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    WorkingDirectory = agentProjectPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            agentProcess.StartInfo.ArgumentList.Add("-m");
            agentProcess.StartInfo.ArgumentList.Add("src.main");
            agentProcess.StartInfo.ArgumentList.Add("--serve");
            agentProcess.StartInfo.ArgumentList.Add("--managed-by-server");
            agentProcess.StartInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            agentProcess.StartInfo.ArgumentList.Add("--managed-agent-root");
            agentProcess.StartInfo.ArgumentList.Add(agentProjectPath);
            agentProcess.StartInfo.ArgumentList.Add("--managed-home");
            agentProcess.StartInfo.ArgumentList.Add(configDir);

            // v1.1 §4.10 / 模板 §4.10:写初始 LaunchContext (Projectless 模式) 文件,
            // CLI arg --launch-context 传给 Agent 子进程。现有环境变量注入并存,组3 完成后逐步迁移。
            try
            {
                var runtimeDir = Path.Combine(configDir, ".runtime");
                Directory.CreateDirectory(runtimeDir);
                var serverPid = Process.GetCurrentProcess().Id;
                var launchContextPath = Path.Combine(runtimeDir, $"launch-context-{serverPid}.json");
                var initialActivePlugin = string.IsNullOrWhiteSpace(config.Agent.ActivePlugin)
                    ? "core-base"
                    : config.Agent.ActivePlugin;
                var initialContext = new
                {
                    activePluginId = initialActivePlugin,
                    activePluginRoot = Path.Combine(configDir, "plugins", initialActivePlugin),
                    mode = "projectless",
                    projectPath = (string?)null,
                    activeSceneId = (string?)null,
                    scenes = (object?)null,
                    @lock = (object?)null,
                    serverUrl = serverBaseUrl,
                    trustMode = "fullTrust",
                    readOnlySceneIds = Array.Empty<string>(),
                };
                var ctxJson = JsonConvert.SerializeObject(
                    initialContext,
                    new JsonSerializerSettings
                    {
                        Formatting = Newtonsoft.Json.Formatting.Indented,
                        ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
                        NullValueHandling = NullValueHandling.Include,
                        // initialContext 内字段(mode/trustMode)已是字符串字面值;
                        // plugin enum 字符串化由各 enum 类型上 [JsonConverter] attribute 控制。
                    });
                File.WriteAllText(launchContextPath, ctxJson, Encoding.UTF8);
                agentProcess.StartInfo.ArgumentList.Add("--launch-context");
                agentProcess.StartInfo.ArgumentList.Add(launchContextPath);
                WriteWithColoredPrefix(
                    "[Server]",
                    $"Initial LaunchContext: mode=projectless, plugin={initialActivePlugin}, path={launchContextPath}",
                    ConsoleColor.White);
            }
            catch (Exception ex)
            {
                WriteWithColoredPrefix(
                    "[Server:WARN]",
                    $"写 LaunchContext 失败,Agent 仅靠环境变量启动: {ex.Message}",
                    ConsoleColor.DarkYellow);
            }

            // 设置环境变量确保 Python 输出 UTF-8
            agentProcess.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            agentProcess.StartInfo.Environment["BIMCANVAS_HOME"] = configDir;
            agentProcess.StartInfo.Environment["BIMCANVAS_AGENT_MANAGED_BY_SERVER"] = "1";
            agentProcess.StartInfo.Environment["BIMCANVAS_SERVER_URL"] = serverBaseUrl;
            agentProcess.StartInfo.Environment["SERVER_HOST"] = "127.0.0.1";
            agentProcess.StartInfo.Environment["SERVER_PORT"] = agentPort.ToString();

            // API 网关配置：CCR 启用时走网关，禁用时 Agent 直连 config.json 中的供应商
            if (config.Ccr.Enabled)
            {
                var ccrGatewayUrl = runtimeEndpointState.GetSnapshot().Ccr.ActualUrl;
                agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-ccr";
                agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = ccrGatewayUrl;

                // 模型映射交给 CCR Router，不注入 ANTHROPIC_DEFAULT_*_MODEL

                WriteWithColoredPrefix("[Server]", $"Agent 网关: CCR ({ccrGatewayUrl})", ConsoleColor.White);
            }
            else
            {
                WriteWithColoredPrefix("[Server]", "Agent 网关: 直连模式 (使用 <BIMCANVAS_HOME>/config.json)", ConsoleColor.White);
            }

            agentProcess.Start();

            // 后台读取 Agent 输出（避免缓冲区阻塞）
            // 注意：stdout 使用 WriteWithTimestampOnly，前缀 [Agent] 或 [Agent#n] 由 Python 输出
            _ = Task.Run(async () =>
            {
                while (!agentProcess.HasExited)
                {
                    var line = await agentProcess.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        WriteWithTimestampOnly(line, ConsoleColor.Cyan);
                }
            });
            _ = Task.Run(async () =>
            {
                while (!agentProcess.HasExited)
                {
                    var line = await agentProcess.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[Agent:ERR]", line, ConsoleColor.DarkCyan);
                }
            });

            agentReadyTask = WaitForServiceReadyAsync(
                "127.0.0.1",
                agentPort,
                timeoutMs: 15000,
                monitoredProcess: agentProcess);
        }
        catch (Exception ex)
        {
            runtimeEndpointState.SetAgent(CreateRuntimeEndpoint(
                "agent",
                "Agent",
                managedByServer: true,
                autoShifted: resolvedAgentPort.AutoShifted,
                configuredUrl: config.Agent.GetResolvedBaseUrl(),
                actualUrl: string.Empty,
                configuredPort: resolvedAgentPort.PreferredPort,
                actualPort: null));
            WriteWithColoredPrefix("[Server:ERR]", $"Agent 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
            if (isProduction)
            {
                startupErrors.Add($"生产模式下 Agent 服务启动失败: {ex.Message}");
            }
        }
    }
    else if (!agentManagedByServer)
    {
        agentRuntimeReady = !string.IsNullOrWhiteSpace(agentBaseUrl);
        runtimeEndpointState.SetAgent(CreateRuntimeEndpoint(
            "agent",
            "Agent",
            managedByServer: false,
            autoShifted: false,
            configuredUrl: agentBaseUrl,
            actualUrl: agentBaseUrl,
            configuredPort: agentPort,
            actualPort: TryGetPortFromUrl(agentBaseUrl)));
        WriteWithColoredPrefix("[Server]", $"Agent 服务由外部容器提供: {agentBaseUrl}", ConsoleColor.White);
    }
    else
    {
        runtimeEndpointState.SetAgent(CreateRuntimeEndpoint(
            "agent",
            "Agent",
            managedByServer: true,
            autoShifted: false,
            configuredUrl: config.Agent.GetResolvedBaseUrl(),
            actualUrl: string.Empty,
            configuredPort: agentPort,
            actualPort: null));
    }

    // 3. 启动 Web 服务（不等待，后台运行）
    if (!isProduction && webReady && Directory.Exists(webProjectPath))
    {
        var configuredWebUrl = BuildUrl(Uri.UriSchemeHttp, "localhost", configuredWebPort);
        var resolvedWebPort = ResolveManagedPort(
            "Web",
            "0.0.0.0",
            configuredWebPort,
            (_, occupant) => ClassifyBIMCanvasWebOccupant(occupant.ProcessId, webProjectPath));
        var plannedWebBaseUrl = BuildUrl(Uri.UriSchemeHttp, "localhost", resolvedWebPort.ActualPort);
        runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
            "web",
            "Web",
            managedByServer: true,
            autoShifted: resolvedWebPort.AutoShifted,
            configuredUrl: configuredWebUrl,
            actualUrl: plannedWebBaseUrl,
            configuredPort: resolvedWebPort.PreferredPort,
            actualPort: resolvedWebPort.ActualPort));
        WriteWithColoredPrefix(
            "[Server]",
            $"Web 开发端口: {resolvedWebPort.PreferredPort} → {resolvedWebPort.ActualPort}",
            ConsoleColor.White);

        WriteWithColoredPrefix("[Server]", "Web 开发服务器启动中...", ConsoleColor.White);
        try
        {
            webReadyUrlSource = new TaskCompletionSource<string>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var viteCliPath = Path.Combine(webProjectPath, "node_modules", "vite", "bin", "vite.js");
            if (!File.Exists(viteCliPath))
            {
                throw new FileNotFoundException($"未找到 Vite CLI: {viteCliPath}");
            }

            webProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{viteCliPath}\" --host 0.0.0.0 --port {resolvedWebPort.ActualPort} --strictPort",
                    WorkingDirectory = webProjectPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            webProcess.StartInfo.Environment["VITE_SERVER_URL"] = serverBaseUrl;
            webProcess.StartInfo.Environment["VITE_AGENT_URL"] = $"{serverBaseUrl.TrimEnd('/')}/agent";
            webProcess.Start();

            // 后台读取 Web 输出（避免缓冲区阻塞）
            _ = Task.Run(async () =>
            {
                while (!webProcess.HasExited)
                {
                    var line = await webProcess.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        var viteLocalUrl = TryExtractViteLocalUrl(line);
                        if (!string.IsNullOrEmpty(viteLocalUrl))
                        {
                            webBaseUrl = viteLocalUrl;
                            runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
                                "web",
                                "Web",
                                managedByServer: true,
                                autoShifted: resolvedWebPort.AutoShifted,
                                configuredUrl: configuredWebUrl,
                                actualUrl: viteLocalUrl,
                                configuredPort: resolvedWebPort.PreferredPort,
                                actualPort: resolvedWebPort.ActualPort));
                            webReadyUrlSource.TrySetResult(viteLocalUrl);
                        }

                        // 过滤 Vite 冗余输出（多网卡地址、help 提示）
                        if (line.Contains("Network:") || line.Contains("press h + enter"))
                            continue;
                        WriteWithColoredPrefix("[Web]", line, ConsoleColor.Green);
                    }
                }

                if (!webReadyUrlSource.Task.IsCompleted)
                {
                    webReadyUrlSource.TrySetException(
                        new InvalidOperationException("Web 开发服务器在输出 Local 地址前已退出。"));
                }
            });
            _ = Task.Run(async () =>
            {
                while (!webProcess.HasExited)
                {
                    var line = await webProcess.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[Web:ERR]", line, ConsoleColor.DarkGreen);
                }
            });
        }
        catch (Exception ex)
        {
            runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
                "web",
                "Web",
                managedByServer: true,
                autoShifted: false,
                configuredUrl: BuildUrl(Uri.UriSchemeHttp, "localhost", configuredWebPort),
                actualUrl: string.Empty,
                configuredPort: configuredWebPort,
                actualPort: null));
            WriteWithColoredPrefix("[Server:ERR]", $"Web 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
        }
    }
    else if (!isProduction && !webReady)
    {
        runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
            "web",
            "Web",
            managedByServer: true,
            autoShifted: false,
            configuredUrl: BuildUrl(Uri.UriSchemeHttp, "localhost", configuredWebPort),
            actualUrl: string.Empty,
            configuredPort: configuredWebPort,
            actualPort: null));
        WriteWithColoredPrefix("[Server:WARN]", "Web 服务跳过启动（环境未就绪）", ConsoleColor.DarkYellow);
    }
    else if (!isProduction)
    {
        runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
            "web",
            "Web",
            managedByServer: true,
            autoShifted: false,
            configuredUrl: BuildUrl(Uri.UriSchemeHttp, "localhost", configuredWebPort),
            actualUrl: string.Empty,
            configuredPort: configuredWebPort,
            actualPort: null));
        WriteWithColoredPrefix("[Server:ERR]", $"Web 项目目录不存在: {webProjectPath}", ConsoleColor.DarkGray);
    }
    else if (productionWebDistPath != null)
    {
        runtimeEndpointState.SetWeb(CreateRuntimeEndpoint(
            "web",
            "Web",
            managedByServer: true,
            autoShifted: resolvedServerPort.AutoShifted,
            configuredUrl: configuredServerBinding.DisplayUrl,
            actualUrl: serverBaseUrl,
            configuredPort: configuredServerBinding.PreferredPort,
            actualPort: resolvedServerPort.ActualPort));
        WriteWithColoredPrefix("[Server]", $"生产静态资源目录: {productionWebDistPath}", ConsoleColor.White);
    }

    // 5. 提前捕获 DI 服务引用（ProcessExit 时 DI 容器已 Dispose，不能再 GetRequiredService）
    var projectContextRef = app.Services.GetRequiredService<ProjectContext>();
    var gitWorktreeServiceRef = app.Services.GetRequiredService<GitWorktreeService>();

    // 6. 注册退出时清理进程和 Worktree
    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
    {
        // ① 先关闭 Agent（释放 CWD 文件锁）
        if (agentProcess != null && !agentProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Agent 服务...", ConsoleColor.White);
            agentProcess.Kill(true);
            Thread.Sleep(500); // 等待进程完全退出，释放文件锁
        }

        // ② 再清理 Worktree（Agent 已退出，目录不再被占用）
        try
        {
            if (!string.IsNullOrEmpty(projectContextRef.CurrentProjectPath))
            {
                WriteWithColoredPrefix("[Server]", "正在清理 Worktree...", ConsoleColor.White);
                gitWorktreeServiceRef.CleanupAllWorktrees(projectContextRef.CurrentProjectPath);
            }
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Worktree 清理失败: {ex.Message}", ConsoleColor.DarkGray);
        }

        // ③ 关闭 CCR
        if (ccrProcess != null && !ccrProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 CCR 服务...", ConsoleColor.White);
            ccrProcess.Kill(true);
        }

        // ④ 最后关闭 Web
        if (webProcess != null && !webProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Web 开发服务器...", ConsoleColor.White);
            webProcess.Kill(true);
        }
    };
}

if (isProduction && startupErrors.Count > 0)
{
    foreach (var error in startupErrors.Distinct())
    {
        WriteWithColoredPrefix("[Server:ERR]", error, ConsoleColor.DarkGray);
    }

    throw new InvalidOperationException("生产模式依赖未就绪，Server 已终止启动。");
}

await app.StartAsync();
WriteWithColoredPrefix("[Server]", $"HTTP 服务已就绪: {serverBaseUrl}", ConsoleColor.White);

if (agentReadyTask != null)
{
    WriteWithColoredPrefix("[Server]", "等待 Agent 服务启动...", ConsoleColor.White);
    agentRuntimeReady = await agentReadyTask;

    if (agentRuntimeReady)
    {
        WriteWithColoredPrefix("[Server]", $"Agent 服务已就绪: {agentBaseUrl}", ConsoleColor.White);
    }
    else if (agentProcess?.HasExited == true)
    {
        WriteWithColoredPrefix(
            "[Server:WARN]",
            $"Agent 进程在监听端口 {agentPort} 前已退出；Agent 功能暂不可用",
            ConsoleColor.DarkYellow);
    }
    else
    {
        WriteWithColoredPrefix(
            "[Server:WARN]",
            $"Agent 未在预期时间内就绪: {agentBaseUrl}；Agent 功能暂不可用",
            ConsoleColor.DarkYellow);
    }
}

if (!isProduction && webProcess != null && webReadyUrlSource != null)
{
    WriteWithColoredPrefix("[Server]", "等待 Web 服务启动...", ConsoleColor.White);

    try
    {
        webBaseUrl = await webReadyUrlSource.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
    catch (TimeoutException)
    {
        WriteWithColoredPrefix("[Server:WARN]", "未在预期时间内获取到 Web 开发服务器地址", ConsoleColor.DarkYellow);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Web 服务未能正常就绪: {ex.Message}", ConsoleColor.DarkGray);
    }
}

var launchUrl = !string.IsNullOrWhiteSpace(webBaseUrl) ? webBaseUrl : serverBaseUrl;
if (string.IsNullOrWhiteSpace(webBaseUrl))
{
    if (!agentRuntimeReady)
    {
        WriteWithColoredPrefix("[Server:WARN]", "Server 已就绪，但 Agent 未就绪；Agent 功能暂不可用", ConsoleColor.DarkYellow);
    }
    else
    {
        WriteWithColoredPrefix("[Server]", "所有服务已就绪", ConsoleColor.White);
    }
}
else
{
    if (!agentRuntimeReady)
    {
        WriteWithColoredPrefix("[Server:WARN]", "Web 已就绪，但 Agent 未就绪；Agent 功能暂不可用", ConsoleColor.DarkYellow);
    }
    else
    {
        WriteWithColoredPrefix("[Server]", "所有服务已就绪", ConsoleColor.White);
    }
}

var isRestart = Environment.GetEnvironmentVariable("BIMCANVAS_RESTART") == "1";
if (config.Startup.OpenBrowser && !isRestart)
{
    WriteWithColoredPrefix("[Server]", $"打开浏览器: {launchUrl}", ConsoleColor.White);
    try
    {
        if (!string.IsNullOrEmpty(config.Startup.BrowserPath))
        {
            Process.Start(config.Startup.BrowserPath, launchUrl);
        }
        else
        {
            Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"无法自动打开浏览器: {ex.Message}", ConsoleColor.DarkGray);
    }
}

await app.WaitForShutdownAsync();

// ─────────────────────────────────────────────────────────────────────────────
// 重启检查：app.Run() 返回后（进程已停止监听），检测重启标志文件
// ─────────────────────────────────────────────────────────────────────────────
{
    var restartFlagPath = SettingsRestartService.RestartFlagPath;
    if (File.Exists(restartFlagPath))
    {
        File.Delete(restartFlagPath);

        var isDocker = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        if (!isDocker)
        {
            WriteWithColoredPrefix("[Server]", "检测到重启标志，正在启动新实例...", ConsoleColor.White);

            // 获取当前进程的启动方式并重新启动
            var exePath = Environment.ProcessPath;
            var cmdArgs = Environment.GetCommandLineArgs();

            // dotnet run 场景：ProcessPath 是 dotnet.exe，参数包含 run --project ...
            // 编译后直接运行场景：ProcessPath 是 BIMCanvas.Server.exe
            var arguments = cmdArgs.Length > 1
                ? string.Join(" ", cmdArgs.Skip(1))
                : "";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exePath!,
                    Arguments = arguments,
                    UseShellExecute = false
                };
                psi.Environment["BIMCANVAS_RESTART"] = "1";
                // 清除 VS 调试注入的 Hosting Startup 程序集，新进程不在 VS 下运行会加载失败
                psi.Environment.Remove("ASPNETCORE_HOSTINGSTARTUPASSEMBLIES");
                Process.Start(psi);
                WriteWithColoredPrefix("[Server]", "新实例已启动，当前进程退出。", ConsoleColor.White);
            }
            catch (Exception ex)
            {
                WriteWithColoredPrefix("[Server:ERR]", $"启动新实例失败: {ex.Message}", ConsoleColor.DarkGray);
            }
        }
        // Docker 环境：不需要手动启动新实例，restart policy 会接管
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 环境检测辅助函数
// ─────────────────────────────────────────────────────────────────────────────

// 辅助函数：检测 Python 是否可用
static bool IsPythonAvailable(string pythonCommand)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonCommand,
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit(5000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：检测 Agent 核心依赖是否就绪
static bool IsAgentDependencyReady(string pythonCommand)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonCommand,
            Arguments = "-c \"import claude_agent_sdk\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit(10000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：交互式安装 Agent 依赖（返回 true = 安装成功）
static bool TryInstallAgentDependencies(string agentProjectPath, string pythonCommand)
{
    WriteWithColoredPrefix("[Server]", "检测到 Agent 依赖缺失", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write(" 是否自动安装依赖？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    // 默认 Y（直接回车 = 同意，null = 非交互模式也同意）
    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server]", "跳过安装，Agent 服务将不启动", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", "正在安装 Agent 依赖 (pip install -e .)...", ConsoleColor.White);

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonCommand,
            Arguments = "-m pip install -e .",
            WorkingDirectory = agentProjectPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            WriteWithColoredPrefix("[Server:ERR]", "无法启动 pip 进程", ConsoleColor.DarkGray);
            return false;
        }

        // 实时输出安装过程（使用 CancellationToken 控制线程退出）
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[pip]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[pip]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });

        // 同步等待安装完成（超时 10 分钟）
        var completed = process.WaitForExit(600_000);

        if (!completed)
        {
            WriteWithColoredPrefix("[Server:ERR]", "依赖安装超时（10分钟），跳过 Agent 启动", ConsoleColor.DarkGray);
            cts.Cancel();
            process.Kill(true);
            return false;
        }

        if (process.ExitCode == 0)
        {
            WriteWithColoredPrefix("[Server]", "Agent 依赖安装成功", ConsoleColor.White);
            return true;
        }
        else
        {
            WriteWithColoredPrefix("[Server:ERR]", $"依赖安装失败 (exit code: {process.ExitCode})", ConsoleColor.DarkGray);
            return false;
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"依赖安装异常: {ex.Message}", ConsoleColor.DarkGray);
        return false;
    }
}

// 辅助函数：检测 Node.js 是否可用
static bool IsNodeAvailable()
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit(5000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：检测 ripgrep 是否可用（OpenAI Runtime 的 Glob/Grep 工具依赖）
static bool IsRipgrepAvailable()
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "rg",
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit(5000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：交互式安装 ripgrep（返回 true = 安装成功）
static bool TryInstallRipgrep()
{
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    string installCommand;
    string commandLabel;

    if (isWindows)
    {
        installCommand = "winget install BurntSushi.ripgrep.MSVC --accept-package-agreements --accept-source-agreements";
        commandLabel = "winget";
    }
    else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
    {
        installCommand = "brew install ripgrep";
        commandLabel = "brew";
    }
    else
    {
        WriteWithColoredPrefix("[Server:WARN]", "未检测到 ripgrep (rg)，OpenAI Runtime 的 Glob/Grep 工具将不可用", ConsoleColor.DarkYellow);
        WriteWithColoredPrefix("[Server:WARN]", "提示: 请通过系统包管理器安装 ripgrep (apt/dnf/pacman install ripgrep)", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", "未检测到 ripgrep (rg)，OpenAI Runtime 的 Glob/Grep 工具依赖它", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write($" 是否自动安装 ripgrep？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server:WARN]", "跳过安装，OpenAI Runtime 的 Glob/Grep 工具将不可用", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", $"正在安装 ripgrep ({installCommand})...", ConsoleColor.White);

    try
    {
        var psi = CreateShellProcessStartInfo(installCommand);

        using var process = Process.Start(psi);
        if (process == null)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"无法启动 {commandLabel} 进程", ConsoleColor.DarkGray);
            return false;
        }

        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix($"[{commandLabel}]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix($"[{commandLabel}]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });

        var completed = process.WaitForExit(180_000);

        if (!completed)
        {
            WriteWithColoredPrefix("[Server:ERR]", "ripgrep 安装超时（3分钟）", ConsoleColor.DarkGray);
            cts.Cancel();
            process.Kill(true);
            return false;
        }

        if (process.ExitCode != 0)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"ripgrep 安装失败 (exit code: {process.ExitCode})", ConsoleColor.DarkGray);
            return false;
        }

        // 重置缓存，让 IsRipgrepAvailable 重新检测
        if (IsRipgrepAvailable())
        {
            WriteWithColoredPrefix("[Server]", "ripgrep 安装成功", ConsoleColor.White);
            return true;
        }
        else
        {
            WriteWithColoredPrefix("[Server:WARN]", "ripgrep 安装后未能在 PATH 中检测到，可能需要重启终端生效", ConsoleColor.DarkYellow);
            return false;
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"ripgrep 安装异常: {ex.Message}", ConsoleColor.DarkGray);
        return false;
    }
}

// 辅助函数：检测 CCR (claude-code-router) 是否可用
static bool IsCcrAvailable()
{
    try
    {
        var psi = CreateShellProcessStartInfo("ccr version");
        using var process = Process.Start(psi);
        if (process == null) return false;
        process.WaitForExit(5000);
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：交互式安装 CCR（返回 true = 安装成功）
static bool TryInstallCcr()
{
    WriteWithColoredPrefix("[Server]", "未检测到 CCR (claude-code-router)", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write(" 是否自动安装 CCR？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    // 默认 Y（直接回车 = 同意）
    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server:WARN]", "跳过安装，CCR 服务将不启动", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", "正在安装 CCR (npm install -g claude-code-router)...", ConsoleColor.White);

    try
    {
        var psi = CreateShellProcessStartInfo("npm install -g claude-code-router");

        using var process = Process.Start(psi);
        if (process == null)
        {
            WriteWithColoredPrefix("[Server:ERR]", "无法启动 npm 进程", ConsoleColor.DarkGray);
            return false;
        }

        // 实时输出安装过程
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[npm]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[npm]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });

        // 同步等待安装完成（超时 3 分钟）
        var completed = process.WaitForExit(180_000);

        if (!completed)
        {
            WriteWithColoredPrefix("[Server:ERR]", "CCR 安装超时（3分钟），跳过 CCR 启动", ConsoleColor.DarkGray);
            cts.Cancel();
            process.Kill(true);
            return false;
        }

        if (process.ExitCode != 0)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"CCR 安装失败 (exit code: {process.ExitCode})", ConsoleColor.DarkGray);
            return false;
        }

        // 验证安装结果
        if (IsCcrAvailable())
        {
            WriteWithColoredPrefix("[Server]", "CCR 安装成功", ConsoleColor.White);
            return true;
        }
        else
        {
            WriteWithColoredPrefix("[Server:ERR]", "CCR 安装后验证失败，请检查 npm 全局路径是否在 PATH 中", ConsoleColor.DarkGray);
            return false;
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"CCR 安装异常: {ex.Message}", ConsoleColor.DarkGray);
        return false;
    }
}

// 辅助函数：交互式安装 Web 依赖（返回 true = 安装成功）
static bool TryInstallWebDependencies(string webProjectPath)
{
    WriteWithColoredPrefix("[Server]", "检测到 Web 依赖缺失 (node_modules 不存在)", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write(" 是否自动安装 Web 依赖？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    // 默认 Y（直接回车 = 同意）
    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server:WARN]", "跳过安装，Web 服务将不启动", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", "正在安装 Web 依赖 (npm install)...", ConsoleColor.White);

    try
    {
        var psi = CreateShellProcessStartInfo("npm install", webProjectPath);

        using var process = Process.Start(psi);
        if (process == null)
        {
            WriteWithColoredPrefix("[Server:ERR]", "无法启动 npm 进程", ConsoleColor.DarkGray);
            return false;
        }

        // 实时输出安装过程
        var cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[npm]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });
        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) break;
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[npm]", line, ConsoleColor.DarkMagenta);
                }
            }
            catch { }
        });

        // 同步等待安装完成（超时 5 分钟）
        var completed = process.WaitForExit(300_000);

        if (!completed)
        {
            WriteWithColoredPrefix("[Server:ERR]", "Web 依赖安装超时（5分钟），跳过 Web 启动", ConsoleColor.DarkGray);
            cts.Cancel();
            process.Kill(true);
            return false;
        }

        if (process.ExitCode == 0)
        {
            WriteWithColoredPrefix("[Server]", "Web 依赖安装成功", ConsoleColor.White);
            return true;
        }
        else
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Web 依赖安装失败 (exit code: {process.ExitCode})", ConsoleColor.DarkGray);
            return false;
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Web 依赖安装异常: {ex.Message}", ConsoleColor.DarkGray);
        return false;
    }
}

// 辅助函数：检测 Playwright Chromium 是否已安装
static bool IsPlaywrightChromiumInstalled()
{
    var msPlaywrightDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ms-playwright");
    if (!Directory.Exists(msPlaywrightDir))
        return false;
    return Directory.GetDirectories(msPlaywrightDir, "chromium-*").Length > 0;
}

// 辅助函数：交互式安装 Playwright Chromium
static void TryInstallPlaywrightChromium()
{
    WriteWithColoredPrefix("[Server]", "Playwright Chromium 未安装，后台截图功能不可用", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write(" 是否自动安装 Playwright Chromium？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    // 默认 Y（直接回车 = 同意）
    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server]", "跳过安装，后台截图功能将不可用", ConsoleColor.DarkYellow);
        return;
    }

    WriteWithColoredPrefix("[Server]", "正在安装 Playwright Chromium...", ConsoleColor.White);

    try
    {
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode == 0)
        {
            WriteWithColoredPrefix("[Server]", "Playwright Chromium 安装成功", ConsoleColor.White);
        }
        else
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Playwright Chromium 安装失败 (exit code: {exitCode})", ConsoleColor.DarkGray);
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Playwright Chromium 安装异常: {ex.Message}", ConsoleColor.DarkGray);
    }
}

// 辅助函数：启动 CCR 子进程
static Process? StartCcrProcess(ServerConfig config, string configPath)
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                // Windows 下 ccr 是 .cmd 脚本，必须通过 cmd.exe /c 调用
                FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "ccr",
                Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c ccr start" : "start",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        // CCR 通过环境变量 SERVICE_PORT 指定端口
        process.StartInfo.Environment["SERVICE_PORT"] = config.Ccr.Port.ToString();
        process.Start();

        // 后台读取 CCR 输出
        var ccrStdoutLogFilter = new CcrStdoutLogFilter();
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                foreach (var filteredLine in ccrStdoutLogFilter.ProcessLine(line))
                {
                    WriteWithColoredPrefix("[CCR]", filteredLine, ConsoleColor.Magenta);
                }
            }

            foreach (var filteredLine in ccrStdoutLogFilter.Flush())
            {
                WriteWithColoredPrefix("[CCR]", filteredLine, ConsoleColor.Magenta);
            }
        });
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    WriteWithColoredPrefix("[CCR:ERR]", line, ConsoleColor.DarkMagenta);
            }
        });

        return process;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"CCR 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
        return null;
    }
}

// 辅助函数：通用端口就绪检测
static async Task<bool> WaitForServiceReadyAsync(string host, int port, int timeoutMs = 15000, Process? monitoredProcess = null)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (monitoredProcess?.HasExited == true)
        {
            return false;
        }

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(500));
            if (completedTask == connectTask && client.Connected)
                return true;
        }
        catch
        {
            // 端口尚未就绪，继续等待
        }
        await Task.Delay(300);
    }
    return false;
}

static ProcessStartInfo CreateShellProcessStartInfo(string command, string? workingDirectory = null)
{
    var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    var psi = new ProcessStartInfo
    {
        FileName = isWindows ? "cmd.exe" : "/bin/bash",
        Arguments = isWindows
            ? $"/c {command}"
            : $"-c \"{command.Replace("\"", "\\\"")}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    if (!string.IsNullOrWhiteSpace(workingDirectory))
    {
        psi.WorkingDirectory = workingDirectory;
    }

    return psi;
}

static string? ResolveWebDistPath(string startDir)
{
    var configuredDist = Environment.GetEnvironmentVariable("BIMCANVAS_WEB_DIST");
    if (!string.IsNullOrWhiteSpace(configuredDist))
    {
        var configuredPath = Path.GetFullPath(
            Environment.ExpandEnvironmentVariables(configuredDist.Trim()));
        return File.Exists(Path.Combine(configuredPath, "index.html"))
            ? configuredPath
            : null;
    }

    var dir = new DirectoryInfo(startDir);
    for (int i = 0; i < 8 && dir != null; i++)
    {
        var candidate = Path.Combine(dir.FullName, "BIMCanvas.Web", "dist");
        if (File.Exists(Path.Combine(candidate, "index.html")))
        {
            return candidate;
        }
        dir = dir.Parent;
    }

    var fallback = Path.Combine(FindWebProjectPath(startDir), "dist");
    return File.Exists(Path.Combine(fallback, "index.html"))
        ? fallback
        : null;
}

static string? TryExtractViteLocalUrl(string line)
{
    if (string.IsNullOrWhiteSpace(line))
    {
        return null;
    }

    var normalizedLine = Regex.Replace(line, @"\x1B\[[0-9;]*[A-Za-z]", string.Empty);
    if (!normalizedLine.Contains("Local:", StringComparison.Ordinal))
    {
        return null;
    }

    var match = Regex.Match(
        normalizedLine,
        @"https?://(?:localhost|127\.0\.0\.1):\d+/?",
        RegexOptions.IgnoreCase);
    return match.Success ? match.Value : null;
}

static bool IsLocalDevelopmentOrigin(string origin)
{
    if (string.IsNullOrWhiteSpace(origin) ||
        !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return IPAddress.TryParse(uri.Host, out var ipAddress) && IPAddress.IsLoopback(ipAddress);
}

static ServerBindingInfo ResolveConfiguredServerBinding(IConfiguration configuration, int configuredPort)
{
    var configuredUrl = ExtractPrimaryHttpUrl(
        configuration["urls"],
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"),
        configuration["ASPNETCORE_URLS"],
        configuration["Kestrel:Endpoints:Http:Url"],
        configuration["Kestrel:Endpoints:Https:Url"])
        ?? "http://localhost:5000";

    if (!TryParseHttpBinding(configuredUrl, out var parsedBinding))
    {
        parsedBinding = new ParsedHttpBinding(Uri.UriSchemeHttp, "localhost", 5000);
    }

    var effectivePort = configuredPort > 0 ? configuredPort : parsedBinding.Port;
    var listenHost = NormalizeListenHost(parsedBinding.Host);
    var browserHost = GetReachableLocalHost(listenHost);
    return new ServerBindingInfo(
        parsedBinding.Scheme,
        listenHost,
        browserHost,
        effectivePort,
        BuildUrl(parsedBinding.Scheme, browserHost, effectivePort));
}

static ResolvedPortReservation ResolveManagedPort(
    string serviceName,
    string host,
    int preferredPort,
    Func<int, PortOccupantInfo, PortOccupantOwnership> classifyOccupant)
{
    const int maxPortOffset = 20;
    var normalizedHost = NormalizeListenHost(host);
    var currentProcessId = Environment.ProcessId;

    for (var offset = 0; offset <= maxPortOffset; offset++)
    {
        var candidatePort = preferredPort + offset;
        var occupants = GetPortOccupants(candidatePort)
            .Where(item => item.ProcessId != currentProcessId)
            .ToList();

        if (occupants.Count == 0 && CanBindTcpPort(normalizedHost, candidatePort))
        {
            if (offset > 0)
            {
                WriteWithColoredPrefix(
                    "[Server]",
                    $"{serviceName} 端口已自动避让: {preferredPort} → {candidatePort}",
                    ConsoleColor.White);
            }

            return new ResolvedPortReservation(preferredPort, candidatePort, offset > 0);
        }

        var classifiedOccupants = occupants
            .Select(item => new ClassifiedPortOccupantInfo(
                item.ProcessId,
                item.State,
                classifyOccupant(candidatePort, item)))
            .ToList();

        var ownedOccupants = classifiedOccupants
            .Where(item => item.Ownership is PortOccupantOwnership.OwnedManaged or PortOccupantOwnership.OwnedLegacy)
            .ToList();
        if (ownedOccupants.Count > 0)
        {
            foreach (var ownedOccupant in ownedOccupants)
            {
                WriteWithColoredPrefix(
                    "[Server]",
                    $"清理残留 {serviceName} 进程 (PID: {ownedOccupant.ProcessId})，准备复用端口 {candidatePort}",
                    ConsoleColor.White);
                KillProcess(ownedOccupant.ProcessId);
                Thread.Sleep(500);
            }

            var remainingOccupants = GetPortOccupants(candidatePort)
                .Where(item => item.ProcessId != currentProcessId)
                .ToList();
            if (remainingOccupants.Count == 0 && CanBindTcpPort(normalizedHost, candidatePort))
            {
                return new ResolvedPortReservation(preferredPort, candidatePort, offset > 0);
            }

            classifiedOccupants = remainingOccupants
                .Select(item => new ClassifiedPortOccupantInfo(
                    item.ProcessId,
                    item.State,
                    classifyOccupant(candidatePort, item)))
                .ToList();
        }

        if (offset == maxPortOffset)
        {
            break;
        }

        if (classifiedOccupants.Count > 0)
        {
            var occupant = SelectOccupantForLogging(classifiedOccupants);
            switch (occupant.Ownership)
            {
                case PortOccupantOwnership.ForeignBimCanvasInstance:
                    WriteWithColoredPrefix(
                        "[Server:WARN]",
                        $"端口 {candidatePort} 被其他 BIMCanvas {serviceName} 实例占用 (PID: {occupant.ProcessId}, 状态: {occupant.State})，{serviceName} 将尝试 {candidatePort + 1}",
                        ConsoleColor.DarkYellow);
                    break;
                case PortOccupantOwnership.OwnedManaged:
                case PortOccupantOwnership.OwnedLegacy:
                    WriteWithColoredPrefix(
                        "[Server:WARN]",
                        $"端口 {candidatePort} 的残留 {serviceName} 进程未能及时退出 (PID: {occupant.ProcessId}, 状态: {occupant.State})，{serviceName} 将尝试 {candidatePort + 1}",
                        ConsoleColor.DarkYellow);
                    break;
                default:
                    WriteWithColoredPrefix(
                        "[Server:WARN]",
                        $"端口 {candidatePort} 被外部进程占用 (PID: {occupant.ProcessId}, 状态: {occupant.State})，{serviceName} 将尝试 {candidatePort + 1}",
                        ConsoleColor.DarkYellow);
                    break;
            }
        }
        else
        {
            WriteWithColoredPrefix(
                "[Server:WARN]",
                $"端口 {candidatePort} 当前不可绑定，{serviceName} 将尝试 {candidatePort + 1}",
                ConsoleColor.DarkYellow);
        }
    }

    throw new InvalidOperationException(
        $"{serviceName} 在端口 {preferredPort}-{preferredPort + maxPortOffset} 范围内未找到可用端口。");
}

static RuntimeServiceEndpointDto CreateRuntimeEndpoint(
    string key,
    string title,
    bool managedByServer,
    bool autoShifted,
    string configuredUrl,
    string actualUrl,
    int? configuredPort,
    int? actualPort)
{
    return new RuntimeServiceEndpointDto
    {
        Key = key,
        Title = title,
        ManagedByServer = managedByServer,
        AutoShifted = autoShifted,
        ConfiguredUrl = configuredUrl,
        ActualUrl = actualUrl,
        ConfiguredPort = configuredPort,
        ActualPort = actualPort
    };
}

static string BuildUrl(string scheme, string host, int port)
{
    var normalizedHost = host.Trim();
    if (normalizedHost.Contains(':') &&
        !normalizedHost.StartsWith("[", StringComparison.Ordinal))
    {
        normalizedHost = $"[{normalizedHost}]";
    }

    return $"{scheme}://{normalizedHost}:{port}";
}

static int? TryGetPortFromUrl(string url)
{
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        return uri.Port;
    }

    return null;
}

static string GetReachableLocalHost(string host)
{
    return IsWildcardHost(host) ? "localhost" : NormalizeListenHost(host);
}

static string NormalizeListenHost(string host)
{
    var normalized = (host ?? string.Empty).Trim();
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return "127.0.0.1";
    }

    if (normalized.StartsWith("[", StringComparison.Ordinal) &&
        normalized.EndsWith("]", StringComparison.Ordinal) &&
        normalized.Length > 2)
    {
        normalized = normalized[1..^1];
    }

    return normalized;
}

static bool IsWildcardHost(string host)
{
    var normalized = NormalizeListenHost(host);
    return string.Equals(normalized, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
           || string.Equals(normalized, "::", StringComparison.OrdinalIgnoreCase)
           || string.Equals(normalized, "*", StringComparison.OrdinalIgnoreCase)
           || string.Equals(normalized, "+", StringComparison.OrdinalIgnoreCase);
}

static string? ExtractPrimaryHttpUrl(params string?[] candidates)
{
    foreach (var candidate in candidates)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            continue;
        }

        foreach (var part in candidate.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                part.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }
    }

    return null;
}

static bool TryParseHttpBinding(string rawUrl, out ParsedHttpBinding binding)
{
    binding = new ParsedHttpBinding(Uri.UriSchemeHttp, "localhost", 5000);
    if (string.IsNullOrWhiteSpace(rawUrl))
    {
        return false;
    }

    if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
    {
        binding = new ParsedHttpBinding(uri.Scheme, uri.Host, uri.Port);
        return true;
    }

    var match = Regex.Match(
        rawUrl.Trim(),
        @"^(?<scheme>https?)://(?<host>\[[^\]]+\]|[^:/]+):(?<port>\d+)",
        RegexOptions.IgnoreCase);
    if (!match.Success)
    {
        return false;
    }

    var host = match.Groups["host"].Value;
    if (host.StartsWith("[", StringComparison.Ordinal) &&
        host.EndsWith("]", StringComparison.Ordinal) &&
        host.Length > 2)
    {
        host = host[1..^1];
    }

    binding = new ParsedHttpBinding(
        match.Groups["scheme"].Value.ToLowerInvariant(),
        host,
        int.Parse(match.Groups["port"].Value));
    return true;
}

static bool CanBindTcpPort(string host, int port)
{
    try
    {
        var bindAddress = ResolveBindAddress(host);
        using var socket = new Socket(bindAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            socket.ExclusiveAddressUse = true;
        }

        socket.Bind(new IPEndPoint(bindAddress, port));
        return true;
    }
    catch
    {
        return false;
    }
}

static IPAddress ResolveBindAddress(string host)
{
    var normalizedHost = NormalizeListenHost(host);
    if (string.Equals(normalizedHost, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.Loopback;
    }

    if (string.Equals(normalizedHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalizedHost, "*", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(normalizedHost, "+", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.Any;
    }

    if (string.Equals(normalizedHost, "::", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.IPv6Any;
    }

    if (IPAddress.TryParse(normalizedHost, out var parsedAddress))
    {
        return parsedAddress;
    }

    return Dns.GetHostAddresses(normalizedHost)
        .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork)
        ?? IPAddress.Loopback;
}

static List<PortOccupantInfo> GetPortOccupants(int port)
{
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return GetWindowsPortOccupants(port);
        }

        return GetUnixPortOccupants(port);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"端口检测失败: {ex.Message}，将退化为直接尝试绑定", ConsoleColor.DarkYellow);
        return [];
    }
}

static List<PortOccupantInfo> GetWindowsPortOccupants(int port)
{
    var psi = new ProcessStartInfo
    {
        FileName = "powershell",
        Arguments = $"-NoProfile -Command \"$items = Get-NetTCPConnection -LocalPort {port} -ErrorAction SilentlyContinue | Select-Object OwningProcess, State -Unique; if ($items) {{ $items | ConvertTo-Json -Compress }}\"",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };

    using var process = Process.Start(psi);
    if (process == null)
    {
        return [];
    }

    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit(5000);
    if (string.IsNullOrWhiteSpace(output))
    {
        return [];
    }

    var result = new List<PortOccupantInfo>();
    var token = Newtonsoft.Json.Linq.JToken.Parse(output);
    if (token is Newtonsoft.Json.Linq.JObject obj)
    {
        AddPortOccupant(result, obj);
    }
    else if (token is Newtonsoft.Json.Linq.JArray array)
    {
        foreach (var item in array.OfType<Newtonsoft.Json.Linq.JObject>())
        {
            AddPortOccupant(result, item);
        }
    }

    return result
        .GroupBy(item => item.ProcessId)
        .Select(group => group.First())
        .ToList();
}

static void AddPortOccupant(List<PortOccupantInfo> target, Newtonsoft.Json.Linq.JObject element)
{
    var pid = element.Value<int?>("OwningProcess") ?? 0;
    if (pid <= 0)
    {
        return;
    }

    var state = element.Value<string>("State") ?? "Unknown";
    target.Add(new PortOccupantInfo(pid, state));
}

static List<PortOccupantInfo> GetUnixPortOccupants(int port)
{
    var psi = new ProcessStartInfo
    {
        FileName = "/bin/bash",
        Arguments = $"-c \"lsof -nP -iTCP:{port} -t\"",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var process = Process.Start(psi);
    if (process == null)
    {
        return [];
    }

    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit(5000);
    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => int.TryParse(line, out var pid) ? new PortOccupantInfo(pid, "Bound") : null)
        .Where(item => item != null)
        .Select(item => item!)
        .GroupBy(item => item.ProcessId)
        .Select(group => group.First())
        .ToList();
}

// ─────────────────────────────────────────────────────────────────────────────
// 路径查找辅助函数
// ─────────────────────────────────────────────────────────────────────────────

// 辅助函数：向上查找 BIMCanvas.Web 目录
static string FindWebProjectPath(string startDir)
{
    var dir = new DirectoryInfo(startDir);

    // 向上最多查找 5 层
    for (int i = 0; i < 5 && dir != null; i++)
    {
        var webPath = Path.Combine(dir.FullName, "BIMCanvas.Web");
        if (Directory.Exists(webPath))
        {
            return webPath;
        }
        dir = dir.Parent;
    }

    // 兜底：返回相对路径（兼容 dotnet run）
    return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "BIMCanvas.Web"));
}

// 辅助函数：向上查找 BIMCanvas.Agent 目录
static string FindAgentProjectPath(string startDir)
{
    var dir = new DirectoryInfo(startDir);

    // 向上最多查找 5 层
    for (int i = 0; i < 5 && dir != null; i++)
    {
        var agentPath = Path.Combine(dir.FullName, "BIMCanvas.Agent");
        if (Directory.Exists(agentPath))
        {
            return agentPath;
        }
        dir = dir.Parent;
    }

    // 兜底：返回相对路径（兼容 dotnet run）
    return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "BIMCanvas.Agent"));
}

// 辅助函数：向上查找 BIMCanvas.Server 目录
static string FindServerProjectPath(string startDir)
{
    var dir = new DirectoryInfo(startDir);

    // 向上最多查找 5 层
    for (int i = 0; i < 5 && dir != null; i++)
    {
        var serverPath = Path.Combine(dir.FullName, "BIMCanvas.Server");
        if (Directory.Exists(serverPath))
        {
            return serverPath;
        }
        dir = dir.Parent;
    }

    // 兜底：返回相对路径（兼容 dotnet run）
    return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "BIMCanvas.Server"));
}

static PortOccupantOwnership ClassifyBIMCanvasAgentOccupant(
    int port,
    int pid,
    string agentProjectPath,
    string managedHome)
{
    try
    {
        var process = Process.GetProcessById(pid);
        var cmdLine = GetProcessCommandLine(pid);
        if (!LooksLikeBIMCanvasAgentCommand(process.ProcessName, cmdLine))
        {
            return PortOccupantOwnership.ExternalProcess;
        }

        var commandArgs = TokenizeCommandLine(cmdLine);
        if (ContainsCommandLineArgument(commandArgs, "--managed-by-server"))
        {
            // 若 --managed-by-server 带父 Server PID，且该 Server 已不存在，则视为孤儿，
            // 沿用 OwnedLegacy 清理路径回收端口。老版本无 PID 时跳过该检查。
            var parentServerPidRaw = GetCommandLineArgumentValue(commandArgs, "--managed-by-server");
            var parentServerPid = 0;
            var hasParentServerPid = !string.IsNullOrWhiteSpace(parentServerPidRaw)
                && !parentServerPidRaw.StartsWith("-", StringComparison.Ordinal)
                && int.TryParse(
                    parentServerPidRaw,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out parentServerPid);
            var parentServerAlive = hasParentServerPid && IsProcessAlive(parentServerPid);
            if (hasParentServerPid && !parentServerAlive)
            {
                return PortOccupantOwnership.OwnedLegacy;
            }

            var normalizedAgentRoot = NormalizePathForMatch(
                GetCommandLineArgumentValue(commandArgs, "--managed-agent-root") ?? string.Empty);
            var normalizedCurrentRoot = NormalizePathForMatch(agentProjectPath);
            var normalizedManagedHome = NormalizePathForMatch(
                GetCommandLineArgumentValue(commandArgs, "--managed-home") ?? string.Empty);
            var normalizedCurrentHome = NormalizePathForMatch(managedHome);

            if (!string.IsNullOrWhiteSpace(normalizedAgentRoot) &&
                normalizedAgentRoot.Equals(normalizedCurrentRoot, StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(normalizedManagedHome)
                    ? normalizedManagedHome.Equals(normalizedCurrentHome, StringComparison.OrdinalIgnoreCase)
                    : true))
            {
                if (hasParentServerPid && parentServerPid != Environment.ProcessId && parentServerAlive)
                {
                    return PortOccupantOwnership.ForeignBimCanvasInstance;
                }

                return PortOccupantOwnership.OwnedManaged;
            }

            return PortOccupantOwnership.ForeignBimCanvasInstance;
        }

        return ProbeBIMCanvasAgentHealth(port)
            ? PortOccupantOwnership.OwnedLegacy
            : PortOccupantOwnership.ExternalProcess;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"Agent 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return PortOccupantOwnership.ExternalProcess;
    }
}

static bool LooksLikeBIMCanvasAgentCommand(string processName, string commandLine)
{
    if (string.IsNullOrWhiteSpace(commandLine) || !IsPythonHostProcess(processName))
    {
        return false;
    }

    var commandArgs = TokenizeCommandLine(commandLine);
    return HasModuleLaunchArgument(commandArgs, "src.main")
           && ContainsCommandLineArgument(commandArgs, "--serve");
}

static bool IsPythonHostProcess(string processName)
{
    if (string.IsNullOrWhiteSpace(processName))
    {
        return false;
    }

    return processName.Contains("python", StringComparison.OrdinalIgnoreCase)
           || processName.Equals("py", StringComparison.OrdinalIgnoreCase);
}

static bool HasModuleLaunchArgument(IReadOnlyList<string> args, string moduleName)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (args[i].Equals("-m", StringComparison.OrdinalIgnoreCase) &&
            args[i + 1].Equals(moduleName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static bool ContainsCommandLineArgument(IReadOnlyList<string> args, string argumentName)
{
    return args.Any(arg => arg.Equals(argumentName, StringComparison.OrdinalIgnoreCase));
}

static string? GetCommandLineArgumentValue(IReadOnlyList<string> args, string argumentName)
{
    for (var i = 0; i < args.Count - 1; i++)
    {
        if (args[i].Equals(argumentName, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static List<string> TokenizeCommandLine(string commandLine)
{
    var tokens = new List<string>();
    if (string.IsNullOrWhiteSpace(commandLine))
    {
        return tokens;
    }

    var current = new StringBuilder();
    var inQuotes = false;

    foreach (var ch in commandLine)
    {
        if (ch == '"')
        {
            inQuotes = !inQuotes;
            continue;
        }

        if (char.IsWhiteSpace(ch) && !inQuotes)
        {
            if (current.Length > 0)
            {
                tokens.Add(current.ToString());
                current.Clear();
            }

            continue;
        }

        current.Append(ch);
    }

    if (current.Length > 0)
    {
        tokens.Add(current.ToString());
    }

    return tokens;
}

static bool ProbeBIMCanvasAgentHealth(int port)
{
    try
    {
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(1500)
        };
        using var response = client.GetAsync(BuildUrl(Uri.UriSchemeHttp, "127.0.0.1", port) + "/health")
            .GetAwaiter()
            .GetResult();
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }
        var document = Newtonsoft.Json.Linq.JObject.Parse(body);
        var service = document.Value<string>("service");
        return service?.Equals("bimcanvas-agent", StringComparison.OrdinalIgnoreCase) == true;
    }
    catch
    {
        return false;
    }
}

static ClassifiedPortOccupantInfo SelectOccupantForLogging(IReadOnlyList<ClassifiedPortOccupantInfo> occupants)
{
    return occupants
        .OrderByDescending(item => item.Ownership == PortOccupantOwnership.ForeignBimCanvasInstance)
        .ThenByDescending(item => item.Ownership is PortOccupantOwnership.OwnedManaged or PortOccupantOwnership.OwnedLegacy)
        .First();
}

static PortOccupantOwnership ClassifyBIMCanvasWebOccupant(int pid, string webProjectPath)
{
    try
    {
        using var process = Process.GetProcessById(pid);
        var cmdLine = GetProcessCommandLine(pid);
        if (!LooksLikeManagedBIMCanvasWebCommand(process.ProcessName, cmdLine))
        {
            return PortOccupantOwnership.ExternalProcess;
        }

        if (!CommandLineReferencesDirectory(cmdLine, webProjectPath))
        {
            return PortOccupantOwnership.ForeignBimCanvasInstance;
        }

        return ClassifyManagedChildOccupantByParent(
            pid,
            parentCommandMatcher: LooksLikeBIMCanvasServerCommand,
            liveParentOwnership: PortOccupantOwnership.ForeignBimCanvasInstance);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"Web 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return PortOccupantOwnership.ExternalProcess;
    }
}

static bool LooksLikeManagedBIMCanvasWebCommand(string processName, string commandLine)
{
    if (string.IsNullOrWhiteSpace(commandLine) ||
        !processName.Contains("node", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var normalizedCommandLine = NormalizeCommandLineForMatch(commandLine);
    return normalizedCommandLine.Contains("vite", StringComparison.OrdinalIgnoreCase)
           && normalizedCommandLine.Contains("node_modules\\vite\\bin\\vite.js", StringComparison.OrdinalIgnoreCase)
           && normalizedCommandLine.Contains("--port", StringComparison.OrdinalIgnoreCase)
           && normalizedCommandLine.Contains("--strictPort", StringComparison.OrdinalIgnoreCase);
}

static PortOccupantOwnership ClassifyManagedChildOccupantByParent(
    int pid,
    Func<string, string, bool> parentCommandMatcher,
    PortOccupantOwnership liveParentOwnership)
{
    if (!TryGetParentProcessId(pid, out var parentPid))
    {
        return PortOccupantOwnership.ExternalProcess;
    }

    if (parentPid == Environment.ProcessId)
    {
        return PortOccupantOwnership.OwnedManaged;
    }

    if (!IsProcessAlive(parentPid))
    {
        return PortOccupantOwnership.OwnedLegacy;
    }

    try
    {
        using var parentProcess = Process.GetProcessById(parentPid);
        var parentCommandLine = GetProcessCommandLine(parentPid);
        return parentCommandMatcher(parentProcess.ProcessName, parentCommandLine)
            ? liveParentOwnership
            : PortOccupantOwnership.ExternalProcess;
    }
    catch
    {
        return PortOccupantOwnership.ExternalProcess;
    }
}

static bool CommandLineReferencesDirectory(string commandLine, string directoryPath)
{
    if (string.IsNullOrWhiteSpace(commandLine) || string.IsNullOrWhiteSpace(directoryPath))
    {
        return false;
    }

    var normalizedCommandLine = NormalizeCommandLineForMatch(commandLine);
    var normalizedDirectory = NormalizePathForMatch(directoryPath);
    if (!string.IsNullOrWhiteSpace(normalizedDirectory) &&
        normalizedCommandLine.Contains(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    var directoryName = Path.GetFileName(normalizedDirectory);
    return !string.IsNullOrWhiteSpace(directoryName) &&
           (normalizedCommandLine.Contains($"\\{directoryName}\\", StringComparison.OrdinalIgnoreCase) ||
            normalizedCommandLine.Contains($"\\{directoryName}\"", StringComparison.OrdinalIgnoreCase));
}

static bool IsBIMCanvasCcrProcess(int pid)
{
    try
    {
        var process = Process.GetProcessById(pid);
        if (!process.ProcessName.Contains("node", StringComparison.OrdinalIgnoreCase) &&
            !process.ProcessName.Contains("ccr", StringComparison.OrdinalIgnoreCase) &&
            !process.ProcessName.Contains("cmd", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var cmdLine = GetProcessCommandLine(pid);
        return cmdLine.Contains("claude-code-router", StringComparison.OrdinalIgnoreCase)
               || cmdLine.Contains("ccr start", StringComparison.OrdinalIgnoreCase)
               || cmdLine.Contains("\\ccr.cmd", StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"CCR 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return false;
    }
}

static PortOccupantOwnership ClassifyBIMCanvasServerOccupant(int pid, string serverBaseDir)
{
    try
    {
        if (pid == Environment.ProcessId)
        {
            return PortOccupantOwnership.ExternalProcess;
        }

        using var process = Process.GetProcessById(pid);
        var cmdLine = GetProcessCommandLine(pid);
        if (!LooksLikeBIMCanvasServerCommand(process.ProcessName, cmdLine))
        {
            return PortOccupantOwnership.ExternalProcess;
        }

        if (!CommandLineReferencesDirectory(cmdLine, serverBaseDir) &&
            !CommandLineReferencesDirectory(cmdLine, FindServerProjectPath(serverBaseDir)))
        {
            return PortOccupantOwnership.ForeignBimCanvasInstance;
        }

        if (TryGetParentProcessId(pid, out var parentPid) && IsProcessAlive(parentPid))
        {
            return PortOccupantOwnership.ForeignBimCanvasInstance;
        }

        return PortOccupantOwnership.OwnedLegacy;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"Server 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return PortOccupantOwnership.ExternalProcess;
    }
}

static bool LooksLikeBIMCanvasServerCommand(string processName, string commandLine)
{
    if (string.IsNullOrWhiteSpace(processName))
    {
        return false;
    }

    if (processName.Contains("BIMCanvas.Server", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    return processName.Contains("dotnet", StringComparison.OrdinalIgnoreCase)
           && commandLine.Contains("BIMCanvas.Server", StringComparison.OrdinalIgnoreCase);
}

static string GetProcessCommandLine(int pid)
{
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? GetCommandLineWindows(pid)
        : GetCommandLineUnix(pid);
}

static bool TryGetParentProcessId(int pid, out int parentPid)
{
    parentPid = 0;
    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryGetParentProcessIdWindows(pid, out parentPid);
        }

        return TryGetParentProcessIdUnix(pid, out parentPid);
    }
    catch
    {
        parentPid = 0;
        return false;
    }
}

static string NormalizePathForMatch(string path)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return string.Empty;
    }

    var normalized = Environment.ExpandEnvironmentVariables(path)
        .Trim()
        .Trim('"')
        .Replace('/', '\\');
    try
    {
        normalized = Path.GetFullPath(normalized);
    }
    catch
    {
        // command line fragments are not always valid filesystem paths; keep best-effort text.
    }

    return normalized.TrimEnd('\\');
}

static string NormalizeCommandLineForMatch(string commandLine)
{
    return (commandLine ?? string.Empty)
        .Replace('/', '\\')
        .Trim();
}

static string GetCommandLineWindows(int pid)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return "";

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $p = Get-CimInstance Win32_Process -Filter 'ProcessId={pid}' -ErrorAction SilentlyContinue; if ($null -ne $p) {{ $p.CommandLine }}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null) return "";

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output.Trim();
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"命令行查询失败: {ex.Message}，仅验证进程名", ConsoleColor.DarkYellow);
    }
    return "";
}

static bool TryGetParentProcessIdWindows(int pid, out int parentPid)
{
    parentPid = 0;
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        return false;
    }

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -Command \"$p = Get-CimInstance Win32_Process -Filter 'ProcessId={pid}' -ErrorAction SilentlyContinue; if ($null -ne $p) {{ $p.ParentProcessId }}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit(5000);
        return int.TryParse(output, out parentPid) && parentPid > 0;
    }
    catch
    {
        parentPid = 0;
        return false;
    }
}

// Unix 查询命令行
static string GetCommandLineUnix(int pid)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"ps -p {pid} -o args=\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null) return "";

        var cmdLine = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return cmdLine;
    }
    catch
    {
        return "";
    }
}

static bool TryGetParentProcessIdUnix(int pid, out int parentPid)
{
    parentPid = 0;
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"-c \"ps -p {pid} -o ppid=\"",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(psi);
        if (process == null)
        {
            return false;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return int.TryParse(output, out parentPid) && parentPid > 0;
    }
    catch
    {
        parentPid = 0;
        return false;
    }
}

// 辅助函数：检查进程是否仍然存活
static bool IsProcessAlive(int pid)
{
    if (pid <= 0)
    {
        return false;
    }

    try
    {
        using var process = Process.GetProcessById(pid);
        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        // 进程不存在
        return false;
    }
    catch (InvalidOperationException)
    {
        return false;
    }
    catch
    {
        // 权限等异常时保守认为进程仍存活，避免误杀
        return true;
    }
}

// 辅助函数：安全关闭进程（优雅关闭 + 强制终止）
static void KillProcess(int pid)
{
    try
    {
        var process = Process.GetProcessById(pid);

        // 尝试优雅关闭
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            process.CloseMainWindow();
            if (!process.WaitForExit(2000)) // 等待 2 秒
            {
                // 静默强制终止（不输出日志，避免显得像错误）
                process.Kill(entireProcessTree: true);
            }
        }
        else
        {
            // Linux/macOS: 先 SIGTERM，后 SIGKILL
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"kill {pid}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit();

            Thread.Sleep(2000);
            if (!process.HasExited)
            {
                psi.Arguments = $"-c \"kill -9 {pid}\"";
                Process.Start(psi)?.WaitForExit();
            }
        }

        WriteWithColoredPrefix("[Server]", "端口清理完成", ConsoleColor.White);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"清理进程失败: {ex.Message}", ConsoleColor.DarkGray);
    }
}

async Task ProxyToAgentAsync(HttpContext context, string agentBaseUrl)
{
    var targetBase = (agentBaseUrl ?? string.Empty).Trim().TrimEnd('/');
    if (string.IsNullOrWhiteSpace(targetBase))
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = "Agent base URL is not configured." });
        return;
    }

    var targetUri = $"{targetBase}{context.Request.Path}{context.Request.QueryString}";
    using var requestMessage = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

    if (RequestHasBody(context.Request))
    {
        requestMessage.Content = new StreamContent(context.Request.Body);
        CopyContentHeaders(context.Request.Headers, requestMessage.Content.Headers);
    }

    CopyRequestHeaders(context.Request.Headers, requestMessage.Headers);

    try
    {
        using var responseMessage = await agentProxyHttpClient.SendAsync(
            requestMessage,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        context.Response.StatusCode = (int)responseMessage.StatusCode;
        CopyResponseHeaders(responseMessage, context.Response.Headers);
        context.Response.Headers.Remove("transfer-encoding");

        await using var responseStream = await responseMessage.Content.ReadAsStreamAsync(context.RequestAborted);
        await responseStream.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        // 客户端主动断开时不再写回错误
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Agent service is unavailable.",
            detail = ex.Message
        });
    }
}

bool RequestHasBody(HttpRequest request)
{
    if (request.ContentLength is > 0)
    {
        return true;
    }

    return request.Headers.ContainsKey("Transfer-Encoding");
}

void CopyRequestHeaders(IHeaderDictionary source, HttpHeaders destination)
{
    foreach (var header in source)
    {
        if (hopByHopHeaders.Contains(header.Key))
        {
            continue;
        }

        destination.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
}

void CopyContentHeaders(IHeaderDictionary source, HttpContentHeaders destination)
{
    foreach (var header in source)
    {
        if (hopByHopHeaders.Contains(header.Key))
        {
            continue;
        }

        destination.TryAddWithoutValidation(header.Key, header.Value.ToArray());
    }
}

void CopyResponseHeaders(HttpResponseMessage source, IHeaderDictionary destination)
{
    foreach (var header in source.Headers)
    {
        if (hopByHopHeaders.Contains(header.Key))
        {
            continue;
        }

        destination[header.Key] = header.Value.ToArray();
    }

    foreach (var header in source.Content.Headers)
    {
        if (hopByHopHeaders.Contains(header.Key))
        {
            continue;
        }

        destination[header.Key] = header.Value.ToArray();
    }
}

file sealed record ParsedHttpBinding(string Scheme, string Host, int Port);

file sealed record ServerBindingInfo(
    string Scheme,
    string ListenHost,
    string BrowserHost,
    int PreferredPort,
    string DisplayUrl);

file enum PortOccupantOwnership
{
    OwnedManaged,
    OwnedLegacy,
    ForeignBimCanvasInstance,
    ExternalProcess
}

file sealed record ResolvedPortReservation(int PreferredPort, int ActualPort, bool AutoShifted);

file sealed record PortOccupantInfo(int ProcessId, string State);

file sealed record ClassifiedPortOccupantInfo(int ProcessId, string State, PortOccupantOwnership Ownership);

file sealed class CcrStdoutLogFilter
{
    private const int MaxBufferedLines = 80;
    private const int MaxBufferedChars = 16 * 1024;

    private static readonly Regex ImageUrlTypePattern = new(
        "(?:\"type\"\\s*:\\s*\"image_url\"|type\\s*:\\s*['\"]image_url['\"])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<string> _buffer = new();
    private int _bufferedChars;
    private int _braceDepth;
    private bool _bufferingObject;
    private bool _suppressingImageObject;
    private bool _passthroughObject;

    public IEnumerable<string> ProcessLine(string line)
    {
        if (_suppressingImageObject)
        {
            TrackBraceDepth(line);
            if (_braceDepth <= 0)
            {
                ResetObjectState();
            }

            return Array.Empty<string>();
        }

        if (_passthroughObject)
        {
            if (LooksLikeImageUrlLog(line))
            {
                _suppressingImageObject = true;
                _passthroughObject = false;
                TrackBraceDepth(line);
                if (_braceDepth <= 0)
                {
                    ResetObjectState();
                }

                return Array.Empty<string>();
            }

            TrackBraceDepth(line);
            if (_braceDepth <= 0)
            {
                ResetObjectState();
            }

            return new[] { line };
        }

        if (_bufferingObject)
        {
            BufferLine(line);
            TrackBraceDepth(line);

            if (LooksLikeImageUrlLog(line))
            {
                _buffer.Clear();
                _bufferedChars = 0;
                _suppressingImageObject = true;
                _bufferingObject = false;
                if (_braceDepth <= 0)
                {
                    ResetObjectState();
                }

                return Array.Empty<string>();
            }

            if (_braceDepth <= 0)
            {
                return FlushBufferAndReset();
            }

            if (_buffer.Count >= MaxBufferedLines || _bufferedChars >= MaxBufferedChars)
            {
                var output = FlushBuffer();
                _bufferingObject = false;
                _passthroughObject = true;
                return output;
            }

            return Array.Empty<string>();
        }

        if (line.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            StartBufferingObject(line);

            if (LooksLikeImageUrlLog(line))
            {
                _buffer.Clear();
                _bufferedChars = 0;
                _suppressingImageObject = true;
                _bufferingObject = false;
                if (_braceDepth <= 0)
                {
                    ResetObjectState();
                }

                return Array.Empty<string>();
            }

            if (_braceDepth <= 0)
            {
                return FlushBufferAndReset();
            }

            return Array.Empty<string>();
        }

        return new[] { line };
    }

    public IEnumerable<string> Flush()
    {
        if (_suppressingImageObject)
        {
            ResetObjectState();
            return Array.Empty<string>();
        }

        return FlushBufferAndReset();
    }

    private void StartBufferingObject(string line)
    {
        _bufferingObject = true;
        _passthroughObject = false;
        _suppressingImageObject = false;
        _braceDepth = CountBraceDelta(line);
        _buffer.Clear();
        _bufferedChars = 0;
        BufferLine(line);
    }

    private void TrackBraceDepth(string line)
    {
        _braceDepth += CountBraceDelta(line);
    }

    private void BufferLine(string line)
    {
        _buffer.Add(line);
        _bufferedChars += line.Length;
    }

    private string[] FlushBuffer()
    {
        if (_buffer.Count == 0)
        {
            return Array.Empty<string>();
        }

        var output = _buffer.ToArray();
        _buffer.Clear();
        _bufferedChars = 0;
        return output;
    }

    private string[] FlushBufferAndReset()
    {
        var output = FlushBuffer();
        ResetObjectState();
        return output;
    }

    private void ResetObjectState()
    {
        _buffer.Clear();
        _bufferedChars = 0;
        _braceDepth = 0;
        _bufferingObject = false;
        _suppressingImageObject = false;
        _passthroughObject = false;
    }

    private static bool LooksLikeImageUrlLog(string line)
    {
        return ImageUrlTypePattern.IsMatch(line)
            || line.Contains("data:image/", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountBraceDelta(string line)
    {
        var delta = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var escaped = false;

        foreach (var ch in line)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if ((inSingleQuote || inDoubleQuote) && ch == '\\')
            {
                escaped = true;
                continue;
            }

            if (!inDoubleQuote && ch == '\'')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (!inSingleQuote && ch == '"')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (ch == '{')
            {
                delta++;
            }
            else if (ch == '}')
            {
                delta--;
            }
        }

        return delta;
    }
}
