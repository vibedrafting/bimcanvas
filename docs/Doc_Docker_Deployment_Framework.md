# BIMCanvas Docker 打包与运行框架说明

> 文档定位：说明当前仓库中 Docker 打包、容器运行、实例配置与后续服务器部署承接的整体框架。
> 重点覆盖：运行依赖分层、镜像构建流程、容器启动流程、`deploy/` 目录职责、实例配置真源。
> 当前状态：阶段一到三已完成；远程 Linux 服务器正式部署仍待阶段四实施。

---

## 1. 文档范围

本文覆盖两部分：

1. 当前仓库已经落地的 Docker 打包、运行与实例配置机制
2. 阶段四服务器部署需要承接的现有基础与边界

本文不展开：

- 远程服务器的完整上线 SOP
- 域名、HTTPS、发布自动化
- 外层“容器化单租户实例平台”的平台化设计

相关参考：

- `plans/Docker_Deployment_Implementation_Plan.md`
- `plans/archives/运行依赖分级表.md`
- `deploy/`
- 仓库根目录 `.dockerignore`

---

## 2. 当前实现边界

### 2.1 已落地

- 基于 `ubuntu:22.04` 的单镜像构建方案
- 镜像内预装 `.NET 8`、`Git`、`Node.js`、`Python 3`、Agent 依赖、`claude`、`ccr`
- Web 前端在构建期产出 `dist/`，由 Server 在生产模式直接托管
- Agent 使用独立 venv，并作为容器内默认 Python 解释器
- Playwright Chromium 已在镜像构建期安装
- 容器默认以生产模式启动 Server，并由 Server 自动拉起 Agent
- `deploy/start.sh` 已实现实例 bootstrap：仅在配置缺失时初始化 `/data`，后续重启不覆盖现有 JSON
- 统一配置 UI 已落地：首页“实例设置”可管理 `server/web/agent/ccr` 四组实例配置
- 单实例链路已验证：`docker build`、`docker run`、`/health`、生产静态页面加载、`.bcp` 上传、AI 对话、后台截图
- `deploy/docker-compose.yml` 与 `deploy/nginx.conf` 已提供多实例编排基础稿

### 2.2 阶段四待实施或待复核

- 远程 Linux 服务器正式部署
- 双实例编排的端到端回归
- Nginx 同源 `/agent` 代理的完整复核
- 基于最新镜像重新确认部分生产日志噪音是否已消失

这里的关键区别是：

- 阶段一到三已经形成代码与文档上的稳定基线
- 阶段四不是“从零设计”，而是把现有镜像、配置 UI、compose/nginx 基础带到远程服务器环境

---

## 3. 运行依赖分层

`plans/archives/运行依赖分级表.md` 是这套 Docker 方案的基础文档。它回答三个问题：

1. 这项依赖是不是运行时必需
2. 这项依赖是否应该打进 Docker 镜像
3. 缺失后会影响哪一段链路

### 3.1 分层思路

| 层级 | 典型依赖 | 含义 |
|------|----------|------|
| 核心启动依赖 | `.NET 8`、`Git`、`Node.js` | 缺失后主进程或主链路无法成立 |
| AI 主流程依赖 | `Python`、Agent Python 包、`claude`、AI 凭据 | 决定 AI 对话与生成链路能否工作 |
| 部分功能依赖 | `Playwright Chromium` | 只影响局部能力，例如后台截图 |
| 可选 AI 能力 | `CCR / claude-code-router` | 只在启用 CCR 网关模式时必需 |
| 部署场景依赖 | `Docker Engine`、`Nginx` | 属于宿主机或编排层，不属于应用本体 |

### 3.2 对镜像策略的实际影响

当前仓库采用的是“全功能单镜像”，而不是“极小运行镜像”：

- 包含 Server 生产运行所需内容
- 包含 Agent 运行时依赖
- 包含 `claude` 与 `ccr`
- 包含 Playwright Chromium 与中文字体

这样做的目标不是最小体积，而是先把单实例生产链路打通：

- 启动即具备 AI 能力
- 启动即具备截图能力
- 不把关键依赖推迟到容器首次运行时再安装

### 3.3 明确不进镜像的内容

以下内容保持外部提供或实例持久化：

- API Key、Provider Base URL 等敏感配置
- `/data` 下的实例数据和配置
- Nginx 反向代理能力
- Revit 子系统

这也是为什么镜像只承诺“应用能力”，不承诺“业务数据持久化”。

---

## 4. Docker 打包总体流程

当前打包入口：

```bash
docker build -t bimcanvas:local -f deploy/Dockerfile .
```

这条命令的关键点：

- `-f deploy/Dockerfile` 指定构建文件
- 最后的 `.` 表示构建上下文是仓库根目录
- 因为上下文是根目录，真正生效的是仓库根目录 `.dockerignore`

### 4.1 `.dockerignore` 的作用

根目录 `.dockerignore` 主要负责：

