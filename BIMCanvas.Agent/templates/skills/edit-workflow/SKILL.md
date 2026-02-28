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
2. Read modules.json
3. 定位目标模块
4. 执行修改操作
5. 预检约束（门前净空、通道宽度）
6. Write 保存结果
7. 调用 `mcp__canvas__validate_layout()` 编译检查
   - 通过（0 个错误）→ 修改完成
   - 失败 → 根据错误报告修正违规模块 → Write → 再次 validate_layout 直到通过
8. 视需要在修改后调用截图工具验证视觉效果

**示例**：
- "移动沙发到靠窗位置" → Read → 修改 bounds → Write → validate_layout → 完成
- "删除茶几" → Read → 移除对应项 → Write → validate_layout → 完成
- "旋转床 90 度" → Read → 修改 facing 和 bounds → Write → validate_layout → 完成
