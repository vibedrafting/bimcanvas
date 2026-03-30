# BIMCanvas Docker 容器化部署实施计划

> 基于 `reviews/DockerDeployment_Review.md` 讨论共识
> 分支：`feature/docker-deployment`
> 初版日期：2026-03-25
> 最近更新：2026-03-30

## 当前状态（2026-03-30）

- 阶段一核心改造：已完成，Windows 开发态与 Docker 生产态已具备分叉运行能力。
- 阶段二单实例 Docker：已完成并通过人工烟测。
- 阶段三统一配置 UI：已完成，实例内部配置已可通过首页“实例设置”统一管理。
- 已验证链路：镜像构建、容器启动、`/health`、生产静态页面加载、`.bcp` 上传导入、AI 对话、后台截图。
- 已补充的实现性修复：模板 `.gitignore` 保留、`.bcp` 压缩包 Windows 反斜杠路径兼容、Playwright 安装链路调整、`instance.env` bootstrap 仅在配置缺失时生效。
- 阶段四待实施：远程 Linux 服务器正式部署。
- 阶段四前建议复核：双实例编排、Nginx 同源 `/agent` 代理、最新镜像下的生产日志噪音。
- 待回归：最新 CORS 本机来源回退修复已落地，需基于新镜像再确认日志噪音是否消失。

> 说明：本文后续“计划段落”仍保留决策背景；凡涉及 Docker 启动脚本、镜像构建和生产启动行为，以仓库当前实现为准。

## Context

BIMCanvas 当前仅在 Windows 本机运行，单用户使用。目标是将其部署到远程 Linux 服务器的 Docker 容器中，支持多用户并行使用（每用户一个容器实例）。

原始代码存在以下阻塞部署的问题（其中大部分已在当前分支解决）：
- 16+ 个 Web 文件硬编码 `localhost:5000` / `localhost:8865`
- Server 启动流程依赖 `cmd.exe`、`Console.ReadLine()` 交互、Vite dev server
- 配置目录硬编码 `MyDocuments`，Linux 下不可用
- 无静态文件托管能力，生产环境无法脱离 Vite dev server
- CORS 仅允许 localhost 源
- Agent 端 `SERVER_URL` 硬编码

本计划分四个阶段实施：本地代码改造 → Docker 打包 → 统一配置 UI → 服务器部署。

---

## 阶段一：本地代码改造

> 目标：在不破坏 Windows 本地开发体验的前提下，使代码具备在 Linux Docker 生产环境中运行的能力。
> 当前状态：改动 0~10 主体已落地；本节保留原始拆解，用于追溯设计过程。

### Phase 1a：地基（优先级最高）

#### 改动 0：验证并修复 Web 生产构建

**为什么先做**：`npm run build` 是否通过决定了后续所有生产链路能否落地。

- 文件：`BIMCanvas.Web/` 目录
- 操作：
  1. 执行 `cd BIMCanvas.Web && npm run build`（当前构建命令：`vue-tsc -b && vite build`）
  2. 如果 TypeScript 类型检查失败，逐一修复编译错误
  3. 确认 `dist/` 目录产出完整（`index.html` + 静态资源）
- 验证：`dist/index.html` 存在，`dist/assets/` 下有 JS/CSS 文件

#### 改动 1：`BIMCANVAS_HOME` 环境变量覆盖入口

**文件 1**：`BIMCanvas.Server/Services/ConfigService.cs`（第 15-18 行）

```csharp
// 改前
private static readonly string ConfigDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "BIMCanvas"
);

// 改后
private static readonly string ConfigDir = GetBimCanvasHome();

private static string GetBimCanvasHome()
{
    var envHome = Environment.GetEnvironmentVariable("BIMCANVAS_HOME");
    if (!string.IsNullOrEmpty(envHome))
        return envHome;
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "BIMCanvas");
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".bimcanvas");
}
```

需要在文件顶部添加 `using System.Runtime.InteropServices;`（如果尚未存在）。

**文件 2**：`BIMCanvas.Server/Services/ProjectService.cs`（第 58-61 行）

```csharp
// 改前
public static string DefaultProjectsRoot => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "BIMCanvas",
    "Projects");

// 改后
public static string DefaultProjectsRoot => Path.Combine(
    ConfigService.ConfigDir, "Projects");
```

