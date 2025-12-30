# BIMCanvas Agent MVP 实施计划

> **版本**：v2.0 | **日期**：2025-12-30
> **定位**：快速验证实施指南，聚焦"做什么"和"怎么做"
> **理论参考**：完整架构理论见 [`docs/Agent_Design_Spec.md`](../docs/Agent_Design_Spec.md)

---

## 一、MVP 目标与范围

### 验证假设

> AI Agent 能够理解房间功能，并在设计区内完成符合设计常识的家具布置。

### MVP 简化清单

| 完整版功能 | MVP 简化 | 理由 |
|------------|----------|------|
| **Phase A 三步骤** | 仅 Step1（tags推断） | Room Zone = Designable Zone |
| **模块库过滤** | 使用全部 modules/*.svg | 跳过 tags 匹配 |
| **设计区划分** | 不划分子区域 | 房间即设计区 |
| **Git Worktree 并行** | 单任务串行 | 无需并行能力 |
| **策略参数化** | 使用默认策略 | 无策略配置 |
| **自动 Commit** | 人工检查后手动提交 | 简化 Git 流程 |
| **设计说明 README** | 不生成 | 跳过自我辩护 |
| **Server 实时验证** | 人工检查 | Server 未完善 |
| **SSE 事件触发** | 命令行直接调用 | 无需事件机制 |

### 路径兼容原则

MVP 阶段 Agent 的 `--project-path` 参数支持：
- 普通项目目录：`C:/Users/.../Projects/demo_1`
- Worktree 目录：`C:/Users/.../Projects/demo_1/.worktrees/ai-job-1`

Agent 代码不区分两者，为后续并行扩展预留接口。

---

## 二、技术栈

| 组件 | 选型 |
|------|------|
| 语言 | Python 3.10+ |
| AI 框架 | Anthropic Agent SDK |
| 模型 | Claude Sonnet 4 |
| 依赖 | `anthropic>=0.40.0` |

---

## 三、项目结构

```
BIMCanvas.Agent/
├── pyproject.toml              # Python 项目配置
├── src/
│   ├── __init__.py
│   ├── main.py                 # 入口：解析参数，启动 Agent
│   ├── agent/
│   │   ├── __init__.py
│   │   ├── placement_agent.py  # 主 Agent：协调两个阶段
│   │   ├── zone_designer.py    # Phase A：tags 推断
│   │   └── layout_planner.py   # Phase B：布置决策
│   ├── tools/
│   │   ├── __init__.py
│   │   ├── file_tools.py       # 读写 JSON 工具
│   │   └── svg_parser.py       # SVG 文件名解析
│   └── config/
│       ├── __init__.py
│       └── settings.py         # 配置项
```

---

## 四、MVP 工具集（精简版）

> 完整工具定义见理论文档第六节

### 读取工具

| 工具 | 数据路径 | 说明 |
|------|----------|------|
| `read_room_zones` | `computed/zones.json` | 读取 Room Zone |
| `read_openings` | `baseline/openings.json` | 读取门窗数据 |
| `list_modules` | `modules/*.svg` | 列出素材库 |

### 写入工具

| 工具 | 数据路径 | 说明 |
|------|----------|------|
| `write_design_zones` | `schemes/{s}/zones.json` | 写入 Designable Zone |
| `write_modules` | `schemes/{s}/modules.json` | 写入布置结果 |

**MVP 不需要的工具**：
- ❌ Git 工具（`git_add`, `git_commit`, `git_status`）
- ❌ README 工具（`write_readme`）
- ❌ 策略工具（`read_strategy`）

---

## 五、实施步骤

### Phase 1：基础设施（预计 2h）

**目标**：搭建项目框架，准备测试数据

- [ ] 创建 `BIMCanvas.Agent/` 目录结构
- [ ] 创建 `pyproject.toml`
- [ ] 安装依赖 `pip install anthropic`
- [ ] 实现 `file_tools.py`（JSON 读写）
- [ ] 实现 `svg_parser.py`（文件名解析）
- [ ] 在 demo_1 创建 `modules/` 并准备 10+ SVG 文件

**关键代码片段**：

```python
# pyproject.toml
[project]
name = "bimcanvas-agent"
version = "0.1.0"
requires-python = ">=3.10"
dependencies = ["anthropic>=0.40.0"]

[build-system]
requires = ["hatchling"]
build-backend = "hatchling.build"
```

```python
# src/tools/svg_parser.py
def parse_svg_filename(filename: str) -> dict:
    """解析文件名：床_双人_2000x1800.svg → {name, size, templateId}"""
    name = filename.replace(".svg", "")
    parts = name.rsplit("_", 1)
    width, height = map(int, parts[-1].split("x"))
    return {
        "templateId": name,
        "name": parts[0],
        "size": [width, height],
        "svgPath": f"modules/{filename}"
    }
```

### Phase 2：Phase A 实现（预计 3h）

**目标**：实现 tags 推断功能

- [ ] 实现 `zone_designer.py` 框架
- [ ] 实现 `read_room_zones` 工具
- [ ] 编写 tags 推断 Prompt
- [ ] 实现 `write_design_zones` 工具
- [ ] 测试 demo_1 的 6 个房间

**tags 推断规则（简表）**：

| 房间类型 | 推荐 tags |
|----------|-----------|
| 客厅 | sitting, entertainment, tv_media |
| 主卧 | sleeping, rest, storage, dressing |
| 次卧 | sleeping, rest |
| 卫生间 | bathing, toilet |
| 厨房 | cooking, storage |
| 餐厅 | dining |

### Phase 3：Phase B 实现（预计 4h）

**目标**：实现布置决策功能

- [ ] 实现 `layout_planner.py` 框架
- [ ] 实现 `read_openings` 工具
- [ ] 实现 `list_modules` 工具
- [ ] 编写布置决策 Prompt（含设计原则）
- [ ] 实现 `write_modules` 工具
- [ ] 逐房间测试布置结果

**设计原则简表**（详见理论文档第四节）：

| 规则 | 说明 |
|------|------|
| 靠墙 | 大型家具（床、衣柜、沙发）尽量靠墙 |
| 居中 | 电视柜居中于电视墙 |
| 对位 | 沙发正对电视 |
| 避门窗 | 不阻挡门开启范围 |

### Phase 4：集成测试（预计 2h）

**目标**：端到端验证

- [ ] Phase A → Phase B 完整流程
- [ ] 检查输出 JSON 格式
- [ ] Web 端渲染验证
- [ ] 修复问题

---

## 六、模块素材库

### 文件命名规范

```
{名称}_{规格}_{宽}x{高}.svg

示例：
- 床_双人_2000x1800.svg
- 沙发_三人_2400x900.svg
- 衣柜_三门_2400x600.svg
```

### MVP 必需素材（最少 10 个）

```
modules/
├── 床_双人_2000x1800.svg
├── 床_单人_1200x1900.svg
├── 衣柜_三门_2400x600.svg
├── 床头柜_500x500.svg
├── 沙发_三人_2400x900.svg
├── 茶几_方形_1200x600.svg
├── 电视柜_1800x400.svg
├── 餐桌_六人_1800x900.svg
├── 餐椅_450x450.svg
└── 马桶_400x700.svg
```

---

## 七、测试数据

### 测试项目

```
路径：C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1
```

### 项目结构

```
demo_1/
├── baseline/
│   ├── rooms.json            # 6 个房间
│   └── openings.json         # 门窗数据
├── computed/
│   └── zones.json            # Room Zone（Agent 输入）
├── schemes/default/
│   ├── zones.json            # Designable Zone（Agent 输出）
│   └── modules.json          # 布置结果（Agent 输出）
└── modules/                  # SVG 素材库（待创建）
```

### demo_1 房间数据

| Zone ID | 房间名 | 类型 |
|---------|--------|------|
| rz_1 | 次卧一 | Bedroom |
| rz_2 | 次卧二 | Bedroom |
| rz_3 | 主卧 | MasterBedroom |
| rz_4 | 主卫 | Bathroom |
| rz_5 | 公卫 | Bathroom |
| rz_6 | 公共空间 | LivingRoom |

---

## 八、验收标准

### 运行命令

```bash
cd BIMCanvas.Agent
python -m src.main \
    --project-path "C:\Users\huhaonan\Documents\BIMCanvas\Projects\demo_1" \
    --scheme "default" \
    --prompt "帮我设计这个户型"
```

### 预期输出

```
[Phase A] 读取 6 个 Room Zone
[Phase A] 推断功能标签...
  - 主卧 → tags: [sleeping, rest, storage, dressing]
  - 公共空间 → tags: [sitting, entertainment, dining]
  ...
[Phase A] 写入 schemes/default/zones.json ✓

[Phase B] 读取 6 个 Designable Zone
[Phase B] 加载 10 个模块素材
[Phase B] 布置主卧...
  - 床_双人 @ (10100, 2750), facing=east
  - 衣柜_三门 @ (12650, 5450), facing=south
[Phase B] 布置公共空间...
  - 沙发_三人 @ (5400, 3700), facing=east
  - 电视柜 @ (8550, 3325), facing=west
  ...
[Phase B] 写入 schemes/default/modules.json ✓

完成！共布置 19 个模块
```

### 检查项

- [ ] `computed/zones.json` 正确读取
- [ ] 各房间 tags 推断合理
- [ ] `schemes/default/zones.json` 格式正确
- [ ] `modules/*.svg` 正确读取
- [ ] `baseline/openings.json` 正确读取
- [ ] 每个房间有合理的家具布置
- [ ] 家具不阻挡门开启
- [ ] `schemes/default/modules.json` 格式正确
- [ ] Web 端能正确渲染

---

## 九、MVP 前置条件

### 必需数据（已就绪）

| 数据 | 路径 | 状态 |
|------|------|------|
| Room Zone | `computed/zones.json` | ✅ demo_1 已有 |
| 门窗数据 | `baseline/openings.json` | ✅ demo_1 已有 |

### 简化处理

| 场景 | MVP 处理方式 |
|------|--------------|
| `exclusions.json` 不存在 | 使用 `openings.json` 简单避让 |
| Zone 无 `innerBoundary` | 使用 `rawBoundary` 作为边界 |
| 无 Server 验证 | 人工检查布置结果 |

---

## 十、后续扩展（MVP 后）

> 完整扩展路线见理论文档第十一节

| 优先级 | 功能 | 参考 |
|--------|------|------|
| P1 | Phase A 完整实现（模块过滤、区域划分） | 理论文档 §3 |
| P1 | Git Worktree 并行架构 | `AI_Parallel_Design_Patterns.md` |
| P2 | 策略参数化（storage_weight 等） | 理论文档 §5 |
| P2 | 自动 Commit + 设计说明 | 理论文档 §6.3 |
| P3 | SSE 事件触发 | 理论文档 §8 |

---

## 十一、风险与缓解

| 风险 | 缓解措施 |
|------|----------|
| Claude API 限流 | 批量处理房间，减少调用次数 |
| SVG 素材缺失 | 提前准备完整模块库 |
| 布置不合理 | 完善 Prompt 中的设计原则 |
| 边界计算错误 | MVP 依赖人工检查，后续由 Server 兜底 |

---

## 附录：关键文件清单

### 需要创建

| 文件 | 优先级 |
|------|--------|
| `BIMCanvas.Agent/pyproject.toml` | P1 |
| `BIMCanvas.Agent/src/main.py` | P1 |
| `BIMCanvas.Agent/src/tools/file_tools.py` | P1 |
| `BIMCanvas.Agent/src/tools/svg_parser.py` | P1 |
| `BIMCanvas.Agent/src/agent/placement_agent.py` | P2 |
| `BIMCanvas.Agent/src/agent/zone_designer.py` | P2 |
| `BIMCanvas.Agent/src/agent/layout_planner.py` | P3 |
| `BIMCanvas.Agent/src/config/settings.py` | P1 |
| `demo_1/modules/*.svg` | P1 |

### Agent 读取

| 文件 | 生成者 |
|------|--------|
| `computed/zones.json` | Server |
| `baseline/openings.json` | Revit |
| `modules/*.svg` | 手动准备 |

### Agent 写入

| 文件 | 内容 |
|------|------|
| `schemes/{s}/zones.json` | Designable Zone + tags |
| `schemes/{s}/modules.json` | 布置结果 |
