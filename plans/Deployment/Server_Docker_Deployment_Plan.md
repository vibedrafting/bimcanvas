# BIMCanvas 服务器部署 + Docker 多用户方案

> 讨论日期：2026-03-17
> 当前分支：refactor/workflow-zoning
> 结论：采用 Docker 容器化部署，分三阶段实施

---

## 一、背景与目标

### 当前状态
- 项目在 Windows 本机运行，单用户使用
- 无远程仓库、无 CI/CD、无自动化测试
- 技术栈：.NET 8 (Server) + Vue 3 (Web) + Python 3.10 (Agent) + .NET FW 4.7.2 (Revit)

### 目标
1. 部署到远程 Ubuntu 服务器（阿里云/腾讯云/华为云）
2. 通过浏览器在线设计（手机/电脑/iPad 均可）
3. 支持多用户并行使用，互不干扰
4. 用户自带 API Key，费用自理，速率互不影响
5. 手机 SSH 一键部署更新

---

## 二、方案评估过程

### 评估了三种部署模型

#### 模型 A：单人远程

```
你的设备 → Nginx → 一个 Server + 一个 Agent
```

| 维度 | 评估 |
|------|------|
| 代码改动 | 最小（URL 环境变量化 + 平台适配） |
| 并发问题 | 不存在（只有一个人） |
| 资源需求 | 2 核 4GB 即可 |
| 适用场景 | 个人远程使用 |

#### 模型 B：产品级多用户（共享服务）

```
多用户 → Nginx → 一个 Server（重写）→ Agent 池
```

需要新增：用户认证系统、数据库、项目隔离、SignalR 房间、Agent 队列。
**工作量巨大（1-3 个月），且 Anthropic API 速率限制仍是瓶颈。不推荐。**

#### 模型 C：容器多用户（每用户一个实例） ← 最终选择

```
多用户 → Nginx → 每人一个 Docker 容器（Server + Agent）
```

| 维度 | 评估 |
|------|------|
| 代码改动 | 与模型 A 一样小 |
| 并发问题 | 天然隔离，互不干扰 |
| API Key | 每人自带，速率额度独立 |
| 扩容方式 | `docker run` 一行命令 |
| 适用场景 | 2-5 人小团队 |

### 为什么选择模型 C

核心优势是**用资源换隔离，零架构改动**：

1. **不改 Server 架构**：每个容器里就是一个完整的单用户 BIMCanvas，代码不用动
2. **API Key 隔离**：每个容器注入各自的 Key，谁用谁付钱，速率互不争抢
3. **崩溃隔离**：一个用户的 Agent 崩了不影响其他人
4. **数据隔离**：每个容器有独立的项目文件目录
5. **弹性伸缩**：多一个用户 = `docker run`，少一个用户 = `docker stop`

---

## 三、当前架构的并发瓶颈分析

以下是不采用容器隔离时，当前架构面临的并发限制：

### 瓶颈 1：单项目状态

`ProjectService` 在内存中持有**当前项目**的状态。所有 API 操作针对同一个项目，SignalR 广播给所有客户端。用户 A 打开项目 X，用户 B 也只能看到项目 X。

### 瓶颈 2：单 Agent 进程

Server 启动一个 Agent 子进程。虽然 aiohttp 是异步的，但 Agent 内部的 Claude API 调用是有状态的对话链。generate 任务可能包含 10-50 次工具调用循环，期间其他请求必须等待。

### 瓶颈 3：Anthropic API 速率限制

| 限制类型 | 典型值（取决于 Tier） |
|----------|---------------------|
| 请求数/分钟 (RPM) | 50-4000 |
| 输入 token/分钟 | 40K-400K |
| 输出 token/分钟 | 8K-80K |

一个 generate 工作流约 10-50 次 API 调用，持续 1-5 分钟。5 用户同时 generate 会触发速率限制。
**容器 + 自带 Key 方案完美解决此问题。**

### 瓶颈 4：Playwright 单实例

`BackgroundScreenshotService` 维护一个 Chromium 实例，截图串行执行。容器隔离后每人独立 Chromium。

---

## 四、代码迁移改动清单

### 已经做好跨平台准备的部分（无需改动）

