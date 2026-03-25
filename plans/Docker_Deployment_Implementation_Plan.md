# BIMCanvas Docker 容器化部署实施计划

> 基于 `reviews/DockerDeployment_Review.md` 讨论共识
> 分支：`feature/docker-deployment`
> 日期：2026-03-25

## Context

BIMCanvas 当前仅在 Windows 本机运行，单用户使用。目标是将其部署到远程 Linux 服务器的 Docker 容器中，支持多用户并行使用（每用户一个容器实例）。

当前代码存在以下阻塞部署的问题：
- 16+ 个 Web 文件硬编码 `localhost:5000` / `localhost:8865`
- Server 启动流程依赖 `cmd.exe`、`Console.ReadLine()` 交互、Vite dev server
- 配置目录硬编码 `MyDocuments`，Linux 下不可用
- 无静态文件托管能力，生产环境无法脱离 Vite dev server
- CORS 仅允许 localhost 源
- Agent 端 `SERVER_URL` 硬编码

本计划分三个阶段实施：本地代码改造 → Docker 打包 → 服务器部署。

---

## 阶段一：本地代码改造

> 目标：在不破坏 Windows 本地开发体验的前提下，使代码具备在 Linux Docker 生产环境中运行的能力。

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

### Phase 1b：Dockerfile + 启动脚本

#### 部署文件 A：`deploy/start.sh`

```bash
#!/bin/bash
set -e

# === 统一状态根目录 ===
BIMCANVAS_HOME="${BIMCANVAS_HOME:-/data}"
export BIMCANVAS_HOME

echo "[start.sh] BIMCANVAS_HOME=$BIMCANVAS_HOME"

# === 初始化用户状态目录 ===
mkdir -p "$BIMCANVAS_HOME/Projects"

TEMPLATES_DIR="/app/BIMCanvas.Server/Templates"
MANIFEST="$TEMPLATES_DIR/program_manifest.json"

# 从模板初始化配置（仅首次）
if [ -f "$MANIFEST" ]; then
    # 读取 manifest 中的文件列表，逐个检查并复制
    for f in server_config.json web_config.json config.json; do
        if [ ! -f "$BIMCANVAS_HOME/$f" ] && [ -f "$TEMPLATES_DIR/$f" ]; then
            cp "$TEMPLATES_DIR/$f" "$BIMCANVAS_HOME/$f"
            echo "[start.sh] Initialized $f from template"
        fi
    done
fi

# === CCR 运行时配置生成（仅 CCR 启用时）===
if [ "${CCR_ENABLED}" = "true" ]; then
    CCR_TEMPLATE="$BIMCANVAS_HOME/ccr_config.json"
    if [ ! -f "$CCR_TEMPLATE" ]; then
        CCR_TEMPLATE="$TEMPLATES_DIR/ccr_config.json"
    fi

    if [ -f "$CCR_TEMPLATE" ]; then
        # 用环境变量 patch secrets，生成到临时路径
        python3 -c "
import json, os, sys
with open('$CCR_TEMPLATE') as f:
    cfg = json.load(f)
key = os.environ.get('CCR_API_KEY', '')
base = os.environ.get('CCR_API_BASE', '')
if key:
    for p in cfg.get('Providers', []):
        p['api_key'] = key
if base:
    for p in cfg.get('Providers', []):
        p['api_base_url'] = base
with open('/tmp/ccr_runtime.json', 'w') as f:
    json.dump(cfg, f, indent=2)
print('[start.sh] Generated /tmp/ccr_runtime.json')
"
    fi
fi

# === 设置生产环境变量 ===
export ASPNETCORE_ENVIRONMENT=Production
export ASPNETCORE_URLS="http://0.0.0.0:5000"
export SERVER_HOST=0.0.0.0

# 截图服务指向 Server 自身（生产模式 Server 托管 Web 静态文件）
export BIMCANVAS_WEB_URL="http://localhost:5000"

# === 启动 Server（Server 内部会拉起 Agent 和 CCR）===
echo "[start.sh] Starting BIMCanvas Server in Production mode..."
cd /app
exec dotnet run --project BIMCanvas.Server --configuration Release --no-build
```

