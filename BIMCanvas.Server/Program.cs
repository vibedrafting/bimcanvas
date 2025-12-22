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

// 开发环境自动打开 Web 前端
if (app.Environment.IsDevelopment())
{
    var webUrl = "http://localhost:5173";
    Console.WriteLine($"正在打开 Web 前端: {webUrl}");
    try
    {
        Process.Start(new ProcessStartInfo(webUrl) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"无法自动打开浏览器: {ex.Message}");
    }
}

app.Run();