注意：需要确认 `ConfigService.ConfigDir` 的访问权限。如果是 `private`，改为 `internal` 或新增一个 `public static string HomeDir` 属性。

- 验证（Windows）：不设 `BIMCANVAS_HOME` → 行为不变（`Documents/BIMCanvas/`）
- 验证（测试）：设 `BIMCANVAS_HOME=C:\temp\bimtest` → 配置和项目写入新位置

#### 改动 2：Server 生产模式分叉

**文件**：`BIMCanvas.Server/Program.cs`

**(2a) 跳过交互式依赖检查**（第 608、760、868、981 行的 `Console.ReadLine()`）

在每个交互检查函数中添加生产模式判断。模式判断方式：通过环境变量 `ASPNETCORE_ENVIRONMENT`。

```csharp
// 在每个 Console.ReadLine() 调用前添加判断
var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";

// TryInstallAgentDependencies (line 608 区域)
if (isProduction)
{
    Console.WriteLine("[ERROR] Agent dependencies missing. In production mode, dependencies must be pre-installed.");
    Environment.Exit(1);
}
// else: 保持原有的 Console.ReadLine() 交互逻辑

// 同样处理 TryInstallCcr (line 760)、TryInstallWebDependencies (line 868)、TryInstallPlaywrightChromium (line 981)
```

**(2b) 生产模式跳过 Web dev server 启动**（第 389-407 行）

```csharp
// 在 Web dev server 启动块外层包裹判断
if (!app.Environment.IsProduction())
{
    // 原有的 Vite dev server 启动逻辑 (lines 389-450)
}
```

**(2c) 生产模式跳过浏览器打开**（第 477-497 行）

```csharp
// 在浏览器打开逻辑外层添加判断
if (config.Startup.OpenBrowser && !app.Environment.IsProduction())
{
    // 原有的浏览器打开逻辑
}
```

#### 改动 3：Server 添加静态文件托管

**文件**：`BIMCanvas.Server/Program.cs`

在 middleware pipeline 中（`app.UseRouting()` 之后、`app.MapControllers()` 之前）添加：

```csharp
// 生产模式：托管 Web 前端构建产物
var webDistPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BIMCanvas.Web", "dist"));
if (app.Environment.IsProduction() && Directory.Exists(webDistPath))
{
    var fileProvider = new PhysicalFileProvider(webDistPath);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
    // SPA fallback：非 API/hubs 路径返回 index.html
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = fileProvider });
    Console.WriteLine($"[Production] Serving Web static files from: {webDistPath}");
}
```

需要在 csproj 或 using 中引入 `Microsoft.Extensions.FileProviders`（通常 ASP.NET Core 已内置）。

注意：`webDistPath` 的相对路径取决于 `dotnet run` 的工作目录，Docker 中可能需要调整。建议同时支持环境变量覆盖：

```csharp
var webDistPath = Environment.GetEnvironmentVariable("BIMCANVAS_WEB_DIST")
    ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "BIMCanvas.Web", "dist"));
```

#### 改动 4：cmd.exe 跨平台化

**文件**：`BIMCanvas.Server/Program.cs`

**(4a) Web dev server 启动**（第 398-399 行）
```csharp
// 改前
FileName = "cmd.exe",
Arguments = "/c npm run dev",

// 改后
FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/bash",
Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c npm run dev" : "-c \"npm run dev\"",
```

**(4b) npm install**（第 868 行区域，`TryInstallWebDependencies` 函数内）
同样添加平台判断。

**(4c) CCR 启动**（第 1020-1021 行）
当前已有平台判断（`cmd.exe /c ccr start` vs `ccr start`），确认逻辑正确即可。

#### 改动 5：Server 监听 0.0.0.0

**新建文件**：`BIMCanvas.Server/appsettings.Production.json`

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  },
  "Web": {
    "BaseUrl": "http://localhost:5000"
  }
}
```

注意：`Web:BaseUrl` 设为 `http://localhost:5000` 是给 `BackgroundScreenshotService` 用的（原默认值 `http://localhost:5173`，见 `BackgroundScreenshotService.cs:63-65`），生产模式下 Server 自己托管 Web，所以截图服务应访问自己的 5000 端口。