| 能力 | 位置 | 处理方式 |
|------|------|---------|
| System.Management 条件排除 | `Server.csproj` | `Condition="'$(OS)' == 'Windows_NT'"` |
| kernel32.dll P/Invoke 防护 | `Program.cs:74` | `RuntimeInformation.IsOSPlatform(Windows)` 守卫 |
| 端口占用检测 Linux 分支 | `Program.cs:719-738` | 已用 `lsof` 实现 |
| WMIC → ps 替代 | `Program.cs:822-845` | 已实现 Linux 用 `ps` |
| Agent Windows Git Bash 路径 | `main.py:6-16` | `sys.platform == "win32"` 守卫 |
| Core 全部代码 | `BIMCanvas.Core/` | .NET Standard 2.0，零平台依赖 |
| Git Worktree 服务 | `GitWorktreeService.cs` | `Path.Combine()` 跨平台 |

### P0：不改就无法运行

#### 改动 1：Web 端 API URL 硬编码 → 环境变量化

**问题**：14 个文件中 19 处硬编码 `localhost:5000` 和 `localhost:8765`，部署到服务器后浏览器无法连接。

**涉及文件**：

| 文件 | 硬编码内容 |
|------|-----------|
| `Web/src/services/ProjectService.ts` | `http://localhost:5000/api/project` |
| `Web/src/services/GitService.ts` | `http://localhost:5000/api/git` |
| `Web/src/services/GitWorktreeService.ts` | `http://localhost:5000/api/git` |
| `Web/src/services/ModuleLibraryService.ts` | `http://localhost:5000/api/modules` |
| `Web/src/services/SchemeService.ts` | `http://localhost:5000/api/scheme` |
| `Web/src/services/SignalRService.ts` | `http://localhost:5000/hubs/canvas` |
| `Web/src/services/QuestionService.ts` | `http://localhost:8765` |
| `Web/src/services/ScreenshotService.ts` | `http://localhost:8765` |
| `Web/src/stores/canvasStore.ts` | `http://localhost:5000`（2 处） |
| `Web/src/stores/gitStore.ts` | `http://localhost:5000`（2 处） |
| `Web/src/stores/windowStore.ts` | `http://localhost:5000` |
| `Web/src/stores/mergeStore.ts` | `http://localhost:5000` |
| `Web/src/composables/.../useWindowManager.ts` | `http://localhost:5000`（3 处） |
| `Web/src/components/UI/AICommandCenter.vue` | 两个 BASE 常量 |

**改法**：

```typescript
// 新建 Web/src/config/api.ts
export const SERVER_API_BASE = import.meta.env.VITE_SERVER_URL || 'http://localhost:5000'
export const AGENT_API_BASE = import.meta.env.VITE_AGENT_URL || 'http://localhost:8765'
```

```bash
# Web/.env.development（本机开发）
VITE_SERVER_URL=http://localhost:5000
VITE_AGENT_URL=http://localhost:8765

# Web/.env.production（服务器部署）
VITE_SERVER_URL=http://服务器IP:5000
VITE_AGENT_URL=http://服务器IP:8765
```

#### 改动 2：Server 启动 Web 时硬编码 `cmd.exe`

**位置**：`BIMCanvas.Server/Program.cs` 第 309-310 行

```csharp
// 当前
FileName = "cmd.exe",
Arguments = "/c npm run dev",

// 改为
FileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "cmd.exe" : "/bin/bash",
Arguments = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "/c npm run dev" : "-c \"npm run dev\"",
```

#### 改动 3：配置目录 `SpecialFolder.MyDocuments`

**位置**：
- `BIMCanvas.Server/Services/ConfigService.cs` 第 15-18 行
- `BIMCanvas.Server/Services/ProjectService.cs` 第 38-41 行

```csharp
// 当前
Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BIMCanvas")

// 改为
Path.Combine(
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "BIMCanvas" : ".bimcanvas"
)
// Windows: C:\Users\xxx\Documents\BIMCanvas\
// Linux:   /home/xxx/.bimcanvas/
```

#### 改动 4：Agent 端 SERVER_URL 硬编码

**位置**：
- `BIMCanvas.Agent/src/agent/worktree_manager.py` 第 16 行
- `BIMCanvas.Agent/src/mcp/canvas.py` 第 15 行

```python
# 当前
SERVER_URL = "http://localhost:5000"

# 改为
import os
SERVER_URL = os.environ.get("BIMCANVAS_SERVER_URL", "http://localhost:5000")
```

