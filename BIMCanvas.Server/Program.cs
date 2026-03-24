using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
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
var pythonCommand = string.IsNullOrWhiteSpace(config.Server.PythonCommand)
    ? "python"
    : config.Server.PythonCommand.Trim();
var configDir = ConfigService.GetConfigDir();

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
    var webProjectPath = FindWebProjectPath(baseDir);
    Process? agentProcess = null;
    Process? webProcess = null;

    // 1. 启动 CCR（唯一 API 网关）
    Process? ccrProcess = null;
    bool ccrRuntimeReady = false;
    if (config.Ccr.Enabled && config.Ccr.AutoStart)
    {
        // CCR 配置文件由 EnsureDefaultConfigs() 从 Templates 复制到 configDir
        var ccrConfigPath = Path.Combine(configDir, config.Ccr.ConfigFileName);

        // CCR 默认从 ~/.claude-code-router/config.json 读取配置，需要将我们的配置同步过去
        var ccrHomeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-code-router");
        Directory.CreateDirectory(ccrHomeDir);
        var ccrHomeConfigPath = Path.Combine(ccrHomeDir, "config.json");
        if (File.Exists(ccrConfigPath))
        {
            File.Copy(ccrConfigPath, ccrHomeConfigPath, overwrite: true);
        }

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

    // 2. 启动 Agent 服务（不等待，后台运行）
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

            // API 网关配置：CCR 启用时走网关，禁用时 Agent 直连 config.json 中的供应商
            if (config.Ccr.Enabled)
            {
                var defaultModelFamily = NormalizeModelFamily(config.Ccr.DefaultModelFamily);
                var ccrGatewayUrl = $"http://{config.Ccr.Host}:{config.Ccr.Port}";
                agentProcess.StartInfo.Environment["AGENT_SDK_API_KEY"] = "bimcanvas-ccr";
                agentProcess.StartInfo.Environment["AGENT_SDK_BASE_URL"] = ccrGatewayUrl;
                agentProcess.StartInfo.Environment["MODEL_NAME"] = defaultModelFamily;

                // 模型映射交给 CCR Router，不注入 ANTHROPIC_DEFAULT_*_MODEL
                // 仅设置 SubAgent 模型名，让 CCR 的 background 路由生效
                agentProcess.StartInfo.Environment["CLAUDE_CODE_SUBAGENT_MODEL"] = "claude-haiku";

                WriteWithColoredPrefix("[Server]", $"Agent 网关: CCR ({ccrGatewayUrl}), 默认家族: {defaultModelFamily}", ConsoleColor.White);
            }
            else
            {
                WriteWithColoredPrefix("[Server]", "Agent 网关: 直连模式 (使用 ~/.bimcanvas/config.json)", ConsoleColor.White);
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

    // 3. 启动 Web 服务（不等待，后台运行）
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
