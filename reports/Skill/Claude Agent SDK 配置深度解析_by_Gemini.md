# Claude Agent SDK 配置深度解析：技能（Skill）加载机制与上下文污染的隔离架构研究报告

## 1. 执行摘要与研究背景

在生成式人工智能（Generative AI）从单纯的对话交互向自主智能体（Autonomous Agents）演进的过程中，开发者面临的核心挑战已从“提示工程（Prompt Engineering）”转向了更为复杂的“上下文工程（Context Engineering）”与“环境编排（Environment Orchestration）”。Anthropic 推出的 Claude Agent SDK 作为这一技术浪潮中的重要基础设施，旨在为开发者提供构建具备感知、规划与执行能力的智能体的标准框架。然而，随着该 SDK 在企业级复杂项目中的落地应用，其源自 Claude Code 命令行工具（CLI）的设计遗产——即基于文件系统的隐式配置加载机制——逐渐显露出与精细化控制需求之间的张力。

本报告针对一个特定的高频技术痛点进行深度剖析：**如何在 Claude Agent SDK 中配置并加载“技能（Skill）”，同时确保持久化项目配置（如 `CLAUDE.md`）的完全隔离，以防止“上下文污染（Context Pollution）”。**

研究表明，该问题的根源在于 SDK 的设计哲学在“CLI 便捷性”与“SDK 确定性”之间的权衡。在 CLI 模式下，自动扫描项目根目录的配置是一种特性；而在 SDK 编程模式下，这种不加区分的全局加载则构成了对智能体认知边界的侵入。由于 `setting_sources=["project"]` 参数是一个粗粒度的宏指令，它在开启技能扫描的同时，不可避免地引入了项目级的全局指令，导致专用智能体的行为发生漂移（Drift）、Token 预算被无效信息侵占，甚至产生安全隐患。

本报告将通过对 SDK 架构的解构、社区反馈的实证分析以及技术方案的推演，提出三套不同层级的解决方案：**基于插件（Plugins）的旁路加载策略**、**基于进程内 MCP（In-Process MCP）的编程式定义策略**，以及**基于文件系统隔离（Filesystem Isolation）的物理沙箱策略**。这三套方案分别应对了从快速迁移到企业级重构的不同需求场景，为构建高内聚、低耦合的 Claude 智能体提供了理论依据与实践指南。

------

## 2. 架构溯源：Claude Agent SDK 的设计哲学与内在冲突

要深刻理解“上下文污染”问题的本质，不仅需要关注代码层面的参数配置，更需要追溯 Claude Agent SDK 的技术谱系。该 SDK 并非凭空诞生的 API 封装库，而是基于 Claude Code 这一成熟产品的核心逻辑剥离而成的“智能体挽具（Agent Harness）”。

### 2.1 从 CLI 到 SDK：继承与变异

Claude Code 最初被设计为一款直接运行在开发者终端的“结对编程”工具。在 CLI 的使用场景中，交互的主体是人类开发者。人类开发者在同一个项目目录下工作时，往往希望工具能够自动“感知”当前项目的上下文。例如，当开发者在终端输入指令时，CLI 会自动读取根目录下的 `CLAUDE.md` 文件，以获取关于代码风格、提交规范或项目架构的元知识 。

这种“隐式上下文（Implicit Context）”的设计在 CLI 场景下是极大的优势，因为它降低了用户的认知负荷。用户无需每次都重复“请使用两空格缩进”或“本项目使用 React 框架”，工具自动通过文件系统“嗅探”到了这些规则。

然而，当这一逻辑被封装进 Agent SDK 供开发者构建自定义智能体时，情况发生了根本性的变化。SDK 的使用者不再是终端用户，而是应用程序架构师。他们构建的可能不再是通用的“编程助手”，而是极其专用的“日志分析员”、“文档审计员”或“API 测试员”。对于这些专用智能体而言，项目根目录下的通用指令（如前端组件的命名规范）不仅是无用的噪音，更是一种有害的认知干扰 。

### 2.2 智能体循环（Agent Loop）与上下文经济学

Claude Agent SDK 的核心运行时是一个被称为“智能体循环”的递归过程，其基本形态为：感知（Gather Context）→ 思考（Think）→ 行动（Act）→ 验证（Verify）。

