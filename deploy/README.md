# deploy 目录说明

> 本目录集中存放 BIMCanvas 的 Docker 镜像、Compose 编排、网关配置和部署模板。

---

## 目录目标

这个目录解决四件事：

1. 定义镜像怎么构建
2. 定义容器启动时要做什么
3. 定义不同环境如何编排容器
4. 提供部署时需要复制的配置模板

当前采用的是 `base + overlay` 结构：

- `docker-compose.yml`：公共 base
- `docker-compose.local.yml`：本机 smoke test overlay
- `docker-compose.server.yml`：Linux 服务器 overlay

---

## 文件清单

### 镜像与启动入口

- `Dockerfile`
  - 多阶段构建文件
  - 产出 `server-runtime` / `agent-runtime` 两个最终镜像 target

- `start.sh`
  - Server 容器入口脚本
  - 初始化 `/data`
  - 根据首次启动环境变量补齐 `/data/*.json`
  - 最终启动发布版 `BIMCanvas.Server`

- `start-agent.sh`
  - Agent 容器入口脚本
  - 等待 `/data` 下运行时配置准备完成
  - 根据 `server_config.json` 判断直连 / CCR 模式后启动 Agent

### Compose 编排

- `docker-compose.yml`
  - 公共服务定义
  - 定义 `instance1` / `instance2` Server 服务以及对应的 `instance1-agent` / `instance2-agent`
  - 不直接暴露宿主机端口

- `docker-compose.local.yml`
  - 本机 Docker 快测用
  - 只给 `instance1` 暴露本地 HTTP 端口
  - 默认读取 `BIMCANVAS_HTTP_PORT`

- `docker-compose.server.yml`
  - Linux 服务器部署用
  - 为 `instance1`、`instance2` 暴露 `6001`、`6002`
  - 追加 `gateway` 服务

### 网关配置

- `nginx.server.conf`
  - `gateway` 使用的 Nginx 配置
  - 所有对外流量都转发到 Server
  - `/agent/` 也经由 Server 同源代理，而不是直连 Agent 容器

### 模板文件

- `.env.local.example`
  - 本机 Docker 模板
  - 主要配置宿主机数据路径和本地访问端口

- `.env.server.example`
  - Linux 服务器模板
  - 主要配置宿主机数据根目录

- `instance.env.example`
  - 单个实例的 bootstrap 模板
  - 复制后生成 `instance-1.env`、`instance-2.env`
  - 只负责首次启动的缺省值，不是长期配置真源

### 实际运行文件

- `.env`
  - 当前机器实际使用的 Compose 环境文件
  - 由 `docker compose` 自动读取

- `instance-1.env`
- `instance-2.env`
  - 当前实例实际使用的 bootstrap 文件
  - 会被 `docker-compose.yml` 里的 `env_file` 读取

---

## 组合方式

### 本机 smoke test

先复制模板：

```powershell
Copy-Item deploy/.env.local.example deploy/.env
Copy-Item deploy/instance.env.example deploy/instance-1.env
```

启动：

```powershell
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.local.yml up -d --build instance1
```

访问：

- `http://127.0.0.1:16001/`

### Linux 服务器部署

先复制模板：

```bash
cp deploy/.env.server.example deploy/.env
cp deploy/instance.env.example deploy/instance-1.env
cp deploy/instance.env.example deploy/instance-2.env
```

启动：

```bash
docker compose -f deploy/docker-compose.yml -f deploy/docker-compose.server.yml up -d --build instance1 instance2 gateway
```

---

## 维护约定

- `.env`、`instance-1.env`、`instance-2.env` 是实际运行文件，不是共享模板
- 长期应用配置真源是容器挂载目录 `/data/*.json`，不是 `instance-*.env`
- 容器启动时会对旧版 `/data/server_config.json` 做兼容迁移；若检测到历史“Server 内嵌 Agent”拓扑，会自动收敛到当前 Compose 指定的外部 Agent 地址
- 修改 Linux shell 脚本时必须保持 `LF` 换行，避免容器启动失败
- 端口、本地路径这类“宿主机差异”优先放进 `.env` 和 compose overlay，不要塞进应用配置

---

## 相关文档

- [`docs/Doc_Docker_Local_SmokeTest.md`](../docs/Doc_Docker_Local_SmokeTest.md)
- [`docs/Doc_Docker_Linux_Deployment.md`](../docs/Doc_Docker_Linux_Deployment.md)
- [`docs/Doc_Docker_Deployment_Framework.md`](../docs/Doc_Docker_Deployment_Framework.md)
