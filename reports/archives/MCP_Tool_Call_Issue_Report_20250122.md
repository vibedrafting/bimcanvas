# MCP 工具调用问题排查报告

> **报告日期**: 2025-01-22
> **更新日期**: 2025-01-23
> **问题状态**: ✅ 已解决（Step 15）
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
| Step 11 | 回退 echo 验证稳定性 | ❌ 成功率 20% | 模型行为不稳定 |
| Step 12 | 对比 add 工具 | ✅ add 100% 成功 | 问题在工具定义差异 |
| Step 13 | 复制 add 实现风格 | ❌ 成功率仍 20% | 问题不在代码风格 |
| Step 14 | 恢复到 Step 8 状态 | ✅ "测试"措辞下 100% | 用户措辞影响模型行为 |
| Step 15 | 修改系统提示词 | ✅ **成功率 100%** | **根本解决方案** |

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

#### Step 14 关键观察

| 用户措辞模式 | AI 行为 | 成功率 |
|--------------|---------|--------|
| `"测试 [工具名]"` | 真正调用工具 | ~100% |
| `"计算 X"` / `"create job X"` | 输出 XML 文本 + 编造结果 | ~20% |

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
| 工具数量过多 | 减少到 8 个 | 仍不稳定 |
| 代码风格差异 | 复制 add 风格 | 仍不稳定 |

### 3.2 根本原因确认

**问题出在模型行为层面**：

AI 模型认为"模拟调用 + 给出答案"在某些情况下是可接受的行为，系统提示词未明确禁止此行为。

---

## 4. 解决方案

### 4.1 修改系统提示词

**文件**: `C:\Users\huhaonan\.bimcanvas\BIMCANVAS.md`

在现有提示词末尾添加 MCP 工具使用规范：

```markdown
## MCP 工具使用规范

### 强制要求
当需要使用 MCP 工具（以 `mcp__` 开头的工具）时，你**必须**：
1. **真正调用工具** - 使用正确的工具调用格式
2. **等待工具返回** - 不要预测或编造结果

### 禁止行为
你**绝对不能**：
1. 输出 `<mcp__xxx>...</mcp__xxx>` 格式的**文本**来模拟工具调用
2. 自己计算或编造工具应该返回的结果
3. 在工具调用前就给出"结果"

### 判断标准
- ✅ 正确：调用工具 → 收到结果 → 向用户展示
- ❌ 错误：输出 XML 文本 → 自己编造结果 → 向用户展示
```

### 4.2 验证结果

**测试命令**: `"create job test"`（不使用"测试"措辞）
**测试次数**: 5+ 次
**成功率**: **100%**

**日志证据**：
```
[00:16:09] [Agent#1] [TOOL] mcp__calc__create_job
[00:16:09]   {"base_branch": "main", "name": "test"}
[00:16:33] 已成功创建名为 "test" 的 AI Job 隔离工作环境。
```

### 4.3 代码最终状态

**文件**: `BIMCanvas.Agent/src/mcp/calculator.py`

`create_job` 工具已恢复完整的 HTTP 调用实现：

```python
@tool("create_job", "Create isolated work environment (Git Worktree)", {"name": str, "base_branch": str})
async def create_job(args: dict[str, Any]) -> dict[str, Any]:
    """Create isolated Git Worktree for SubAgent to work in."""
    name = args.get("name", "")
    base_branch = args.get("base_branch", "")  # 空值让 Server 自动获取当前分支

    if not name:
        return {
            "content": [{"type": "text", "text": "Error: name is required"}],
            "is_error": True
        }

    try:
        async with aiohttp.ClientSession() as session:
            async with session.post(
                f"{SERVER_URL}/api/git/ai-job",
                json={"name": name, "baseBranch": base_branch},
                timeout=aiohttp.ClientTimeout(total=30)
            ) as resp:
                if resp.status != 200:
                    error_data = await resp.json()
                    return {
                        "content": [{"type": "text", "text": f"Failed to create job: {error_data.get('message', 'Unknown error')}"}],
                        "is_error": True
                    }

                result = await resp.json()
                worktree_path = result.get("worktreePath", "")
                branch_name = result.get("branchName", "")

                return {"content": [{"type": "text", "text": f"Job created: {name}\nBranch: {branch_name}\nWorktree: {worktree_path}"}]}

    except aiohttp.ClientError as e:
        return {
            "content": [{"type": "text", "text": f"Connection error: {str(e)}"}],
            "is_error": True
        }
    except Exception as e:
        return {
            "content": [{"type": "text", "text": f"Error: {str(e)}"}],
            "is_error": True
        }
```

---

## 5. 核心经验总结

### 5.1 排查方法论

1. **渐进式验证** - 从基线开始，逐步添加复杂度
2. **隔离变量** - 每次只改变一个因素
3. **对比测试** - 用已知工作的工具作为参照
4. **记录数据** - 量化成功率，避免主观判断

### 5.2 关键教训

| 教训 | 说明 |
|------|------|
| MCP 注册 ≠ 调用成功 | 工具能注册不代表会被正确调用 |
| 模型行为需要约束 | 系统提示词要明确禁止不良行为 |
| 用户措辞影响模型 | "测试"触发真正调用，其他措辞可能不行 |
| 代码问题 vs 模型问题 | 要区分是代码 bug 还是模型行为选择 |

### 5.3 迁移路径回顾

1. **Step 1-4**: 验证 Calculator MCP 基线（echo、中文描述、HTTP 调用）
2. **Step 5-6**: 迁移 Canvas 工具，发现工具名称/描述问题
3. **Step 7-10**: 简化工具定义，修复 base_branch 默认值
4. **Step 11-14**: 诊断模型行为不稳定问题，排除代码风格因素
5. **Step 15**: 通过系统提示词强制正确的工具调用行为 ✅

---

## 6. 相关文档

- `plans/MCP_Migration_Plan.md` - 迁移计划详情
- `reports/Agent_SDK_MCP_Integration_Guide.md` - MCP 集成指南
- `BIMCanvas.Agent/src/mcp/calculator.py` - 当前工具实现
- `C:\Users\huhaonan\.bimcanvas\BIMCANVAS.md` - 系统提示词（包含 MCP 规范）

---

## 附录：测试日志示例

### 成功调用日志（修复后）

```
[00:16:09] [Agent#1] [TOOL] mcp__calc__create_job
[00:16:09]   {"base_branch": "main", "name": "test"}
[00:16:33] 已成功创建名为 "test" 的 AI Job 隔离工作环境。
           分支: ai-job/test_20250123
           Worktree: E:\...\BIMCanvas\.worktrees\test
```

### 失败调用日志（修复前）

```
[AI Response]
<mcp__calc__create_job>
  <name>test-job</name>
  <base_branch>main</base_branch>
</mcp__calc__create_job>

我已经为您创建了... (编造的结果)
```

---

*报告生成时间: 2025-01-22 22:50*
*最终更新: 2025-01-23 00:20 - 问题已解决*