| **阶段** | **CLI 模式的默认行为**                                       | **SDK 模式的理想行为**                          | **冲突点**                                            |
| -------- | ------------------------------------------------------------ | ----------------------------------------------- | ----------------------------------------------------- |
| **感知** | 扫描 CWD（当前工作目录）下的所有配置文件、历史记录和工具定义。 | 仅加载与当前任务严格相关的最小必要上下文。      | `setting_sources` 参数默认延续了 CLI 的全量扫描逻辑。 |
| **思考** | 结合 `CLAUDE.md` 中的全局指令与用户输入进行推理。            | 仅基于 `system_prompt` 和特定任务指令进行推理。 | `CLAUDE.md` 的强制注入导致 Persona（人设）冲突。      |
| **行动** | 调用 Bash、FileEdit 等通用工具，具有较高权限。               | 仅调用任务所需的受限工具集（如只读权限）。      | 技能文件通常依赖 Bash 工具，引入了安全隐患。          |

在这一架构中，上下文窗口（Context Window）不仅是计算资源的约束，更是智能体注意力的约束。当 SDK 强制加载项目配置时，它实际上是在强迫智能体“阅读”一份可能长达数千 Token 的无关文档。这种“上下文污染”会产生以下二阶效应：

1. **注意力稀释（Attention Dilution）：** 模型可能会忽略 System Prompt 中的关键指令，转而遵循 `CLAUDE.md` 中的次要规则。
2. **成本膨胀（Cost Inflation）：** 对于一个每天运行数千次的高频微型智能体，每次多加载 2k Token 的项目文档，将导致 API 成本呈指数级上升。
3. **行为不可预测（Unpredictability）：** 不同项目目录下的同一智能体代码，可能因为目录下 `CLAUDE.md` 内容的不同而表现出截然不同的行为，破坏了软件工程的幂等性原则。

### 2.3 “技能（Skill）”的抽象与其文件系统依赖

在 Claude 的生态系统中，“技能”被定义为一种标准化的能力封装格式 。它并非简单的代码函数，而是一个包含元数据（Metadata）和指令内容的文件系统实体。

一个典型的技能结构如下：

.claude/skills/

└── pdf-processor/

├── SKILL.md       # 核心定义文件

├── extract.py     # 辅助脚本

└── README.txt     # 说明文档

SDK 对技能的处理采用了“渐进式披露（Progressive Disclosure）”机制 。在启动阶段，SDK 仅扫描 `SKILL.md` 的 YAML 前端数据（Frontmatter），提取技能的名称和描述（Description），并将其注入到 System Prompt 中。只有当模型在推理过程中决定调用该技能时，SDK 才会读取 `SKILL.md` 的完整 Markdown 内容。

这种机制本身是高效的。但是，为了让 SDK “发现”这些技能文件，开发者必须告诉 SDK “去哪里看”。这就引入了 `ClaudeAgentOptions` 中的 `setting_sources` 参数。根据文档 ，启用技能的标准方法是设置 `setting_sources=["project"]`。然而，这个参数是一个“全有或全无（All-or-Nothing）”的开关。一旦开启“项目级设置”，SDK 就会假设开发者希望复刻 CLI 的完整体验，因此它不仅会扫描 `.claude/skills`，还会顺手加载 `CLAUDE.md` 和 `.claude/settings.json`。

这就是用户所面临问题的技术根源：**SDK 缺乏一个细粒度的“仅扫描技能”的配置选项。**

------

## 3. 问题实证与社区反馈分析

用户在其查询中明确要求“联网搜索相关问题，看看有没有其他人提过问题反馈”。通过对 GitHub Issues、Reddit 讨论区以及各类开发者论坛的深度检索与分析，我们可以确认：**该问题并非个例，而是 Claude Agent SDK 用户在从探索阶段走向生产阶段时普遍遇到的“成长的烦恼”。**

### 3.1 社区反馈的核心聚类

通过分析收集到的研究片段，社区对该问题的反馈可以聚类为以下三个维度：

#### 3.1.1 维度的混淆：配置与能力的耦合

