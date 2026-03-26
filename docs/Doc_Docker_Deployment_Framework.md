# BIMCanvas Docker 打包与运行框架说明

> 文档定位：说明当前仓库中“Docker 打包”和“Docker 运行”的完整框架。
> 重点覆盖：运行依赖分层、镜像构建流程、容器启动流程、`deploy/` 目录职责。
> 当前状态：单镜像本地运行链路已打通；服务器部署部分暂时只保留占位。

---

## 1. 文档范围

本文只覆盖两部分：

1. 当前已经落地的 Docker 打包与运行机制
2. 后续服务器部署需要承接的框架边界

本文不展开：

- 远程 Linux 服务器的正式部署步骤
- 域名、HTTPS、发布自动化
- 统一配置 UI 的后续实现细节

相关参考：

- `plans/运行依赖分级表.md`
- `plans/Docker_Deployment_Implementation_Plan.md`
- `deploy/`
- 仓库根目录 `.dockerignore`

---

## 2. 当前实现边界

### 2.1 已实现

- 基于 `ubuntu:22.04` 的单镜像构建
- 镜像内预装 `.NET 8`、`Git`、`Node.js`、`Python 3`、Agent 依赖、`claude`、`ccr`
- Web 前端在构建期产出 `dist/`，由 Server 在生产模式直接托管
- Agent 使用独立 venv，并作为容器内默认 Python 解释器
- Playwright Chromium 已在镜像构建期安装
- 容器默认以生产模式启动 Server，并由 Server 拉起 Agent
- 单实例链路已验证：`docker build`、`docker run`、`/health`、页面加载、`.bcp` 上传、AI 对话、后台截图

### 2.2 未实现

- 面向远程服务器的正式部署流程
- 多实例编排的完整回归验证
- Nginx 同源 `/agent` 代理的最终收口
- 统一配置 UI

---

## 3. 运行依赖分层

`plans/运行依赖分级表.md` 是这套 Docker 方案的基础文档。它的价值不在于简单罗列依赖，而在于回答三个问题：

1. 这项依赖是不是运行时必需
2. 这项依赖是否要打进 Docker 镜像
3. 缺失后会影响哪一段链路

### 3.1 分层思路

当前仓库把依赖分成五类：

| 层级 | 典型依赖 | 含义 |
|------|----------|------|
| 核心启动依赖 | `.NET 8`、`Git`、`Node.js` | 缺失后主进程或主链路无法成立 |
| AI 主流程依赖 | `Python`、Agent Python 包、`claude`、AI 凭据 | 主要决定 AI 对话/生成链路能否工作 |
| 部分功能依赖 | `Playwright Chromium` | 只影响局部能力，例如后台截图 |
| 可选 AI 能力 | `CCR / claude-code-router` | 只在启用 CCR 网关模式时必需 |
| 部署场景依赖 | `Docker Engine`、`Nginx` | 属于宿主机或编排层，不属于应用本体 |

### 3.2 这张表对 Docker 设计的实际影响

它直接决定了本轮镜像策略不是“最小运行镜像”，而是“全功能单镜像”：

- 既包含 Server 生产运行所需内容
- 也包含 Agent 运行时依赖
- 还包含 `claude` 与 `ccr`
- 同时包含 Playwright Chromium 和中文字体

这样做的目的不是追求最小体积，而是先把阶段二验收打通：

- 启动即具备 AI 能力
- 启动即具备截图能力
- 不把关键能力推迟到容器首次运行时再安装

### 3.3 哪些东西明确不进镜像

根据该分级表，以下内容应保持“外部提供”：

- API Key、Provider Base URL 等敏感配置
- `/data` 下的实例数据和配置
- Nginx 反向代理能力
- Revit 子系统

这也是为什么当前镜像只提供应用能力，不承诺持久化业务数据。

---

## 4. Docker 打包总体流程

当前打包入口是：

```bash
docker build -t bimcanvas:local -f deploy/Dockerfile .
```

这条命令的关键点是：

- `-f deploy/Dockerfile` 指定构建文件
- 最后的 `.` 表示构建上下文是仓库根目录
- 由于上下文是根目录，因此真正生效的忽略规则是根目录 `.dockerignore`

