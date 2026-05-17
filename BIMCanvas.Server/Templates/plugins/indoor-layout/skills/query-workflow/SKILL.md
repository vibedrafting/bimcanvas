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
2. Read `schemes/zones.json` 定位目标分区 ID
3. 若目标 zone 无 `subZones`，Read `schemes/{zoneId}/modules.json`
4. 若目标 zone 有 `subZones`，聚合读取其所有叶子子分区的 `modules.json`
   - ❌ 禁止读取 `schemes/modules.json`（此路径不存在）
5. 空数据检查 → 空则报告"数量为 0"
6. 分析/统计（仅基于实际读取的数据）
7. 验证：报告内容必须与文件实际内容一致
8. 返回结果

**禁止行为**：
- 根据房间信息推断/编造不存在的模块
- 空数据时自动创建示例数据

**示例**：
- "统计当前卧室有多少家具" → Read zones.json 找到卧室分区 ID → 若有 subZones 则聚合叶子分区 modules.json → 统计模块数量
- "查看客厅布置状态" → Read zones.json 找到客厅分区 ID → Read 目标分区或其叶子分区 modules.json → 展示模块列表