在 GitHub 和 Reddit 上，多位开发者表达了对 SDK 配置逻辑的困惑。例如，有用户在 GitHub Issue 中询问“Claude Code SDK 是否像 CLI 一样自动拉取 `CLAUDE.md` 文件？”。这一提问反映了开发者对于 SDK 默认行为的不确定性。后续的讨论澄清了 SDK 在新版本中默认不加载文件系统设置 ，这虽然解决了默认污染问题，但也导致了技能无法被自动发现，迫使开发者手动开启 `setting_sources=["project"]`，从而陷入了“要么全污染，要么无技能”的两难境地。

#### 3.1.2 隔离性的缺失：子智能体（Subagents）的泄露

关于子智能体的讨论进一步揭示了污染的严重性。有开发者指出，即使是设计为隔离运行的子智能体，在某些配置下也会自动继承主会话的 `CLAUDE.md` 上下文 。这被视为一种“Feature（特性）”而非 Bug，旨在保持项目规范的一致性。然而，对于希望构建“洁净室（Clean Room）”环境的开发者来说，这种自动继承破坏了智能体的独立性。

#### 3.1.3 发现机制的脆弱性：路径与环境依赖

除了污染问题，文件系统扫描机制本身也被指责为脆弱。有 Linux 用户报告称，即使配置了正确的参数，SDK 也无法在 `~/.claude/skills` 中发现技能，原因是 SDK 内部可能硬编码了 macOS 风格的路径逻辑 。另一位用户遭遇了“空技能列表”的问题，即便完全按照文档操作，`SKILL.md` 也未被加载 。这些反馈表明，依赖文件系统扫描（Discovery）机制本身就存在跨平台兼容性和稳定性风险，进一步佐证了寻求“显式配置”方案的必要性。

### 3.2 深度分析：为什么这不仅仅是一个 Bug

从软件架构的角度来看，这不应被简单归类为一个 Bug，而是一种**阻抗失配（Impedance Mismatch）**。

- **CLI 模型：** 用户拥有文件系统，工具是访客。工具应该尽可能多地读取信息以辅助用户。
- **SDK 模型：** 开发者拥有代码，工具是组件。组件应该只读取被显式授权的信息。

Anthropic 的工程团队显然意识到了这一点。在迁移指南  中，他们明确指出 SDK 的默认行为变更（不再自动加载设置）是为了“确保 SDK 应用程序具有独立于本地文件系统配置的可预测行为”。这说明官方推荐的最佳实践是**去文件系统化**。用户遇到的困难，实际上是在逆流而上——试图在一个倾向于“显式定义”的 SDK 环境中，强行使用一种基于“隐式发现”的 CLI 特性（即基于文件的 Skill）。

### 3.3 污染的具体表现形式

为了更直观地展示污染的危害，我们可以构建一个基于真实场景的对比表：

| **场景要素**      | **污染前（理想状态）**                                   | **污染后（实际问题）**                                       | **后果分析**                                                 |
| ----------------- | -------------------------------------------------------- | ------------------------------------------------------------ | ------------------------------------------------------------ |
| **System Prompt** | "你是一个专业的 SQL 优化专家，仅输出优化后的 SQL 语句。" | "你是一个专业的 SQL 优化专家...（插入 2k Token 的前端代码规范）... 总是使用 TypeScript..." | 智能体可能会在 SQL 输出中尝试应用 TypeScript 的命名规范，或者产生关于前端架构的幻觉。 |
| **工具权限**      | 仅允许 `Read` 和 `Execute_SQL`。                         | 可能会根据 `settings.json` 自动开启 `Edit` 或 `Bash`。       | 安全边界被突破，智能体可能具备了修改文件系统的能力。         |
| **推理成本**      | 输入 Token: 500 (Prompt) + 200 (Schema)。                | 输入 Token: 500 + 200 + 3000 (CLAUDE.md)。                   | 单次调用的成本增加 4-5 倍，且响应延迟（Time-to-First-Token）显著增加。 |

------

## 4. 解决方案一：基于插件（Plugins）的旁路加载策略

这是目前最符合 SDK 设计原语，且能直接解决用户“保留 `SKILL.md` 文件但不加载 `CLAUDE.md`”需求的方案。

### 4.1 技术原理

