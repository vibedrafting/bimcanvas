# BIMCanvas 三套初始化系统说明

## 1. 文档目的

本文说明当前仓库中已经落地的三套初始化系统，重点回答以下问题：

- 为什么要拆成三套系统
- 每套系统各自负责什么
- 运行时由谁触发
- 模板文件放在哪里
- 以后新增初始化项应该改哪里

本文以当前代码实现为准，不讨论历史旧方案。

---

## 2. 设计目标

本次重构的核心目标有 5 个：

1. 把原来分散在 Server 和 Agent 内部的初始化职责拆清楚。
2. 把“缺失即初始化”和“按条件派生生成”明确分开。
3. 把所有运行时模板统一收口到 `BIMCanvas.Server/Templates/`。
4. 让 Agent 不再负责模板初始化，只负责读取和校验。
5. 让 Server 成为唯一初始化入口。

重构后，初始化系统被固定为 3 套：

1. 全局配置初始化系统
2. 项目固定文件初始化系统
3. 项目条件派生初始化系统

---

## 3. 统一路径规则

全局根目录统一叫 `BIMCANVAS_HOME`。

解析规则如下：

- 若设置了环境变量 `BIMCANVAS_HOME`，优先使用它
- Windows 默认值：`Documents/BIMCanvas`
- 非 Windows 默认值：`~/.bimcanvas`

当前这一规则同时用于：

- Server 的配置目录解析
- Agent 的配置目录解析
- 默认项目根目录 `<BIMCANVAS_HOME>/Projects`

对应实现：

- `BIMCanvas.Server/Services/ConfigService.cs`
- `BIMCanvas.Agent/src/config/loader.py`

---

## 4. 总体分层

### 4.1 三套系统的边界

#### A. 全局配置初始化系统

作用范围：`<BIMCANVAS_HOME>/`

特点：

- 缺失即初始化
- 使用模板
- 由 Server 启动最早阶段触发
- Agent 不参与写入

#### B. 项目固定文件初始化系统

作用范围：项目目录根路径

特点：

- 缺失即初始化
- 使用模板
- 在导入项目、打开项目、补资源时触发

#### C. 项目条件派生初始化系统

作用范围：项目目录内部的运行期派生产物

特点：

- 不使用模板
- 不按“缺失即复制”工作
- 依据项目状态、baseline 状态、computed 状态、zones 状态、git 状态决定是否生成或刷新

---

## 5. 模板目录结构

运行时模板现在统一放在：

```text
BIMCanvas.Server/Templates/
  global-config/
    manifest.json
    server/
      server_config.json
      web_config.json
      ccr_config.json
    agent/
      config.json
      BIMCANVAS.md
      agents/
      skills/
      .claude-plugin/
  project-fixed/
    manifest.json
    README.md
    .gitignore
    modules/
  legacy/
    context/
    old-manifests/
```

约束如下：

- `global-config/` 只服务全局配置初始化系统
- `project-fixed/` 只服务项目固定文件初始化系统
- `legacy/` 不参与运行时初始化
- `BIMCanvas.Agent/templates/` 已退出运行时主流程

---

## 6. 共享底层服务

三套系统里，前两套共用一个底层模板服务：

- `BIMCanvas.Server/Services/BootstrapTemplateService.cs`

它负责 3 件事：

1. 定位 `BIMCanvas.Server/Templates`
2. 读取 manifest
3. 按统一规则复制模板

### 6.1 模板规则

manifest 当前统一使用一套结构：

```json
{
  "version": "1.0",
  "items": [
    {
      "name": "source path",
      "target": "target path",
      "type": "template | directory",
      "enabled": true,
      "description": "..."
    }
  ]
}
```

### 6.2 复制规则

这是本次重构最重要的约束之一：

- 文件项：目标文件不存在才创建
- 目录项：目标目录不存在才整体复制
- 目标已存在则完全跳过
- 不覆盖已有内容
- 不因为空文件而重建
- 不因为空目录而重建
- 不对已存在目录做增量 merge

也就是说，`skills/`、`agents/`、`modules/` 这类目录资产都是“目录级补齐”，不是“目录内子文件修复”。

---

## 7. 全局配置初始化系统

### 7.1 服务与职责

服务：

- `BIMCanvas.Server/Services/GlobalConfigBootstrapService.cs`

职责：

- 使用 `Templates/global-config/manifest.json`
- 把全局资产初始化到 `<BIMCANVAS_HOME>/`