### 4.1 构建上下文与 `.dockerignore`

根目录 `.dockerignore` 的作用是控制“哪些文件会被送进 Docker build context”。

当前它主要做四件事：

1. 排除 `.git`、`.vs`、`.vscode`、`bin/`、`obj/`、`node_modules/` 等无关内容
2. 减少 build context 体积
3. 避免把本机环境污染带进镜像
4. 保留模板中必需的 `.gitignore`

这里有一个关键实现细节：

- 仓库全局排除了 `**/.gitignore`
- 但又显式保留了 `!BIMCanvas.Server/Templates/**/.gitignore`

这一步很重要，因为项目模板初始化依赖这些占位文件。如果它们在 build context 中被过滤掉，容器启动后会出现模板缺失问题。

### 4.2 Dockerfile 分阶段说明

当前 `deploy/Dockerfile` 的构建流程可以理解为 10 个步骤。

#### Step 1：选择基础镜像

- 基础镜像：`ubuntu:22.04`
- 理由：与当前 Linux 依赖兼容性较好，便于安装 Playwright 所需系统库

#### Step 2：安装系统级运行依赖

这一层安装：

- 基础工具：`curl`、`wget`、`git`、`gnupg`
- Python 运行环境：`python3`、`python3-venv`、`python3-pip`
- Playwright 运行库
- 中文字体与 emoji 字体

同时加入了重试逻辑，主要是为了缓解 Ubuntu 软件源偶发 `503` 导致的构建失败。

#### Step 3：安装 .NET 8

通过 `dotnet-install.sh` 安装到 `/usr/share/dotnet`，并链接到 `/usr/bin/dotnet`。

这样镜像内可以直接执行：

```bash
dotnet
```

#### Step 4：安装 Node.js 20

通过 NodeSource 源安装 `nodejs`，并同样加入重试逻辑。

当前之所以保留 Node，不只是为了前端构建，还因为：

- 需要安装 `@anthropic-ai/claude-code`
- 需要安装 `claude-code-router`
- 需要用 Node 执行 Playwright CLI 安装 Chromium

#### Step 5：安装 Claude CLI 与 CCR

当前镜像会全局安装：

- `@anthropic-ai/claude-code`
- `claude-code-router`

并在构建阶段直接校验：

- `claude --version`
- `ccr --version`

这样可以尽量在 build 阶段暴露问题，而不是等到容器运行后才发现 AI CLI 不可用。

#### Step 6：复制仓库到 `/app`

Dockerfile 中使用：

```dockerfile
WORKDIR /app
COPY . /app
```

这一步之后，容器内应用代码的主目录就是 `/app`。

后续所有构建与启动动作都以 `/app` 为代码根目录。

#### Step 7：构建 Server Release

执行：

```bash
dotnet restore BIMCanvas.Server
dotnet build BIMCanvas.Server -c Release --no-restore
```

产物会输出到 `BIMCanvas.Server/bin/Release/net8.0/`。

容器启动时最终运行的也是这套发布产物，而不是 `dotnet run`。

#### Step 8：构建 Web 静态资源

执行：

```bash
cd BIMCanvas.Web && npm ci && npm run build
```

构建结果是 `BIMCanvas.Web/dist/`。

这意味着：

- Vite dev server 只属于开发态
- Docker 生产态使用的是静态构建产物 `dist/`
- Server 在生产模式下直接托管 `dist/`

#### Step 9：创建 Agent venv 并安装 BIMCanvas.Agent

执行：

```bash
cd BIMCanvas.Agent
python -m venv venv
./venv/bin/pip install --upgrade pip
./venv/bin/pip install -e .
python -c "import claude_agent_sdk"
```

这里的关键不是“镜像里装过 Python 包”，而是：

- 要有独立 venv
- 运行时默认 Python 必须指向这个 venv

否则会出现“镜像里明明安装过，但容器实际启动时 import 不到”的问题。

#### Step 10：安装 Playwright Chromium

当前没有走旧方案里 `dotnet tool install Microsoft.Playwright.CLI --version ...` 的路线，而是改成：

- 直接使用项目构建输出目录里的 Playwright CLI
- 用 Node 执行该 CLI 安装 Chromium