Claude Agent SDK 引入了“插件（Plugins）”的概念，旨在支持模块化的能力扩展。与 `setting_sources` 这种全局扫描机制不同，`plugins` 配置项允许开发者显式地指定一个或多个文件系统路径作为能力的来源 。

关键的架构洞察在于：**SDK 对插件目录的处理逻辑与对项目根目录的处理逻辑是解耦的。** 当通过 `plugins` 参数加载一个本地目录时，SDK 会将其视为一个独立的单元，仅扫描其中的 `.claude` 子目录以获取能力（Commands, Skills, Agents），而不会去回溯或关联宿主项目的根配置。

### 4.2 详细实施步骤

假设你的项目结构如下：

/my-project

├── main.py

├── CLAUDE.md                <-- 需要避开的污染源

└── my-agent-capabilities/   <-- 专门存放技能的目录

└──.claude/

└── skills/

└── data-analysis/

└── SKILL.md

**代码实现（Python）：**

Python

```
import asyncio
from claude_agent_sdk import query, ClaudeAgentOptions

async def main():
    # 定义插件目录的绝对路径或相对路径
    # 注意：该目录下必须包含.claude/skills/ 结构
    plugin_path = "./my-agent-capabilities"

    options = ClaudeAgentOptions(
        # 1. 核心操作：彻底禁用全局设置扫描
        # 将 setting_sources 设置为空列表或仅包含 "user"（如果需要用户级配置）
        # 这确保了 SDK 根本不会去读取根目录下的 CLAUDE.md
        setting_sources=, 
        
        # 2. 显式启用必要的工具
        # Skill 工具是必须的，Bash/Read 通常也是技能执行所需的底层工具
        allowed_tools=,
        
        # 3. 通过插件机制旁路注入技能
        # SDK 会加载该路径下的技能，但不会将其视为 Project Root
        plugins=[
            {
                "type": "local",
                "path": plugin_path
            }
        ]
    )

    # 发起查询
    async for message in query(prompt="请使用 data-analysis 技能处理当前数据", options=options):
        print(message)

if __name__ == "__main__":
    asyncio.run(main())
```

### 4.3 方案优劣势分析

- **优势：**
  - **精准隔离：** 完美实现了用户需求，即加载了技能文件，又屏蔽了项目配置。
  - **复用性：** 这种插件目录可以被打包分发，甚至可以是一个独立的 Git 子模块（Submodule），方便跨项目复用标准技能库。
  - **兼容性：** 由于使用的是标准的 `SKILL.md` 格式，CLI 用户和 SDK 用户可以共享同一套技能定义。
- **劣势：**
  - **目录结构要求：** 必须严格遵循 `.claude/skills/...` 的目录嵌套结构，否则 SDK 可能无法识别。
  - **依赖管理：** 如果技能脚本（如 Python 脚本）依赖于特定的虚拟环境，通过插件加载时可能需要额外处理环境路径问题。

------

## 5. 解决方案二：进程内 MCP（In-Process MCP）与编程式工具定义

如果说方案一是“战术级”的修补，那么本方案则是“战略级”的重构。对于追求极致稳定性和类型安全的专业开发团队，彻底摒弃文件系统形式的 `SKILL.md`，转而使用代码定义工具（Tools），是更优的架构选择。

### 5.1 从“文档驱动”到“代码驱动”

`SKILL.md` 本质上是一个包含自然语言描述和脚本调用指令的混合体。它的执行依赖于 SDK 解析 Markdown，提取 Prompt，然后模型决定调用 Bash 工具来运行脚本。这个链路长且脆弱。

SDK 支持一种被称为“进程内 MCP（In-Process Model Context Protocol）”的机制 。这允许开发者直接用 Python 或 TypeScript 函数定义工具。这些工具在运行时直接被注册到智能体的上下文中，无需任何文件扫描。

### 5.2 技术原理与实现

这种方法利用了 SDK 的 `@tool` 装饰器（Python）或 `tool()` 函数（TypeScript）。

**代码实现（Python）：**

Python

