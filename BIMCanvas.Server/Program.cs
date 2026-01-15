using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BIMCanvas.Server.Hubs;
using BIMCanvas.Server.Logging;
using BIMCanvas.Server.Services;
using BIMCanvas.Server.Services.Git;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;

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

// v3.1 Git Worktree 架构服务（单仓库 + 多分支 + Worktree 并行）
builder.Services.AddSingleton<GitWorktreeService>();
builder.Services.AddSingleton<StrategyService>();
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<ProjectContext>();  // 单项目模式上下文
builder.Services.AddSingleton<ModuleLibraryService>();  // 模块库服务

// v3.3 多窗口并行架构服务
builder.Services.AddSingleton<BranchLockManager>();  // 分支锁管理（多窗口互斥）
builder.Services.AddSingleton<MergeService>();       // 可视化合并服务

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

WriteWithColoredPrefix("[Server]", "BIMCanvas.Server 启动中...", ConsoleColor.White);
WriteWithColoredPrefix("[Server]", "Swagger: http://localhost:5000/swagger", ConsoleColor.White);

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
                projectPath = existingPath!;
            }
            else
            {
                projectPath = projectService.LoadProject(bcpFilePath);
            }

            // 设置 ProjectContext
            projectContext.SetProject(projectPath, bcpFilePath);

            // 输出项目信息（2行：项目名 + 路径）
            var projectName = Path.GetFileNameWithoutExtension(bcpFilePath);
            WriteWithColoredPrefix("[Server]", $"项目: {projectName}", ConsoleColor.White);
            WriteWithColoredPrefix("[Server]", $"  路径: {projectPath}", ConsoleColor.White);
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"项目加载失败: {ex.Message}", ConsoleColor.DarkGray);
        }
    }
    else
    {
        WriteWithColoredPrefix("[Server:ERR]", "未找到可加载的 BCP 文件", ConsoleColor.DarkGray);
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
        // 读取 Agent 端口配置
        var agentPort = LoadAgentPort(agentProjectPath);

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
                        WriteWithColoredPrefix("[Agent]", line, ConsoleColor.Cyan);
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
            WriteWithColoredPrefix("[Server:ERR]", "提示: 请确保已安装 Python 并配置到 PATH，且已运行 pip install -e . 安装依赖", ConsoleColor.DarkGray);
        }
    }
    else
    {
        WriteWithColoredPrefix("[Server:ERR]", $"Agent 项目目录不存在: {agentProjectPath}", ConsoleColor.DarkGray);
    }

    // 2. 启动 Web 服务（不等待，后台运行）
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

    // 3. 等待 Web 服务就绪后打开浏览器（Agent 在后台继续启动，Web 端通过 health 检查感知状态）
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
                    WriteWithColoredPrefix("[Server]", "所有服务已就绪", ConsoleColor.White);
                    break;
                }
            }
            catch { /* 服务未就绪，继续等待 */ }
            await Task.Delay(200);
        }

        // 打开浏览器
        WriteWithColoredPrefix("[Server]", $"打开浏览器: {webBaseUrl}", ConsoleColor.White);
        try
        {
            Process.Start(new ProcessStartInfo(webBaseUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WriteWithColoredPrefix("[Server:ERR]", $"无法自动打开浏览器: {ex.Message}", ConsoleColor.DarkGray);
        }
    }

    // 4. 注册退出时清理进程
    AppDomain.CurrentDomain.ProcessExit += (s, e) =>
    {
        if (agentProcess != null && !agentProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Agent 服务...", ConsoleColor.White);
            agentProcess.Kill(true);
        }
        if (webProcess != null && !webProcess.HasExited)
        {
            WriteWithColoredPrefix("[Server]", "正在关闭 Web 开发服务器...", ConsoleColor.White);
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

// 辅助函数：读取 Agent 端口配置
// Agent 配置文件位置：~/.bimcanvas/config.json
static int LoadAgentPort(string agentProjectPath)
{
    try
    {
        // Agent 配置目录：~/.bimcanvas/
        var userConfigDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".bimcanvas"
        );
        var configPath = Path.Combine(userConfigDir, "config.json");

        if (!File.Exists(configPath))
        {
            // 配置文件不存在是正常的，Agent 启动时会自动从模板创建
            return 8765;
        }

        var json = File.ReadAllText(configPath);
        var config = JObject.Parse(json);
        var port = config?["server"]?["port"]?.Value<int>() ?? 8765;

        return port;
    }
    catch (Exception ex)
    {
        WriteWithColoredPrefix("[Server:WARN]", $"读取 Agent 配置失败: {ex.Message}，使用默认端口 8765", ConsoleColor.DarkYellow);
        return 8765;
    }
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