#### 改动 5：CORS 允许服务器 IP 访问

**位置**：`BIMCanvas.Server/Program.cs` 第 147-156 行

```csharp
// 当前：只允许 localhost
policy.WithOrigins("http://localhost:5173", "http://localhost:3000")

// 改为：从配置读取
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };
policy.WithOrigins(allowedOrigins)
```

### P1：不改功能受限

#### 改动 6：Server 监听地址

部署时通过环境变量覆盖：`dotnet run --urls "http://0.0.0.0:5000"`
或添加 `appsettings.Production.json`。

#### 改动 7：浏览器自动打开

`Program.cs:377-386`，服务器上不需要。添加配置项 `"autoOpenBrowser": false`。

---

## 五、Docker 部署架构

### 生产环境架构图

```
  浏览器 → http://服务器IP
              │
         ┌────┴─────┐
         │  Nginx   │  :80
         │  ┌───────┴──────────────────────────────┐
         │  │ /userA/*    → 容器 A :6001            │
         │  │ /userB/*    → 容器 B :6002            │
         │  │ /login      → 简单登录页（可选）        │
         │  └──────────────────────────────────────┘
         └───────────┘
              │
    ┌─────────┴────────────────────────┐
    │          Docker                   │
    │                                   │
    │  ┌──────────────┐ ┌────────────┐ │
    │  │ 容器 A        │ │ 容器 B      │ │
    │  │ Server :5000  │ │ Server     │ │
    │  │ Agent  :8765  │ │ Agent      │ │
    │  │ KEY=sk-A的    │ │ KEY=sk-B的 │ │
    │  │ 项目→/data/A  │ │ 项目→/data/B│ │
    │  └──────────────┘ └────────────┘ │
    └──────────────────────────────────┘
```

### 容器内部结构

```
容器内部（每个容器相同）：
  Ubuntu 22.04
  ├── .NET 8.0 SDK
  ├── Python 3.10 + venv
  ├── Git
  ├── Playwright + Chromium
  │
  ├── /app/BIMCanvas.Server    → dotnet run
  │     └── 自动启动 Agent     → python -m src.main --serve
  │
  ├── /app/BIMCanvas.Web/dist  → 静态文件（由 Nginx 或 Server 托管）
  │
  └── /root/.bimcanvas/        → 挂载到宿主机 /data/{用户名}/
        ├── server_config.json
        ├── web_config.json
        └── Projects/
              ├── 项目A/
              └── 项目B/
```

### 资源消耗估算

| 组件 | 单容器内存 | 说明 |
|------|-----------|------|
| .NET Server | ~200-400 MB | 常驻 |
| Playwright Chromium | ~300-500 MB | 截图时启动 |
| Python Agent | ~100-200 MB | 常驻 |
| **合计** | **~600 MB - 1.1 GB** | 空闲低，AI 时高 |

| 用户数 | 建议服务器配置 | 月费参考（国内云） |
|--------|-------------|-----------------|
| 1 人 | 2 核 4GB | ~100-200 元 |
| 2-3 人 | 4 核 8GB | ~300-500 元 |
| 5 人 | 8 核 16GB | ~800-1200 元 |

### Docker 实现要素

#### Dockerfile 示意

```dockerfile
FROM ubuntu:22.04

# 系统依赖
RUN apt-get update && apt-get install -y \
    curl wget git \
    dotnet-sdk-8.0 \
    python3 python3-venv python3-pip \
    # Playwright 系统依赖
    libgbm1 libnss3 libxss1 libxrandr2 libxdamage1 \
    libxshmfence1 fonts-noto-cjk fonts-liberation

# 复制代码
COPY . /app
WORKDIR /app

# 编译 Server + Core
RUN dotnet build BIMCanvas.Server -c Release

# 构建 Web 静态文件
RUN cd BIMCanvas.Web && npm ci && npm run build

# 安装 Agent 依赖
RUN cd BIMCanvas.Agent && python3 -m venv venv \
    && venv/bin/pip install -e .

# 启动脚本
COPY deploy/start.sh /app/start.sh
RUN chmod +x /app/start.sh

EXPOSE 5000
CMD ["/app/start.sh"]
```

#### 启动容器命令

