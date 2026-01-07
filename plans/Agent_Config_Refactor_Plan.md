# BIMCanvas.Agent 配置系统改造计划

> **状态**：方案定稿
> **更新日期**：2026-01-08
>
> **用户决策**：
> - 配置目录：`C:\Users\{用户名}\Documents\BIMCanvas`
> - 改造范围：完整改造（硬编码 → 配置文件驱动）
> - 热更新：不需要，重启生效
>
> **改造原则**：
> - 不影响现有功能
> - 不多做额外功能
> - 不缺失现有功能
> - **不允许硬编码**：配置文件不存在时自动从模板初始化

---

## 当前硬编码配置（需要迁移）

### 1. 系统提示词

| 文件 | 常量 | 用途 |
|------|------|------|
| `src/agent/prompts/main_prompt.py` | `MAIN_AGENT_PROMPT` | 主 Agent 系统提示词 |
| `src/agent/prompts/layout_prompt.py` | `LAYOUT_AGENT_PROMPT` | layout-agent 系统提示词 |

### 2. SubAgent 定义 (`src/agent/subagents.py`)

```python
"layout-agent": AgentDefinition(
    description="家具布置专家。用于空间规划、家具摆放、布局优化任务。当用户请求布置家具、设计布局、调整摆放位置时使用。",
    prompt=LAYOUT_AGENT_PROMPT,
    tools=["Read", "Glob", "Write"],
    model="inherit",
)
```

### 3. 工具配置 (`src/agent/main_agent.py`)

- 主 Agent: `["Read", "Glob", "Grep", "Task"]`
- layout-agent: `["Read", "Glob", "Write"]`

### 4. 模型配置 (`src/config/settings.py`)

- 从环境变量读取 (`ANTHROPIC_API_KEY`, `MODEL_NAME`, 等)
- 默认模型: `claude-sonnet-4-20250514`

---

## 目标目录结构

```
C:\Users\{用户名}\Documents\BIMCanvas\
├── BIMCANVAS.md                    # 项目指令（附加到系统提示词）
├── config.json                     # 配置（API、模型、工具、服务器）
└── agents/                         # 子 Agent 配置
    └── layout-agent.md             # layout-agent（YAML frontmatter + 提示词）
```

---

## 最终配置文件架构

### 用户配置目录

```
C:\Users\{用户名}\Documents\BIMCanvas\
│
├── BIMCANVAS.md              ← 系统提示词（类似 CLAUDE.md）
├── config.json               ← API/模型/工具/服务器配置
│
└── agents/                   ← 子 Agent 目录
    └── layout-agent.md       ← 子 Agent 配置（YAML frontmatter + 提示词）
```

### 代码模板目录

```
src/config/templates/
│
├── BIMCANVAS.md.template              ← 从 main_prompt.py 生成
├── config.json.template               ← 默认配置
│
└── agents/
    └── layout-agent.md.template       ← 从 layout_prompt.py 生成
```

### 配置文件完整内容

#### `config.json`

```json
{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-sonnet-4-20250514",
  "maxTokens": 4096,
  "tools": ["Read", "Glob", "Grep", "Task"],
  "server": {
    "host": "127.0.0.1",
    "port": 8765
  }
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `apiKey` | string | API 密钥，`$` 前缀表示环境变量引用 |
| `model` | string | 模型名称 |
| `maxTokens` | number | 最大 token 数 |
| `tools` | string[] | 可用工具列表 |
| `server.host` | string | WebSocket 服务器地址 |
| `server.port` | number | WebSocket 服务器端口 |

#### `BIMCANVAS.md`

```markdown
（从 src/agent/prompts/main_prompt.py 的 MAIN_AGENT_PROMPT 迁移）
```

- 纯 Markdown，无 YAML frontmatter
- 作为系统提示词使用
- 类似 Claude Code 的 `CLAUDE.md`

#### `agents/layout-agent.md`

```markdown
---
name: layout-agent
description: 家具布置专家。用于空间规划、家具摆放、布局优化任务。当用户请求布置家具、设计布局、调整摆放位置时使用。
tools: Read, Glob, Write
model: inherit
---

（从 src/agent/prompts/layout_prompt.py 的 LAYOUT_AGENT_PROMPT 迁移）
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `name` | string | Agent 名称（必须与文件名一致） |
| `description` | string | Agent 描述（用于 Task 工具调度） |
| `tools` | string | 可用工具，逗号分隔 |
| `model` | string | 模型，`inherit` 表示继承主 Agent |

---

### 配置加载流程

```
启动时
  │
  ├─ 检查配置目录是否存在
  │     │
  │     ├─ 不存在 → 从 templates/ 复制初始化
  │     │
  │     └─ 存在 → 继续
  │
  ├─ 加载 config.json
  │     └─ 展开环境变量（$XXX → os.getenv）
  │
  ├─ 加载 BIMCANVAS.md → 系统提示词
  │
  └─ 加载 agents/*.md → 子 Agent 定义
```

### 配置优先级