**Phase 1a 验证点**：
- Windows：`dotnet run --project BIMCanvas.Server` 正常工作（Development 模式，行为不变）
- Windows：`npm run build` 在 `BIMCanvas.Web/` 下通过，`dist/` 产出完整

---

### Phase 1c：前端 URL 环境变量化 + 其余改动

#### 改动 6：Web URL 环境变量化

**(6a) 新建配置模块**

**新建文件**：`BIMCanvas.Web/src/config/api.ts`

```typescript
// Server API 基础地址（生产环境为空 = 同源相对路径）
export const SERVER_BASE = import.meta.env.VITE_SERVER_URL || ''

// Agent API 基础地址（生产环境为空 = 同源 /agent 前缀，由 Nginx 代理）
export const AGENT_BASE = import.meta.env.VITE_AGENT_URL || ''

// 派生地址
export const SERVER_API = `${SERVER_BASE}/api`
export const SIGNALR_HUB = `${SERVER_BASE}/hubs/canvas`
export const AGENT_API = AGENT_BASE || `${SERVER_BASE}/agent`
```

**(6b) 新建环境变量文件**

**新建文件**：`BIMCanvas.Web/.env.development`
```
VITE_SERVER_URL=http://localhost:5000
VITE_AGENT_URL=http://localhost:8865
```

**新建文件**：`BIMCanvas.Web/.env.production`
```
# 生产环境：空值 = 同源相对路径，由 Nginx 反向代理
VITE_SERVER_URL=
VITE_AGENT_URL=
```

**(6c) 逐文件替换硬编码 URL**

以下 14 个文件需要修改，每个文件改动模式相同：导入 `api.ts` 常量，替换硬编码 URL。

| # | 文件 | 行号 | 改前 | 改后 |
|---|------|------|------|------|
| 1 | `services/ProjectService.ts` | 4 | `'http://localhost:5000/api/project'` | `\`${SERVER_API}/project\`` |
| 2 | `services/GitService.ts` | 3 | `'http://localhost:5000/api/git'` | `\`${SERVER_API}/git\`` |
| 3 | `services/GitWorktreeService.ts` | 13 | `'http://localhost:5000/api/git'` | `\`${SERVER_API}/git\`` |
| 4 | `services/ModuleLibraryService.ts` | 7 | `'http://localhost:5000/api/modules'` | `\`${SERVER_API}/modules\`` |
| 5 | `services/SchemeService.ts` | 3 | `'http://localhost:5000/api/scheme'` | `\`${SERVER_API}/scheme\`` |
| 6 | `services/SignalRService.ts` | 16 | `'http://localhost:5000/hubs/canvas'` | `SIGNALR_HUB` |
| 7 | `services/QuestionService.ts` | 18 | `'http://localhost:8865'` | `AGENT_API` |
| 8 | `services/ScreenshotService.ts` | 28 | `'http://localhost:8865'` | `AGENT_API` |
| 9 | `stores/canvasStore.ts` | 224 | `'http://localhost:5000/api/project'` | `\`${SERVER_API}/project\`` |
| 10 | `stores/canvasStore.ts` | 585 | `'http://localhost:5000/api/project/save'` | `\`${SERVER_API}/project/save\`` |
| 11 | `stores/windowStore.ts` | 4 | `'http://localhost:5000'` | `SERVER_BASE` |
| 12 | `stores/gitStore.ts` | 6, 270 | `'http://localhost:5000'` | `SERVER_BASE`（2 处） |
| 13 | `stores/mergeStore.ts` | 5 | `'http://localhost:5000'` | `SERVER_BASE` |
| 14 | `composables/.../useWindowManager.ts` | 115, 332, 450 | `'http://localhost:5000/api/windows/...'` | `\`${SERVER_API}/windows/...\`` |
| 15 | `components/UI/AICommandCenter.vue` | 42 | `'http://127.0.0.1:8865'` | `AGENT_API` |
| 16 | `components/UI/AICommandCenter.vue` | 43 | `'http://localhost:5000'` | `SERVER_BASE` |

每个文件顶部添加：`import { SERVER_BASE, SERVER_API, AGENT_API, SIGNALR_HUB } from '@/config/api'`（按需导入）。