```bash
# 给用户 A 创建实例
docker run -d \
  --name bimcanvas-userA \
  -p 6001:5000 \
  -v /data/userA:/root/.bimcanvas \
  -e ANTHROPIC_API_KEY=sk-用户A的Key \
  -e ASPNETCORE_URLS=http://0.0.0.0:5000 \
  --restart unless-stopped \
  bimcanvas:latest

# 给用户 B 创建实例
docker run -d \
  --name bimcanvas-userB \
  -p 6002:5000 \
  -v /data/userB:/root/.bimcanvas \
  -e ANTHROPIC_API_KEY=sk-用户B的Key \
  -e ASPNETCORE_URLS=http://0.0.0.0:5000 \
  --restart unless-stopped \
  bimcanvas:latest
```

#### Nginx 配置示意

```nginx
server {
    listen 80;

    # 用户 A
    location /userA/ {
        proxy_pass http://127.0.0.1:6001/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;      # WebSocket 支持
        proxy_set_header Connection "upgrade";        # SignalR 需要
        proxy_set_header Host $host;
    }

    # 用户 B
    location /userB/ {
        proxy_pass http://127.0.0.1:6002/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }
}
```

---

## 六、不能迁移的部分

**BIMCanvas.Revit 永远只能在 Windows 本机运行。**

- .NET Framework 4.7.2 不支持 Linux
- Revit API DLL 硬编码本机路径（`D:\Autodesk\Revit 2019\`）
- Revit 插件依赖 Revit UI 进程

工作流：用户在 Windows Revit 中导出 .bcp 文件 → 上传到服务器 → 在线设计。

---

## 七、服务器系统依赖清单

```bash
# Ubuntu 22.04 基础环境
sudo apt-get update && sudo apt-get install -y \
    curl wget git build-essential libssl-dev libffi-dev

# .NET 8.0
wget https://dot.net/v1/dotnet-install.sh && chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Node.js 20
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt-get install -y nodejs

# Python 3.10+
sudo apt-get install -y python3 python3-venv python3-pip

# Nginx
sudo apt-get install -y nginx

# Playwright 系统依赖
sudo apt-get install -y \
    libgbm1 libnss3 libxss1 libxrandr2 \
    libxdamage1 libxshmfence1 \
    fonts-noto-cjk fonts-noto-color-emoji fonts-liberation

# Docker（容器化部署时需要）
curl -fsSL https://get.docker.com | sudo sh
```

---

## 八、实施路线

```
阶段 1（现在）             阶段 2（跑通后）          阶段 3（有人要用时）
代码改动                   Docker 化                多用户
│                         │                        │
├─ URL 环境变量化           ├─ 写 Dockerfile          ├─ docker run 新容器
├─ cmd.exe 平台适配         ├─ 写 start.sh           ├─ 分配端口
├─ 配置目录跨平台           ├─ 构建镜像               ├─ 配置 Nginx 路由
├─ Agent SERVER_URL        ├─ 自己先用一个容器         ├─ 用户提供 API Key
├─ CORS 配置化             ├─ 验证功能完整            │
├─ 本地验证不 break         │                        │
│                         │                        │
预计 1-2 天                预计 1-2 天               每人 5 分钟
```

### 后续改进方向（按需）

| 方向 | 时机 | 内容 |
|------|------|------|
| deploy.sh 脚本 | 阶段 2 后 | 一键更新：`git pull → docker build → 滚动重启容器` |
| systemd 自启 | 部署后立即 | Docker 服务开机自启 |
| HTTPS | 有域名时 | Let's Encrypt 免费证书 + Nginx 配置 |
| 监控 | 稳定运行后 | 容器健康检查 + 资源监控 |
| 自动化测试 | 有精力时 | Core 算法测试 + Web 类型检查 |

---

## 九、潜在风险与应对

| 风险 | 严重度 | 应对方案 |
|------|--------|---------|
| Playwright 首次安装慢 | 中 | Dockerfile 中预装，镜像构建时完成 |
| 中文字体缺失（截图方框） | 低 | 安装 `fonts-noto-cjk` |
| 国内服务器访问 Anthropic API | 中 | 可能需要代理，容器中配置 `HTTP_PROXY` |
| 服务器内存不足 | 中 | 监控内存使用，按需升配 |
| Docker 镜像体积大（2-3GB） | 低 | 多阶段构建优化，或接受 |
| 代码更新后需重建镜像 | 低 | deploy.sh 脚本自动化 |