1. 排除 `.git`、`.vs`、`.vscode`、`bin/`、`obj/`、`node_modules/` 等无关内容
2. 减少 build context 体积
3. 避免把本机环境污染带进镜像
4. 保留模板初始化所需的占位文件

关键实现细节：

- 仓库全局排除了 `**/.gitignore`
- 但显式保留了 `!BIMCanvas.Server/Templates/**/.gitignore`

如果这一步缺失，容器启动后可能因为模板目录中的占位文件丢失而初始化失败。

### 4.2 `deploy/Dockerfile` 的主要阶段

当前 `deploy/Dockerfile` 可以理解为以下几个阶段：

1. 基于 `ubuntu:22.04`
2. 安装系统级运行依赖与 Playwright 运行库
3. 安装 `.NET 8`
4. 安装 Node.js 20
5. 全局安装 `@anthropic-ai/claude-code` 与 `claude-code-router`
6. 复制仓库到 `/app`
7. 构建 Server Release
8. 构建 Web `dist/`
9. 创建 Agent venv 并安装本地包
10. 安装 Playwright Chromium

构建期会直接校验：

- `claude --version`
- `ccr --version`
- `python -c "import claude_agent_sdk"`

目的是尽量在 build 阶段暴露问题，而不是等容器运行后才发现关键依赖不可用。

---

## 5. 容器运行总体流程

当前最常用的本地运行入口：

```bash
docker run --rm -p 5000:5000 -p 8865:8865 bimcanvas:local
```

### 5.1 这条命令的实际意义

- 启动的是本地 Docker 中的 `bimcanvas:local` 镜像
- 运行环境是容器内的 Ubuntu 用户态
- 应用模式是生产模式
- Web 页面由 Server 托管的 `dist/` 提供，不依赖 Vite dev server

### 5.2 容器内两个关键目录

| 路径 | 含义 |
|------|------|
| `/app` | 应用代码与构建产物目录 |
| `/data` | 实例配置、项目数据、截图等持久化目录 |

可以理解为：

- `/app` 是镜像内容
- `/data` 是实例状态

### 5.3 `deploy/start.sh` 的职责

`deploy/start.sh` 是容器 `ENTRYPOINT`。它不是简单地“启动 Server”，而是负责统一承接实例启动语义。

#### 第一层：设置运行时环境变量

默认设置包括：

- `BIMCANVAS_HOME=/data`
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_URLS=http://0.0.0.0:5000`
- `SERVER_HOST=0.0.0.0`
- `BIMCANVAS_WEB_URL=http://localhost:5000`
- `BIMCANVAS_WEB_DIST=/app/BIMCanvas.Web/dist`
- `BIMCANVAS_PYTHON_COMMAND=/app/BIMCanvas.Agent/venv/bin/python`

这些默认值决定了：

- Server 在容器内监听 `5000`
- Agent 对容器网络监听 `8865`
- 截图服务访问当前容器中的 Server
- Agent 默认使用自身 venv

#### 第二层：支持命令透传

如果 `docker run` 在镜像名后面追加命令，例如：

```bash
docker run --rm bimcanvas:local which python
```

入口脚本会直接执行该命令，而不会启动整套 Server/Agent。

这使得镜像内工具链验证非常直接。

#### 第三层：初始化 `/data`

脚本会确保 `/data` 下具备以下实例资产：

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

这意味着：

- `instance.env` 只承担首次部署与缺省值补齐
- UI 已保存的配置在后续容器重启时不会被覆盖回旧值

#### 第四层：整理运行期配置

`start.sh` 内嵌 Python 脚本，会把外部环境变量转换成实例配置文件中的实际值。

当前真实语义是：

- 只有在对应 JSON 首次 bootstrap 时，才根据环境变量补齐默认值
- `server_config.json` 会写入 `pythonCommand`，并按 `CCR_ENABLED`、`CCR_MODEL_FAMILY` 补齐 CCR 相关默认配置
- 直连模式下，会在 `config.json` 中补齐 `ANTHROPIC_API_KEY`、`ANTHROPIC_BASE_URL`、`ANTHROPIC_MODEL`
- CCR 模式下，会在 `ccr_config.json` 首次 bootstrap 时补齐 provider 密钥与 base URL

因此，运行期配置真源分成两层：

- 首次启动前：`instance.env` 等环境变量负责 bootstrap
- 首次启动后：`/data/*.json` 才是长期真源

#### 第五层：启动发布版 Server

脚本最终执行：

```bash
dotnet /app/BIMCanvas.Server/bin/Release/net8.0/BIMCanvas.Server.dll
```

这保证容器内运行的是发布产物，而不是开发态 `dotnet run`。

结果是：

- 不会回退到 Development 模式
- 不会重新编译
- 不会再启动 Vite dev server

### 5.4 生产态访问模型

当前生产态访问模型已经清晰：

- `http://<host>:5000/` 由 Server 直接提供 Web 静态页面
- `http://<host>:5000/api/*` 由 Server 提供 REST API
- `http://<host>:5000/hubs/*` 由 Server 提供 SignalR
- `http://<host>:8865/*` 是 Agent 直连端口