注意：`QuestionService.ts` 和 `ScreenshotService.ts` 中 `localhost:8865` 是构造函数默认参数，需要确认替换后的 `AGENT_API` 在模块顶层求值是否正常（Vite 环境变量在 import 时已可用，应该没问题）。

#### 改动 7：Agent 端 SERVER_URL 环境变量化

**文件 1**：`BIMCanvas.Agent/src/mcp/canvas.py`（第 15 行）
```python
# 改前
SERVER_URL = "http://localhost:5000"
# 改后
import os
SERVER_URL = os.environ.get("BIMCANVAS_SERVER_URL", "http://localhost:5000")
```

**文件 2**：`BIMCanvas.Agent/src/agent/worktree_manager.py`（第 16 行）
```python
# 改前
SERVER_URL = "http://localhost:5000"
# 改后
import os
SERVER_URL = os.environ.get("BIMCANVAS_SERVER_URL", "http://localhost:5000")
```

#### 改动 8：Agent 监听地址确认

**文件**：`BIMCanvas.Agent/src/config/settings.py`

确认 `SERVER_HOST` 环境变量已支持。Docker 中需设 `SERVER_HOST=0.0.0.0`，使 Agent 监听所有接口（Nginx 需通过 Docker 内部网络访问 8865）。

如果当前默认值是 `127.0.0.1`，无需改代码，在 Docker 环境变量中覆盖即可。

#### 改动 9：CORS 配置化

**文件**：`BIMCanvas.Server/Program.cs`（第 152-161 行）

```csharp
// 改前
policy.WithOrigins("http://localhost:5173", "http://localhost:3000")

// 改后
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsOrigins != null && corsOrigins.Length > 0)
{
    policy.WithOrigins(corsOrigins)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials();
}
else if (builder.Environment.IsProduction())
{
    // 生产模式：Server 在 Nginx 后面，静态文件由 Server 自己托管，同源请求不需要 CORS
    // 但 SignalR 需要 credentials，所以不能用 AllowAnyOrigin
    // 最安全的做法：AllowAnyOrigin 不搭配 AllowCredentials
    policy.AllowAnyOrigin()
          .AllowAnyHeader()
          .AllowAnyMethod();
}
else
{
    // 开发模式：保持原有 localhost 白名单
    policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
          .AllowAnyHeader()
          .AllowAnyMethod()
          .AllowCredentials();
}
```

**重要**：SignalR 需要 `AllowCredentials()`，但 `AllowAnyOrigin()` 和 `AllowCredentials()` 不能同时使用。生产环境中如果 Web 由 Server 自己托管（同源），SignalR 请求不触发 CORS，这个冲突不存在。但如果通过 Nginx 反代，`Origin` 头可能存在，需要测试。备选方案：在 `appsettings.Production.json` 中配置具体的 `Cors:AllowedOrigins`。

#### 改动 10：截图服务 Web URL 生产适配

已在改动 5 的 `appsettings.Production.json` 中通过 `Web:BaseUrl = http://localhost:5000` 覆盖。`BackgroundScreenshotService.cs:63-65` 的优先级链 `Web:BaseUrl > BIMCANVAS_WEB_URL env > 默认 localhost:5173` 会自动生效，无需改代码。

**Phase 1c 验证点**：
- Windows：`dotnet run --project BIMCanvas.Server` 正常工作（`.env.development` 生效，所有 URL 指向 localhost）
- Windows：浏览器打开后，所有功能正常（Web/Agent/项目/截图）
- 确认：`npm run build` 仍然通过（改动 6 后重新构建）

---

## 阶段二：Docker 打包

> 目标：构建可用的 Docker 镜像，实现单实例和双实例运行。
> 当前状态：单实例链路已完成并验证通过；多实例编排与 Nginx 同源代理基础稿已落地，待阶段四服务器环境回归。

### Phase 1b：Dockerfile + 启动脚本（已完成）

#### 部署文件 A：`deploy/start.sh`

当前实现已不再使用本文初稿中的 `dotnet run --no-build` 方案，关键行为如下：