```
环境变量 > config.json
```

---

## 实现步骤

### Phase 1: 配置模板

**新增目录**：`src/config/templates/`

将现有硬编码内容转换为模板文件，用于首次运行时自动初始化配置目录：

```
src/config/templates/
├── BIMCANVAS.md.template          # 从 main_prompt.py 生成
├── config.json.template           # 默认配置
└── agents/
    └── layout-agent.md.template   # 从 layout_prompt.py 生成
```

### Phase 2: 统一配置加载器

**新增文件**：`src/config/loader.py`

```python
import re
import json
import yaml
import shutil
from pathlib import Path
from dataclasses import dataclass
from typing import Optional

@dataclass
class AgentConfig:
    """子 Agent 配置"""
    name: str
    description: str
    tools: list[str]
    model: str
    prompt: str

class ConfigLoader:
    """统一配置加载器"""

    TEMPLATES_DIR = Path(__file__).parent / "templates"

    def __init__(self, config_dir: Path = None):
        self.config_dir = config_dir or Path.home() / "Documents" / "BIMCanvas"
        self._config: Optional[dict] = None
        self._ensure_config_exists()

    def _ensure_config_exists(self):
        """确保配置目录存在，不存在则从模板初始化"""
        if not self.config_dir.exists():
            self._init_from_templates()

    def _init_from_templates(self):
        """从模板初始化配置目录"""
        self.config_dir.mkdir(parents=True, exist_ok=True)
        agents_dir = self.config_dir / "agents"
        agents_dir.mkdir(exist_ok=True)

        # 复制模板文件（去掉 .template 后缀）
        for template in self.TEMPLATES_DIR.rglob("*.template"):
            relative = template.relative_to(self.TEMPLATES_DIR)
            target = self.config_dir / str(relative).replace(".template", "")
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy(template, target)

        print(f"配置已初始化到: {self.config_dir}")

    def load_config(self) -> dict:
        """加载 config.json"""
        if self._config is None:
            config_path = self.config_dir / "config.json"
            if not config_path.exists():
                raise FileNotFoundError(f"配置文件不存在: {config_path}")
            self._config = json.loads(config_path.read_text(encoding='utf-8'))
            self._expand_env_vars(self._config)
        return self._config

    def load_project_instructions(self) -> str:
        """加载 BIMCANVAS.md 项目指令"""
        path = self.config_dir / "BIMCANVAS.md"
        if not path.exists():
            raise FileNotFoundError(f"项目指令文件不存在: {path}")
        return path.read_text(encoding='utf-8')

    def load_tools(self) -> list[str]:
        """加载主 Agent 工具列表"""
        config = self.load_config()
        tools = config.get('tools')
        if not tools:
            raise ValueError("config.json 中缺少 tools 配置")
        return tools

    def load_agents(self) -> dict[str, AgentConfig]:
        """加载 agents/ 目录下所有子 Agent 配置"""
        agents_dir = self.config_dir / "agents"
        if not agents_dir.exists():
            raise FileNotFoundError(f"agents 目录不存在: {agents_dir}")

        agents = {}
        for md_file in agents_dir.glob('*.md'):
            config = self._parse_agent_md(md_file)
            agents[config.name] = config

        if not agents:
            raise ValueError(f"agents 目录为空: {agents_dir}")

        return agents

    def _parse_agent_md(self, file_path: Path) -> AgentConfig:
        """解析子 Agent .md 文件"""
        content = file_path.read_text(encoding='utf-8')

        match = re.match(r'^---\n(.*?)\n---\n(.*)$', content, re.DOTALL)
        if not match:
            raise ValueError(f"Invalid agent file format: {file_path}")

        frontmatter = yaml.safe_load(match.group(1))
        prompt = match.group(2).strip()

        tools_str = frontmatter.get('tools', '')
        tools = [t.strip() for t in tools_str.split(',') if t.strip()] if tools_str else []

        return AgentConfig(
            name=frontmatter['name'],
            description=frontmatter['description'],
            tools=tools,
            model=frontmatter.get('model', 'inherit'),
            prompt=prompt
        )

    def _expand_env_vars(self, config: dict):
        """展开 config 中的环境变量引用"""
        import os
        for key, value in config.items():
            if isinstance(value, str) and value.startswith('$'):
                env_name = value[1:]
                config[key] = os.getenv(env_name, '')
            elif isinstance(value, dict):
                self._expand_env_vars(value)
```

### Phase 3: 修改 Settings

**修改文件**：`src/config/settings.py`

