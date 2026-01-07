# BIMCanvas.Agent 配置系统改造计划

> **状态**：初稿，待深入研究 Claude Code CLI 后完善
>
> **用户决策**：
> - 配置目录：`C:\Users\{用户名}\Documents\BIMCanvas`
> - 改造范围：完整改造
> - 热更新：不需要，重启生效

---

## 现状分析

### 当前 BIMCanvas.Agent 配置方式

| 配置项 | 存储位置 | 存储方式 |
|--------|---------|---------|
| API Key / 模型 | `.env` + `config/settings.py` | 环境变量 + Python 常量 |
| 主代理系统提示词 | `agent/prompts/main_prompt.py` | Python 字符串常量 |
| 子代理提示词 | `agent/prompts/layout_prompt.py` | Python 字符串常量 |
| 子代理定义 | `agent/subagents.py` | Python 代码 (AgentDefinition) |
| 工具启用 | `main_agent.py` 第119行 | 硬编码列表 |
| MCP 配置 | 无 | 不支持 |

**特点**：代码驱动，修改需改代码重启

### Claude Code CLI 配置方式（待深入研究）

```
~/.claude/
├── config.json                 # API 密钥配置
├── settings.json               # 全局设置（权限、模型、钩子）
├── settings.local.json         # 项目级权限覆盖
├── CLAUDE.md                   # 全局系统提示词/知识库
├── commands/                   # 自定义命令（.md 文件）
│   ├── git/commit.md
│   └── doc/read.md
├── output-styles/              # 输出样式（.md 文件）
└── projects/[path]/            # 项目级配置
```

**特点**：文件驱动，分层继承，高度可定制

---

## 改造目标

将 BIMCanvas.Agent 改造为 **文件驱动配置架构**：

1. **提示词外部化**：从 Python 代码移到 `.md` 文件
2. **子代理配置化**：从代码定义移到 JSON 文件
3. **工具权限配置化**：运行时从配置文件读取
4. **MCP 配置化**：支持外部 MCP 服务器配置

---

## 目标目录结构（初步设计）

```
C:\Users\{用户名}\Documents\BIMCanvas\   # 配置根目录
├── config.json                          # 基础配置（API Key、模型等）
├── settings.json                        # 运行时设置（权限、启用的代理等）
│
├── prompts/                             # 提示词目录
│   ├── main.md                          # 主代理系统提示词
│   └── agents/                          # 子代理提示词
│       └── layout-agent.md
│
├── agents/                              # 子代理配置
│   └── layout-agent.json                # 子代理定义（描述、工具、模型）
│
├── mcp/                                 # MCP 配置
│   └── servers.json                     # MCP 服务器列表
│
└── knowledge/                           # 知识库（可选）
    └── BIMCANVAS.md                     # 领域知识
```

---

## 配置文件格式设计（初步）

### 1. `config.json` - 基础配置
```json
{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-sonnet-4-20250514",
  "maxTokens": 4096,
  "server": {
    "host": "127.0.0.1",
    "port": 8765
  }
}
```
- `$` 前缀表示引用环境变量

### 2. `settings.json` - 运行时设置
```json
{
  "mainAgent": {
    "prompt": "prompts/main.md",
    "tools": ["Read", "Glob", "Grep", "Task"],
    "maxTurns": 20
  },
  "agents": {
    "enabled": ["layout-agent"]
  },
  "mcp": {
    "enabled": true,
    "configPath": "mcp/servers.json"
  }
}
```

### 3. `prompts/main.md` - 主代理提示词
```markdown
---
name: BIMCanvas Main Agent
version: 1.0
---

你是 BIMCanvas 的主控 Agent，一个专业的室内布置协调者。

## 职责
1. 分析用户的布置需求，理解设计意图
2. 评估任务复杂度，制定执行计划
...
```
- 支持 YAML front matter 元数据

### 4. `agents/layout-agent.json` - 子代理定义
```json
{
  "name": "layout-agent",
  "description": "家具布置专家。用于空间规划、家具摆放、布局优化任务。",
  "prompt": "prompts/agents/layout-agent.md",
  "tools": ["Read", "Write", "Glob"],
  "model": "inherit"
}
```

### 5. `mcp/servers.json` - MCP 配置
```json
{
  "servers": [
    {
      "name": "canvas-mcp",
      "command": "dotnet",
      "args": ["run", "--project", "path/to/BIMCanvas.MCP.Canvas"],
      "enabled": true
    }
  ]
}
```

---

## 实现步骤（初步）

### Phase 1: 配置加载器 (src/config/loader.py)

**新增文件**：`src/config/loader.py`

