using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Services;
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
    Console.ForegroundColor = prefixColor;
    Console.Write(prefix);
    Console.ForegroundColor = originalColor;
    Console.WriteLine($" {message}");
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
builder.Services.AddSingleton<ComputedDataService>();

// v3.1 Git Worktree 架构服务（单仓库 + 多分支 + Worktree 并行）
builder.Services.AddSingleton<GitWorktreeService>();
builder.Services.AddSingleton<StrategyService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<ProjectContext>();  // 单项目模式上下文

// v3.2 实时通信服务
builder.Services.AddSignalR();
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

WriteWithColoredPrefix("[Server]", "BIMCanvas.Server 启动中...", ConsoleColor.Cyan);
WriteWithColoredPrefix("[Server]", "API: http://localhost:5000/api/canvas", ConsoleColor.Cyan);
WriteWithColoredPrefix("[Server]", "Swagger: http://localhost:5000/swagger", ConsoleColor.Cyan);

// v3.0 项目加载流程（单项目模式）
{
    var projectService = app.Services.GetRequiredService<ProjectService>();
    var projectContext = app.Services.GetRequiredService<ProjectContext>();
    var baseDir = AppContext.BaseDirectory;

    // Case1: 通过命令行参数指定 .bcp 文件
    // Case2: 默认加载 demo_1.bcp
    string? bcpFilePath = args.Length > 0 ? args[0] : null;

    if (string.IsNullOrEmpty(bcpFilePath))
    {
        bcpFilePath = projectService.FindDemoBcpFile(baseDir, "demo_1");
        if (bcpFilePath != null)
        {
            WriteWithColoredPrefix("[Server]", $"使用默认 BCP 文件: {bcpFilePath}", ConsoleColor.Cyan);
        }
    }
    else
    {
        WriteWithColoredPrefix("[Server]", $"使用指定 BCP 文件: {bcpFilePath}", ConsoleColor.Cyan);
    }

    if (!string.IsNullOrEmpty(bcpFilePath))
    {
        try
        {
            // 检测冲突
            var (hasConflict, existingPath) = projectService.CheckProjectConflict(bcpFilePath);
            string projectPath;

            if (hasConflict)
            {
                // 启动时默认使用已存在的项目（不覆盖）
                WriteWithColoredPrefix("[Server]", $"使用已存在的项目目录: {existingPath}", ConsoleColor.Cyan);
                projectPath = existingPath!;
            }
            else
            {
                projectPath = projectService.LoadProject(bcpFilePath);
            }

            // 设置 ProjectContext
            projectContext.SetProject(projectPath, bcpFilePath);
            WriteWithColoredPrefix("[Server]", $"项目已加载: {projectPath}", ConsoleColor.Cyan);
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"项目加载失败: {ex.Message}", ConsoleColor.DarkCyan);
        }
    }
    else
    {
        WriteWithColoredPrefix("[Server:ERR]", "未找到可加载的 BCP 文件", ConsoleColor.DarkCyan);
    }
}

// 自动启动 Agent 和 Web 服务（并行启动）
{
    var baseDir = AppContext.BaseDirectory;
    var agentProjectPath = FindAgentProjectPath(baseDir);
    var webProjectPath = FindWebProjectPath(baseDir);
    Process? agentProcess = null;
    Process? webProcess = null;

    // 1. 启动 Agent 服务（不等待，后台运行）
    if (Directory.Exists(agentProjectPath))
    {
        WriteWithColoredPrefix("[Server]", $"启动 Agent 服务: {agentProjectPath}", ConsoleColor.Cyan);
        try
        {
            agentProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "python",
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
            agentProcess.Start();

            // 后台读取 Agent 输出（避免缓冲区阻塞）
            _ = Task.Run(async () =>
            {
                while (!agentProcess.HasExited)
                {
                    var line = await agentProcess.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[Agent]", line, ConsoleColor.Yellow);
                }
            });
            _ = Task.Run(async () =>
            {
                while (!agentProcess.HasExited)
                {
                    var line = await agentProcess.StandardError.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                        WriteWithColoredPrefix("[Agent:ERR]", line, ConsoleColor.Red);
                }
            });
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"Agent 服务启动失败: {ex.Message}", ConsoleColor.DarkCyan);
            WriteWithColoredPrefix("[Server:ERR]", "提示: 请确保已安装 Python 并配置到 PATH，且已运行 pip install -e . 安装依赖", ConsoleColor.DarkCyan);
        }
    }
    else
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Agent 项目目录不存在: {agentProjectPath}", ConsoleColor.DarkCyan);
    }

    // 2. 启动 Web 服务（不等待，后台运行）
    if (Directory.Exists(webProjectPath))
    {
        WriteWithColoredPrefix("[Server]", $"启动 Web 开发服务器: {webProjectPath}", ConsoleColor.Cyan);
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
        WriteWithColoredPrefix("[Server:ERR]", $"Web 项目目录不存在: {webProjectPath}", ConsoleColor.DarkCyan);
    }

    // 3. 等待 Web 服务就绪后打开浏览器（Agent 在后台继续启动，Web 端通过 health 检查感知状态）
    if (webProcess != null)
    {
        WriteWithColoredPrefix("[Server]", "等待 Web 服务启动...", ConsoleColor.Cyan);
        var webBaseUrl = "http://localhost:5173";
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        for (int i = 0; i < 50; i++)
        {
            try
            {
                var response = await httpClient.GetAsync(webBaseUrl);
                if (response.IsSuccessStatusCode)
                {
                    WriteWithColoredPrefix("[Server]", "Web 服务已就绪", ConsoleColor.Cyan);
                    break;
                }
            }
            catch { /* 服务未就绪，继续等待 */ }
            await Task.Delay(200);
        }

        // 打开浏览器
        WriteWithColoredPrefix("[Server]", $"打开浏览器: {webBaseUrl}", ConsoleColor.Cyan);
        try
        {
            Process.Start(new ProcessStartInfo(webBaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"无法自动打开浏览器: {ex.Message}", ConsoleColor.DarkCyan);
        }
    }

    // 4. 注册退出时清理进程
    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
    {
        if (agentProcess != null && !agentProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Agent 服务...", ConsoleColor.Cyan);
            agentProcess.Kill(true);
        }
        if (webProcess != null && !webProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Web 开发服务器...", ConsoleColor.Cyan);
            webProcess.Kill(true);
        }
    };
}

app.Run();

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