### 7.2 当前初始化内容

当前全局初始化项包括：

- `server_config.json`
- `web_config.json`
- `ccr_config.json`
- `config.json`
- `BIMCANVAS.md`
- `agents/`
- `skills/`
- `.claude-plugin/`

对应模板来源：

- Server 侧配置模板：`Templates/global-config/server/`
- Agent 侧配置模板：`Templates/global-config/agent/`

### 7.3 运行入口

运行入口在 Server 启动最早阶段：

- `BIMCanvas.Server/Program.cs`

当前顺序是：

1. 创建 `BootstrapTemplateService`
2. 创建 `GlobalConfigBootstrapService`
3. 调用 `EnsureInitialized()`
4. 然后才执行 `ConfigService.Load()`

这个顺序的意义是：

- 先保证配置文件存在
- 再读配置
- 避免 `ConfigService` 再承担模板复制职责

### 7.4 Agent 在这里的角色

Agent 不再初始化任何模板。

Agent 现在只做两件事：

1. 解析 `<BIMCANVAS_HOME>`
2. 校验所需配置是否已由 Server 初始化完成

相关实现：

- `BIMCanvas.Agent/src/config/loader.py`
- `BIMCanvas.Agent/src/main.py`

如果缺少关键文件，Agent 会直接报错退出。

---

## 8. 项目固定文件初始化系统

### 8.1 服务与职责

服务：

- `BIMCanvas.Server/Services/ProjectFixedFilesBootstrapService.cs`

职责：

- 使用 `Templates/project-fixed/manifest.json`
- 给项目目录补齐固定模板文件

### 8.2 当前初始化内容

当前项目固定文件只有 3 类：

- `README.md`
- `.gitignore`
- `modules/`

注意：

- `context/`
- `knowledge/`

这类历史内容不再属于项目固定初始化系统。

### 8.3 运行入口

这套系统由 `ProjectService` 编排：

- `LoadProject()`：导入 `.bcp` 后触发
- `EnsureProjectAssets()`：打开已有项目或修复固定资源时触发
- `OpenFolder()`：打开项目时会先调用固定文件初始化

对应实现：

- `BIMCanvas.Server/Services/ProjectService.cs`

### 8.4 占位符替换

项目固定文件支持少量模板占位符：

- `{PROJECT_NAME}`
- `{EXPORT_DATE}`
- `{PROJECT_FOLDER}`

当前由 `ProjectFixedFilesBootstrapService` 在写入文本模板时替换。

### 8.5 重要限制

项目固定文件系统依然遵守“只补缺失，不覆盖已有”的规则。

因此：

- 已有 `README.md` 不会被重写
- 已有 `.gitignore` 不会被重写
- 已有 `modules/` 不会做目录内增量修补

---

## 9. 项目条件派生初始化系统

### 9.1 服务与职责

服务：

- `BIMCanvas.Server/Services/ProjectDerivedBootstrapService.cs`

职责：

- 负责项目运行期/派生产物
- 完全不依赖模板目录
- 根据项目状态决定是否生成、刷新或跳过

### 9.2 当前负责的派生产物

当前包括：

- `baseline/baseline.manifest`
- `schemes/strategy.json`
- `schemes/finishes.json`
- `project.json`
- `computed/*`
- `schemes/zones.json`
- 分区目录树
- 叶子分区下的 `modules.json`
- `.git/`

### 9.3 当前运行机制

#### 1. baseline

- 若 `baseline.manifest` 已存在，直接使用已有 hash
- 若不存在，则重新计算 baseline hash 并写入

#### 2. schemes

- 若 `schemes/` 不存在则创建
- 若已经存在策略，则跳过默认策略创建
- 若没有任何策略，则创建默认策略

#### 3. project.json

- 导入新项目时：`refreshProjectMetadata = true`
- 打开已有项目时：`refreshProjectMetadata = false`

这样做的目的：

- 新项目导入时补齐元数据
- 已有项目打开时避免无意义修改 `UpdatedAt`

#### 4. computed

- 通过 `ComputedDataService.AnalyzeComputedData(projectPath)` 分析状态
- 若 computed 有效，则跳过
- 若无效或 baseline 变化导致失效，则重新生成

#### 5. zones

- 若 `schemes/zones.json` 已存在，保留现有分区设计，不覆盖
- 若缺失且 `computed/room_zones.json` 存在，则用它初始化
- 若两者都缺失，则写入空数组

