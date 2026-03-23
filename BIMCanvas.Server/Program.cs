using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Logging;
using BIMCanvas.Server.Models;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Git;
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

var builder = WebApplication.CreateBuilder(args);

// 配置统一日志格式（替换默认 Console Logger 格式）
builder.Logging.ClearProviders();
builder.Logging.AddServerConsoleFormatter();

// 加载用户配置（提前到 DI 注册前，供 AgentClientService 等服务使用）
ConfigService.EnsureDefaultConfigs();
var config = ConfigService.Load();
var pythonCommand = string.IsNullOrWhiteSpace(config.LiteLlm.PythonCommand)
    ? "python"
    : config.LiteLlm.PythonCommand.Trim();
var configDir = ConfigService.GetConfigDir();
var liteLlmSourceConfigPath = Path.Combine(
    configDir,
    string.IsNullOrWhiteSpace(config.LiteLlm.ConfigFileName) ? "litellm_config.yaml" : config.LiteLlm.ConfigFileName
);
var liteLlmConfigPath = liteLlmSourceConfigPath;
LiteLlmRuntimeConfigBuildResult? liteLlmRuntimeArtifacts = null;
var liteLlmConfigReady = !config.LiteLlm.Enabled;

// 注册配置 + Agent HTTP 客户端
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<AgentClientService>();

// 配置 JSON 序列化选项（使用 Newtonsoft.Json，与 BIMCanvas.Core 保持一致）
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
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

// v3.1 Git Worktree 架构服务（单仓库 + 多分支 + Worktree 并行）
builder.Services.AddSingleton<GitWorktreeService>();
builder.Services.AddSingleton<IWorktreeMetadataServiceFactory, WorktreeMetadataServiceFactory>();  // ✅ 工厂模式
builder.Services.AddSingleton<StrategyService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<ProjectContext>();  // 单项目模式上下文
builder.Services.AddSingleton<ModuleLibraryService>();  // 模块库服务
builder.Services.AddSingleton<ProjectSnapshotService>();
builder.Services.AddSingleton<BackgroundScreenshotService>();

// v3.3 多窗口并行架构服务
builder.Services.AddSingleton<BranchLockManager>();  // 分支锁管理（多窗口互斥）
builder.Services.AddSingleton<MergeService>();       // 可视化合并服务

// v3.4 可视化 Diff 服务
builder.Services.AddSingleton<SchemeDataService>();  // 跨分支/Worktree 模块数据读写

// 首页项目管理服务
builder.Services.AddSingleton<RecentProjectsService>();

// v3.2 实时通信服务（使用 Newtonsoft.Json 避免 JsonElement 序列化问题）
builder.Services.AddSignalR()
    .AddNewtonsoftJsonProtocol(options =>
    {
        options.PayloadSerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
        options.PayloadSerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
    });
builder.Services.AddHostedService<ProjectWatcherService>();

// 配置 Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 配置 CORS - 允许 Web 前端跨域访问
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowWebClient", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000") // Vite 默认端口
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// 配置中间件
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 启用 CORS
app.UseCors("AllowWebClient");

// 启用控制器路由
app.MapControllers();

// SignalR Hub 路由
app.MapHub<CanvasHub>("/hubs/canvas");

// 健康检查端点
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

// config 已在 DI 注册阶段加载（line 85 附近）
WriteWithColoredPrefix("[Server]", "BIMCanvas.Server 启动中...", ConsoleColor.White);
WriteWithColoredPrefix("[Server]", "Swagger: http://localhost:5000/swagger", ConsoleColor.White);

// 空启动模式：不自动加载项目，等待用户通过首页选择
WriteWithColoredPrefix("[Server]", "空启动模式，等待用户选择项目", ConsoleColor.White);

// ─── 环境检测阶段 ───
var agentReady = true;
var liteLlmReady = !config.LiteLlm.Enabled;
{
    var baseDir = AppContext.BaseDirectory;
    var agentDir = FindAgentProjectPath(baseDir);

    WriteWithColoredPrefix("[Server]", "环境检测中...", ConsoleColor.White);

    if (!Directory.Exists(agentDir))
    {
        WriteWithColoredPrefix("[Server:WARN]", $"Agent 项目目录不存在: {agentDir}", ConsoleColor.DarkYellow);
        agentReady = false;
    }
    else if (!IsPythonAvailable(pythonCommand))
    {
        WriteWithColoredPrefix("[Server:WARN]", "未检测到 Python，Agent 服务将不启动", ConsoleColor.DarkYellow);
        WriteWithColoredPrefix("[Server:WARN]", "提示: 请安装 Python 3.10+ 并添加到 PATH", ConsoleColor.DarkYellow);
        agentReady = false;
    }
    else if (!IsAgentDependencyReady(pythonCommand))
    {
        agentReady = TryInstallAgentDependencies(agentDir, pythonCommand);
    }

    if (config.LiteLlm.Enabled && IsPythonAvailable(pythonCommand))
    {
        if (!File.Exists(liteLlmSourceConfigPath))
        {
            WriteWithColoredPrefix("[Server:WARN]", $"LiteLLM 配置模板缺失: {liteLlmSourceConfigPath}", ConsoleColor.DarkYellow);
        }
        else
        {
            try
            {
                liteLlmRuntimeArtifacts = LiteLlmRuntimeConfigBuilder.Generate(config, liteLlmSourceConfigPath, configDir);
                liteLlmConfigPath = liteLlmRuntimeArtifacts.RuntimeLiteLlmConfigPath;
                liteLlmConfigReady = true;
            }
            catch (Exception ex)
            {
                WriteWithColoredPrefix("[Server:WARN]", $"LiteLLM 运行时配置生成失败: {ex.Message}", ConsoleColor.DarkYellow);
                liteLlmReady = false;
                liteLlmConfigReady = false;
            }
        }

        if (liteLlmConfigReady)
        {
            var liteLlmDependencyMissing =
                !IsLiteLlmReady(pythonCommand) ||
                (LiteLlmConfigUsesGemini(liteLlmSourceConfigPath) && !IsPythonModuleAvailable(pythonCommand, "google.genai"));

            if (liteLlmDependencyMissing)
            {
                liteLlmReady = TryInstallLiteLlmDependencies(pythonCommand);
            }
            else
            {
                liteLlmReady = true;
            }
        }
    }

    // 检测 Playwright Chromium（Server 自身依赖，不影响启动）
    if (!IsPlaywrightChromiumInstalled())
    {
        TryInstallPlaywrightChromium();
    }

    // 未来扩展点：
    // if (!IsNodeAvailable()) { webReady = false; }
}