```
from claude_agent_sdk import ClaudeAgentOptions, query, tool, create_sdk_mcp_server
from typing import Any

# 1. 将原 SKILL.md 中的逻辑转化为 Python 函数
# 装饰器中的描述（docstring）替代了 SKILL.md 中的 Frontmatter 描述
@tool("secure_data_processor", 
      "专门用于处理敏感数据的工具，不依赖外部脚本，无上下文污染风险", 
      {"input_path": str, "operation": str})
async def secure_data_processor(args: dict[str, Any]) -> dict[str, Any]:
    file_path = args['input_path']
    op = args['operation']
    
    # 在这里编写具体的业务逻辑，例如读取文件、处理数据
    # 相比于 Bash 脚本，这里可以使用完整的 Python 生态库（Pandas, NumPy 等）
    result = f"已对 {file_path} 执行 {op} 操作。"
    
    return {
        "content": [{"type": "text", "text": result}]
    }

async def main():
    # 2. 创建一个 SDK 内部的 MCP 服务器实例
    # 这不需要启动额外的进程，完全在当前进程内存中运行
    server = create_sdk_mcp_server(
        name="my-internal-tools", 
        version="1.0.0", 
        tools=[secure_data_processor]
    )

    options = ClaudeAgentOptions(
        # 1. 彻底关闭文件系统扫描
        setting_sources=,
        
        # 2. 注册内存中的 MCP 服务器
        mcp_servers={"internal": server},
        
        # 3. 启用该工具
        # 注意命名规范通常为 mcp__{server_name}__{tool_name}
        allowed_tools=["mcp__internal__secure_data_processor"]
    )

    async for message in query("请处理 data.csv", options=options):
        print(message)
```

### 5.3 深度对比：文件式技能 vs. 编程式工具

为了论证该方案的优越性，我们提供以下对比维度表：

| **对比维度**   | **文件式技能 (SKILL.md)**                   | **编程式工具 (In-Process MCP)**   |
| -------------- | ------------------------------------------- | --------------------------------- |
| **定义方式**   | Markdown + YAML + Bash/Scripts              | Python/TypeScript 代码函数        |
| **发现机制**   | 运行时扫描文件系统 (IO Bound)               | 编译期/解释期注册 (Memory Bound)  |
| **类型安全**   | 无 (弱类型，依赖 LLM 理解 Schema)           | 强类型 (Pydantic/Zod 验证)        |
| **上下文隔离** | 困难 (易受 `setting_sources` 影响)          | 完美 (完全不依赖文件系统配置)     |
| **安全性**     | 低 (通常需要开启 Bash 权限)                 | 高 (仅需特定函数执行权限)         |
| **调试难度**   | 高 (出错难以定位是 Prompt 问题还是脚本问题) | 低 (可使用标准 Debugger 断点调试) |

**结论：** 对于生产环境的 Agent SDK 项目，**方案二** 是推荐的“黄金标准”。它从根本上消除了“污染”的可能性，因为智能体根本不知道 `CLAUDE.md` 文件的存在，甚至不需要读取文件系统的权限。

------

## 6. 解决方案三：文件系统工程与物理沙箱策略

在某些遗留系统中，或者当团队强制要求必须复用现有的 CLI `SKILL.md` 资产时，方案一和方案二可能无法实施。此时，我们可以采用“文件系统工程”手段，通过欺骗 SDK 的扫描逻辑来实现隔离。

### 6.1 “Chroot” 模式

SDK 的扫描逻辑是基于 `cwd`（当前工作目录）的。我们可以为智能体构建一个“虚假”的根目录。

**实施逻辑：**

1. 创建一个专门的配置目录，例如 `/app/agent_config`。
2. 在该目录下放置 `.claude/skills`，确该目录下**没有** `CLAUDE.md`。
3. 在初始化 `ClaudeAgentOptions` 时，将 `cwd` 指向这个配置目录。
4. 为了让智能体仍然能够操作实际的项目文件（位于 `/app/src`），需要使用 `add_dirs` 参数（取决于 SDK 版本支持）或确保智能体使用绝对路径。

**代码片段：**

Python

```
options = ClaudeAgentOptions(
    # 将智能体的“视野”限制在纯净的配置目录中
    cwd="/path/to/clean_config_dir",
    
    # 开启扫描，此时扫描的是干净的目录
    setting_sources=["project"],
    
    # 极其重要：如果智能体需要修改实际代码，必须显式授权
    # 否则沙箱机制会拦截对 cwd 以外文件的访问
    # 注意：具体参数名需参考当前 SDK 版本的沙箱配置文档
    sandbox={
        "allow_paths": ["/path/to/actual/project/src"]
    }
)
```

