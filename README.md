<div align="center">

# BIMCanvas

**连接 AI 与 Revit 的设计平台：自然语言 → AI 设计方案 → 可编辑 BIM 模型**

[![License](https://img.shields.io/badge/License-Apache_2.0-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/status-pre--release-orange.svg)](#-status)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.10+-3776AB)](https://www.python.org/)
[![Vue](https://img.shields.io/badge/Vue-3-4FC08D)](https://vuejs.org/)

[Documentation](docs/README.md) · [Architecture](docs/Architecture.md) · [在线总览](https://bimcanvas.com/bimcanvas-overview.html)

</div>

---

![BIMCanvas](docs/images/hero-overview.gif)

## ⚠️ Status

**Pre-release。** 当前可用：Revit 户型导出、自然语言 AI 设计（确定性 Workflow 五段流）、Web 实时画布协作、多方案指针式采纳、平台 / 插件系统（首个领域：室内布置）。开发中：Revit 回写（方案 → Revit）、Docker 化部署。

- ✅ 适合：本地体验、二次开发、集成实验、内部研究
- ❌ 不建议：生产部署、关键设计交付

## Why BIMCanvas

Revit 里改一处家具，往往要走"找族 → 选参数 → 调位置 → 对齐 → 刷视图"五个动作——设计师的大块时间消耗在重复制图上。BIMCanvas 让 AI 接管这部分：你写自然语言，AI 落出符合空间逻辑的设计方案，Web 上直接调整，最终回到 Revit 作为可编辑的 BIM 模型。

理念上这是一次 **"vibe drafting"** ——像用自然语言写代码那样"写"空间设计：设计师专注关键决策，AI 完成繁重的落地制图。所有项目数据以 Git 仓库形式管理，多方案平级共存、"采纳即翻指针"，决策可追溯、可回滚。

架构上，BIMCanvas 是 **通用平台基座 + 可插拔领域插件**：平台不含任何领域知识，每个垂直领域（室内布置、MEP、点位……）封装为独立插件。**室内布置（interior-layout）是首个、也是当前唯一的领域插件**。

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js](https://nodejs.org/) ≥ 18
- [Python 3.10+](https://www.python.org/)（Agent 服务）
- [Git](https://git-scm.com/)

### Run（开发态）

```bash
git clone <repo-url>
cd BIMCanvas
dotnet run --project BIMCanvas.Server
```

默认行为：

- Server API → `http://localhost:5000`
- Web 前端 → `http://localhost:5173`（自动打开浏览器）
- Agent 服务后台启动（端口 8865，统一经 Server `/agent` 代理，不直连）

首次启动会在 `%USERPROFILE%\Documents\BIMCanvas\` 下生成配置模板。LLM 连接（`apiKey` / `baseUrl`）写入 `config.dev.local.json`（或在设置 UI 里填）；CCR 路由配置写入 `ccr_config.dev.local.json`。

### 其他启动模式

- **Windows 发布态**：`dotnet publish BIMCanvas.Server -c Release -o publish`，双击 `publish/BIMCanvas.Server.exe`
- **Linux Docker**：开发中（部署文档暂未公开）

## Features

- **自然语言 → 设计方案**：AI 解析意图、Server 计算几何、结果落盘为 JSON
- **平台 + 插件架构**：平台基座零领域知识，业务封装为可插拔领域插件（粘贴 GitHub URL 即可安装）
- **确定性 Workflow 编排**：五段流（感知 → 规划推演 → 多方案 → 落地 → 对比），代码控制流程 + 粗粒度 Agent 做领域判断
- **多方案指针式采纳**：候选平级共存，采纳 = 翻指针（零复制、零删除、可回溯）
- **三层文件驱动架构**：`baseline/` 只读 · `computed/` 派生 · `schemes/` 可写——详见 [Schema](docs/Schema.md)
- **2.5D Web 画布**：Vue 3 + Three.js 实时编辑，人机协作
- **Claude Agent SDK 驱动**：LLM 经可配置 `baseUrl` / CCR 路由（默认 Claude）
- **Canvas-MCP 工具接口**：Agent 与 Server 之间标准化通信——详见 [Arch_Plugin](docs/Arch_Plugin.md)
- **Git 原生版本管理**：每次 AI 决策进 Git 历史，可追、可 diff、可回滚
- **Revit 双向同步**：导出（→ `.bcp`）已实现；回写到 Revit 开发中

## Architecture

五个子系统协作，通过 REST / SignalR / SSE / MCP 互联（解决方案 `BIMCanvas.sln` 含 Core / Server / Revit 三个 .NET 项目）：

| 子系统 | 职责 | 运行时 |
|---|---|---|
| [**Core**](BIMCanvas.Core/README.md) | 数据模型 · 空间算法（碰撞 / 验证 / 几何转换 / Zone） | .NET Standard 2.0 |
| [**Server**](BIMCanvas.Server/README.md) | 状态管理 · 几何计算 · Git Worktree · Canvas-MCP · 通信中枢 | .NET 8 |
| [**Agent**](BIMCanvas.Agent/README.md) | AI 决策 · 意图解析 · Workflow 编排（含 core-base 平台插件） | Python 3.10+ |
| [**Web**](BIMCanvas.Web/README.md) | 画布渲染 · 拖拽编辑 · AI 对话 UI | Vue 3 + Vite |
| [**Revit**](BIMCanvas.Revit/README.md) | 户型导出 · 方案回写 (WIP) | .NET FW 4.7.2 |

领域插件（如 `interior-layout`）是**独立 GitHub 仓库**，通过 install 流程下载、信任后激活，不在主仓库内。平台 / 插件机制详见 [docs/Arch_Plugin.md](docs/Arch_Plugin.md)。

详细架构：[docs/Architecture.md](docs/Architecture.md) · [.bcp 项目格式](docs/Schema.md) · [Workflow 执行](docs/Arch_Workflow.md)

## Project Structure

```
BIMCanvas/
├── BIMCanvas.Core/      数据模型 + 空间算法（.NET Standard 2.0）
├── BIMCanvas.Server/    REST + SignalR + SSE + Canvas-MCP（.NET 8）
├── BIMCanvas.Agent/     AI 决策（Python）+ core-base 平台插件
├── BIMCanvas.Web/       Vue 画布前端
├── BIMCanvas.Revit/     Revit 插件（.NET FW 4.7.2）
├── libs/                第三方库
└── docs/                架构 / 设计 / 数据格式文档
```

## Documentation

| 主题 | 文档 |
|---|---|
| 整体架构 | [docs/Architecture.md](docs/Architecture.md) |
| `.bcp` 项目格式 | [docs/Schema.md](docs/Schema.md) |
| 空间几何与约束 | [docs/Arch_Spatial.md](docs/Arch_Spatial.md) |
| Workflow 执行架构 | [docs/Arch_Workflow.md](docs/Arch_Workflow.md) |
| 设计交付物模型 | [docs/Arch_Design_Delivery.md](docs/Arch_Design_Delivery.md) |
| 平台 / 插件体系 | [docs/Arch_Plugin.md](docs/Arch_Plugin.md) |
| Agent↔Web 流协议 | [docs/Arch_Stream_Protocol.md](docs/Arch_Stream_Protocol.md) |
| SDK 参数配置 | [docs/Doc_SDK_Config.md](docs/Doc_SDK_Config.md) |
| 完整索引 | [docs/README.md](docs/README.md) |

## Roadmap

- ✅ 文件驱动 `.bcp` 项目格式（三层数据 + Git 原生）
- ✅ 平台基座 + 可插拔领域插件系统
- ✅ 确定性 Workflow 五段流编排（感知 → 规划推演 → 多方案 → 落地 → 对比）
- ✅ 多方案指针式采纳（候选平级共存、采纳 = 翻指针）
- ✅ Git Worktree 多方案并行
- ✅ 网格选择集
- 🔶 Server Docker 化部署（开发中）
- ⬜ 多分区并行派发稳态化
- ⬜ 可视化 / 选择性合并
- ⬜ Revit 双向同步（回写家具到 Revit）
- ⬜ 更多领域插件（MEP / 点位 / 施工序列……）

## Contributing

欢迎 PR 与 Issue 反馈。当前尚无正式 `CONTRIBUTING.md`——可以从 [docs/README.md](docs/README.md) 入手熟悉架构，或在 Issues 里讨论想法。

## License

[Apache License 2.0](LICENSE) · Copyright 2026 BIMCanvas Contributors
