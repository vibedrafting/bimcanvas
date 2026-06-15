# BIMCanvas 技术文档

> 面向想理解 BIMCanvas 系统设计的工程师。产品定位与快速上手见[仓库根 README](../README.md)。

## 阅读动线

从 [Architecture.md](./Architecture.md)（总览枢纽）入手，按需深入：

```
Architecture（总览 · 组件 · 数据流 · 文档地图）
├ 数据契约   Schema · Arch_Design_Delivery
├ 核心机制   Arch_Spatial（空间几何）· Arch_Workflow（AI 编排）
├ 通信       Arch_Stream_Protocol
└ 扩展       Arch_Plugin · Doc_SDK_Config
```

## 文档清单

| 文档 | 内容 |
|------|------|
| [Architecture](./Architecture.md) | 系统架构总览：五大组件、数据流、文档地图 |
| [Schema](./Schema.md) | `.bcp` 数据格式字段级规范 |
| [Arch_Design_Delivery](./Arch_Design_Delivery.md) | 设计交付物模型：指针式多方案、Zone 递归嵌套、采纳=翻指针 |
| [Arch_Spatial](./Arch_Spatial.md) | 空间几何与约束：Y-up 坐标、OBB 规划师、几何转换、validate 两道防线 |
| [Arch_Workflow](./Arch_Workflow.md) | Workflow 执行架构：五层 / 五段流 / 确定性控制流 / 实测教训 |
| [Arch_Stream_Protocol](./Arch_Stream_Protocol.md) | Agent↔Web 实时流协议契约 |
| [Arch_Plugin](./Arch_Plugin.md) | 平台 / 插件体系：生命周期、安全模型、manifest、MCP 契约 |
| [Doc_SDK_Config](./Doc_SDK_Config.md) | Claude Agent SDK 参数配置 |

> 内部设计快照（不维护、可能过时）散在 `.dev/docs/`，判断现状以代码 + 本目录为准。