- 默认 `BIMCANVAS_HOME=/data`
- 默认 `ASPNETCORE_ENVIRONMENT=Production`
- 默认 `ASPNETCORE_URLS=http://0.0.0.0:5000`
- 默认 `BIMCANVAS_WEB_DIST=/app/BIMCanvas.Web/dist`
- 默认 `BIMCANVAS_PYTHON_COMMAND=/app/BIMCanvas.Agent/venv/bin/python`
- 将 Agent venv 与 `/root/.dotnet/tools` 前置到 `PATH`
- 首次启动时，把全局模板初始化到 `/data`
- 支持命令透传；`docker run --rm bimcanvas:local which python` 这类命令不会被 entrypoint 吃掉
- 最终直接执行已构建的 `BIMCanvas.Server.dll`，避免容器内回退到开发态 / Vite 模式

#### 部署文件 B：`deploy/Dockerfile`

当前实现基于 `ubuntu:22.04`，并已补齐以下真实落地细节：

- 安装 .NET 8、Node 20、Python 3、Playwright 依赖和中文字体
- 基础 `apt` 安装与 Node 安装均加入重试，降低 Ubuntu 源偶发 `503` 导致的构建失败
- 全局安装 `@anthropic-ai/claude-code` 与 `claude-code-router`
- 构建期校验 `claude --version` 与 `ccr --version`
- 构建 Server Release、构建 Web `dist`、创建 Agent venv 并安装本地包
- Playwright 改为使用项目输出目录中的 CLI 安装 Chromium，不再依赖失效的 NuGet tool 版本锁定方案
- 暴露 `5000`、`8865`，并声明 `/data` 为数据卷

#### 部署文件 C：`.dockerignore`

实际使用的是仓库根目录 `.dockerignore`，不是 `deploy/.dockerignore`。本轮实现中最重要的补充是：

- 保留 `BIMCanvas.Server/Templates/**/.gitignore`
- 避免模板初始化时因为 Docker build context 过滤而丢失占位文件

#### 部署文件 D：`deploy/instance.env.example`

该文件已存在，可作为服务器侧实例 bootstrap 模板继续沿用。当前定位仍然是：

- 初始化实例密钥与运行参数
- 首次启动时为 `/data` 下的 JSON 配置提供缺省值
- 后续长期配置真源仍以持久化 JSON 为主

**Phase 1b 当前验证结果**：
1. `docker build -t bimcanvas:local -f deploy/Dockerfile .` 已成功
2. `docker run --rm bimcanvas:local which python` 已验证输出 `/app/BIMCanvas.Agent/venv/bin/python`
3. `docker run --rm bimcanvas:local python -c "import claude_agent_sdk; print('ok')"` 已成功
4. `docker run --rm bimcanvas:local claude --version` 已成功
5. `docker run --rm bimcanvas:local ccr --version` 已成功
6. `docker run --rm -p 5000:5000 -p 8865:8865 bimcanvas:local` 已成功启动生产模式
7. `http://localhost:5000/health` 与 `http://localhost:8865/health` 已成功
8. 浏览器访问 `http://localhost:5000` 已可加载生产静态页面
9. `.bcp` 上传导入、AI 对话、后台截图链路已成功

**当前补充说明**：

- 目前单实例 smoke test 仍采用浏览器直接访问 `5000` 与 `8865` 的模式，尚未收敛到 Nginx 同源 `/agent` 代理
- `CORS policy execution failed` 日志噪音已在代码中追加本机来源回退修复，需基于最新镜像再回归一次

---

### Phase 1d：多实例编排（进行中）

当前仓库已提供 `deploy/docker-compose.yml`、`deploy/nginx.conf`、`deploy/instance.env.example` 作为阶段四实施基础，但尚未完成双实例端到端验证。

#### 部署文件 E：`deploy/docker-compose.yml`

