# MCP 工具调用问题排查报告

> **报告日期**: 2025-01-22
> **问题状态**: 排查中（Step 12）
> **严重程度**: 高 - 影响 AI Agent 核心功能

---

## 1. 问题概述

### 1.1 现象描述

在 BIMCanvas.Agent 项目中，Canvas MCP 工具（如 `ai_job_create`）无法被正确调用：

- AI 输出 `<mcp__canvas__ai_job_create>...</mcp__canvas__ai_job_create>` 是**纯文本**
- Web 端没有显示工具调用控件（Calculator 工具有）
- 返回的路径不存在（Server API 从未被调用）

### 1.2 预期行为

- AI 应该触发真正的工具调用
- 日志显示 `[TOOL] mcp__calc__create_job` 格式
- Server API 收到请求并返回结果

---

## 2. 排查过程

### 2.1 渐进式验证策略

采用"先恢复基线，再渐进迁移"的策略，逐步隔离变量：

| 步骤 | 目标 | 结果 | 结论 |
|------|------|------|------|
| Step 1 | Calculator 基础运算 | ✅ 通过 | MCP 机制正常 |
| Step 2 | echo 工具（英文描述） | ✅ 通过 | 自定义工具可工作 |
| Step 3 | echo 工具（中文描述） | ✅ 通过 | 中文描述不影响注册 |
| Step 4 | HTTP 调用工具 | ✅ 通过 | aiohttp 异步无问题 |
| Step 5 | Canvas 工具迁移 | ✅ 注册成功 | 工具能被注册 |
| Step 6 | 最小差异测试 | ✅ test_api 成功 | 问题在工具名称/描述 |
| Step 7 | 简化名称和描述 | ❌ 仍失败 | 问题不在名称/描述 |
| Step 8 | 简化为 echo 实现 | ✅ 成功 | 问题在原实现代码 |
| Step 9 | 恢复 HTTP 实现 | ✅ 调用成功，404 | API 端点问题 |
| Step 10 | 修复 base_branch 默认值 | ✅ 代码已修改 | - |
| Step 11 | 回退 echo 验证稳定性 | ❌ 成功率 20% | **模型行为不稳定** |

### 2.2 关键发现

#### Step 11 测试详情

| 测试 | 结果 | base_branch 值 | 输出格式 |
|------|------|----------------|----------|
| 测试 1 | ❌ 失败 | `"main"` | XML 文本输出 |
| 测试 2 | ✅ 成功 | `"master"` | `[TOOL]` 格式 |
| 测试 3 | ❌ 失败 | `"main"` | XML 文本输出 |
| 测试 4 | ❌ 失败 | `"main"` | XML 文本输出 |
| 测试 5 | ❌ 失败 | `"main"` | XML 文本输出 |

**成功率：1/5 = 20%**

#### 关键观察

1. **问题不在工具实现代码** - echo 版本同样不稳定
2. **成功时 AI 能获取上下文** - 正确填入 `"master"` 分支
3. **失败时 AI 只是猜测** - 填入默认的 `"main"` 分支
4. **工具名称/描述已简化为英文** - 与 `test_api` 风格一致

---

## 3. 问题定位

### 3.1 已排除的原因

| 可能原因 | 验证方法 | 结果 |
|----------|----------|------|
| 参数类型 `list` 不支持 | 改用 `str` 类型 | 仍不稳定 |
| 中文描述影响工具发现 | 改为英文描述 | 仍不稳定 |
| `aiohttp` 异步问题 | 测试 HTTP 工具 | 正常工作 |
| 工具实现代码问题 | 简化为 echo | 仍不稳定 |
| API 端点问题 | test_api 对照测试 | API 可达 |

### 3.2 当前怀疑方向

问题出在**模型层面**，可能原因：

| 层级 | 可能原因 | 证据 |
|------|----------|------|
| **模型随机性** | Claude 模型在某些情况下选择文本模拟 | 同一工具 20% 成功率 |
| **工具数量过多** | 9 个工具导致模型混淆 | 待验证 |
| **工具目的抽象** | "创建工作环境"不如"计算"明确 | 待验证 |
| **系统提示不足** | 缺少强制使用工具的指令 | 待验证 |

---

## 4. Step 12 排查方案