这样更贴近项目自身实际依赖，也避开了某些 NuGet 版本不可用的问题。

---

## 5. 容器运行总体流程

当前运行入口是：

```bash
docker run --rm -p 5000:5000 -p 8865:8865 bimcanvas:local
```

### 5.1 这条命令的实际意义

- 启动的是本地 Docker 中的 `bimcanvas:local` 镜像
- 运行环境是容器内的 Ubuntu 用户态
- 应用模式是生产模式
- 不是 VS 本地开发模式

### 5.2 容器内两个关键目录

运行时必须区分两个路径：

| 路径 | 含义 |
|------|------|
| `/app` | 应用代码与构建产物目录 |
| `/data` | 实例配置、项目数据、截图等持久化目录 |

可以简单理解为：

- `/app` 是镜像内容
- `/data` 是实例状态

### 5.3 `start.sh` 的职责

`deploy/start.sh` 是容器的入口脚本。它负责的事情很多，不只是“启动 Server”。

#### 第一层：设置运行时环境变量

默认设置包括：

- `BIMCANVAS_HOME=/data`
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://0.0.0.0:5000`
- `SERVER_HOST=0.0.0.0`
- `BIMCANVAS_WEB_URL=http://localhost:5000`
- `BIMCANVAS_WEB_DIST=/app/BIMCanvas.Web/dist`
- `BIMCANVAS_PYTHON_COMMAND=/app/BIMCanvas.Agent/venv/bin/python`

这些默认值决定了容器内：

- Server 监听 `5000`
- Agent 监听 `8865`
- 截图服务访问本容器中的 Server
- Agent 默认使用自身 venv

#### 第二层：支持命令透传

如果 `docker run` 在镜像名后面追加了命令，例如：

```bash
docker run --rm bimcanvas:local which python
```

入口脚本会直接执行这个命令，而不会启动整套 Server/Agent。

这是当前镜像非常实用的能力，因为它让容器验证变得很直接。

#### 第三层：初始化 `/data`

脚本会在 `/data` 下初始化：

- `Projects/`
- `screenshots/`
- `server_config.json`
- `web_config.json`
- `config.json`
- `ccr_config.json`
- `BIMCANVAS.md`
- `agents/`
- `skills/`
- `.claude-plugin`

初始化策略是：

- 目标文件不存在时才复制模板
- 已存在时不覆盖

这保证了“首次启动可自举，后续重启不抹掉用户数据”。

#### 第四层：用环境变量补齐运行时配置

`start.sh` 内嵌了一段 Python 脚本，负责做运行期配置整理：

- 把 `pythonCommand` 写回 `server_config.json`
- 按 `CCR_ENABLED` 决定使用直连模式还是 CCR 模式
- 在直连模式下把 `ANTHROPIC_API_KEY`、`ANTHROPIC_BASE_URL`、`ANTHROPIC_MODEL` 注入 `config.json`
- 在 CCR 模式下根据 `CCR_API_KEY`、`CCR_API_BASE` 生成 `/tmp/ccr_runtime.json`

这一步的作用是把“外部环境变量”转成“容器内实际生效的配置文件”。

#### 第五层：启动发布版 Server

脚本最后执行的是：

```bash
dotnet /app/BIMCanvas.Server/bin/Release/net8.0/BIMCanvas.Server.dll
```

这很关键，因为它意味着容器内运行的是已编译发布产物，而不是开发态 `dotnet run`。

这样可以避免：

- 误回 Development 模式
- 重新编译
- 再次启动 Vite dev server

---

## 6. `deploy/` 目录文件职责

`deploy/` 目录是 Docker 与后续服务器部署的集中入口。当前文件职责如下。

### 6.1 `deploy/Dockerfile`

作用：

- 定义镜像构建过程
- 负责把仓库代码和运行依赖打包成单镜像
- 在 build 阶段提前完成大部分依赖校验

它决定了“镜像里有什么能力”。

### 6.2 `deploy/start.sh`

作用：

- 作为容器 `ENTRYPOINT`
- 初始化 `/data`
- 处理环境变量与运行期配置落盘
- 支持命令透传
- 最终启动生产模式 Server

它决定了“容器一启动会做什么”。