#### 6. 分区目录

- 根据 `schemes/zones.json` 刷新目录树
- 容器分区只建目录
- 叶子分区创建 `modules.json`

#### 7. git

- 调用 `GitWorktreeService.InitializeRepository(projectPath)`
- 初始化失败目前记 warning，不阻断整个项目启动

### 9.4 运行入口

这套系统由以下入口触发：

#### A. 导入项目

`ProjectService.LoadProject()` 顺序是：

1. 解压 `.bcp`
2. 运行项目固定文件初始化
3. 运行项目条件派生初始化
4. 返回项目加载结果

#### B. 打开项目

`ProjectService.OpenFolder()` 顺序是：

1. 校验项目路径
2. 运行项目固定文件初始化
3. 运行项目条件派生初始化

#### C. zones 变更后的目录刷新

`ProjectWatcherService` 在检测到 `zones.json` 变更后，会调用：

- `ProjectService.CreateZoneDirectories()`

这会继续委派到：

- `ProjectDerivedBootstrapService.RefreshZoneDirectories()`

所以，`zones.json` 改动后的目录树刷新也属于第三套系统的一部分。

---

## 10. 三套系统之间的关系

### 10.1 正确的依赖方向

依赖方向应该始终是：

```text
ConfigService
  只负责路径解析和配置读写

BootstrapTemplateService
  只负责模板定位、manifest 读取、缺失检查、复制

GlobalConfigBootstrapService
  负责全局配置模板初始化

ProjectFixedFilesBootstrapService
  负责项目固定文件模板初始化

ProjectDerivedBootstrapService
  负责项目状态驱动的派生产物生成

ProjectService
  只负责编排
```

### 10.2 明确禁止的职责混淆

以下做法现在应视为错误做法：

- 在 `ConfigService` 里重新加模板复制逻辑
- 在 `ProjectService` 里重新写 manifest 解析和模板复制细节
- 让 Agent 再次承担模板初始化
- 把条件派生产物塞回 `Templates/`

---

## 11. Agent 管理机制

### 11.1 为什么 Agent 不能脱离 Server 独立启动

原因不是单纯“流程上不推荐”，而是运行时依赖真实存在：

- Agent 依赖 Server 提供的 MCP 和辅助服务
- Agent 依赖 Server 注入运行环境
- Agent 依赖 Server 先完成 `<BIMCANVAS_HOME>` 初始化

### 11.2 当前约束方式

当前 Agent 启动时必须满足：

- `BIMCANVAS_AGENT_MANAGED_BY_SERVER=1`
- `BIMCANVAS_SERVER_URL` 已设置

这是由 Server 在拉起 Agent 子进程时注入的。

对应实现：

- `BIMCanvas.Server/Program.cs`
- `BIMCanvas.Agent/src/main.py`

如果直接手工执行：

```bash
python -m src.main --serve
```

Agent 会直接拒绝启动。

---

## 12. 现在应该如何维护

### 12.1 新增全局配置模板

应修改：

1. `BIMCanvas.Server/Templates/global-config/`
2. `BIMCanvas.Server/Templates/global-config/manifest.json`

不应该修改：

- Agent 内部模板目录
- `ConfigService`

### 12.2 新增项目固定模板

应修改：

1. `BIMCanvas.Server/Templates/project-fixed/`
2. `BIMCanvas.Server/Templates/project-fixed/manifest.json`

### 12.3 新增项目条件派生产物

应修改：

- `BIMCanvas.Server/Services/ProjectDerivedBootstrapService.cs`
- 或其依赖的 `ManifestService` / `StrategyService` / `ComputedDataService` / `GitWorktreeService`

不应该修改：

- `Templates/project-fixed/`
- `Templates/global-config/`

因为第三套系统不是模板系统。

### 12.4 历史资源怎么处理

历史模板和旧 manifest 统一放在：

- `BIMCanvas.Server/Templates/legacy/`

它们的作用只是保留历史资料，不应再进入运行时初始化路径。

---

## 13. 当前实现摘要

一句话概括现在的状态：

- 全局配置：Server 启动时一次性补齐
- 项目固定文件：项目导入/打开时只补缺失项
- 项目派生文件：按项目状态条件生成或刷新
- Agent：只读、校验、执行，不再自初始化

这就是当前 BIMCanvas 初始化系统的正式结构。