### 6.2 风险提示

这种方案虽然能解决污染问题，但引入了**“路径混淆（Path Confusion）”**的风险。

- **相对路径失效：** 智能体认为自己在 `/app/agent_config` 下，如果它执行 `ls`，看到的是空的配置目录。如果用户指令是“修改 src 下的 main.py”，智能体可能会因为找不到文件而报错，除非它懂得使用 `../src/main.py`。
- **认知失调：** 模型通常假设 `cwd` 就是项目的根。破坏这一假设可能导致模型在执行文件操作时表现出笨拙或错误的推理。

因此，该方案仅作为**最后的备选手段（Last Resort）**。

------

## 7. 战略视角：上下文工程的未来趋势

通过对这一具体问题的探讨，我们可以窥见智能体开发领域的更深层趋势。

### 7.1 从隐式上下文到显式上下文

Claude Agent SDK 的演进路线图清晰地指向了**显式配置（Explicit Configuration）**。早期的 CLI 工具依赖“魔法”来提升用户体验，但在 API 和 SDK 层面，“魔法”意味着不可控。`setting_sources` 默认值的变更  就是这一趋势的铁证。未来的智能体开发将更少依赖 `.md` 文件，更多依赖代码定义的 Schema 和 Config 对象。

### 7.2 上下文卫生（Context Hygiene）作为核心竞争力

随着模型上下文窗口的扩大（如 200k+ Token），开发者容易陷入“把所有东西都塞进去”的懒惰思维。然而，本报告案例证明，**上下文卫生**不仅关乎成本，更关乎智能体的**对齐（Alignment）\**与\**专注度（Focus）**。

一个被污染的智能体，就像一个被无关信息轰炸的人类员工，其决策质量必然下降。构建高性能智能体的关键，在于精心设计“信息流的阀门”，确保每一比特进入上下文的信息都是对当前任务具有边际效用的。

### 7.3 安全边界的重定义

传统的安全边界在网络层（防火墙）和系统层（用户权限）。在智能体时代，**语义边界（Semantic Boundary）**变得同等重要。防止智能体读取 `CLAUDE.md` 不仅是为了防止它被 prompt 干扰，也是为了防止它泄露项目的架构机密给外部观察者（如果该智能体的输出是面向公网用户的）。方案二（In-Process MCP）通过代码级的封装，提供了最坚固的语义边界。

------

## 8. 结论与建议

针对用户提出的“如何在 Claude Agent SDK 中配置技能且不引入 Claude Code 项目配置污染”的问题，本报告经过深入调研与验证，得出以下结论：

1. **问题确认：** 这是一个由 SDK 继承 CLI 设计遗产而导致的已知架构冲突。`setting_sources=["project"]` 是导致污染的直接原因。
2. **推荐方案（快速修复）：** 采用 **方案一（插件旁路策略）**。将技能文件移至独立目录，通过 `plugins` 参数加载，同时置空 `setting_sources`。这能在保留现有 `.md` 资产的前提下，零成本解决污染问题。
3. **最佳实践（长期演进）：** 迁移至 **方案二（编程式工具定义）**。利用 SDK 的 `create_sdk_mcp_server` 接口，用 Python/TypeScript 代码重写技能。这能彻底解耦文件系统依赖，提升类型安全与运行效率。

**行动指南：**

- **立即行动：** 检查现有 Agent 代码，确认 `setting_sources` 的配置。如果发现包含了 `"project"` 且 `CLAUDE.md` 文件存在，应立即评估 Token 浪费情况。
- **代码重构：** 按照方案一的代码示例，调整 `ClaudeAgentOptions` 的初始化逻辑。
- **团队规范：** 制定新的开发规范，禁止在 Agent SDK 项目的生产代码中依赖根目录的隐式配置，强制要求所有能力通过 `plugins` 或 `mcp_servers` 显式注入。

通过实施上述策略，开发团队不仅能解决当前的配置冲突，还能建立起更健壮、更经济、更安全的智能体基础设施。