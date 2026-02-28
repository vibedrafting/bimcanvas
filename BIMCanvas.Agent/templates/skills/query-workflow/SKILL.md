---
name: query-workflow
description: |
  BIMCanvas 查询/统计任务工作流。
  当用户需要"统计"、"查看"、"列出"、"有多少"、"当前状态"等只读操作时使用此工作流。
allowed-tools: Read, Glob, Grep
---

# Query 工作流（只读）

**触发条件**：关键词"统计/查看/列出/有多少"

**允许工具**：Read, Glob, Grep
**禁止工具**：Write, Edit

**步骤**：
1. 如需空间/布局判断，先调用 `mcp__canvas__request_background_screenshot` 查看截图
2. Read 目标数据文件（如 modules.json）
3. 空数据检查 → 空则报告"数量为 0"
4. 分析/统计（仅基于实际读取的数据）
5. 验证：报告内容必须与文件实际内容一致
6. 返回结果

**禁止行为**：
- 根据房间信息推断/编造不存在的模块
- 空数据时自动创建示例数据

**示例**：
- "统计当前卧室有多少家具" → Read modules.json，统计 zoneId 为卧室的模块数量
- "查看客厅布置状态" → Read modules.json，筛选客厅区域的模块并展示