```python
class ConfigLoader:
    """统一配置加载器"""

    CONFIG_DIR = Path.home() / "Documents" / "BIMCanvas"

    def load_config(self) -> dict:
        """加载 config.json，支持环境变量展开"""

    def load_settings(self) -> dict:
        """加载 settings.json"""

    def load_prompt(self, path: str) -> str:
        """加载 .md 提示词文件，解析 YAML front matter"""

    def load_agents(self) -> dict[str, AgentConfig]:
        """加载 agents/ 目录下所有子代理配置"""

    def load_mcp_servers(self) -> list[McpServerConfig]:
        """加载 MCP 服务器配置"""
```

**修改文件**：`src/config/settings.py`
- 改用 `ConfigLoader` 加载配置
- 保留环境变量作为备用

### Phase 2: 提示词外部化

1. **创建配置目录和文件**：
   - `Documents/BIMCanvas/prompts/main.md`
   - `Documents/BIMCanvas/prompts/agents/layout-agent.md`

2. **实现 Markdown 解析器**：
   - 解析 YAML front matter（`---` 包裹的元数据）
   - 返回内容和元数据

3. **修改**：`src/agent/main_agent.py`
   - 从 `ConfigLoader.load_prompt()` 获取提示词
   - 删除对 `prompts/main_prompt.py` 的依赖

### Phase 3: 子代理配置化

1. **创建子代理配置文件**：
   - `Documents/BIMCanvas/agents/layout-agent.json`

2. **新增**：`src/config/agent_loader.py`
   ```python
   @dataclass
   class AgentConfig:
       name: str
       description: str
       prompt_path: str
       tools: list[str]
       model: str

   def load_agent_configs(config_dir: Path) -> dict[str, AgentConfig]:
       """扫描 agents/ 目录，加载所有 .json 配置"""
   ```

3. **修改**：`src/agent/subagents.py`
   - 改为动态加载，而非硬编码 AgentDefinition
   - 根据 `settings.json` 的 `agents.enabled` 过滤

### Phase 4: MCP 配置化

1. **创建 MCP 配置文件**：
   - `Documents/BIMCanvas/mcp/servers.json`

2. **新增**：`src/config/mcp_loader.py`
   ```python
   @dataclass
   class McpServerConfig:
       name: str
       command: str
       args: list[str]
       enabled: bool

   def load_mcp_config(config_dir: Path) -> list[McpServerConfig]:
       """加载 MCP 服务器配置"""
   ```

3. **修改**：`src/agent/main_agent.py`
   - 根据 MCP 配置初始化 MCP 客户端
   - 将 MCP 工具注入到主代理

### Phase 5: 初始化脚本

**新增**：`src/scripts/init_config.py`
- 首次运行时创建默认配置目录和文件
- 从现有 Python 代码提取默认提示词
- 生成 `config.json.example` 模板

---

## 配置优先级

```
命令行参数 > 环境变量 > Documents/BIMCanvas/ > 代码默认值
```

---

## 关键文件清单

### 需要新增的文件

| 文件 | 职责 |
|------|------|
| `src/config/loader.py` | 统一配置加载器 |
| `src/config/agent_loader.py` | 子代理配置加载 |
| `src/config/mcp_loader.py` | MCP 配置加载 |
| `src/scripts/init_config.py` | 初始化配置目录 |

### 需要修改的文件

| 文件 | 修改内容 |
|------|----------|
| `src/config/settings.py` | 改用 ConfigLoader |
| `src/agent/main_agent.py` | 从文件加载提示词和配置 |
| `src/agent/subagents.py` | 动态加载子代理配置 |
| `src/main.py` | 添加 `--init` 参数支持 |

### 需要删除/弃用的文件

| 文件 | 说明 |
|------|------|
| `src/agent/prompts/main_prompt.py` | 迁移到 .md 文件后弃用 |
| `src/agent/prompts/layout_prompt.py` | 迁移到 .md 文件后弃用 |

---

## 待深入研究

> 下一步需要深入研究 Claude Code CLI 的配置系统，包括：

1. **配置加载机制**：Claude Code 如何发现和加载配置文件？
2. **分层继承**：全局配置与项目配置如何合并？
3. **提示词注入**：CLAUDE.md 如何被注入到系统提示词？
4. **命令系统**：commands/ 目录下的 .md 文件如何被解析和执行？
5. **权限系统**：settings.json 中的 permissions 如何控制工具调用？
6. **MCP 集成**：MCP 服务器配置和工具权限如何管理？

---

## 配置文件模板

执行 `python -m src.main --init` 后将生成以下默认配置：

```
Documents/BIMCanvas/
├── config.json              # API 配置模板
├── settings.json            # 默认设置
├── prompts/
│   ├── main.md              # 主代理提示词（从代码迁移）
│   └── agents/
│       └── layout-agent.md  # 子代理提示词（从代码迁移）
├── agents/
│   └── layout-agent.json    # 子代理定义（从代码迁移）
└── mcp/
    └── servers.json         # MCP 配置模板
```
