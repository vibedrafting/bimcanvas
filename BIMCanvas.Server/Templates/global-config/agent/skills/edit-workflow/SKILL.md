---
name: edit-workflow
description: |
  BIMCanvas 编辑任务工作流。
  当用户需要"移动"、"删除"、"旋转"、"调整"等单一修改操作时使用此工作流。
---

# Edit 工作流（单一修改）

**触发条件**：关键词"移动/删除/旋转/调整"

**步骤**：
1. 视需要在修改前调用 `mcp__canvas__request_background_screenshot` 查看截图
2. Read `schemes/zones.json` 定位目标分区 ID
3. 若目标 zone 无 `subZones`，Read `schemes/{zoneId}/modules.json`
4. 若目标 zone 有 `subZones`，聚合读取其所有叶子子分区的 `modules.json`
5. 定位目标模块
6. 执行修改操作
   - 修改方向时，写入 `facing` 对象：`{ "value": [x, y] | null, "semantic": string | null }`
   - `value` 是常规读取阶段的方向真理；`semantic` 是 AI 语义输入槽，**推荐**默认修改 `semantic`
   - 示例：`"facing": { "value": null, "semantic": "north" }`
   - 若 `value` 与 `semantic` 同时存在：常规读取只认 `value`；`validate_layout` 会用有效 `semantic` 覆盖 `value` 并清空 `semantic`
7. 预检约束（门前净空、通道宽度）
8. Write 只保存到目标模块所在的叶子分区 `modules.json`
9. 调用 `mcp__canvas__validate_layout()` 编译检查
   - 通过（0 个错误）→ 修改完成
   - 失败 → 根据错误报告修正违规模块 → Write → 再次 validate_layout 直到通过
10. 视需要在修改后调用截图工具验证视觉效果

**示例**：
- "移动沙发到靠窗位置" → Read zones.json → Read 分区 modules.json → 修改 bounds → Write → validate_layout → 完成
- "删除茶几" → Read zones.json → Read 分区 modules.json → 移除对应项 → Write → validate_layout → 完成
- "旋转床 90 度" → Read zones.json → Read 分区 modules.json → 修改 bounds + `facing`（如 `{ "value": null, "semantic": "west" }`）→ Write → validate_layout → 完成