// ─── 自动启动 Agent 和 Web 服务 ───
{
    var baseDir = AppContext.BaseDirectory;
    var agentProjectPath = FindAgentProjectPath(baseDir);
    var providerAdapterProjectPath = FindProviderAdapterProjectPath(baseDir);
    var webProjectPath = FindWebProjectPath(baseDir);
    Process? providerAdapterProcess = null;
    var providerAdapterRequired = liteLlmRuntimeArtifacts?.RequiresAdapter == true;
    var providerAdapterRuntimeReady = !providerAdapterRequired;
    Process? liteLlmProcess = null;
    var liteLlmRuntimeReady = !config.LiteLlm.Enabled || !config.LiteLlm.AutoStart;
    Process? agentProcess = null;
    Process? webProcess = null;

    // 1. 启动 ProviderAdapter（如果 LiteLLM 运行时配置需要）
    if (config.LiteLlm.Enabled && providerAdapterRequired)
    {
        if (!config.ProviderAdapter.Enabled)
        {
            WriteWithColoredPrefix("[Server:WARN]", "当前 LiteLLM provider 需要 ProviderAdapter，但 providerAdapter.enabled=false", ConsoleColor.DarkYellow);
        }
        else if (config.ProviderAdapter.AutoStart)
        {
            if (!Directory.Exists(providerAdapterProjectPath))
            {
                WriteWithColoredPrefix("[Server:WARN]", $"ProviderAdapter 项目目录不存在: {providerAdapterProjectPath}", ConsoleColor.DarkYellow);
            }
            else
            {
                if (IsPortOccupied(config.ProviderAdapter.Port, out var occupyingPid))
                {
                    if (IsProviderAdapterProcess(occupyingPid))
                    {
                        WriteWithColoredPrefix("[Server]", $"清理残留 ProviderAdapter 进程 (PID: {occupyingPid})...", ConsoleColor.White);
                        KillProcess(occupyingPid);
                        Thread.Sleep(500);
                    }
                    else
                    {
                        WriteWithColoredPrefix("[Server:WARN]", $"ProviderAdapter 端口 {config.ProviderAdapter.Port} 被其他进程占用 (PID: {occupyingPid})", ConsoleColor.DarkYellow);
                        WriteWithColoredPrefix("[Server:WARN]", "ProviderAdapter 将尝试继续启动，若失败请手动处理端口占用", ConsoleColor.DarkYellow);
                    }
                }

                if (liteLlmRuntimeArtifacts != null)
                {
                    WriteWithColoredPrefix("[Server]", $"生成 LiteLLM 运行时配置: {liteLlmRuntimeArtifacts.RuntimeLiteLlmConfigPath}", ConsoleColor.White);
                    WriteWithColoredPrefix("[Server]", $"生成 ProviderAdapter 运行时配置: {liteLlmRuntimeArtifacts.RuntimeAdapterConfigPath}", ConsoleColor.White);
                }

                WriteWithColoredPrefix("[Server]", "ProviderAdapter 服务启动中...", ConsoleColor.White);
                providerAdapterProcess = StartProviderAdapterProcess(config, providerAdapterProjectPath, liteLlmRuntimeArtifacts!.RuntimeAdapterConfigPath);
                if (providerAdapterProcess != null)
                {
                    providerAdapterRuntimeReady = await WaitForHttpServiceReadyAsync(
                        BuildProviderAdapterHealthUrl(config),
                        providerAdapterProcess);

                    if (!providerAdapterRuntimeReady)
                    {
                        if (providerAdapterProcess.HasExited)
                        {
                            WriteWithColoredPrefix("[Server:WARN]", "ProviderAdapter 进程启动后立即退出，LiteLLM 下游适配将不可用", ConsoleColor.DarkYellow);
                            providerAdapterProcess = null;
                        }
                        else
                        {
                            WriteWithColoredPrefix("[Server:WARN]", "ProviderAdapter 进程已启动，但在等待窗口内未就绪", ConsoleColor.DarkYellow);
                        }
                    }
                }
            }
        }
        else
        {
            WriteWithColoredPrefix("[Server]", "ProviderAdapter 自动启动已禁用，默认依赖外部手工启动", ConsoleColor.DarkYellow);
            providerAdapterRuntimeReady = await WaitForHttpServiceReadyAsync(BuildProviderAdapterHealthUrl(config));
            if (!providerAdapterRuntimeReady)
            {
                WriteWithColoredPrefix("[Server:WARN]", "ProviderAdapter 未就绪，LiteLLM 自动启动将被跳过", ConsoleColor.DarkYellow);
            }
        }
    }

    // 2. 启动 LiteLLM（如果启用）
    if (config.LiteLlm.Enabled)
    {
        if (config.LiteLlm.AutoStart && liteLlmReady && (!providerAdapterRequired || providerAdapterRuntimeReady))
        {
            if (IsPortOccupied(config.LiteLlm.Port, out var occupyingPid))
            {
                if (IsLiteLlmProcess(occupyingPid))
                {
                    WriteWithColoredPrefix("[Server]", $"清理残留 LiteLLM 进程 (PID: {occupyingPid})...", ConsoleColor.White);
                    KillProcess(occupyingPid);
                    Thread.Sleep(500);
                }
                else
                {
                    WriteWithColoredPrefix("[Server:WARN]", $"LiteLLM 端口 {config.LiteLlm.Port} 被其他进程占用 (PID: {occupyingPid})", ConsoleColor.DarkYellow);
                    WriteWithColoredPrefix("[Server:WARN]", "LiteLLM 将尝试继续启动，若失败请手动处理端口占用", ConsoleColor.DarkYellow);
                }
            }

            WriteWithColoredPrefix("[Server]", "LiteLLM 服务启动中...", ConsoleColor.White);
            liteLlmProcess = StartLiteLlmProcess(config, liteLlmConfigPath, pythonCommand);
            if (liteLlmProcess != null)
            {
                liteLlmRuntimeReady = await WaitForLiteLlmServiceReadyAsync(liteLlmProcess, config);
                if (!liteLlmRuntimeReady)
                {
                    if (liteLlmProcess.HasExited)
                    {
                        WriteWithColoredPrefix("[Server:WARN]", "LiteLLM 进程启动后立即退出，AI 请求后续将失败", ConsoleColor.DarkYellow);
                        liteLlmProcess = null;
                    }
                    else
                    {
                        WriteWithColoredPrefix("[Server:WARN]", "LiteLLM 进程已启动，但在等待窗口内未就绪；AI 功能可能暂不可用", ConsoleColor.DarkYellow);
                    }
                }
            }
        }
        else if (!config.LiteLlm.AutoStart)
        {
            WriteWithColoredPrefix("[Server]", "LiteLLM 自动启动已禁用，默认依赖外部手工启动", ConsoleColor.DarkYellow);
        }
        else if (providerAdapterRequired && !providerAdapterRuntimeReady)
        {
            WriteWithColoredPrefix("[Server:WARN]", "ProviderAdapter 未就绪，跳过 LiteLLM 自动启动", ConsoleColor.DarkYellow);
        }
        else
        {
            WriteWithColoredPrefix("[Server:WARN]", "LiteLLM 未就绪，跳过自动启动", ConsoleColor.DarkYellow);
        }
    }

    if (config.LiteLlm.Enabled && (!config.LiteLlm.AutoStart || liteLlmProcess == null))
    {
        WriteWithColoredPrefix("[Server:WARN]", "Agent 仍将指向 LiteLLM 网关，若网关不可用，后续 AI 请求会明确失败", ConsoleColor.DarkYellow);
    }

    // 2.5 启动 CCR（如果启用，替代 LiteLLM 用于 Gemini 供应商）
    Process? ccrProcess = null;
    bool ccrRuntimeReady = false;
    if (config.Ccr.Enabled && config.Ccr.AutoStart)
    {
        // CCR 配置文件由 EnsureDefaultConfigs() 从 Templates 复制到 configDir
        var ccrConfigPath = Path.Combine(configDir, config.Ccr.ConfigFileName);

        if (IsPortOccupied(config.Ccr.Port, out var ccrOccupyingPid))
        {
            WriteWithColoredPrefix("[Server]", $"清理残留 CCR 进程 (PID: {ccrOccupyingPid})...", ConsoleColor.White);
            KillProcess(ccrOccupyingPid);
            Thread.Sleep(500);
        }

        WriteWithColoredPrefix("[Server]", "CCR 服务启动中...", ConsoleColor.White);
        ccrProcess = StartCcrProcess(config, ccrConfigPath);
        if (ccrProcess != null)
        {
            ccrRuntimeReady = await WaitForServiceReadyAsync(config.Ccr.Host, config.Ccr.Port, timeoutMs: 15000);
            if (ccrRuntimeReady)
                WriteWithColoredPrefix("[CCR]", $"CCR 已就绪: http://{config.Ccr.Host}:{config.Ccr.Port}", ConsoleColor.Magenta);
            else
                WriteWithColoredPrefix("[Server:WARN]", "CCR 未在预期时间内就绪", ConsoleColor.DarkYellow);
        }
    }

    // 3. 启动 Agent 服务（不等待，后台运行）
    if (agentReady)
    {
        // 读取 Agent 端口配置
        var agentPort = config.Server.Port;

        // 检测并清理端口占用
        if (IsPortOccupied(agentPort, out var occupyingPid))
        {
            // 验证进程身份
            if (IsBIMCanvasAgentProcess(occupyingPid, agentProjectPath))
            {
                WriteWithColoredPrefix("[Server]", $"清理残留 Agent 进程 (PID: {occupyingPid})...", ConsoleColor.White);
                KillProcess(occupyingPid);
                Thread.Sleep(500); // 等待端口释放
            }
            else
            {
                WriteWithColoredPrefix("[Server:WARN]", $"端口 {agentPort} 被其他进程占用 (PID: {occupyingPid})", ConsoleColor.DarkYellow);
                WriteWithColoredPrefix("[Server:WARN]", $"提示: 可使用 'tasklist /FI \"PID eq {occupyingPid}\"' 查看进程详情", ConsoleColor.DarkYellow);
                // 继续尝试启动（可能端口已释放或验证失败）
            }
        }

        WriteWithColoredPrefix("[Server]", "Agent 服务启动中...", ConsoleColor.White);
        try
        {
            agentProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonCommand,
                    Arguments = "-m src.main --serve",
                    WorkingDirectory = agentProjectPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            // 设置环境变量确保 Python 输出 UTF-8
            agentProcess.StartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            if (config.LiteLlm.Enabled)
            {
                var activeProvider = NormalizeProviderName(config.LiteLlm.ActiveProvider);
                // LiteLLM 模式下，默认模型真源来自 server_config.json > liteLlm.defaultModelFamily。
                var defaultModelFamily = NormalizeModelFamily(config.LiteLlm.DefaultModelFamily);

                // 判断当前供应商是否走 CCR（Gemini 供应商 + CCR 已启用并就绪）
                bool useCcr = config.Ccr.Enabled && ccrRuntimeReady
                              && activeProvider.StartsWith("gemini_");

                if (useCcr)
                {
                    // CCR 模式：指向 CCR 端口，使用 CCR 格式的模型名（provider,model）
                    var ccrGatewayUrl = $"http://{config.Ccr.Host}:{config.Ccr.Port}";
                    agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-ccr";
                    agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = ccrGatewayUrl;
                    agentProcess.StartInfo.Environment["MODEL_NAME"] = defaultModelFamily;

                    var ccrProvider = activeProvider.Replace("_", "-"); // gemini_yescode → gemini-yescode
                    var opusModel = "gemini-3.1-pro-preview";
                    var defaultModel = "gemini-3-flash-preview";

                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_OPUS_MODEL"] = $"{ccrProvider},{opusModel}";
                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_SONNET_MODEL"] = $"{ccrProvider},{defaultModel}";
                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = $"{ccrProvider},{defaultModel}";
                    agentProcess.StartInfo.Environment["CLAUDE_CODE_SUBAGENT_MODEL"] = $"{ccrProvider},{defaultModel}";

                    WriteWithColoredPrefix("[Server]", $"Agent 网关: CCR ({ccrGatewayUrl}), 模型: {ccrProvider},{defaultModel}", ConsoleColor.White);
                }
                else
                {
                    // LiteLLM 模式（保持不变）
                    var gatewayUrl = $"http://{config.LiteLlm.Host}:{config.LiteLlm.Port}";
                    if (!agentProcess.StartInfo.Environment.TryGetValue("AGENT_SDK_API_KEY", out var existingAgentSdkApiKey)
                        || string.IsNullOrWhiteSpace(existingAgentSdkApiKey))
                    {
                        agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-local-gateway";
                    }
                    agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = gatewayUrl;
                    agentProcess.StartInfo.Environment["MODEL_NAME"] = defaultModelFamily;
                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_OPUS_MODEL"] = $"bc-{activeProvider}-opus";
                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_SONNET_MODEL"] = $"bc-{activeProvider}-sonnet";
                    agentProcess.StartInfo.Environment["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = $"bc-{activeProvider}-haiku";
                    agentProcess.StartInfo.Environment["CLAUDE_CODE_SUBAGENT_MODEL"] = $"bc-{activeProvider}-subagent";
                }
            }
            else
            {
                agentProcess.StartInfo.Environment.Remove("AGENT_SDK_BASE_URL");
                agentProcess.StartInfo.Environment.Remove("AGENT_SDK_API_KEY");
                agentProcess.StartInfo.Environment.Remove("MODEL_NAME");
                agentProcess.StartInfo.Environment.Remove("ANTHROPIC_DEFAULT_OPUS_MODEL");
                agentProcess.StartInfo.Environment.Remove("ANTHROPIC_DEFAULT_SONNET_MODEL");
                agentProcess.StartInfo.Environment.Remove("ANTHROPIC_DEFAULT_HAIKU_MODEL");
                agentProcess.StartInfo.Environment.Remove("CLAUDE_CODE_SUBAGENT_MODEL");
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
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Agent 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
        }
    }

    // 4. 启动 Web 服务（不等待，后台运行）
    if (Directory.Exists(webProjectPath))
    {
        WriteWithColoredPrefix("[Server]", "Web 开发服务器启动中...", ConsoleColor.White);
        webProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c npm run dev",
                WorkingDirectory = webProjectPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        webProcess.Start();

        // 后台读取 Web 输出（避免缓冲区阻塞）
        _ = Task.Run(async () =>
        {
            while (!webProcess.HasExited)
            {
                var line = await webProcess.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    // 过滤 Vite 冗余输出（多网卡地址、help 提示）
                    if (line.Contains("Network:") || line.Contains("press h + enter"))
                        continue;
                    WriteWithColoredPrefix("[Web]", line, ConsoleColor.Green);
                }
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
    else
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Web 项目目录不存在: {webProjectPath}", ConsoleColor.DarkGray);
    }

    // 4. 等待 Web 服务就绪后打开浏览器（Agent 在后台继续启动，Web 端通过 health 检查感知状态）
    if (webProcess != null)
    {
        WriteWithColoredPrefix("[Server]", "等待 Web 服务启动...", ConsoleColor.White);
        var webBaseUrl = "http://localhost:5173";
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        for (int i = 0; i < 50; i++)
        {
            try
            {
                var response = await httpClient.GetAsync(webBaseUrl);
                if (response.IsSuccessStatusCode)
                {
                    if (agentReady && agentProcess == null)
                    {
                        WriteWithColoredPrefix("[Server:WARN]", "Web 已就绪，但 Agent 未启动；Agent 功能暂不可用", ConsoleColor.DarkYellow);
                    }
                    else if (config.LiteLlm.Enabled && config.LiteLlm.AutoStart && !liteLlmRuntimeReady)
                    {
                        WriteWithColoredPrefix("[Server:WARN]", "Web 已就绪，但 LiteLLM 未就绪；AI 功能暂不可用", ConsoleColor.DarkYellow);
                    }
                    else
                    {
                        WriteWithColoredPrefix("[Server]", "所有服务已就绪", ConsoleColor.White);
                    }
                    break;
                }
            }
            catch { /* 服务未就绪，继续等待 */ }
            await Task.Delay(200);
        }

        // 打开浏览器（根据配置）
        if (config.Startup.OpenBrowser)
        {
            WriteWithColoredPrefix("[Server]", $"打开浏览器: {webBaseUrl}", ConsoleColor.White);
            try
            {
                if (!string.IsNullOrEmpty(config.Startup.BrowserPath))
                {
                    // 使用指定浏览器
                    Process.Start(config.Startup.BrowserPath, webBaseUrl);
                }
                else
                {
                    // 使用系统默认浏览器
                    Process.Start(new ProcessStartInfo(webBaseUrl) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                WriteWithColoredPrefix("[Server:ERR]", $"无法自动打开浏览器: {ex.Message}", ConsoleColor.DarkGray);
            }
        }
    }

    // 5. 注册退出时清理进程和 Worktree
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
            var projectContext = app.Services.GetRequiredService<ProjectContext>();
            if (!string.IsNullOrEmpty(projectContext.CurrentProjectPath))
            {
                WriteWithColoredPrefix("[Server]", "正在清理 Worktree...", ConsoleColor.White);
                var gitWorktreeService = app.Services.GetRequiredService<GitWorktreeService>();
                gitWorktreeService.CleanupAllWorktrees(projectContext.CurrentProjectPath);
            }
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Worktree 清理失败: {ex.Message}", ConsoleColor.DarkGray);
        }

        // ③ 关闭 LiteLLM
        if (liteLlmProcess != null && !liteLlmProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 LiteLLM 服务...", ConsoleColor.White);
            liteLlmProcess.Kill(true);
        }

        // ③.5 关闭 CCR
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

app.Run();

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

// 辅助函数：检测 LiteLLM 是否可用
static bool IsLiteLlmReady(string pythonCommand)
{
    try
    {
        var locatorCommand = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where.exe" : "which";
        var psi = new ProcessStartInfo
        {
            FileName = locatorCommand,
            Arguments = GetLiteLlmCommand(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;
        if (!process.WaitForExit(5000))
            return false;
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：检测 Python 模块是否可用
static bool IsPythonModuleAvailable(string pythonCommand, string moduleName)
{
    try
    {
        var escapedModuleName = moduleName.Replace("'", "\\'");
        var psi = new ProcessStartInfo
        {
            FileName = pythonCommand,
            Arguments = $"-c \"import importlib.util, sys; sys.exit(0 if importlib.util.find_spec('{escapedModuleName}') else 1)\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return false;
        if (!process.WaitForExit(5000))
            return false;
        return process.ExitCode == 0;
    }
    catch
    {
        return false;
    }
}

// 辅助函数：检测 LiteLLM 配置是否使用 Gemini provider
static bool LiteLlmConfigUsesGemini(string configPath)
{
    try
    {
        if (!File.Exists(configPath))
            return false;

        var content = File.ReadAllText(configPath);
        return content.Contains("model: gemini/", StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        return false;
    }
}

// 辅助函数：交互式安装 LiteLLM 依赖（返回 true = 安装成功）
static bool TryInstallLiteLlmDependencies(string pythonCommand)
{
    WriteWithColoredPrefix("[Server]", "检测到 LiteLLM 依赖缺失", ConsoleColor.DarkYellow);
    Console.Write($"[{DateTime.Now:HH:mm:ss}] ");
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.Write("[Server]");
    Console.ResetColor();
    Console.Write(" 是否自动安装 LiteLLM 依赖？(Y/n): ");
    var input = Console.ReadLine()?.Trim().ToLower();

    if (!string.IsNullOrEmpty(input) && input != "y" && input != "yes")
    {
        WriteWithColoredPrefix("[Server]", "跳过安装，LiteLLM 将不可用", ConsoleColor.DarkYellow);
        return false;
    }

    WriteWithColoredPrefix("[Server]", "正在安装 LiteLLM 依赖 (pip install \"litellm[proxy]\" google-genai)...", ConsoleColor.White);

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = pythonCommand,
            Arguments = "-m pip install \"litellm[proxy]\" google-genai",
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
            WriteWithColoredPrefix("[Server:ERR]", "无法启动 LiteLLM pip 进程", ConsoleColor.DarkGray);
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

        var completed = process.WaitForExit(600_000);
        if (!completed)
        {
            WriteWithColoredPrefix("[Server:ERR]", "LiteLLM 依赖安装超时（10分钟）", ConsoleColor.DarkGray);
            cts.Cancel();
            process.Kill(true);
            return false;
        }

        if (process.ExitCode == 0)
        {
            WriteWithColoredPrefix("[Server]", "LiteLLM 依赖安装成功", ConsoleColor.White);
            return true;
        }

        WriteWithColoredPrefix("[Server:ERR]", $"LiteLLM 依赖安装失败 (exit code: {process.ExitCode})", ConsoleColor.DarkGray);
        return false;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"LiteLLM 依赖安装异常: {ex.Message}", ConsoleColor.DarkGray);
        return false;
    }
}

// 辅助函数：构建 ProviderAdapter 健康检查地址
static string BuildProviderAdapterHealthUrl(ServerConfig config)
{
    return $"http://{config.ProviderAdapter.Host}:{config.ProviderAdapter.Port}/health";
}

// 辅助函数：启动 ProviderAdapter 进程
static Process? StartProviderAdapterProcess(ServerConfig config, string projectPath, string runtimeConfigPath)
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments =
                $"run -v quiet --no-launch-profile --project \"{Path.Combine(projectPath, "BIMCanvas.ProviderAdapter.csproj")}\" -- --config \"{runtimeConfigPath}\" --host {config.ProviderAdapter.Host} --port {config.ProviderAdapter.Port}",
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        var process = new Process { StartInfo = startInfo };
        process.Start();

        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                    WriteWithColoredPrefix("[ProviderAdapter]", line, ConsoleColor.Magenta);
            }
        });
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                    WriteWithColoredPrefix("[ProviderAdapter:ERR]", line, ConsoleColor.DarkMagenta);
            }
        });

        return process;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"ProviderAdapter 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
        return null;
    }
}

// 辅助函数：等待 HTTP 服务健康检查通过
static async Task<bool> WaitForHttpServiceReadyAsync(string healthUrl, Process? process = null, int timeoutMs = 20000)
{
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (process?.HasExited == true)
            return false;

        try
        {
            using var response = await httpClient.GetAsync(healthUrl);
            if (response.IsSuccessStatusCode)
                return true;
        }
        catch
        {
            // 服务尚未就绪，继续等待
        }

        await Task.Delay(300);
    }

    return false;
}

// 辅助函数：启动 LiteLLM 进程
static Process? StartLiteLlmProcess(ServerConfig config, string configPath, string pythonCommand)
{
    try
    {
        var startInfo = BuildLiteLlmStartInfo(config, configPath, pythonCommand);
        var process = new Process { StartInfo = startInfo };
        var stderrFilter = new LiteLlmStderrFilter(message =>
            WriteWithColoredPrefix("[LiteLLM]", message, ConsoleColor.Yellow));
        process.Start();

        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null)
                    break;

                if (!string.IsNullOrWhiteSpace(line))
                    WriteWithColoredPrefix("[LiteLLM]", line, ConsoleColor.Yellow);
            }
        });
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardError.ReadLineAsync();
                if (line == null)
                    break;

                stderrFilter.ProcessLine(line);
            }

            stderrFilter.Flush();
        });

        return process;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:ERR]", $"LiteLLM 服务启动失败: {ex.Message}", ConsoleColor.DarkGray);
        return null;
    }
}