#### 部署文件 B：`deploy/Dockerfile`

```dockerfile
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# === 系统依赖 ===
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl wget git ca-certificates gnupg \
    # Python
    python3 python3-venv python3-pip \
    # Playwright 系统依赖
    libgbm1 libnss3 libxss1 libxrandr2 libxdamage1 libxshmfence1 \
    libatk1.0-0 libatk-bridge2.0-0 libcups2 libdrm2 libxcomposite1 \
    libxfixes3 libpango-1.0-0 libcairo2 libasound2 \
    # 中文字体（截图需要）
    fonts-noto-cjk fonts-noto-color-emoji fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

# === .NET 8 SDK ===
RUN wget https://dot.net/v1/dotnet-install.sh -O /tmp/dotnet-install.sh \
    && chmod +x /tmp/dotnet-install.sh \
    && /tmp/dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
    && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet \
    && rm /tmp/dotnet-install.sh

# === Node.js 20 ===
RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
    && apt-get install -y nodejs \
    && rm -rf /var/lib/apt/lists/*

# === CCR (Claude Code Router) ===
RUN npm install -g claude-code-router

# === 复制项目代码 ===
COPY . /app
WORKDIR /app

# === 编译 Server + Core ===
RUN dotnet restore BIMCanvas.Server \
    && dotnet build BIMCanvas.Server -c Release --no-restore

# === 构建 Web 前端 ===
RUN cd BIMCanvas.Web && npm ci && npm run build

# === 安装 Agent 依赖 ===
RUN cd BIMCanvas.Agent \
    && python3 -m venv venv \
    && venv/bin/pip install --no-cache-dir -e .

# === 安装 Playwright Chromium ===
RUN cd BIMCanvas.Server \
    && dotnet tool install --global Microsoft.Playwright.CLI || true \
    && /root/.dotnet/tools/playwright install chromium --with-deps || \
       dotnet exec /app/BIMCanvas.Server/bin/Release/net8.0/Microsoft.Playwright.dll install chromium --with-deps || \
       echo "Playwright install via alternative method needed"

# === 启动脚本 ===
COPY deploy/start.sh /app/deploy/start.sh
RUN chmod +x /app/deploy/start.sh

# === 默认数据目录 ===
RUN mkdir -p /data

EXPOSE 5000 8865
VOLUME ["/data"]

ENTRYPOINT ["/app/deploy/start.sh"]
```

#### 部署文件 C：`deploy/.dockerignore`

```
# Git
.git
.gitignore

# IDE
.vs
.vscode
.idea
*.user
*.suo

# 构建产物（Docker 内重新构建）
**/bin/
**/obj/
**/node_modules/
BIMCanvas.Web/dist/
BIMCanvas.Agent/venv/
BIMCanvas.Agent/__pycache__/

# 文档和计划
docs/
plans/
reviews/
*.md
!README.md

# Revit（Docker 中不需要）
BIMCanvas.Revit/

# 敏感文件
**/.env.local
**/secrets/
```

#### 部署文件 D：`deploy/instance.env.example`

```bash
# ===================================================
# BIMCanvas 实例环境变量模板
# 复制此文件为 instance-{用户名}.env 并填入实际值
# ===================================================

# --- Secrets（必填其一）---
# 直连 Anthropic 模式：
ANTHROPIC_API_KEY=sk-ant-your-key-here

# CCR 网关模式（如使用第三方代理）：
# CCR_ENABLED=true
# CCR_API_KEY=your-provider-key
# CCR_API_BASE=https://your-provider-base-url

# --- 路径（通常不需要修改）---
# BIMCANVAS_HOME=/data

# --- 以下变量由 start.sh 自动设置，通常不需要手动指定 ---
# ASPNETCORE_ENVIRONMENT=Production
# ASPNETCORE_URLS=http://0.0.0.0:5000
# SERVER_HOST=0.0.0.0
# BIMCANVAS_WEB_URL=http://localhost:5000
```