```yaml
version: '3.8'

services:
  alice:
    build:
      context: ..
      dockerfile: deploy/Dockerfile
    container_name: bimcanvas-alice
    env_file: ./instance-alice.env
    volumes:
      - /data/alice:/data
    ports:
      - "6001:5000"
    restart: unless-stopped
    networks:
      - bimcanvas

  bob:
    build:
      context: ..
      dockerfile: deploy/Dockerfile
    container_name: bimcanvas-bob
    env_file: ./instance-bob.env
    volumes:
      - /data/bob:/data
    ports:
      - "6002:5000"
    restart: unless-stopped
    networks:
      - bimcanvas

  nginx:
    image: nginx:alpine
    container_name: bimcanvas-nginx
    ports:
      - "80:80"
    volumes:
      - ./nginx.conf:/etc/nginx/nginx.conf:ro
    depends_on:
      - alice
      - bob
    restart: unless-stopped
    networks:
      - bimcanvas

networks:
  bimcanvas:
    driver: bridge
```

#### 部署文件 F：`deploy/nginx.conf`

```nginx
events {
    worker_connections 1024;
}

http {
    # 长连接支持（Agent SSE + SignalR WebSocket）
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;

    # --- 用户 Alice ---
    server {
        listen 80;
        server_name alice.example.com;  # 或使用 IP:端口 方式

        # Web 静态文件 + SPA
        location / {
            proxy_pass http://alice:5000;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }

        # Server API
        location /api/ {
            proxy_pass http://alice:5000;
            proxy_set_header Host $host;
        }

        # SignalR (WebSocket)
        location /hubs/ {
            proxy_pass http://alice:5000;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
        }

        # Agent API (SSE 长连接)
        location /agent/ {
            proxy_pass http://alice:8865/;
            proxy_http_version 1.1;
            proxy_set_header Connection "";
            proxy_buffering off;
            proxy_cache off;
            proxy_set_header Host $host;
        }
    }

    # --- 用户 Bob ---
    server {
        listen 80;
        server_name bob.example.com;

        location / {
            proxy_pass http://bob:5000;
            proxy_set_header Host $host;
            proxy_set_header X-Real-IP $remote_addr;
            proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        }

        location /api/ {
            proxy_pass http://bob:5000;
            proxy_set_header Host $host;
        }

        location /hubs/ {
            proxy_pass http://bob:5000;
            proxy_http_version 1.1;
            proxy_set_header Upgrade $http_upgrade;
            proxy_set_header Connection "upgrade";
            proxy_set_header Host $host;
        }

        location /agent/ {
            proxy_pass http://bob:8865/;
            proxy_http_version 1.1;
            proxy_set_header Connection "";
            proxy_buffering off;
            proxy_cache off;
            proxy_set_header Host $host;
        }
    }

    # --- 无域名 fallback（按端口直接访问）---
    server {
        listen 80 default_server;
        return 444;
    }
}
```

---

## 阶段三：统一配置 UI

> 目标：建立实例级配置控制台，让 Docker 部署后的实例内部应用配置统一通过 Web UI 管理，并继续落到现有 JSON 文件中持久化。
> 当前状态：Phase 3a ~ 3d 已落地；本节保留设计边界与验收口径，供阶段四部署前复核。

### 阶段三边界（先写清）

- **UI 管理范围**：仅管理实例内部应用配置，即 `server_config.json`、`web_config.json`、`config.json`、`ccr_config.json`
- **不纳入本阶段**：Docker 端口映射、Compose 编排、Nginx 域名 / 路由、宿主机卷挂载
- **配置持久化落点**：继续写回现有四份 JSON，不新增统一 `settings.json`
- **Secrets 策略（v1）**：UI 中可读可改，但属于高风险能力，默认应遮罩显示并提供显式查看动作
- **权限边界（v1）**：暂不做权限控制，谁能进入实例配置页，谁就能修改实例配置

### Phase 3a：配置聚合后端（已完成）

#### 改动目标

新增统一配置聚合层，把目前分散的配置读写入口收敛为实例配置 API。

- 新增统一聚合 API：
  - `GET /api/settings`：聚合返回 `server/web/agent/ccr` 四组配置
  - `PUT /api/settings`：按分组写回现有 JSON，并返回哪些改动需要重启
  - `POST /api/settings/restart`：触发实例重启
- 保留现有 `/api/web_config` 兼容入口，避免破坏现有前端逻辑
- 为 `server_config.json`、`config.json`、`ccr_config.json` 补齐与 `web_config.json` 对等的读写和基础校验服务
- 聚合 API 的返回结构需明确区分：
  - 配置分组（Web / Agent / Server / CCR）
  - 字段值
  - 生效方式（即时生效 / 需重启）

