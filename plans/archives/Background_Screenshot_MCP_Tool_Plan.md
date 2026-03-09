# 后台截图 MCP 工具实施计划

## 目标
- 新增后台截图 MCP 工具，面向 layout-agent 使用。
- 默认保存到 `projectPath/screenshots`，返回保存后的完整路径。
- 更新 layout-agent 工作流指导，明确不同任务类型的截图时机。

## 约束
- 仅开放参数：`projectPath` + `viewport`（支持单张与批量）。
- 默认使用 Agent 图层预设（`layerPreset=Agent`），其他参数暂不开放。
- 仅当任务需要时调用；`generate` 前后必须截图。

## 实施步骤
1. 在 `BIMCanvas.Agent/src/mcp/canvas.py` 新增截图工具：
   - 支持单张/批量请求。
   - 调用后台 API (`/api/screenshot/render`、`/api/screenshot/render-batch`)。
   - 将返回的 `imageData` 保存到 `projectPath/screenshots`。
2. 更新 MCP Server 注册与白名单：
   - `create_sdk_mcp_server(...)` 增加新工具。
   - `CANVAS_ALLOWED_TOOLS` 增加新工具名。
3. 更新 `get_workflow_guide`：
   - 增加截图工具的前置/后置调用规则（query/edit/generate）。
4. 限制仅 layout-agent 使用：
   - `C:\Users\huhaonan\.bimcanvas\agents\layout-agent.md` 工具白名单中加入新工具。
5. 手动验证：
   - 触发单张与批量截图，检查返回路径与文件落盘。

## 验收标准
- 工具调用成功后仅返回截图文件的完整路径。
- 文件落盘路径为 `projectPath/screenshots`。
- layout-agent 指南中明确截图时机规则。