// 辅助函数：等待 LiteLLM 端口就绪
static async Task<bool> WaitForLiteLlmServiceReadyAsync(Process process, ServerConfig config, int timeoutMs = 20000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
        if (process.HasExited)
            return false;

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(config.LiteLlm.Host, config.LiteLlm.Port);
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

// 辅助函数：构建 LiteLLM 启动参数
static ProcessStartInfo BuildLiteLlmStartInfo(ServerConfig config, string configPath, string pythonCommand)
{
    var psi = new ProcessStartInfo
    {
        FileName = GetLiteLlmCommand(),
        Arguments = $"--config \"{configPath}\" --host {config.LiteLlm.Host} --port {config.LiteLlm.Port}",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };
    psi.Environment["PYTHONIOENCODING"] = "utf-8";
    psi.Environment["DEBUG"] = "false";
    return psi;
}

// 辅助函数：获取 LiteLLM CLI 命令
static string GetLiteLlmCommand()
{
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "litellm.exe" : "litellm";
}

// 辅助函数：验证进程是否为 LiteLLM
// 辅助函数：启动 CCR 子进程
static Process? StartCcrProcess(ServerConfig config, string configPath)
{
    try
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ccr",
                Arguments = "start",
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
        _ = Task.Run(async () =>
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync();
                if (line == null) break;
                if (!string.IsNullOrWhiteSpace(line))
                    WriteWithColoredPrefix("[CCR]", line, ConsoleColor.Magenta);
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
static async Task<bool> WaitForServiceReadyAsync(string host, int port, int timeoutMs = 15000)
{
    var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (DateTime.UtcNow < deadline)
    {
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

static bool IsLiteLlmProcess(int pid)
{
    try
    {
        var process = Process.GetProcessById(pid);
        var processName = process.ProcessName.ToLowerInvariant();
        if (!processName.Contains("python"))
            return false;

        string cmdLine;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cmdLine = GetCommandLineWindows(pid);
        }
        else
        {
            cmdLine = GetCommandLineUnix(pid);
        }

        return cmdLine.Contains("-m litellm", StringComparison.OrdinalIgnoreCase)
            || cmdLine.Contains("litellm", StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"LiteLLM 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return false;
    }
}

// 辅助函数：验证进程是否为 ProviderAdapter
static bool IsProviderAdapterProcess(int pid)
{
    try
    {
        var process = Process.GetProcessById(pid);
        var processName = process.ProcessName.ToLowerInvariant();
        if (!processName.Contains("dotnet") && !processName.Contains("provideradapter"))
            return false;

        string cmdLine;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cmdLine = GetCommandLineWindows(pid);
        }
        else
        {
            cmdLine = GetCommandLineUnix(pid);
        }

        return cmdLine.Contains("BIMCanvas.ProviderAdapter", StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"ProviderAdapter 进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return false;
    }
}

// 辅助函数：归一化 provider 名称
static string NormalizeProviderName(string? provider)
{
    if (string.IsNullOrWhiteSpace(provider))
        return "anthropic";

    return provider.Trim().ToLowerInvariant();
}

// 辅助函数：归一化模型家族名称
static string NormalizeModelFamily(string? modelFamily)
{
    var normalized = string.IsNullOrWhiteSpace(modelFamily)
        ? "sonnet"
        : modelFamily.Trim().ToLowerInvariant();

    return normalized switch
    {
        "opus" => "opus",
        "haiku" => "haiku",
        _ => "sonnet"
    };
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

// 辅助函数：向上查找 BIMCanvas.ProviderAdapter 目录
static string FindProviderAdapterProjectPath(string startDir)
{
    var dir = new DirectoryInfo(startDir);

    for (int i = 0; i < 5 && dir != null; i++)
    {
        var adapterPath = Path.Combine(dir.FullName, "BIMCanvas.ProviderAdapter");
        if (Directory.Exists(adapterPath))
        {
            return adapterPath;
        }
        dir = dir.Parent;
    }

    return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "BIMCanvas.ProviderAdapter"));
}

// 辅助函数：检测端口是否被占用（跨平台）
static bool IsPortOccupied(int port, out int pid)
{
    pid = 0;

    try
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: netstat -ano | findstr :端口
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c netstat -ano | findstr :{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // 解析输出（示例）：TCP    127.0.0.1:8765    0.0.0.0:0    LISTENING    12345
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && parts[3] == "LISTENING")
                {
                    if (int.TryParse(parts[4], out pid))
                        return true;
                }
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Linux/macOS: lsof -ti:端口
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"lsof -ti:{port}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output) && int.TryParse(output, out pid))
                return true;
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"端口检测失败: {ex.Message}，跳过检查", ConsoleColor.DarkYellow);
    }

    return false;
}

// 辅助函数：验证进程是否为 BIMCanvas Agent（通过进程名 + 命令行参数）
static bool IsBIMCanvasAgentProcess(int pid, string agentProjectPath)
{
    try
    {
        var process = Process.GetProcessById(pid);

        // 检查 1: 进程名必须是 python/python.exe
        var processName = process.ProcessName.ToLower();
        if (!processName.Contains("python"))
            return false;

        // 检查 2: 命令行参数包含 BIMCanvas.Agent 或 src.main
        string cmdLine;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            cmdLine = GetCommandLineWindows(pid);
        }
        else
        {
            cmdLine = GetCommandLineUnix(pid);
        }

        return cmdLine.Contains("BIMCanvas.Agent") || cmdLine.Contains("src.main");
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"进程验证失败: {ex.Message}", ConsoleColor.DarkYellow);
        return false; // 无法确认时，保守拒绝
    }
}