阶段四的目标不是改变这个内部模型，而是在外层通过 `docker-compose.yml` 与 `nginx.conf` 收口成更稳定的同源入口：

- `/` → Server
- `/api/` → Server
- `/hubs/` → Server
- `/agent/` → Agent

---

## 6. `deploy/` 目录文件职责

`deploy/` 目录是 Docker 与后续服务器部署的集中入口。

### 6.1 `deploy/Dockerfile`

作用：

- 定义镜像构建过程
- 负责把仓库代码和运行依赖打包成单镜像
- 在 build 阶段提前完成关键依赖校验

它决定“镜像里有什么能力”。

### 6.2 `deploy/start.sh`

作用：

- 作为容器 `ENTRYPOINT`
- 初始化 `/data`
- 处理环境变量与运行期配置落盘
- 支持命令透传
- 最终启动生产模式 Server

它决定“容器一启动会做什么”。

### 6.3 `deploy/instance.env.example`

作用：

- 提供实例级环境变量模板
- 约定首次部署时哪些值应由部署者在容器外部注入

它当前的定位是：

- 首次实例创建
- 缺省值补齐
- 部署引导

它不是长期配置真源。实例创建完成后，日常配置维护应以 `/data/*.json` 和首页“实例设置”UI 为主。

### 6.4 `deploy/docker-compose.yml`

作用：

- 提供多实例编排基础稿
- 当前示例中定义了 `alice`、`bob` 和 `nginx`
- 让每个实例拥有独立的 `/data`

它当前代表“阶段四服务器部署的基础输入”，而不是已经完整回归通过的最终方案。

### 6.5 `deploy/nginx.conf`

作用：

- 提供 Nginx 反向代理基础稿
- 支持：
  - `/` 转发到 Server 页面
  - `/api/` 转发到 Server API
  - `/hubs/` 转发到 SignalR
  - `/agent/` 转发到 Agent SSE / HTTP 接口

它的目标是把外部访问收口到同源入口，供阶段四继续验证和部署。

### 6.6 `deploy/` 之外但必须一起理解的文件

- `.dockerignore`
  - 决定 build context 内容
- `plans/archives/运行依赖分级表.md`
  - 决定哪些依赖进镜像、哪些依赖外置
- `plans/Docker_Deployment_Implementation_Plan.md`
  - 记录阶段状态、验收现状与阶段四承接点

---

## 7. 配置真源与实例配置 UI

阶段三完成后，实例配置的职责边界已经明确。

### 7.1 四份实例配置文件

长期配置真源仍然是 `/data` 下的四份 JSON：

- `/data/server_config.json`
- `/data/web_config.json`
- `/data/config.json`
- `/data/ccr_config.json`

### 7.2 统一配置接口

Server 当前已经提供：

- `GET /api/settings`
- `PUT /api/settings`
- `POST /api/settings/restart`
- `GET /api/web_config`
- `POST /api/web_config`

其中：

- `/api/settings` 是新的聚合实例配置入口
- `/api/web_config` 继续保留为兼容入口

### 7.3 生效方式

- `web_config.json` 默认按热更新处理，保存后可立即生效
- `config.json`、`server_config.json`、`ccr_config.json` 默认按“保存后需重启实例”处理
- `/api/settings/restart` 通过优雅停机把重启责任交给 Docker `restart: unless-stopped`

这就是为什么阶段四部署时，`instance.env` 不应再被当作长期配置入口反复编辑。

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

- 首页是否正常打开
- `.bcp` 是否可上传并加载
- AI 对话是否可用
- 后台截图是否可产出

---

## 9. 阶段四承接点

阶段四不是重新发明部署方案，而是基于当前仓库的既有产物向远程 Linux 服务器平移：

- 使用 `deploy/Dockerfile` 构建统一镜像
- 使用 `deploy/start.sh` 承接实例 bootstrap 和生产启动
- 使用 `deploy/instance.env.example` 初始化每个实例
- 使用 `deploy/docker-compose.yml` 编排多实例
- 使用 `deploy/nginx.conf` 提供同源反向代理入口
- 使用首页“实例设置”维护实例内部应用配置

阶段四需要重点验证的，是“服务器环境中的组合行为”：

- 多实例隔离
- 同源 `/agent` 代理
- 配置保存与实例重启
- 卷挂载与数据持久化

---

## 10. 总结

当前这套 Docker 框架的核心思想是：

- 用一个“全功能单镜像”先打通单实例生产链路
- 把代码与构建产物放在 `/app`
- 把实例配置与业务数据放在 `/data`
- 用 `deploy/start.sh` 把 bootstrap、环境变量注入和生产启动统一起来
- 把长期配置真源收敛到 `/data/*.json` 与实例配置 UI
- 把服务器部署编排留到阶段四，而不是继续在单实例基线不清晰时扩散复杂度

因此，现阶段最重要的不是重新讨论 Docker 方案，而是基于这条已经稳定的基线继续完成远程服务器部署与多实例回归。
