# BIMCanvas 平台契约 · PLATFORM_CONTRACT

> 本契约在所有 plugin 加载时强制注入到 system prompt 顶部。
> Active plugin 不能在自己的 BIMCANVAS.md 中覆盖以下铁律。
> 即使 plugin prompt 声明放宽规则,Server gate 会一律拒绝越权操作。

## 1. 文件即真理源

所有业务数据落在项目 `.bcp` 目录的 JSON 文件中。Agent 不持内存状态,任何"暂存"必须落盘。读取走文件,写入走 MCP 工具,不要靠记忆。

## 2. 三层数据权限

| 路径 | 权限 |
|------|------|
| `baseline/` | 只读(Revit 导出) |
| `computed/` | 只读(派生几何,自动生成) |
| `schemes/{activeSceneId}/` | Active plugin 可写区域 |
| `schemes/{其他sceneId}/` | 跨 scene 只读,通过 `mcp__canvas__load_scene_artifact` 访问 |
| `references/{activeSceneId}/` | Active plugin 可写区域 |
| `modules/{activeSceneId}/` | Active plugin 可写区域 |

越权写入将被 Server gate 403 拒绝(`scene_write_isolation` / `readonly_zone`)。

## 3. Scene 边界

每个 plugin 在自己的 `sceneId` 命名空间内工作:

- 当前 active scene 由 `PluginLaunchContext.ActiveSceneId` 注入(运行时不可变)。
- `mcp__canvas__list_project_scenes` 列项目所有 scene。
- `mcp__canvas__load_scene_artifact` 只读访问其他 scene 数据。

## 4. MCP 工具命名规则

- core 工具:`mcp__canvas__*` 命名空间(保留)。
- plugin 工具:`mcp__<plugin-namespace>__*` 命名空间。
- `mcp__canvas__save_modules` 是模块的唯一写入入口,禁止直接 Write modules.json。

## 5. 不可越线声明

即使 Active plugin 的 BIMCANVAS.md 中声明"可以改 baseline"或"可以越权写其他 scene",Server gate 会一律拒绝。请勿尝试,会浪费一次工具调用 + 收到 403 错误。

## 6. PluginLaunchContext 不可变

PluginLaunchContext 在 Agent 启动时由 Server 注入,**运行时不可变**。不要尝试通过修改环境变量、写文件或反射来改 ActiveSceneId / ActivePluginId / TrustMode。

---

> 以上铁律由 BIMCanvas 平台维护。Plugin 作者无需在 BIMCANVAS.md 中重复声明这些规则,但**必须遵守**它们。