### Phase 3b：配置 UI 页面（已完成）

#### 改动目标

在 Web 端新增统一配置页，作为 Docker 实例运行后的主配置入口。

- 新增统一配置页，按分组展示：
  - Web
  - Agent
  - Server
  - CCR
- 编辑方式采用**混合模式**：
  - 常用项使用结构化表单
  - 高级项提供 JSON 编辑器
- 配置页需明确展示每项配置的生效方式：
  - 即时生效
  - 保存后需重启实例
- Secrets 默认遮罩显示，但支持显式切换为可见并修改

### Phase 3c：生效与重启机制（已完成）

#### 改动目标

建立“热更新 + 重启生效”并存的实例配置生效模型。

- `web_config.json` 默认按热更新处理，保存后立即生效
- `config.json`、`server_config.json`、`ccr_config.json` 默认按“保存后需重启实例”处理
- UI 保存后：
  - 对热更新项立即反馈成功
  - 对需重启项显示全局提示和“重启实例”按钮
- 对修改内部端口等高影响项，需提示重启后当前连接可能中断
- 重启机制采用：
  - Server 接收 `POST /api/settings/restart`
  - 配置先落盘
  - Server 主动退出
  - Docker `restart: unless-stopped` 自动拉起实例

### Phase 3d：启动时优先级修正（已完成）

#### 改动目标

把 `instance.env` 从长期配置入口退化为 bootstrap 输入，避免 UI 改动在重启后被回写覆盖。当前实现已完成该语义修正。

- 首次部署或目标文件缺失时，`instance.env` 负责初始化 `server/web/agent/ccr` 配置文件
- 一旦持久化 JSON 已存在，后续以 JSON 为长期真源
- 当前实现：`start.sh` 仅在目标 JSON 缺失时执行 bootstrap，不会在每次重启时常规覆盖已有 JSON
- `instance.env` 在阶段三之后仅承担：
  - 首次实例创建
  - 缺省值补齐
  - 部署引导

**阶段三验证点**（能力已落地，建议阶段四前复核）：

- [ ] 配置页能读取四类配置
- [ ] `web_config.json` 修改后立即生效
- [ ] `config/server/ccr` 修改后被标记为“需重启实例”
- [ ] 点击“重启实例”后配置生效
- [ ] UI 修改后的 JSON 在容器重启后保留
- [ ] 重启后不会被 `instance.env` 覆盖回旧值
- [ ] 不同实例的配置页互不影响

---

## 阶段四：服务器部署

> 目标：在远程 Linux 服务器上完成 Docker 部署，并基于阶段三的统一配置 UI 维护实例配置，验证多用户隔离。

**前置条件**：

- 阶段二已完成镜像、`docker-compose.yml`、`nginx.conf`、`start.sh` 等部署产物
- 阶段三已完成统一配置 UI，实例内部配置可在 Web 端维护

### 步骤 1：服务器环境准备

```bash
# 安装 Docker
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker $USER

# 安装 Docker Compose
sudo apt-get install -y docker-compose-plugin

# 创建数据目录
sudo mkdir -p /data/alice /data/bob
sudo chown -R $USER:$USER /data
```

### 步骤 2：上传代码

```bash
# 方案 A：Git clone（如已有远程仓库）
git clone <repo-url> /opt/bimcanvas
cd /opt/bimcanvas
git checkout feature/docker-deployment

# 方案 B：SCP 上传（无远程仓库）
scp -r ./BIMCanvas user@server:/opt/bimcanvas
```

### 步骤 3：初始化用户实例

```bash
cd /opt/bimcanvas/deploy

# 首次为 Alice 创建 bootstrap 环境文件
cp instance.env.example instance-alice.env
# 编辑 instance-alice.env，填入 Alice 的 API Key 和初始值

# 首次为 Bob 创建 bootstrap 环境文件
cp instance.env.example instance-bob.env
# 编辑 instance-bob.env，填入 Bob 的 API Key 和初始值
```

说明：

- `instance.env` 在此阶段的定位是**首次部署 / 缺省补齐 / 实例引导**
- 首次启动完成后，实例内部的常规配置修改默认通过统一配置 UI 完成
- 后续不再把 `instance.env` 作为长期配置入口使用

