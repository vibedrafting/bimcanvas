# SubAgent 行为异常问题报告

**报告日期**: 2026-01-08  
**问题级别**: 严重  
**状态**: 待修复

---

## 1. 问题现象

### 1.1 预期行为（之前）

SubAgent 执行时，前端应显示完整的工具调用过程：

```
LAYOUT-AGENT
├── ✓ 查询客厅家具模块
│   ├── Tool: Read (config.json)     ← 显示工具调用
│   ├── Tool: Glob (*.json)          ← 显示工具调用
│   └── Result: 找到 3 件家具        ← 显示最终结果
```

### 1.2 实际行为（现在）

SubAgent 内部没有工具执行显示，出现了未知的 `TaskOutput` 工具调用：

```
LAYOUT-AGENT
├── ✓ 查询客厅家具模块
│   └── > Result                     ← 只有结果，无工具调用

[MainAgent] Tool: TaskOutput         ← 异常！主 Agent 调用 TaskOutput
    {"task_id": "a0093a2", "block": true, "timeout": 30000}
```

### 1.3 截图证据

![TaskOutput](E:\工作文档\开发类\MyCode\BIMCanvas\reports\TaskOutput.png)

服务端日志显示：

- `[MainAgent] Response: 两个 Agent 已同时启动，正在并行执行查询任务。让我获取执行结果：`
- `[MainAgent] Tool: TaskOutput {"task_id": "a0093a2", "block": true, "timeout": 30000}`

前端显示：
- SubAgent 气泡内只显示 "Result"，没有工具调用列表
- 出现了多个 "TaskOutput" 工具调用气泡

### 1.4 对比之前的执行流程

![Task](E:\工作文档\开发类\MyCode\BIMCanvas\reports\Task.png)

---

## 2. 初步分析

### 2.1 TaskOutput 工具是什么？

**TaskOutput** 是 Anthropic Agent SDK 的内置工具，用于获取后台运行任务的结果。

工作原理：
```
MainAgent 派发 Task (run_in_background=true)
         ↓
SubAgent 后台异步执行（不阻塞主线程）
         ↓
MainAgent 调用 TaskOutput 获取结果
```

**关键点**：当 SubAgent 在后台运行时，MainAgent 看不到其内部的工具调用过程。

### 2.2 问题根因定位

经过排查，问题源于 **Agent 工具配置变化**：

| 时间点 | tools 配置 | 行为 |
|--------|-----------|------|
| 修改前 | `["Read", "Glob", "Grep", "Task"]` | SubAgent 同步执行，显示工具调用 |
| 修改后 | `null` (默认全开) | Agent 可使用 TaskOutput，SubAgent 可能后台执行 |

### 2.3 代码变更追溯

**关键提交**: `6407fdf` - "功能：支持 tools 配置为空时默认全开"

修改内容：
```python
# BIMCanvas.Agent/src/config/loader.py
def load_tools(self) -> list[str] | None:
    config = self.load_config()
    tools = config.get('tools')
    
    # 空数组或 null 都返回 None，表示默认全开
    if not tools:
        return None  # ← 问题所在！
    
    return tools
```

**问题**：当 `config.json` 中没有 `tools` 字段时，`config.get('tools')` 返回 `None`，导致函数返回 `None`（默认全开）。

### 2.4 配置文件对比

**模板文件** (`src/config/templates/config.json.template`):
```json
{
  "tools": ["Read", "Glob", "Grep", "Task"]
}
```

**用户配置** (`~/Documents/BIMCanvas/config.json`):
```json
{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-opus-4-5-20251101",
  "maxTokens": 4096,
  "server": { ... }
  // ← 缺少 tools 字段！
}
```

用户配置是在模板更新前创建的，没有 `tools` 字段，导致 `load_tools()` 返回 `None`。

---

## 3. 影响范围

- **Agent 行为**：SubAgent 可能以后台模式运行，改变同步执行流程
- **前端显示**：SubAgent 内部工具调用不显示，用户体验下降
- **调试困难**：无法看到 SubAgent 执行细节，问题排查困难

---

## 4. 建议修复方案

### 方案 A：更新用户配置文件（快速修复）

在 `~/Documents/BIMCanvas/config.json` 中添加 `tools` 字段：

```json
{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-opus-4-5-20251101",
  "maxTokens": 4096,
  "tools": ["Read", "Glob", "Grep", "Task"],
  "server": {
    "host": "127.0.0.1",
    "port": 8765
  }
}
```

### 方案 B：修改 loader.py 逻辑（长期方案）

区分"字段不存在"和"显式设置为空"：

```python
def load_tools(self) -> list[str] | None:
    config = self.load_config()
    
    # 检查字段是否存在
    if 'tools' not in config:
        # 字段不存在，使用默认限制
        return ["Read", "Glob", "Grep", "Task"]
    
    tools = config['tools']
    
    # 显式设置为 null 或空数组，返回 None（全开）
    if not tools:
        return None
    
    return tools
```

---

## 5. 验证步骤

1. 更新配置文件，添加 `tools` 字段
2. 重启 Agent 服务
3. 发送测试消息，触发 SubAgent 执行
4. 确认：
   - SubAgent 内部显示工具调用
   - 不再出现 TaskOutput 工具调用
   - 执行流程恢复正常

---

## 6. 相关文件

| 文件路径 | 说明 |
|---------|------|
| `BIMCanvas.Agent/src/config/loader.py` | 配置加载器，load_tools() 方法 |
| `BIMCanvas.Agent/src/agent/main_agent.py` | 主 Agent 实现 |
| `~/Documents/BIMCanvas/config.json` | 用户配置文件 |
| `src/config/templates/config.json.template` | 配置模板 |

---

## 7. 附录：相关 Git 提交

```
6407fdf 功能：支持 tools 配置为空时默认全开
de47b81 修复：Agent 工具配置改用 tools 参数实现真正限制
2473b5c 重构：Agent 配置系统改造（硬编码 → 配置文件驱动）
```