// Windows WMIC 查询命令行（使用命令行工具而非 WMI API）
static string GetCommandLineWindows(int pid)
{
    if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return "";

    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "wmic",
            Arguments = $"process where \"ProcessId={pid}\" get CommandLine /format:list",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null) return "";

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        // 解析输出：CommandLine=python -m src.main --serve
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("CommandLine="))
            {
                return trimmed.Substring("CommandLine=".Length);
            }
        }
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"WMIC 查询失败: {ex.Message}，仅验证进程名", ConsoleColor.DarkYellow);
    }
    return "";
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

file sealed class LiteLlmStderrFilter
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromSeconds(30);
    private static readonly Regex JsonErrorRegex = new(
        "\"code\"\\s*:\\s*(?<code>\\d+)\\s*,\\s*\"message\"\\s*:\\s*\"(?<message>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ApiErrorRegex = new(
        "API error:\\s*(?<code>\\d{3})\\s*-\\s*(?<message>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HttpStatusRegex = new(
        "HTTPStatusError:\\s*(?:Client|Server) error '\\s*(?<code>\\d{3})\\s+(?<message>[^']+)'",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RequestAccessLogRegex = new(
        "\"(?:POST|GET)\\s+/v1/messages(?:/count_tokens)?[^\\\"]*\\s+HTTP/1\\.1\"\\s+\\d{3}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Action<string> _writeMessage;
    private readonly Dictionary<string, DateTime> _lastEmittedAtUtc = new(StringComparer.Ordinal);

    private PendingSummary? _pendingSummary;
    private bool _inErrorBlock;

    public LiteLlmStderrFilter(Action<string> writeMessage)
    {
        _writeMessage = writeMessage;
    }

    public void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var trimmed = line.Trim();

        if (IsRequestAccessLog(trimmed))
        {
            FinalizePendingBlock();
            return;
        }

        if (IsTopLevelLiteLlmError(trimmed))
        {
            FinalizePendingBlock();
            _inErrorBlock = true;

            if (TryExtractSummary(trimmed, out var topLevelSummary))
                UpdatePendingSummary(topLevelSummary);

            return;
        }

        if (TryExtractSummary(trimmed, out var summary))
        {
            _inErrorBlock = true;
            UpdatePendingSummary(summary);
            return;
        }

        if (IsErrorMarker(trimmed))
        {
            _inErrorBlock = true;
        }
    }

    public void Flush()
    {
        FinalizePendingBlock();
    }

    private void FinalizePendingBlock()
    {
        if (!_inErrorBlock)
            return;

        if (_pendingSummary is { } pendingSummary)
        {
            EmitDeduped($"下游供应商错误: {pendingSummary.Code} {pendingSummary.Message}");
        }
        else
        {
            EmitDeduped("LiteLLM 请求失败，未提取到下游错误摘要");
        }

        _pendingSummary = null;
        _inErrorBlock = false;
    }

    private void UpdatePendingSummary(PendingSummary candidate)
    {
        if (_pendingSummary == null || candidate.Priority > _pendingSummary.Value.Priority)
        {
            _pendingSummary = candidate;
        }
    }

    private void EmitDeduped(string message)
    {
        var now = DateTime.UtcNow;
        TrimExpiredEntries(now);

        if (_lastEmittedAtUtc.TryGetValue(message, out var lastEmittedAtUtc)
            && now - lastEmittedAtUtc < DedupWindow)
        {
            return;
        }

        _lastEmittedAtUtc[message] = now;
        _writeMessage(message);
    }

    private void TrimExpiredEntries(DateTime now)
    {
        if (_lastEmittedAtUtc.Count == 0)
            return;

        var expiredKeys = _lastEmittedAtUtc
            .Where(kvp => now - kvp.Value >= DedupWindow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _lastEmittedAtUtc.Remove(key);
        }
    }

    private static bool TryExtractSummary(string line, out PendingSummary summary)
    {
        if (TryMatchSummary(JsonErrorRegex, line, priority: 3, out summary))
            return true;

        if (TryMatchSummary(ApiErrorRegex, line, priority: 2, out summary))
            return true;

        if (TryMatchSummary(HttpStatusRegex, line, priority: 1, out summary))
            return true;

        summary = default;
        return false;
    }

    private static bool TryMatchSummary(Regex regex, string line, int priority, out PendingSummary summary)
    {
        var match = regex.Match(line);
        if (!match.Success)
        {
            summary = default;
            return false;
        }

        var code = match.Groups["code"].Value.Trim();
        var message = NormalizeWhitespace(match.Groups["message"].Value);
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message))
        {
            summary = default;
            return false;
        }

        summary = new PendingSummary(code, message, priority);
        return true;
    }

    private static bool IsTopLevelLiteLlmError(string line)
    {
        return line.Contains("LiteLLM Proxy:ERROR:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRequestAccessLog(string line)
    {
        return RequestAccessLogRegex.IsMatch(line);
    }

    private static bool IsErrorMarker(string line)
    {
        return line.Contains("Traceback (most recent call last):", StringComparison.OrdinalIgnoreCase)
            || line.Contains("The above exception was the direct cause", StringComparison.OrdinalIgnoreCase)
            || line.Contains("During handling of the above exception", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ServiceUnavailableError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("APIError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("HTTPStatusError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("VertexAIError", StringComparison.OrdinalIgnoreCase)
            || line.Contains("GeminiException", StringComparison.OrdinalIgnoreCase)
            || line.Contains("count_tokens()", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeWhitespace(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim().TrimEnd('.');
    }

    private readonly record struct PendingSummary(string Code, string Message, int Priority);
}
