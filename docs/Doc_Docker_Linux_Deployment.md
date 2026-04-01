# BIMCanvas Linux 服务器 Docker 部署指南

> 文档定位：这是当前仓库唯一的 Linux 服务器 Docker 操作入口。
> 如果你想了解镜像、启动脚本、配置真源等设计背景，请再阅读 [Doc_Docker_Deployment_Framework.md](./Doc_Docker_Deployment_Framework.md)。

---

## 1. 部署边界

本文只覆盖 Linux 服务器部署：

- 使用 `deploy/docker-compose.yml`
- 使用 `deploy/nginx.conf`
- 使用 `deploy/.env.example`
- 使用 `deploy/instance.env.example`

本文不覆盖：

- Windows 本机 Docker 烟测
- HTTPS / Let's Encrypt
- CI/CD 与自动发布
- 额外的 compose 变体

当前默认部署基线是：

- `alice`、`bob` 两个应用实例
- `nginx` 作为同源反向代理入口
- 宿主机共享数据根目录固定为 `/srv/bimcanvas-data`

---

## 2. 前置条件

服务器需要满足：

- Linux x86_64
- Docker Engine
- Docker Compose Plugin
- 可用磁盘空间用于镜像、项目数据和截图
- 若要启用 Nginx 同源入口，需能开放 `80` 端口

推荐先预留这些访问方式：

- `6001` → `alice` 直连 Server
- `6002` → `bob` 直连 Server
- `80` → Nginx 同源入口

---

## 3. 服务器准备

以下命令以 Ubuntu 为例：

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo apt-get install -y docker-compose-plugin

sudo mkdir -p /srv/bimcanvas-data/alice /srv/bimcanvas-data/bob
sudo chown -R "$USER":"$USER" /srv/bimcanvas-data
```

然后把仓库放到服务器，例如：

```bash
git clone <your-repo-url> /opt/bimcanvas
cd /opt/bimcanvas/deploy
```

---

## 4. 配置文件准备

在 `deploy/` 目录下复制模板：

```bash
cp .env.example .env
cp instance.env.example instance-alice.env
cp instance.env.example instance-bob.env
```

### 4.1 `deploy/.env`

这个文件只负责宿主机共享路径，不放实例密钥。

```env
BIMCANVAS_DATA_ROOT=/srv/bimcanvas-data
```

### 4.2 `instance-*.env`

这个文件只负责实例首次启动时的初始化变量。

推荐默认使用直连模式，最小示例：

```env
ANTHROPIC_API_KEY=sk-ant-your-key-here
# ANTHROPIC_BASE_URL=https://your-provider-base-url
```

CCR 模式属于高级选项，只有当你已经准备好 `/data/ccr_config.json` 的 provider 结构时再启用：

```env
CCR_ENABLED=true
CCR_MODEL_FAMILY=sonnet
# CCR_API_KEY=your-provider-key
# CCR_API_BASE=https://your-provider-base-url
# CCR_PROVIDER_NAME=your-provider-name
```

重要约定：

- `instance-*.env` 只用于首次 bootstrap 和缺省值补齐
- 首次启动后，长期配置真源是 `/data/*.json`
- 常规维护优先通过首页“实例设置”或直接编辑 `/data/*.json`

---

## 5. Nginx 入口准备

当前 [deploy/nginx.conf](../deploy/nginx.conf) 默认使用：

- `alice.example.com`
- `bob.example.com`

在启用 `nginx` 服务前，你需要至少完成其中一种：

1. 把 `server_name` 改成你的真实域名，并完成 DNS 解析
2. 在测试环境通过 `/etc/hosts` 或 `curl -H "Host: ..."` 手动带上对应 Host

如果你还没准备域名，建议先只启动 `alice` / `bob`，直接通过 `6001` / `6002` 做烟测。

---

## 6. 启动顺序

推荐分两步启动。

### 6.1 先做实例烟测

```bash
docker compose up -d --build alice bob
```

检查状态：

```bash
docker compose ps
docker compose logs -f alice
docker compose logs -f bob
```

健康检查：

```bash
curl http://127.0.0.1:6001/health
curl http://127.0.0.1:6002/health
```

浏览器访问：

- `http://<server-ip>:6001`
- `http://<server-ip>:6002`

### 6.2 再启用 Nginx 同源入口

当 `server_name` 和域名准备好后，再启动：

```bash
docker compose up -d nginx
docker compose logs -f nginx
```

验证同源入口：

```bash
curl -H "Host: alice.example.com" http://127.0.0.1/
curl -H "Host: bob.example.com" http://127.0.0.1/
curl -H "Host: alice.example.com" http://127.0.0.1/agent/health
```

---

## 7. 首次启动后你会看到什么

`deploy/start.sh` 会在容器启动时自动处理这些事情：

- 发现 `/data/*.json` 缺失时，从模板复制缺省文件
- 根据 `instance-*.env` 把首次缺省值写入 `/data/*.json`
- 打印当前是首次 bootstrap 还是沿用现有配置
- 打印当前 AI 模式是直连还是 CCR

这意味着：

- 第一次启动前，改 `instance-*.env`
- 第一次启动后，改 `/data/*.json` 或设置 UI

不要把 `instance-*.env` 当长期配置入口反复维护。

---

## 8. 启动后固定验证流程

每次正式部署后，至少做完这组检查：

1. `docker compose ps`，确认容器都处于运行中
2. `docker compose logs -f alice` / `bob` / `nginx`，确认无致命错误
3. `curl http://127.0.0.1:6001/health`
4. `curl http://127.0.0.1:6002/health`
5. 浏览器打开实例页面
6. 上传一个 `.bcp` 项目
7. 发起一次 AI 对话
8. 验证后台截图是否成功产出

如果你已经启用了同源入口，再额外检查：

- `/api/`
- `/hubs/`
- `/agent/`

是否都能通过 Nginx 正常转发。

---

## 9. 常见问题

### 9.1 页面打不开或 80 端口返回异常

优先检查：

- `nginx.conf` 里的 `server_name` 是否已改成真实域名
- 是否用正确的 Host 头访问
- 如果还没准备域名，先用 `6001` / `6002` 直连验证

### 9.2 AI 不可用，日志提示缺少 API Key

说明当前是直连模式，但没有可用的 `ANTHROPIC_API_KEY`。

处理方式：

- 第一次启动前：补到 `instance-*.env`
- 第一次启动后：补到 `/data/config.json` 或设置 UI

### 9.3 CCR 已启用但仍不可用

通常是 `/data/ccr_config.json` 里没有有效 provider 条目。

处理方式：

- 预置一个有效的 `ccr_config.json`
- 或者启动后通过设置 UI 补齐 provider / router

### 9.4 数据目录没有成功初始化

检查：

- `.env` 里的 `BIMCANVAS_DATA_ROOT` 是否写对
- `/srv/bimcanvas-data` 是否有权限
- 容器日志中是否出现 bootstrap 相关报错

### 9.5 只想先跑一个实例

可以只启动 `alice`：

```bash
docker compose up -d --build alice
curl http://127.0.0.1:6001/health
```

等 `alice` 跑通后，再补 `bob` 和 `nginx`。

---

## 10. 相关文件

- [deploy/docker-compose.yml](../deploy/docker-compose.yml)
- [deploy/nginx.conf](../deploy/nginx.conf)
- [deploy/.env.example](../deploy/.env.example)
- [deploy/instance.env.example](../deploy/instance.env.example)
- [deploy/start.sh](../deploy/start.sh)
- [docs/Doc_Docker_Deployment_Framework.md](./Doc_Docker_Deployment_Framework.md)