### 6.3 `deploy/instance.env.example`

作用：

- 提供实例级环境变量模板
- 约定哪些值应该由部署者在容器外部注入

它主要承载：

- `ANTHROPIC_API_KEY`
- `CCR_ENABLED`
- `CCR_API_KEY`
- `CCR_API_BASE`
- 其他可选运行时覆盖项

它决定了“哪些配置不应该固化进镜像，而应该在实例层注入”。

### 6.4 `deploy/docker-compose.yml`

作用：

- 提供多实例编排初稿
- 当前示例中定义了 `alice`、`bob` 和 `nginx` 三个服务
- 让每个实例拥有独立的 `/data`

它目前代表的是“未来服务器多实例部署骨架”，不是已完整验证的最终方案。

### 6.5 `deploy/nginx.conf`

作用：

- 提供 Nginx 反向代理初稿
- 支持：
  - `/` 转发到 Server 的前端页面
  - `/api/` 转发到 Server API
  - `/hubs/` 转发到 SignalR
  - `/agent/` 转发到 Agent SSE / HTTP 接口

它的目标是把外部访问尽量收口到同源入口，但这一层目前还没完成正式回归。

### 6.6 `deploy/` 之外但必须一起理解的文件

虽然不在 `deploy/` 目录下，但以下文件和 Docker 方案强相关：

- `.dockerignore`
  - 决定 build context 内容
- `plans/运行依赖分级表.md`
  - 决定哪些依赖应进镜像，哪些应外置
- `plans/Docker_Deployment_Implementation_Plan.md`
  - 记录当前阶段状态与后续待做项

---

## 7. 打包产物与运行结果

完成 `docker build` 之后，镜像中已经具备：

- Server Release 产物
- Web `dist/`
- Agent venv 与 Python 包
- `claude`
- `ccr`
- Playwright Chromium
- 容器入口脚本

完成 `docker run` 之后，容器实例层会形成：

- `/data/server_config.json`
- `/data/web_config.json`
- `/data/config.json`
- `/data/ccr_config.json`
- `/data/Projects/`
- `/data/screenshots/`

这两个层面必须分开理解：

- 镜像层回答“这台容器会不会跑”
- 实例层回答“这台容器跑起来后保存什么状态”

---

## 8. 推荐的本地 Docker 验证路径

### 8.1 构建镜像

```bash
docker build -t bimcanvas:local -f deploy/Dockerfile .
```

### 8.2 验证镜像内工具

```bash
docker run --rm bimcanvas:local which python
docker run --rm bimcanvas:local python -c "import claude_agent_sdk; print('ok')"
docker run --rm bimcanvas:local claude --version
docker run --rm bimcanvas:local ccr --version
```

### 8.3 启动单实例

```bash
docker run --rm -p 5000:5000 -p 8865:8865 bimcanvas:local
```

### 8.4 做基础烟测

```bash
curl http://localhost:5000/health
curl http://localhost:8865/health
```

再通过浏览器验证：

- 首页是否打开
- `.bcp` 是否可上传并加载
- AI 对话是否可用
- 截图是否可产出

---

## 9. 服务器部署框架（占位）

> 本节暂时只占位，不写实现细节。

### 9.1 宿主机准备

待实现。

### 9.2 多实例编排

待实现。

### 9.3 反向代理与域名

待实现。

### 9.4 Secrets 与持久化卷

待实现。

### 9.5 运维与更新流程

待实现。

---

## 10. 总结

当前这套 Docker 框架的核心思想是：

- 用一个“全功能单镜像”优先打通阶段二验收
- 把运行依赖按层级拆清楚，避免误把运行时问题当成构建问题
- 把代码与构建产物放在 `/app`
- 把配置与业务数据放在 `/data`
- 用 `deploy/start.sh` 统一承接容器启动时的初始化和配置注入
- 把服务器部署编排留到下一阶段，而不是在当前单实例可用性还未稳定时过早展开

因此，现阶段最重要的不是“服务器怎么发”，而是先明确：

- 哪些依赖已经被镜像承诺提供
- 哪些配置必须由实例外部注入
- 容器启动时到底做了哪些初始化动作
- `deploy/` 目录中的每个文件分别承担什么职责
