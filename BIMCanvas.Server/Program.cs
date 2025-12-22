using System.Diagnostics;
using BIMCanvas.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// 配置 JSON 序列化选项
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.WriteIndented = true;
    });

// 注册服务
builder.Services.AddSingleton<CanvasStateManager>();
builder.Services.AddSingleton<ZoneCalculator>();

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

// 健康检查端点
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

Console.WriteLine("BIMCanvas.Server 启动中...");
Console.WriteLine("API: http://localhost:5000/api/canvas");
Console.WriteLine("Swagger: http://localhost:5000/swagger");

// 自动启动 Web 开发服务器（如果 Web 项目存在）
{
    var baseDir = AppContext.BaseDirectory;
    var webProjectPath = FindWebProjectPath(baseDir);
    Process? webProcess = null;

    if (Directory.Exists(webProjectPath))
    {
        // 仅在开发环境启用 Swagger（但 Web 启动不受环境限制）
        Console.WriteLine($"启动 Web 开发服务器: {webProjectPath}");
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

        // 后台读取输出（避免缓冲区阻塞）
        _ = Task.Run(async () =>
        {
            while (!webProcess.HasExited)
            {
                var line = await webProcess.StandardOutput.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                    Console.WriteLine($"[Web] {line}");
            }
        });
        // 2. 等待 Web 服务就绪（最多 10 秒，每 200ms 检测一次）
        Console.WriteLine("等待 Web 服务启动...");
        var webUrl = "http://localhost:5173";
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        for (int i = 0; i < 50; i++)
        {
            try
            {
                var response = await httpClient.GetAsync(webUrl);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("Web 服务已就绪");
                    break;
                }
            }
            catch { /* 服务未就绪，继续等待 */ }
            await Task.Delay(200);
        }

        // 3. 打开浏览器
        Console.WriteLine($"打开浏览器: {webUrl}");
        try
        {
            Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"无法自动打开浏览器: {ex.Message}");
        }

        // 注册退出时清理 Web 进程
        AppDomain.CurrentDomain.ProcessExit += (s, e) =>
        {
            if (webProcess != null && !webProcess.HasExited)
            {
                Console.WriteLine("正在关闭 Web 开发服务器...");
                webProcess.Kill(true);
            }
        };
    }
    else
    {
        Console.WriteLine($"Web 项目目录不存在: {webProjectPath}");
    }
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