```python
from .loader import ConfigLoader

@dataclass
class Settings:
    """Application settings"""

    anthropic_api_key: str = ""
    model_name: str = ""
    max_tokens: int = 0
    tools: list[str] = None
    server_host: str = ""
    server_port: int = 0
    default_project_path: str = ""

    def __post_init__(self):
        """Load settings from config file, then environment variables"""
        loader = ConfigLoader()
        config = loader.load_config()

        # 从配置文件加载
        self.anthropic_api_key = config.get('apiKey', '')
        self.model_name = config.get('model', '')
        self.max_tokens = config.get('maxTokens', 0)
        self.tools = config.get('tools', [])
        server = config.get('server', {})
        self.server_host = server.get('host', '')
        self.server_port = server.get('port', 0)

        # 环境变量覆盖
        import os
        self.anthropic_api_key = os.getenv("ANTHROPIC_API_KEY", self.anthropic_api_key)
        self.model_name = os.getenv("MODEL_NAME", self.model_name)
        # ...
```

### Phase 4: 修改主 Agent 初始化

**修改文件**：`src/agent/main_agent.py`

```python
from src.config.loader import ConfigLoader

class ClaudeSDKClient:
    def __init__(self, project_path: str = None):
        self.config_loader = ConfigLoader()
        # ...

    def _create_options(self) -> ClaudeAgentOptions:
        settings = get_settings()

        # 从配置加载系统提示词
        system_prompt = self.config_loader.load_project_instructions()

        # 从配置加载工具列表
        tools = self.config_loader.load_tools()

        return ClaudeAgentOptions(
            system_prompt=system_prompt,
            cwd=self.project_path,
            max_turns=20,
            model=settings.model_name,
            allowed_tools=tools,  # 从配置加载
            agents=self._subagents,
            permission_mode="acceptEdits",
            include_partial_messages=True,
        )
```

### Phase 5: 修改子 Agent 加载

**修改文件**：`src/agent/subagents.py`

```python
from src.config.loader import ConfigLoader, AgentConfig

def create_subagents() -> dict[str, AgentDefinition]:
    """从配置文件加载子 Agent"""
    loader = ConfigLoader()
    agents_config = loader.load_agents()

    subagents = {}
    for name, config in agents_config.items():
        subagents[name] = AgentDefinition(
            description=config.description,
            prompt=config.prompt,
            tools=config.tools if config.tools else None,
            model=config.model,
        )
    return subagents
```

### Phase 6: 生成模板文件

**新增脚本**：`src/scripts/generate_templates.py`

一次性脚本，从现有硬编码生成模板文件（改造完成后可删除）：

```python
"""从现有硬编码生成配置模板文件"""
from pathlib import Path
from src.agent.prompts import MAIN_AGENT_PROMPT, LAYOUT_AGENT_PROMPT

TEMPLATES_DIR = Path(__file__).parent.parent / "config" / "templates"

def generate_templates():
    TEMPLATES_DIR.mkdir(parents=True, exist_ok=True)
    (TEMPLATES_DIR / "agents").mkdir(exist_ok=True)

    # BIMCANVAS.md.template
    (TEMPLATES_DIR / "BIMCANVAS.md.template").write_text(
        MAIN_AGENT_PROMPT, encoding='utf-8'
    )

    # config.json.template
    (TEMPLATES_DIR / "config.json.template").write_text('''{
  "apiKey": "$ANTHROPIC_API_KEY",
  "model": "claude-sonnet-4-20250514",
  "maxTokens": 4096,
  "tools": ["Read", "Glob", "Grep", "Task"],
  "server": {
    "host": "127.0.0.1",
    "port": 8765
  }
}''', encoding='utf-8')

    # agents/layout-agent.md.template
    (TEMPLATES_DIR / "agents" / "layout-agent.md.template").write_text(f'''---
name: layout-agent
description: 家具布置专家。用于空间规划、家具摆放、布局优化任务。当用户请求布置家具、设计布局、调整摆放位置时使用。
tools: Read, Glob, Write
model: inherit
---

{LAYOUT_AGENT_PROMPT}
''', encoding='utf-8')

    print(f"模板已生成到: {TEMPLATES_DIR}")

if __name__ == "__main__":
    generate_templates()
```

---

## 关键文件清单

### 需要新增的文件

| 文件 | 职责 |
|------|------|
| `src/config/loader.py` | 统一配置加载器（含自动初始化） |
| `src/config/templates/BIMCANVAS.md.template` | 项目指令模板 |
| `src/config/templates/config.json.template` | 配置模板 |
| `src/config/templates/agents/layout-agent.md.template` | layout-agent 模板 |
| `src/scripts/generate_templates.py` | 模板生成脚本（一次性） |

### 需要修改的文件

| 文件 | 修改内容 |
|------|----------|
| `src/config/settings.py` | 从 config.json 加载配置 |
| `src/agent/main_agent.py` | 从配置加载提示词和工具 |
| `src/agent/subagents.py` | 从 agents/*.md 加载子 Agent |

### 改造后可删除的文件

| 文件 | 说明 |
|------|------|
| `src/agent/prompts/main_prompt.py` | 内容已迁移到模板 |
| `src/agent/prompts/layout_prompt.py` | 内容已迁移到模板 |
| `src/scripts/generate_templates.py` | 模板生成完成后删除 |

---

## 不包含的功能（当前不需要）

- MCP 服务器配置（当前项目未使用 MCP）
- 热更新（需要重启生效）