**Phase 1b 验证点**：
1. `docker build -t bimcanvas:latest -f deploy/Dockerfile .` 构建成功
2. `docker run -d --name test -p 6001:5000 -v /tmp/bimtest:/data -e ANTHROPIC_API_KEY=sk-test bimcanvas:latest` 启动成功
3. 浏览器访问 `http://localhost:6001` 能看到前端页面
4. `docker logs test` 无致命错误

---

### Phase 1d：多实例编排

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

## 阶段三：服务器部署

> 目标：在远程 Linux 服务器上完成 Docker 部署，验证多用户隔离。

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

### 步骤 3：配置用户实例

```bash
cd /opt/bimcanvas/deploy

# 为 Alice 创建环境文件
cp instance.env.example instance-alice.env
# 编辑 instance-alice.env，填入 Alice 的 API Key

# 为 Bob 创建环境文件
cp instance.env.example instance-bob.env
# 编辑 instance-bob.env，填入 Bob 的 API Key
```

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

如果没有域名，可以暂时使用端口直接访问：
- Alice: `http://服务器IP:6001`
- Bob: `http://服务器IP:6002`

此时需要修改前端构建为带端口的绝对 URL，或调整 Nginx 为按端口区分（而非按域名）。

### 步骤 6：新增用户

```bash
# 1. 创建数据目录
sudo mkdir -p /data/charlie && sudo chown $USER:$USER /data/charlie

# 2. 创建环境文件
cp instance.env.example instance-charlie.env
# 编辑填入 Charlie 的 API Key

# 3. 在 docker-compose.yml 中添加 charlie 服务（复制 alice 段，改名称/端口/卷）

# 4. 在 nginx.conf 中添加 charlie 的 server 块

# 5. 重启
docker compose up -d
```

---

## 验收清单

### 第一关：本地不回归（Windows）

- [ ] `dotnet run --project BIMCanvas.Server` 正常启动
- [ ] Web 前端能打开，所有页面正常
- [ ] Agent 能调用（AI 对话/生成）
- [ ] 项目能保存和加载
- [ ] 截图功能正常

### 第二关：单实例 Docker

- [ ] `docker build` 成功完成
- [ ] `docker run` 单实例，容器日志无致命错误
- [ ] 浏览器访问容器端口，前端页面正常加载
- [ ] 完整工作流可用（创建项目 → 保存 → AI 调用 → 截图）

### 第三关：双实例烟测

- [ ] (a) 同一镜像启动 alice:6001 和 bob:6002
- [ ] (b) 两个实例的 `server_config.json` 互不影响
- [ ] (c) alice 创建项目出现在 `/data/alice/Projects/`
- [ ] (d) 浏览器分别访问两个实例均独立可用
- [ ] (e) 停止 alice，bob 不受影响
- [ ] (f) 重启 alice，数据仍在
- [ ] (g) 两个实例提供不同的 `web_config.json`，前端配置独立
- [ ] (h) 两个实例提供不同的 AI 连接参数，不交叉污染

### 第四关：生产链路完整性

- [ ] (i) 浏览器网络面板无 `localhost` / `127.0.0.1` / `:5173` / `:8865` 的直接外部请求
- [ ] (j) 容器内后台截图访问 `http://localhost:5000/screenshot-render` 正常产出

---

## 明确延后事项（阶段 2+）

- 高级用户自定义 CCR 路由结构
- `/userA/` 子路径部署模式
- Server 内嵌 Agent 反代（消除 Agent 独立端口）
- 多阶段 Docker 构建优化（缩减镜像体积）
- HTTPS / Let's Encrypt 配置
- deploy.sh 一键更新脚本（git pull → docker build → 滚动重启）
- 容器健康检查与资源监控
- 自动化测试集成
- systemd 服务自启配置