### 4.1 方案 C：对比 add 工具（推荐）

**目的**：确定是模型/SDK 层问题还是工具定义差异

**测试步骤**：
1. 发送 `"calculate 1 + 1"` 5 次
2. 记录成功率
3. 对比分析

**预期结论**：

| add 成功率 | 说明 | 下一步 |
|------------|------|--------|
| 100% | 问题在 `create_job` 工具定义 | 分析工具差异 |
| < 100% | 问题在模型/SDK 层 | 检查 Agent 配置 |

### 4.2 工具对比分析

| 差异点 | `add` | `create_job` |
|--------|-------|--------------|
| 参数类型 | `float` | `str` |
| 参数名 | `a`, `b` | `name`, `base_branch` |
| 工具目的 | 计算（明确） | 创建环境（抽象） |
| 描述长度 | 短 | 较长 |

---

## 5. 当前代码状态

### 5.1 calculator.py 工具清单

```python
tools = [
    add_numbers,        # add - 英文描述 - float 参数
    subtract_numbers,   # subtract - 英文描述 - float 参数
    multiply_numbers,   # multiply - 英文描述 - float 参数
    divide_numbers,     # divide - 英文描述 - float 参数
    echo_message,       # echo - 中文描述 - str 参数
    ping_server,        # ping_server - 中文描述 - str 参数
    test_api,           # test_api - 英文描述 - str 参数
    create_job,         # create_job - 英文描述 - str 参数（echo 实现）
    ai_job_complete,    # ai_job_complete - 中文描述 - list 参数
]
```

### 5.2 create_job 当前实现（echo 版本）

```python
@tool("create_job", "Create isolated work environment (Git Worktree)", {"name": str, "base_branch": str})
async def create_job(args: dict[str, Any]) -> dict[str, Any]:
    name = args.get("name", "")
    base_branch = args.get("base_branch", "")
    return {"content": [{"type": "text", "text": f"Echo: name={name}, base_branch={base_branch}"}]}
```

---

## 6. 后续行动计划

### 6.1 短期（验证阶段）

1. **执行 Step 12 方案 C** - 对比测试 `add` 工具稳定性
2. **根据结果决定下一步**：
   - 如果 `add` 100% 成功 → 分析工具定义差异
   - 如果 `add` 也不稳定 → 检查 Agent SDK 配置

### 6.2 中期（修复阶段）

根据排查结果选择修复方案：

| 问题根因 | 修复方案 |
|----------|----------|
| 工具数量过多 | 减少工具数量，只保留核心工具 |
| 工具目的抽象 | 重写工具描述，使用更明确的动作词 |
| 系统提示不足 | 在 system_prompt 中强制要求使用工具 |
| SDK 配置问题 | 调整 ClaudeAgentOptions 参数 |

### 6.3 长期（架构优化）

- 考虑将工具调用逻辑从 MCP 迁移到更可靠的方式
- 添加工具调用监控和重试机制
- 研究其他 Agent 框架的工具调用实现

---

## 7. 核心观点总结

1. **MCP 注册机制正常** - 工具能被正确注册到 Agent SDK
2. **问题在模型行为层** - 同一工具同一输入，成功率仅 20%
3. **成功时模型能获取上下文** - 说明工具调用路径本身没问题
4. **失败时模型选择文本模拟** - 这是 Claude 模型的行为选择
5. **需要进一步对比测试** - 确认是特定工具问题还是全局问题

---

## 8. 相关文档

- `plans/MCP_Migration_Plan.md` - 迁移计划详情
- `reports/Agent_SDK_MCP_Integration_Guide.md` - MCP 集成指南
- `BIMCanvas.Agent/src/mcp/calculator.py` - 当前工具实现

---

## 附录：测试日志示例

### 成功调用日志

```
[TOOL] mcp__calc__create_job
  name: "test-job"
  base_branch: "master"
[RESULT] Echo: name=test-job, base_branch=master
```

### 失败调用日志

```
[AI Response]
<mcp__calc__create_job>
  <name>test-job</name>
  <base_branch>main</base_branch>
</mcp__calc__create_job>
```

---

*报告生成时间: 2025-01-22 22:50*
*下次更新: Step 12 测试完成后*