### 步骤 4：构建镜像并启动

```bash
cd /opt/bimcanvas/deploy

# 构建镜像
docker compose build

# 启动所有服务
docker compose up -d

# 查看日志
docker compose logs -f alice
docker compose logs -f bob
```

### 步骤 5：配置域名（可选）

如果有域名，将 `alice.example.com` 和 `bob.example.com` DNS 解析到服务器 IP，然后修改 `nginx.conf` 中的 `server_name`。

如果没有域名，可以先使用端口直接访问实例做临时回归：
- Alice: `http://服务器IP:6001`
- Bob: `http://服务器IP:6002`

但正式的同源 `/agent` 收口仍建议通过 Nginx 域名或统一入口完成，而不是长期依赖端口直连。

### 步骤 6：新增用户

```bash
# 1. 创建数据目录
sudo mkdir -p /data/charlie && sudo chown $USER:$USER /data/charlie

# 2. 首次创建 bootstrap 环境文件
cp instance.env.example instance-charlie.env
# 编辑填入 Charlie 的 API Key 和初始值

# 3. 在 docker-compose.yml 中添加 charlie 服务（复制 alice 段，改名称/端口/卷）

# 4. 在 nginx.conf 中添加 charlie 的 server 块

# 5. 重启
docker compose up -d
```

说明：

- 新用户首次接入仍需创建 `instance.env`
- 但实例创建完成后的常规内部配置维护，统一通过 Web UI 进行，而不是继续手动编辑 JSON

---

## 验收清单

### 第一关：本地不回归（Windows）

- [ ] `dotnet run --project BIMCanvas.Server` 正常启动
- [ ] Web 前端能打开，所有页面正常
- [ ] Agent 能调用（AI 对话/生成）
- [ ] 项目能保存和加载
- [ ] 截图功能正常

### 第二关：单实例 Docker（已通过）

- [x] `docker build` 成功完成
- [x] `docker run` 单实例，容器日志无致命错误
- [x] 浏览器访问容器端口，前端页面正常加载
- [x] 完整工作流可用（上传 / 加载 `.bcp` → AI 调用 → 截图）

### 第三关：双实例烟测（阶段四前复核）

- [ ] (a) 同一镜像启动 alice:6001 和 bob:6002
- [ ] (b) 两个实例的 `server_config.json` 互不影响
- [ ] (c) alice 创建项目出现在 `/data/alice/Projects/`
- [ ] (d) 浏览器分别访问两个实例均独立可用
- [ ] (e) 停止 alice，bob 不受影响
- [ ] (f) 重启 alice，数据仍在
- [ ] (g) 两个实例提供不同的 `web_config.json`，前端配置独立
- [ ] (h) 两个实例提供不同的 AI 连接参数，不交叉污染

### 第四关：统一配置 UI（能力已落地，阶段四前复核）

- [ ] (i) 能从 UI 读取 `server/web/agent/ccr` 四类配置
- [ ] (j) `web_config` 修改后可立即生效
- [ ] (k) `config/server/ccr` 修改后会被标记为“需重启”
- [ ] (l) 点击“重启实例”后配置生效
- [ ] (m) UI 修改的 JSON 在容器重启后保留
- [ ] (n) 重启后不会被 `instance.env` 覆盖回旧值
- [ ] (o) 两个实例各自的配置页互不影响

### 第五关：生产链路完整性（部分通过）

- [ ] (p) 浏览器网络面板无 `localhost` / `127.0.0.1` / `:5173` / `:8865` 的直接外部请求
- [x] (q) 容器内后台截图访问 `http://localhost:5000/screenshot-render` 正常产出

---

## 明确延后事项（阶段 5+）

- 配置页权限控制与审计日志
- 高级用户自定义 CCR 路由可视化编辑
- Docker / Nginx 编排控制台
- `/userA/` 子路径部署模式
- Server 内嵌 Agent 反代（消除 Agent 独立端口）
- 多阶段 Docker 构建优化（缩减镜像体积）
- HTTPS / Let's Encrypt 配置
- deploy.sh 一键更新脚本（git pull → docker build → 滚动重启）
- 容器健康检查与资源监控
- 自动化测试集成
- systemd 服务自启配置
